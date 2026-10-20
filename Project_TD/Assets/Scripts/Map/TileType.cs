namespace TowerDefence
{
    // 칸 종류 (GDD 8-2).
    // 주의: 기존 맵 생성기(MapLayout.cs)의 전역 TileType과 이름이 같아 namespace로 구분한다.
    //       기존 값 → 이 값 변환은 작업 3 어댑터에서 한다.
    public enum TileType
    {
        Empty,
        Path,
        Spawn,
        Exit,
        BuildSite,
    }

    // 타워 부지 등급 (GDD 8-2, 7-6 D2).
    // 현재 맵 생성기 정본은 T2를 만들지 않지만(부지 = 경로와 맞닿은 1칸), 문서 시그니처대로 값은 둔다.
    public enum SiteGrade
    {
        None,
        T1,
        TStar,
        T2,
    }
}
