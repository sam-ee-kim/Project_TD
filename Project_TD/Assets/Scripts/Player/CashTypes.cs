namespace TowerDefence
{
    // 캐시 증감 사유 (GDD 6-3). 캐시 로그(보류)가 이 값으로 문구를 고른다.
    public enum CashReason
    {
        Kill,
        Build,
        Sell,
        RecoveryScrap,
        RecoveryExpire,
    }

    // PlayerState.CashChanged 인자. 화면 문자열은 UI가 그릴 때만 만든다.
    public readonly struct CashChange
    {
        public readonly int Cash;  // 바뀐 뒤 캐시
        public readonly int Delta; // 증감량 (+획득 / −사용)
        public readonly CashReason Reason;

        public CashChange(int cash, int delta, CashReason reason)
        {
            Cash = cash;
            Delta = delta;
            Reason = reason;
        }
    }

    // PlayerState.HpChanged 인자.
    public readonly struct HpChange
    {
        public readonly int Hp;    // 바뀐 뒤 넥서스 체력
        public readonly int Delta;

        public HpChange(int hp, int delta)
        {
            Hp = hp;
            Delta = delta;
        }
    }
}
