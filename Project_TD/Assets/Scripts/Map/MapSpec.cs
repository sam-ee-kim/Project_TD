namespace TowerDefence
{
    // 맵 생성 입력: LevelRules가 계산한 값 + 규칙 파라미터 (GDD 8-2).
    public readonly struct MapSpec
    {
        public readonly int N;             // 7, 9, 11, 13, 15
        public readonly int Complexity;    // 1~5: 꺾임 수·스폰 수 (MapGenRules.complexity 표)
        public readonly int Seed;          // 같은 시드 = 같은 맵
        public readonly MapGenRules Rules; // GDD 9장 파라미터 (읽기 전용)

        public MapSpec(int n, int complexity, int seed, MapGenRules rules)
        {
            N = n;
            Complexity = complexity;
            Seed = seed;
            Rules = rules;
        }

        // 이 복잡성의 스폰 수 (표가 없으면 1).
        public int SpawnCount => Rules != null ? Rules.GetLevel(Complexity).spawns : 1;
    }
}
