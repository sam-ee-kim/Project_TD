using System;

namespace TowerDefence
{
    // 층 → 맵 크기·웨이브 수·몬스터 수 계산을 하는 유일한 곳 (GDD 3장, 8-1). 상태 없는 함수.
    // 규칙 숫자는 모두 GameConfig.rules / finalFloor에서 읽는다. 레벨디자인 테이블이 생기면 여기서 교체한다.
    // 몬스터 체력 배율(층·웨이브별)도 여기서 계산한다 (Docs/HpLevelDesign.md).
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
                GetComplexity(config, tier),
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

        // N = min(mapBaseSize + mapGrowPerTier × (tier − 1), mapMaxSize)
        public static int GetMapSize(GameConfig config, int tier)
        {
            if (tier < 1)
                throw new ArgumentOutOfRangeException(nameof(tier), tier, "tier는 1 이상");
            var r = config.rules;
            int n = r.mapBaseSize + r.mapGrowPerTier * (tier - 1);
            return r.mapMaxSize > 0 ? Math.Min(n, r.mapMaxSize) : n;
        }

        // 복잡성 = min(tier, maxComplexity). 꺾임 수·스폰 수는 MapGenRules.complexity 표가 정한다.
        public static int GetComplexity(GameConfig config, int tier)
        {
            if (tier < 1)
                throw new ArgumentOutOfRangeException(nameof(tier), tier, "tier는 1 이상");
            int max = config.rules.maxComplexity;
            return max > 0 ? Math.Min(tier, max) : tier;
        }

