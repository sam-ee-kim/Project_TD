namespace TowerDefence
{
    // 피해량 계산 (DamageDesign 7절). 순수 함수. 방어력·약점·치명타는 여기만 고친다.
    public static class DamageCalculator
    {
        // 속성 상성: 타워가 노리는 속성(BonusTrait)을 몬스터가 가지면 BonusMul배.
        public static float Compute(in DamageInfo info, MonsterDef target)
        {
            if (!string.IsNullOrEmpty(info.BonusTrait) && target != null && target.HasTrait(info.BonusTrait))
                return info.Amount * info.BonusMul;
            return info.Amount;
        }
    }
}
