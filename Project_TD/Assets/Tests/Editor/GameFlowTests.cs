using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;

namespace TowerDefence.Tests
{
    // 작업 4: GameFlow 상태 머신 + PlayerState. GameLoop와 같은 방식으로 매니저를 만들고 연결해 흐름을 돌린다.
    public class GameFlowTests
    {
        // GDD 2-1 상태 표에서 허용되는 전환.
        static readonly HashSet<(GameState, GameState)> Allowed = new HashSet<(GameState, GameState)>
        {
            (GameState.Ready, GameState.Break),
            (GameState.Break, GameState.Wave),
            (GameState.Wave, GameState.Break),
            (GameState.Wave, GameState.FloorTransition),
            (GameState.Wave, GameState.GameOver),
            (GameState.Wave, GameState.GameClear),
            (GameState.FloorTransition, GameState.Break),
            (GameState.GameOver, GameState.Ready),
            (GameState.GameClear, GameState.Ready),
        };

        GameConfig config;
        GameData data;
        MapProvider maps;
        Board board;
        MonsterManager monsters;
        TowerManager towers;
        PlayerState player;
        GameFlow flow;
        RunState run;

        readonly List<StateChange> states = new List<StateChange>();
        readonly List<int> timerSeconds = new List<int>();
        int defeatedCount;
        int restartCount;

        [SetUp]
        public void SetUp()
        {
            config = AssetDatabase.LoadAssetAtPath<GameConfig>("Assets/Data/GameConfig.asset");
            Assert.IsNotNull(config);

            data = new GameData();
            var monsterDefs = new[]
            {
                AssetDatabase.LoadAssetAtPath<MonsterDef>("Assets/Data/Monsters/normal.asset"),
                AssetDatabase.LoadAssetAtPath<MonsterDef>("Assets/Data/Monsters/fast.asset"),
                AssetDatabase.LoadAssetAtPath<MonsterDef>("Assets/Data/Monsters/tank.asset"),
            };
            data.Init(config, AssetDatabase.LoadAssetAtPath<MapGenRules>("Assets/Data/MapGenRules.asset"), monsterDefs, new TowerDef[0]);
            maps = new MapProvider();
            board = new Board();
            monsters = new MonsterManager();
            towers = new TowerManager();
            player = new PlayerState();
            flow = new GameFlow { LogStates = false };

            maps.Init(data, null, null); // 맵 생성은 이 테스트 대상이 아님
            board.Init(data);
            player.Init(data);
            monsters.Init(data, board);
            towers.Init(data, board, monsters, player);
            flow.Init(data, maps, board, monsters, towers);

            states.Clear();
            timerSeconds.Clear();
            defeatedCount = 0;
            restartCount = 0;
            flow.StateChanged += e => states.Add(e);
            flow.StateChanged += monsters.HandleStateChanged;
            flow.TimerSecondChanged += e => timerSeconds.Add(e.Seconds);
            player.Defeated += () => defeatedCount++;
            player.Defeated += flow.HandleDefeated;
            flow.RestartRequested += () => { restartCount++; StartNewRun(); };

            StartNewRun();
        }

        // GameLoop.StartNewRun과 같은 순서.
        void StartNewRun()
        {
            run = RunState.CreateNew(config);
            maps.ResetForNewRun(run);
            board.ResetForNewRun(run);
            player.ResetForNewRun(run);
            monsters.ResetForNewRun(run);
            towers.ResetForNewRun(run);
            flow.ResetForNewRun(run);
        }

        // 디버그 키와 같은 일: 휴식은 즉시 시작, 웨이브는 클리어 처리, 층 전환은 연출 시간만큼 Tick.
        void AdvanceOneStep()
        {
            switch (flow.State)
            {
                case GameState.Break:
                    flow.StartNow();
                    break;
                case GameState.Wave:
                    monsters.DebugFinishWave();
                    flow.CheckWaveClear();
                    break;
                case GameState.FloorTransition:
                    flow.Tick(config.floorTransitionSec + 0.01f);
                    break;
            }
        }

