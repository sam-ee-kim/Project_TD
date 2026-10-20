using System;
using UnityEngine;

namespace TowerDefence
{
    // 상태 머신(Ready/Break/Wave/FloorTransition/GameOver/GameClear), 휴식 타이머, 층 전환 (GDD 2-1, 8-1).
    // 몬스터를 직접 만들지 않고 MonsterManager에 웨이브 구성표만 넘긴다. GameLoop가 Tick / CheckWaveClear를 부른다.
    //
    //   Ready ──클릭──▶ Break ──120초 또는 StartNow──▶ Wave ──스폰 완료 + Alive 0──┬─▶ Break (층의 다음 웨이브)
    //                     ▲                                                       ├─▶ FloorTransition ─연출 끝─▶ Break (다음 층)
    //                     │                                                       └─▶ GameClear (마지막 층)
    //   GameOver / GameClear ──[다시 시작]──▶ (새 RunState) ──▶ Ready      Wave 중 넥서스 체력 0 ──▶ GameOver
    //
    // 층 전환 (GDD 2장, 8-3):
    //   같은 Tier(예: 1→2층): 짧은 연출 → 맵·타워 그대로 → Break
    //   Tier가 바뀌는 층(4, 7, 10…): 연출 시작 → 타워 전부 회수 목록으로 → (연출이 끝나고 다음 맵이 준비되면)
    //     MapProvider.Take → Board.Load(→ MapView 재구성) → 다음 Tier 맵 미리 생성 → Break.
    //     다음 맵이 아직 없으면 연출을 늘려 기다린다 (MapWaitChanged로 로딩 표시).
    public class GameFlow
    {
        public event Action<StateChange> StateChanged;       // → GameUI, MonsterManager, TowerManager, BuildController
        public event Action<TimerSecond> TimerSecondChanged; // → GameUI (정수 초가 바뀔 때만)
        public event Action RestartRequested;                // → GameLoop (새 RunState로 모든 매니저 초기화)
        public event Action<bool> MapWaitChanged;            // → GameUI (true: 다음 맵 준비 대기 중, 로딩 표시)

        int pendingTier;     // Tier가 바뀌는 층 전환에서 올릴 맵 (0 = 맵 교체 없음)
        bool waitingForMap;

        GameData data;
        MapProvider maps;
        Board board;
        MonsterManager monsters;
        TowerManager towers;
        RunState run;

        readonly WaveSpawn[] waveBuffer = new WaveSpawn[8]; // 웨이브 구성표 (할당 없이 재사용)
        float breakRemaining;
        float transitionRemaining;
        int shownSecond = -1;

        public GameState State { get; private set; } = GameState.Ready;
        public int Floor => run != null ? run.Floor : 0;
        public int Wave => run != null ? run.Wave : 0;                     // 층 안 웨이브 번호 (1부터, 0 = 아직 없음)
        public int TotalWaveIndex => run != null ? run.TotalWaveIndex : 0; // 누적 웨이브 g (판 전체)
        public FloorPlan CurrentPlan { get; private set; }
        public float BreakRemaining => breakRemaining;

        public void Init(GameData data, MapProvider maps, Board board, MonsterManager monsters, TowerManager towers)
        {
            this.data = data;
            this.maps = maps;
            this.board = board;
            this.monsters = monsters;
            this.towers = towers;
        }

        // 새 판: 1층 계획을 계산하고 Ready로. RunState 값(1층, g 0)은 RunState.CreateNew가 채운다.
        public void ResetForNewRun(RunState run)
        {
            this.run = run;
            CurrentPlan = LevelRules.GetFloorPlan(data.Config, run.Floor);
            breakRemaining = 0f;
            transitionRemaining = 0f;
            shownSecond = -1;
            pendingTier = 0;
            SetWaitingForMap(false);
            SetState(GameState.Ready);
            // 첫 맵은 MapProvider가 준비되면(TierReady) Board에 올린다.
            if (maps.IsReady(CurrentPlan.Tier))
                ApplyMap(CurrentPlan.Tier);
        }

        // GameLoop 1단계: 맵 생성 완료 확인, 휴식 타이머, 층 전환 연출 대기.
        public void Tick(float dt)
        {
            // 백그라운드 맵 생성 완료 확인 → TierReady (메인 스레드에서만 발행되도록 여기서 호출)
            maps.Poll();

            switch (State)
            {
                case GameState.Break:
                    breakRemaining -= dt;
                    if (breakRemaining <= 0f)
                    {
                        StartWave();
                        return;
                    }
                    RefreshTimerSecond();
                    break;

                case GameState.FloorTransition:
                    transitionRemaining -= dt;
                    if (transitionRemaining > 0f)
                        break;
                    if (pendingTier > 0 && maps.HasGenerator)
                    {
                        if (!maps.IsReady(pendingTier))
                        {
                            SetWaitingForMap(true); // 연출을 늘려 기다린다 (로딩 표시)
                            break;
                        }
                        ApplyMap(pendingTier);
                    }
                    pendingTier = 0;
                    SetWaitingForMap(false);
                    FinishFloorTransition();
                    break;
            }
        }

