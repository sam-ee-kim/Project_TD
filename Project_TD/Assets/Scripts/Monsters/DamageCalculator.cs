namespace TowerDefence
{
    // 피해량 계산 (DamageDesign 7절). 순수 함수. 방어력·약점·치명타는 여기만 고친다.
    public static class DamageCalculator
    {
        public static int Compute(in DamageInfo info, MonsterDef target) => info.Amount;
    }
}
