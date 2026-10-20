namespace TowerDefence
{
    // 맵 생성 입력: LevelRules가 계산한 값 + 규칙 파라미터 (GDD 8-2).
    public readonly struct MapSpec
    {
        public readonly int N;             // 7, 9, 11 ... 37
        public readonly int SpawnCount;    // 현재 1
        public readonly int Seed;          // 같은 시드 = 같은 맵
        public readonly MapGenRules Rules; // GDD 9장 파라미터 (읽기 전용)

        public MapSpec(int n, int spawnCount, int seed, MapGenRules rules)
        {
            N = n;
            SpawnCount = spawnCount;
            Seed = seed;
            Rules = rules;
        }
    }
}