        [Test]
        public void Floor1To10_ReachesGameClear_WithAllowedTransitions()
        {
            Assert.AreEqual(GameState.Ready, flow.State);
            flow.RequestStartGame();

            int guard = 0;
            while (flow.State != GameState.GameClear && guard++ < 10000)
            {
                if (flow.State == GameState.Wave)
                {
                    // Wave 진입 때 MonsterManager에 구성표가 넘어갔는지: 남은 스폰 = 이번 g의 몬스터 수
                    Assert.AreEqual(LevelRules.MonsterCount(config, flow.TotalWaveIndex), monsters.RemainingToSpawn,
                        $"{flow.Floor}층 {flow.Wave}웨이브 구성표");
                }
                AdvanceOneStep();
            }

            Assert.AreEqual(GameState.GameClear, flow.State);
            Assert.AreEqual(10, flow.Floor);
            Assert.AreEqual(5, flow.Wave);
            Assert.AreEqual(30, flow.TotalWaveIndex, "누적 웨이브 g");

            // 전환이 모두 GDD 2-1 표에 있는지
            for (int i = 1; i < states.Count; i++)
                Assert.IsTrue(Allowed.Contains((states[i - 1].State, states[i].State)),
                    $"허용되지 않은 전환 {states[i - 1].State} → {states[i].State} ({states[i].Floor}층)");

            Assert.AreEqual(1 + 30 + 9 + 30 + 1, states.Count, "Ready 1 + Break 30 + FloorTransition 9 + Wave 30 + GameClear 1");
            Assert.AreEqual(9, states.FindAll(s => s.State == GameState.FloorTransition).Count);
            Assert.AreEqual(30, states.FindAll(s => s.State == GameState.Wave).Count);

            // 층 전환은 각 층의 마지막 웨이브 뒤에서만
            foreach (var s in states)
                if (s.State == GameState.FloorTransition)
                    Assert.AreEqual(LevelRules.GetWaveCount(config, s.Floor), s.Wave, $"{s.Floor}층 전환 시점");
        }

        [Test]
        public void BreakTimer_FiresOnlyWhenWholeSecondChanges_ThenStartsWave()
        {
            flow.RequestStartGame();
            Assert.AreEqual(GameState.Break, flow.State);
            Assert.AreEqual(new List<int> { 120 }, timerSeconds);

            int guard = 0;
            while (flow.State == GameState.Break && guard++ < 10000)
                flow.Tick(0.1f);

            Assert.AreEqual(GameState.Wave, flow.State);
            Assert.AreEqual(120, timerSeconds.Count, "120, 119, ..., 1 한 번씩");
            for (int i = 0; i < timerSeconds.Count; i++)
                Assert.AreEqual(120 - i, timerSeconds[i]);
        }

        [Test]
        public void StartNow_OnlyWorksInBreak()
        {
            flow.StartNow();
            Assert.AreEqual(GameState.Ready, flow.State, "Ready에서는 무시");
            flow.RequestStartGame();
            flow.StartNow();
            Assert.AreEqual(GameState.Wave, flow.State);
            Assert.AreEqual(1, flow.TotalWaveIndex);
        }

        [Test]
        public void NexusDamage_ManyTimesInOneFrame_DefeatedOnce_GameOver()
        {
            flow.RequestStartGame();
            flow.StartNow();
            var hpEvents = new List<int>();
            player.HpChanged += e => hpEvents.Add(e.Hp);

            // 같은 프레임에 여러 마리 도착: 1씩 25번 (체력 20)
            for (int i = 0; i < 25; i++)
                player.TakeNexusDamage(1);

            Assert.AreEqual(0, player.NexusHp);
            Assert.AreEqual(1, defeatedCount, "Defeated는 1번만");
            Assert.AreEqual(20, hpEvents.Count, "0이 된 뒤의 피해는 계산하지 않음");
            Assert.IsTrue(player.IsDefeated);
            Assert.AreEqual(GameState.GameOver, flow.State);
            Assert.AreEqual(1, states.FindAll(s => s.State == GameState.GameOver).Count);
            Assert.AreEqual(0, monsters.RemainingToSpawn, "게임오버 시 남은 스폰 버림");
        }

