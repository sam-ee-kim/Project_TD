namespace TowerDefence
{
    // 맵 생성 경계 (GDD 8-2). 구현체는 순수 C#, 스레드 안전, System.Random(seed)만 사용.
    // 구현체 = 기존 맵 생성기를 감싼 어댑터 (작업 3).
    public interface IMapGenerator
    {
        MapData Generate(MapSpec spec);
    }
}