        // GameLoop 6단계: 스폰 완료 + Alive 0마리면 웨이브 클리어 (GDD 2-1).
        public void CheckWaveClear()
        {
            if (State != GameState.Wave || !monsters.IsSpawnFinished || monsters.AliveCount > 0)
                return;

            if (run.Wave < CurrentPlan.Waves)
                EnterBreak();                         // 같은 층 다음 웨이브 전 휴식
            else if (CurrentPlan.IsFinal)
                SetState(GameState.GameClear);        // 마지막 층 마지막 웨이브
            else
                EnterFloorTransition();               // 층 클리어
        }

        // Ready에서 화면 아무 곳 클릭 → Break.
        public void RequestStartGame()
        {
            if (State == GameState.Ready)
                EnterBreak();
        }

        // Break 중 [즉시 시작] → Wave.
        public void StartNow()
        {
            if (State == GameState.Break)
                StartWave();
        }

        // GameOver / GameClear에서 [다시 시작] → GameLoop가 새 RunState로 모든 매니저를 초기화 → Ready.
        public void RequestRestart()
        {
            if (State == GameState.GameOver || State == GameState.GameClear)
                RestartRequested?.Invoke();
        }

        // MapProvider.TierReady 구독: 판 시작 직후 첫 맵이 준비되면 Board에 올린다.
        // (층 전환 중 대기는 Tick이 IsReady로 확인해 이어 간다.)
        public void HandleTierReady(TierReadyInfo e)
        {
            if (board.Map == null && e.Tier == CurrentPlan.Tier)
                ApplyMap(e.Tier);
        }

        // PlayerState.HpChanged 구독. 게임오버 판정은 Defeated(1번만)로 하므로 여기서는 하지 않는다.
        public void HandleHpChanged(HpChange e)
        {
        }

        // PlayerState.Defeated 구독: 웨이브 중 넥서스 체력 0 → GameOver.
        // GameLoop는 3단계 뒤 상태를 다시 보고 이번 프레임의 전투·클리어 판정을 건너뛴다 (DamageDesign 4-2).
        public void HandleDefeated()
        {
            if (State != GameState.GameOver && State != GameState.GameClear)
                SetState(GameState.GameOver);
        }

        void EnterBreak()
        {
            breakRemaining = data.Config.breakTimeSec;
            shownSecond = -1;
            SetState(GameState.Break);
            RefreshTimerSecond();
        }

        void StartWave()
        {
            run.Wave++;
            run.TotalWaveIndex++;
            int lines = LevelRules.GetWaveComposition(data.Config, run.Floor, run.Wave, run.TotalWaveIndex, waveBuffer);
            monsters.BeginWave(waveBuffer, lines);
            SetState(GameState.Wave);
        }

        void EnterFloorTransition()
        {
            transitionRemaining = data.Config.floorTransitionSec;
            SetState(GameState.FloorTransition); // 연출 시작 (전투 정리: 투사체 반납, 고정 대상 해제)

            int nextFloor = run.Floor + 1;
            int nextTier = LevelRules.GetTier(data.Config, nextFloor);
            if (nextTier != CurrentPlan.Tier)
            {
                // Tier가 바뀌는 층: 타워 전부 회수 목록으로. 맵은 연출이 끝날 때 교체한다.
                towers.MoveAllTowersToRecovery(nextFloor);
                pendingTier = nextTier;
            }
            // 같은 Tier: 맵·타워 그대로, 회수 목록 없음
        }

        // MapProvider.Take → Board.Load(→ MapView 재구성) → 다음 Tier 맵 미리 생성 (GDD 8-3).
        bool ApplyMap(int tier)
        {
            var map = maps.Take(tier);
            if (map == null)
                return false;
            board.Load(map);
            maps.EnterTier(tier);
            return true;
        }

        void SetWaitingForMap(bool waiting)
        {
            if (waitingForMap == waiting)
                return;
            waitingForMap = waiting;
            MapWaitChanged?.Invoke(waiting);
        }

        void FinishFloorTransition()
        {
            run.Floor++;
            run.Wave = 0;
            CurrentPlan = LevelRules.GetFloorPlan(data.Config, run.Floor);
            EnterBreak();
        }

        // 휴식 남은 시간의 정수 초가 바뀔 때만 알린다 (올림: 120.0 ~ 119.01초 = 120).
        void RefreshTimerSecond()
        {
            int seconds = Mathf.CeilToInt(Mathf.Max(0f, breakRemaining));
            if (seconds == shownSecond)
                return;
            shownSecond = seconds;
            TimerSecondChanged?.Invoke(new TimerSecond(seconds));
        }

        void SetState(GameState next)
        {
            State = next;
            LogState();
            StateChanged?.Invoke(new StateChange(next, run.Floor, run.Wave));
        }

        [System.Diagnostics.Conditional("UNITY_EDITOR"), System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
        void LogState()
        {
            if (LogStates)
                Debug.Log($"[GameFlow] {State} (층 {run.Floor}, 웨이브 {run.Wave}/{CurrentPlan.Waves}, g={run.TotalWaveIndex})");
        }

        // 디버그: 상태 전환 로그 출력 여부 (상태가 바뀔 때만 찍힌다).
        public bool LogStates { get; set; } = true;
    }
}