        // waves = min(baseWaves + floor((floor − 1) / waveIncreaseEveryFloors), maxWaves)
        public static int GetWaveCount(GameConfig config, int floor)
        {
            RequireFloor(floor);
            var r = config.rules;
            int waves = r.baseWaves + (floor - 1) / r.waveIncreaseEveryFloors;
            return r.maxWaves > 0 ? Math.Min(waves, r.maxWaves) : waves;
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

        // 몬스터 체력 배율 = M(맵 단계) × C(복잡성) × F(단계 안 층) × W(층 안 웨이브). 웨이브 시작 때 1번.
        // waveIndex = 층 안 웨이브 번호(1부터). 표가 비었거나 짧으면 그 항목은 1 / 마지막 값을 쓴다.
        public static float GetHpScale(GameConfig config, int floor, int waveIndex)
        {
            RequireFloor(floor);
            var r = config.rules;
            int tier = GetTier(config, floor);
            float m = TableValue(r.hpMapScale, tier);
            float c = TableValue(r.hpComplexityScale, GetComplexity(config, tier));
            int every = Math.Max(1, r.mapTierEveryFloors);
            int floorInTier = (floor - 1) % every; // 0 = 새 맵의 첫 층
            float f = 1f + (r.hpSecondFloorScale - 1f) * floorInTier;
            float w = 1f + r.hpPerWave * (Math.Max(1, waveIndex) - 1);
            return m * c * f * w;
        }

        // 기본 체력 × 배율 → hpRoundStep 단위 반올림 (최소 1단위).
        public static float ScaleHp(GameConfig config, float baseHp, float scale)
        {
            float hp = baseHp * scale;
            float step = config != null ? config.rules.hpRoundStep : 0f;
            if (step <= 0f)
                return hp;
            float rounded = (float)Math.Round(hp / step, MidpointRounding.AwayFromZero) * step;
            return Math.Max(step, rounded);
        }

        static float TableValue(float[] table, int level)
        {
            if (table == null || table.Length == 0)
                return 1f;
            return table[Math.Min(Math.Max(level, 1), table.Length) - 1];
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
            int lines = 1;
            // + 일반 몬스터 뒤에 돌격조 줄 (모든 웨이브)
            int runners = RunnerCount(config, floor);
            if (runners > 0 && lines < buffer.Length)
                buffer[lines++] = new WaveSpawn(r.runnerMonsterId, runners, r.runnerIntervalSec, r.runnerDelaySec);
            // + 층의 마지막 웨이브: 맨 끝에 탱커 줄
            int tankers = TankerCount(config, floor, waveIndex);
            if (tankers > 0 && lines < buffer.Length)
                buffer[lines++] = new WaveSpawn(r.tankerMonsterId, tankers, r.tankerIntervalSec, r.tankerDelaySec);
            return lines;
        }

        // ---------- 스타트 지점이 2곳 이상인 맵의 출현 순서 ----------
        // 웨이브 구성(src, 앞 줄이 끝나면 다음 줄)을 스타트 지점마다 나눠 동시 진행 줄(Parallel)로 바꿔 dst에 쓴다.
        //   몫: 줄마다 마리 수를 스타트 수로 나눈다 (나머지는 앞 스타트부터 1마리씩). 총 마리 수는 그대로
        //   rollOrder < spawnSequentialChance → 순서대로: s1 몫이 다 나오면 spawnSequentialGapSec 뒤 s2, 그 뒤 s3
        //   그 밖 → 두 곳 동시 (그 두 곳 몬스터 체력 × simultaneousHpMul):
        //     스타트 2곳: s1s2 동시 / 스타트 3곳: rollPair로 s1·s2s3 | s1s2·s3 | s1s3·s2 중 하나 (세 곳이 한꺼번에 나오는 일은 없다)
        // 이미 스타트 지점을 정했거나 동시 진행인 구성(레벨디자인 표)이면 그대로 복사한다. 반환: dst에 쓴 줄 수
        public static int ArrangeSpawns(GameConfig config, WaveSpawn[] src, int count, int spawnCount, double rollOrder, double rollPair,
            WaveSpawn[] dst, out SpawnOrder order)
        {
            order = SpawnOrder.Single;
            bool custom = false;
            for (int i = 0; i < count; i++)
                custom |= src[i].SpawnIndex >= 0 || src[i].Parallel;
            if (spawnCount <= 1 || custom || count == 0)
            {
                int n = Math.Min(count, dst.Length);
                Array.Copy(src, dst, n);
                return n;
            }
            spawnCount = Math.Min(spawnCount, 3);
            var r = config.rules;

            // 출현 묶음: groupOf[s] = 몇 번째로 나오는지, 같은 번호면 동시
            int g0 = 0, g1 = 1, g2 = 2;
            float hpSim = r.simultaneousHpMul;
            bool sequential = rollOrder < r.spawnSequentialChance;
            if (sequential)
                order = SpawnOrder.Sequential;
            else if (spawnCount == 2)
            {
                g1 = 0;
                order = SpawnOrder.S1S2;
            }
            else
            {
                int pick = Math.Min(2, (int)(rollPair * 3.0));
                if (pick == 0) { g0 = 0; g1 = 1; g2 = 1; order = SpawnOrder.S1_S2S3; }
                else if (pick == 1) { g0 = 0; g1 = 0; g2 = 1; order = SpawnOrder.S1S2_S3; }
                else { g0 = 0; g1 = 1; g2 = 0; order = SpawnOrder.S1S3_S2; }
            }

            // 스타트 하나가 자기 몫을 다 내보내는 시간 (원래 순서 그대로: 줄 지연 + 마리 수 × 간격, 마지막 한 마리 뒤 간격은 뺀다)
            float SpawnDuration(int s)
            {
                float t = 0f, last = 0f;
                for (int i = 0; i < count; i++)
                {
                    int share = Share(src[i].Count, spawnCount, s);
                    if (share <= 0)
                        continue;
                    t += src[i].DelaySec;
                    last = t + (share - 1) * src[i].IntervalSec;
                    t += share * src[i].IntervalSec;
                }
                return last;
            }

            int groups = (Math.Max(g0, Math.Max(g1, spawnCount > 2 ? g2 : 0))) + 1;
            float start = 0f;
            int written = 0;
            for (int g = 0; g < groups; g++)
            {
                float groupEnd = 0f;
                int members = 0;
                for (int s = 0; s < spawnCount; s++)
                    if ((s == 0 ? g0 : s == 1 ? g1 : g2) == g)
                        members++;
                float hpMul = members >= 2 ? hpSim : 1f;
                for (int s = 0; s < spawnCount; s++)
                {
                    if ((s == 0 ? g0 : s == 1 ? g1 : g2) != g)
                        continue;
                    float t = start;
                    for (int i = 0; i < count; i++)
                    {
                        int share = Share(src[i].Count, spawnCount, s);
                        if (share <= 0)
                            continue;
                        t += src[i].DelaySec;
                        if (written < dst.Length)
                            dst[written++] = new WaveSpawn(src[i].MonsterId, share, src[i].IntervalSec, t, s, true, hpMul);
                        t += share * src[i].IntervalSec;
                    }
                    groupEnd = Math.Max(groupEnd, start + SpawnDuration(s));
                }
                start = groupEnd + r.spawnSequentialGapSec; // 앞 묶음의 마지막 몬스터가 나온 뒤 2초
            }
            return written;
        }

        // 마리 수를 스타트 수로 나눈 s번째 몫 (나머지는 앞 스타트부터 1마리씩)
        public static int Share(int total, int spawnCount, int s) => total / spawnCount + (s < total % spawnCount ? 1 : 0);

        // 이 층의 웨이브마다 나오는 돌격조 수: runnerFromFloor층부터, runnerBaseCount에서 Tier마다 1마리씩 늘어난다.
        public static int RunnerCount(GameConfig config, int floor)
        {
            var r = config.rules;
            if (r.runnerFromFloor <= 0 || floor < r.runnerFromFloor || string.IsNullOrEmpty(r.runnerMonsterId))
                return 0;
            return r.runnerBaseCount + GetTier(config, floor) - GetTier(config, r.runnerFromFloor);
        }

        // 이 웨이브에 나오는 탱커 수: tankerFromFloor층부터, 층의 마지막 웨이브에만, Tier마다 1마리씩 늘어난다.
        public static int TankerCount(GameConfig config, int floor, int waveIndex)
        {
            var r = config.rules;
            if (r.tankerFromFloor <= 0 || floor < r.tankerFromFloor || string.IsNullOrEmpty(r.tankerMonsterId)
                || waveIndex != GetWaveCount(config, floor))
                return 0;
            return GetTier(config, floor) - GetTier(config, r.tankerFromFloor) + 1;
        }

        // 레벨디자인 waves 테이블 (GDD 9장 "Waves", GameConfig.waves). 층·웨이브 항목을 찾아 buffer에 채운다.
        // 줄 수가 buffer보다 많으면 앞에서부터 buffer 크기만큼만 쓴다.
        static bool TryGetDesignedWave(GameConfig config, int floor, int waveIndex, WaveSpawn[] buffer, out int count)
        {
            count = 0;
            var waves = config.waves;
            if (waves == null)
                return false;
            for (int i = 0; i < waves.Length; i++)
            {
                var w = waves[i];
                if (w == null || w.floor != floor || w.wave != waveIndex || w.lines == null)
                    continue;
                int n = Math.Min(w.lines.Length, buffer.Length);
                for (int l = 0; l < n; l++)
                {
                    var line = w.lines[l];
                    buffer[l] = new WaveSpawn(line.monsterId, line.count, line.intervalSec, line.delaySec, line.spawnIndex, w.parallel);
                }
                count = n;
                return true;
            }
            return false;
        }

        static void RequireFloor(int floor)
        {
            if (floor < 1)
                throw new ArgumentOutOfRangeException(nameof(floor), floor, "층은 1 이상");
        }
    }
}
