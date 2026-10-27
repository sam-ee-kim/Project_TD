namespace TowerDefence
{
    public enum MonsterState
    {
        Inactive, // 풀에서 쉬는 중
        Alive,
        Dying,    // 사망 판정 끝, 5단계 정리 대기 (보상 지급됨)
        Exited,   // 출구 도착, 5단계 정리 대기 (보상 없음)
    }

    public enum MonsterCategory
    {
        Normal,
        Special, // 일반이 아닌 특수 몬스터 (타워 종류 우선도에 사용)
    }

    public enum DamageType
    {
        Normal, // 나중: Splash, Pierce, Slow ...
    }

    public enum DamageResult
    {
        Ignored, // 무효 핸들, 이미 사망·도착
        Damaged,
        Killed,  // 이번 피해로 사망 (보상 1회)
    }

    // 타워 → 몬스터 피해 값. 투사체에 복사해 넘긴다 (DamageDesign 3-3, 7절).
    public readonly struct DamageInfo
    {
        public readonly float Amount;   // 소수 피해 가능 (화염방사 0.5/틱)
        public readonly DamageType Type;
        public readonly string BonusTrait;  // 이 속성(MonsterDef.traits)을 가진 몬스터에게는 BonusMul배 (예: EMP → "mechanical" 2배)
        public readonly float BonusMul;

        public DamageInfo(float amount, DamageType type, string bonusTrait = null, float bonusMul = 1f)
        {
            Amount = amount;
            Type = type;
            BonusTrait = bonusTrait;
            BonusMul = bonusMul;
        }
    }

    // MonsterManager.Killed 인자 (GDD 8-4).
    public readonly struct MonsterKilled
    {
        public readonly int Reward;

        public MonsterKilled(int reward) => Reward = reward;
    }

    // MonsterManager.ReachedExit 인자 (GDD 8-4).
    public readonly struct MonsterReachedExit
    {
        public readonly int Damage;

        public MonsterReachedExit(int damage) => Damage = damage;
    }
}