        [Test]
        public void NexusDamage_BigHitsPastZero_DefeatedOnce()
        {
            flow.RequestStartGame();
            flow.StartNow();
            player.TakeNexusDamage(15);
            player.TakeNexusDamage(15);
            player.TakeNexusDamage(15);
            Assert.AreEqual(0, player.NexusHp, "음수가 되지 않음");
            Assert.AreEqual(1, defeatedCount);
        }

        [Test]
        public void Restart_AfterGameOver_ResetsRun()
        {
            flow.RequestStartGame();
            for (int i = 0; i < 9; i++)
                AdvanceOneStep(); // 몇 웨이브·층 진행
            player.Spend(30, CashReason.Build);
            player.Earn(5, CashReason.Kill);
            while (flow.State != GameState.Wave)
                AdvanceOneStep();
            player.TakeNexusDamage(100);
            Assert.AreEqual(GameState.GameOver, flow.State);
            Assert.Greater(flow.TotalWaveIndex, 0);

            flow.RequestRestart();

            Assert.AreEqual(1, restartCount);
            Assert.AreEqual(GameState.Ready, flow.State);
            Assert.AreEqual(100, player.Cash);
            Assert.AreEqual(20, player.NexusHp);
            Assert.IsFalse(player.IsDefeated);
            Assert.AreEqual(1, flow.Floor);
            Assert.AreEqual(0, flow.Wave);
            Assert.AreEqual(0, flow.TotalWaveIndex, "g = 0");

            // 새 판에서 다시 지면 Defeated가 또 1번 나온다
            flow.RequestStartGame();
            flow.StartNow();
            player.TakeNexusDamage(100);
            Assert.AreEqual(2, defeatedCount);
        }

        [Test]
        public void Restart_AfterGameClear_ResetsRun()
        {
            flow.RequestStartGame();
            int guard = 0;
            while (flow.State != GameState.GameClear && guard++ < 10000)
                AdvanceOneStep();
            flow.RequestRestart();
            Assert.AreEqual(GameState.Ready, flow.State);
            Assert.AreEqual((100, 20, 1, 0), (player.Cash, player.NexusHp, flow.Floor, flow.TotalWaveIndex));
        }

        [Test]
        public void Restart_IgnoredWhileRunning()
        {
            flow.RequestStartGame();
            flow.RequestRestart();
            Assert.AreEqual(0, restartCount);
            Assert.AreEqual(GameState.Break, flow.State);
        }

        [Test]
        public void Cash_SpendEarnAndEvents()
        {
            var changes = new List<CashChange>();
            player.CashChanged += e => changes.Add(e);

            Assert.IsTrue(player.CanAfford(100));
            Assert.IsFalse(player.CanAfford(101));
            Assert.IsTrue(player.Spend(50, CashReason.Build));
            Assert.IsFalse(player.Spend(60, CashReason.Build), "부족하면 실패");
            Assert.AreEqual(50, player.Cash, "실패한 Spend는 캐시를 바꾸지 않음");
            player.Earn(35, CashReason.Sell);
            player.Earn(0, CashReason.Kill);
            Assert.IsFalse(player.Spend(-5, CashReason.Build), "음수 사용 거부");

            Assert.AreEqual(85, player.Cash);
            Assert.AreEqual(2, changes.Count);
            Assert.AreEqual((50, -50, CashReason.Build), (changes[0].Cash, changes[0].Delta, changes[0].Reason));
            Assert.AreEqual((85, 35, CashReason.Sell), (changes[1].Cash, changes[1].Delta, changes[1].Reason));
        }
    }
}
