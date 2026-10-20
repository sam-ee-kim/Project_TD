using System;

namespace TowerDefence
{
    // 캐시·넥서스 체력. 캐시·체력 증감은 이 모듈 한 곳에서만 한다 (DataArchitecture 경계 4).
    // 값은 RunState에 있고(소유: PlayerState), 새 판이면 새 RunState를 받는다.
    public class PlayerState
    {
        public event Action<CashChange> CashChanged; // → GameUI (HUD, [설치] 활성화), 캐시 로그(보류)
        public event Action<HpChange> HpChanged;     // → GameUI, GameFlow
        public event Action Defeated;                // 체력 0, 판마다 1번만 → GameFlow GameOver (DamageDesign 4-2)

        GameData data;
        RunState run;
        bool isDefeated;

        public int Cash => run != null ? run.Cash : 0;
        public int NexusHp => run != null ? run.NexusHp : 0;
        public bool IsDefeated => isDefeated;

        public void Init(GameData data)
        {
            this.data = data;
        }

        // 새 판: 시작 캐시·체력이 담긴 새 RunState를 받는다 (값은 RunState.CreateNew가 GameConfig로 채움).
        public void ResetForNewRun(RunState run)
        {
            this.run = run;
            isDefeated = false;
        }

        public bool CanAfford(int amount) => amount >= 0 && run.Cash >= amount;

        // 캐시 사용. 부족하거나 음수면 false, 캐시는 그대로.
        public bool Spend(int amount, CashReason reason)
        {
            if (!CanAfford(amount))
                return false;
            run.Cash -= amount;
            CashChanged?.Invoke(new CashChange(run.Cash, -amount, reason));
            return true;
        }

        // 캐시 획득. 0 이하는 무시.
        public void Earn(int amount, CashReason reason)
        {
            if (amount <= 0)
                return;
            run.Cash += amount;
            CashChanged?.Invoke(new CashChange(run.Cash, amount, reason));
        }

        // 몬스터 → 넥서스 피해의 유일한 창구 (DamageDesign 4-2 그대로).
        // 같은 프레임에 여러 번 불려도 피해는 각각 더하고, Defeated는 처음 0이 될 때 1번만 낸다.
        public void TakeNexusDamage(int amount)
        {
            if (isDefeated) return;                         // 이미 0이면 더 계산하지 않음
            run.NexusHp = Math.Max(0, run.NexusHp - amount);
            HpChanged?.Invoke(new HpChange(run.NexusHp, -amount));
            if (run.NexusHp == 0)
            {
                isDefeated = true;
                Defeated?.Invoke();                         // GameFlow → GameOver (1번만)
            }
        }

        // MonsterManager.Killed 구독.
        public void HandleKilled(MonsterKilled e) => Earn(e.Reward, CashReason.Kill);

        // MonsterManager.ReachedExit 구독.
        public void HandleReachedExit(MonsterReachedExit e) => TakeNexusDamage(e.Damage);

        // TowerManager.RecoveryExpired 구독: 첫 웨이브 끝에 남은 회수 타워 강제 반환.
        public void HandleRecoveryExpired(RecoveryExpire e) => Earn(e.Refund, CashReason.RecoveryExpire);
    }
}
