namespace TowerDefence
{
    // 파생 데이터: 층마다 LevelRules가 1번 계산, 저장하지 않음 (DataArchitecture 1·5절).
    public readonly struct FloorPlan
    {
        public readonly int Floor;
        public readonly int Tier;
        public readonly int N;       // 맵 한 변 칸 수
        public readonly int Waves;   // 이 층의 웨이브 수
        public readonly bool IsFinal; // 마지막 층(46층)

        public FloorPlan(int floor, int tier, int n, int waves, bool isFinal)
        {
            Floor = floor;
            Tier = tier;
            N = n;
            Waves = waves;
            IsFinal = isFinal;
        }

        // 웨이브별 몬스터 수는 누적 웨이브 g로 정해지므로 LevelRules.MonsterCount(config, g)로 구한다.
    }

    // LevelRules 웨이브 구성표의 한 줄 (GDD 4장 "스폰 구성").
    // 지금은 "일반 몬스터 × count마리, 1초 간격" 한 줄만 쓰고, 레벨디자인 테이블이 생기면 여러 줄이 된다.
    public readonly struct WaveSpawn
    {
        public readonly string MonsterId;
        public readonly int Count;
        public readonly float IntervalSec;
        public readonly float DelaySec;

        public WaveSpawn(string monsterId, int count, float intervalSec, float delaySec)
        {
            MonsterId = monsterId;
            Count = count;
            IntervalSec = intervalSec;
            DelaySec = delaySec;
        }
    }
}
