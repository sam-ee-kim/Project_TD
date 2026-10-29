using System;
using UnityEngine;

namespace TowerDefence
{
    // TowerManager 타워 회수 목록 (GDD 5-9).
    // Tier가 바뀌는 층 전환에서만 생긴다. 맵이 바뀐 층의 첫 웨이브가 끝나는 순간 남은 타워는 70% 강제 반환 후 사라진다.
    // 목록은 RunState.RecoveryList(소유: TowerManager)에 있고, 판이 끝나면 함께 버려진다 (저장·이월 없음).
    public partial class TowerManager
    {
        public event Action<RecoveryChange> RecoveryChanged; // → GameUI (좌측 패널: 바뀐 종류의 남은 수)
        public event Action<RecoveryExpire> RecoveryExpired; // → GameUI(패널 제거), PlayerState(70% 반환)

        int recoveryFloor;            // 이 층의 첫 웨이브가 끝나면 만료 (0 = 목록 없음)
        GameState lastState = GameState.Ready;

        // 회수 목록에 남은 타워 수 (전체).
        public int RecoveryTotalCount => run != null ? run.RecoveryList.Count : 0;

        // 회수 목록에 남은 이 종류 타워 수.
        public int GetRecoveryCount(TowerDef def)
        {
            if (run == null)
                return 0;
            int count = 0;
            var list = run.RecoveryList;
            for (int i = 0; i < list.Count; i++)
                if (list[i] == def)
                    count++;
            return count;
        }

        // 철거·만료 때 돌려받는 캐시: 건축 비용 × recoveryRefundRate, 반올림.
        public int GetRecoveryRefund(TowerDef def) => Mathf.RoundToInt(def.buildCost * data.Config.recoveryRefundRate);

        // Tier가 바뀌는 층 전환: 맵 위 모든 타워를 철거해 회수 목록으로 옮긴다 (타워 오브젝트는 풀 반납).
        // nextFloor = 맵이 바뀐 층. 그 층의 첫 웨이브가 끝나면 만료된다.
        public void MoveAllTowersToRecovery(int nextFloor)
        {
            ClearCombat();
            for (int i = placed.Count - 1; i >= 0; i--)
            {
                var t = placed[i];
                var def = t.Def;
                var cell = t.Cell;
                run.RecoveryList.Add(def);
                Retire(t); // 층 전환 연출 동안 분해되어 사라진다
                TowerChanged?.Invoke(new TowerChange(cell, def, false));
            }
            placed.Clear();
            run.Towers.Clear();

            if (run.RecoveryList.Count == 0)
                return;
            recoveryFloor = nextFloor;
            // 종류별 남은 수를 알린다 (UI는 종류별 줄로 묶어 "기본 타워 ×3"처럼 표시)
            foreach (var def in data.Towers)
            {
                int count = GetRecoveryCount(def);
                if (count > 0)
                    RecoveryChanged?.Invoke(new RecoveryChange(def, count, RecoveryTotalCount));
            }
        }

        // [재건축]: 캐시 없이 설치, 목록에서 1개 차감.
        public bool RequestRebuild(TowerDef def, Vector2Int cell) => def != null && RequestRebuild(def, cell, BestFacing(def, cell));

        public bool RequestRebuild(TowerDef def, Vector2Int cell, int facing)
        {
            if (def == null || !board.IsBuildable(cell) || !run.RecoveryList.Remove(def))
                return false;
            Place(cell, def, facing);
            RecoveryChanged?.Invoke(new RecoveryChange(def, GetRecoveryCount(def), RecoveryTotalCount));
            return true;
        }

        // [철거]: 확인 없이 철거, PlayerState.Earn(건축 비용 × recoveryRefundRate, RecoveryScrap).
        public bool RequestScrap(TowerDef def)
        {
            if (def == null || !run.RecoveryList.Remove(def))
                return false;
            RecoveryChanged?.Invoke(new RecoveryChange(def, GetRecoveryCount(def), RecoveryTotalCount));
            player.Earn(GetRecoveryRefund(def), CashReason.RecoveryScrap);
            return true;
        }

        // GameFlow.StateChanged 구독.
        public void HandleStateChanged(StateChange change)
        {
            // 층 전환·게임오버·클리어: 날던 투사체 반납, 고정 대상 해제 (DamageDesign 6절).
            if (change.State == GameState.FloorTransition || change.State == GameState.GameOver || change.State == GameState.GameClear)
                ClearCombat();
            if (change.State == GameState.FloorTransition)
                GrantFloorItem(change.Floor); // 층 클리어 보상 (마지막 층은 GameClear라 받지 않는다)

            if (recoveryFloor > 0)
            {
                if (change.State == GameState.GameOver)
                {
                    DiscardRecovery(); // 첫 웨이브 도중 게임오버: 반환 없이 소멸
                }
                else if (lastState == GameState.Wave && change.Floor == recoveryFloor && change.Wave >= 1)
                {
                    ExpireRecovery(); // 맵이 바뀐 층의 첫 웨이브가 끝나는 순간
                }
            }
            lastState = change.State;
        }

        // 남은 타워를 모두 정리하고 RecoveryExpired 발행 (반환은 PlayerState가 RecoveryExpire 사유로 처리).
        void ExpireRecovery()
        {
            var list = run.RecoveryList;
            int count = list.Count;
            int refund = 0;
            for (int i = 0; i < count; i++)
                refund += GetRecoveryRefund(list[i]);
            list.Clear();
            recoveryFloor = 0;
            RecoveryExpired?.Invoke(new RecoveryExpire(count, refund));
        }

        // 반환 없이 목록 제거 (게임오버).
        void DiscardRecovery()
        {
            run.RecoveryList.Clear();
            recoveryFloor = 0;
            RecoveryExpired?.Invoke(new RecoveryExpire(0, 0));
        }

        // 새 판: 목록 상태 초기화 (목록 자체는 새 RunState에 새로 있다).
        void ResetRecovery()
        {
            recoveryFloor = 0;
            lastState = GameState.Ready;
        }
    }
}
