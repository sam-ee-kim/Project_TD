using System;

namespace TowerDefence
{
    // 층 → 맵 크기·웨이브 수·몬스터 수 계산을 하는 유일한 곳 (GDD 3장, 8-1). 상태 없는 함수.
    // 규칙 숫자는 모두 GameConfig.rules / finalFloor에서 읽는다. 레벨디자인 테이블이 생기면 여기서 교체한다.
    // 층 보정 배율(층마다 HP 증가)은 보류라 적용하지 않는다.
    public static class LevelRules
    {
        // 층 하나의 계획. 층당 1번 호출.
        public static FloorPlan GetFloorPlan(GameConfig config, int floor)
        {
            RequireFloor(floor);
            int tier = GetTier(config, floor);
            return new FloorPlan(
                floor,
                tier,
                GetMapSize(config, tier),
                GetWaveCount(config, floor),
                IsFinalFloor(config, floor));
        }

        // tier = ceil(floor / mapTierEveryFloors)
        public static int GetTier(GameConfig config, int floor)
        {
            RequireFloor(floor);
            int every = config.rules.mapTierEveryFloors;
            return (floor + every - 1) / every;
        }

        // N = mapBaseSize + mapGrowPerTier × (tier − 1)
        public static int GetMapSize(GameConfig config, int tier)
        {
            if (tier < 1)
                throw new ArgumentOutOfRangeException(nameof(tier), tier, "tier는 1 이상");
            return config.rules.mapBaseSize + config.rules.mapGrowPerTier * (tier - 1);
        }

        // waves = baseWaves + floor((floor − 1) / waveIncreaseEveryFloors)
        public static int GetWaveCount(GameConfig config, int floor)
        {
            RequireFloor(floor);
            return config.rules.baseWaves + (floor - 1) / config.rules.waveIncreaseEveryFloors;
        }

        // count = baseMonsterCount + monsterIncreaseAmount × floor((g − 1) / monsterIncreaseEveryWaves)
        // g = 이번 판 누적 웨이브 번호 (층이 바뀌어도 리셋되지 않음, 1부터).
        public static int MonsterCount(GameConfig config, int g)
        {
            if (g < 1)
                throw new ArgumentOutOfRangeException(nameof(g), g, "누적 웨이브 g는 1 이상");
            var r = config.rules;
            return r.baseMonsterCount + r.monsterIncreaseAmount * ((g - 1) / r.monsterIncreaseEveryWaves);
        }

        public static bool IsFinalFloor(GameConfig config, int floor)
        {
            RequireFloor(floor);
            return floor == config.finalFloor;
        }

        // 웨이브 구성표: 이 웨이브에 어떤 몬스터를 몇 마리, 몇 초 간격으로 내보낼지.
        // waveIndex = 층 안 웨이브 번호(1부터), g = 누적 웨이브 번호.
        // 결과를 buffer에 채우고 줄 수를 반환한다 (할당 없음). buffer는 1칸 이상.
        public static int GetWaveComposition(GameConfig config, int floor, int waveIndex, int g, WaveSpawn[] buffer)
        {
            RequireFloor(floor);
            if (buffer == null || buffer.Length == 0)
                throw new ArgumentException("buffer는 1칸 이상이어야 합니다.", nameof(buffer));

            // 레벨디자인 waves 테이블(GDD 9장)이 생기면 그 값을 우선한다.
            if (TryGetDesignedWave(config, floor, waveIndex, buffer, out int designedCount))
                return designedCount;

            // 지금 규칙: 일반 몬스터 × count마리, spawnIntervalSec 간격 한 줄.
            var r = config.rules;
            buffer[0] = new WaveSpawn(r.defaultMonsterId, MonsterCount(config, g), r.spawnIntervalSec, 0f);
            return 1;
        }

        // 레벨디자인 waves 테이블 자리 (GDD 9장 "Waves", 지금은 비워둠).
        static bool TryGetDesignedWave(GameConfig config, int floor, int waveIndex, WaveSpawn[] buffer, out int count)
        {
            // TODO(레벨디자인): waves 테이블 데이터가 생기면 floor·waveIndex 항목을 찾아 buffer에 채운다.
            count = 0;
            return false;
        }

        static void RequireFloor(int floor)
        {
            if (floor < 1)
                throw new ArgumentOutOfRangeException(nameof(floor), floor, "층은 1 이상");
        }
    }
}
