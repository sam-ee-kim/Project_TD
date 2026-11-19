namespace TowerDefence
{
    // 파생 데이터: 층마다 LevelRules가 1번 계산, 저장하지 않음 (DataArchitecture 1·5절).
    public readonly struct FloorPlan
    {
        public readonly int Floor;
        public readonly int Tier;
        public readonly int N;          // 맵 한 변 칸 수 (최대 15)
        public readonly int Complexity; // 맵 복잡성 1~5 (꺾임 수·스폰 수)
        public readonly int Waves;      // 이 층의 웨이브 수 (최대 5)
        public readonly bool IsFinal;   // 마지막 층 (GameConfig.finalFloor)

        public FloorPlan(int floor, int tier, int n, int complexity, int waves, bool isFinal)
        {
            Floor = floor;
            Tier = tier;
            N = n;
            Complexity = complexity;
            Waves = waves;
            IsFinal = isFinal;
        }

        // 웨이브별 몬스터 수는 누적 웨이브 g로 정해지므로 LevelRules.MonsterCount(config, g)로 구한다.
    }

    // LevelRules 웨이브 구성표의 한 줄 (GDD 4장 "스폰 구성").
    // 지금은 "일반 몬스터 × count마리, 1초 간격" 한 줄만 쓰고, 레벨디자인 테이블이 생기면 여러 줄이 된다.
    // 스타트 지점이 여러 곳인 웨이브의 출현 방식 (LevelRules.ArrangeSpawns)
    public enum SpawnOrder { Single, Sequential, S1S2, S1_S2S3, S1S2_S3, S1S3_S2 }

    public readonly struct WaveSpawn
    {
        public readonly string MonsterId;
        public readonly int Count;
        public readonly float IntervalSec;
        public readonly float DelaySec;
        public readonly int SpawnIndex; // 이 줄을 낼 스타트 지점 (−1 = 자동: 맵 가중치 또는 번갈아)
        public readonly bool Parallel;  // true면 줄마다 웨이브 시작부터 DelaySec 뒤에 따로 시작 (스타트 지점별 동시 진행). false면 앞 줄이 끝난 뒤 이어서
        public readonly float HpMul;    // 이 줄 몬스터 체력 배율 (동시 출현 0.9, 기본 1)

        public WaveSpawn(string monsterId, int count, float intervalSec, float delaySec, int spawnIndex = -1, bool parallel = false, float hpMul = 1f)
        {
            HpMul = hpMul;
            MonsterId = monsterId;
            Count = count;
            IntervalSec = intervalSec;
            DelaySec = delaySec;
            SpawnIndex = spawnIndex;
            Parallel = parallel;
        }
    }
}
