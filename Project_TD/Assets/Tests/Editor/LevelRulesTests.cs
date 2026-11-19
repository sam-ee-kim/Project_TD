using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using TowerDefence;

// 진행 표(10층, 2층마다 복잡성·맵 단계, 웨이브 최대 5)와 LevelRules 결과를 비교한다. 실제 에셋(Assets/Data/GameConfig.asset)의 값으로 계산.
public class LevelRulesTests
{
    const string ConfigPath = "Assets/Data/GameConfig.asset";

    // 검수표 한 행. Counts가 null이면 문서가 범위만 적은 행이라 첫·마지막 웨이브만 비교한다.
    public class Row
    {
        public int Floor, Tier, N, Complexity, Waves, GFirst, GLast;
        public int[] Counts;
        public int FirstCount, LastCount;

        public override string ToString() => $"{Floor}층";
    }

    static Row R(int floor, int tier, int n, int complexity, int waves, int gFirst, int gLast, params int[] counts) =>
        new Row { Floor = floor, Tier = tier, N = n, Complexity = complexity, Waves = waves, GFirst = gFirst, GLast = gLast, Counts = counts };

    // 진행 표: 층, Tier, 맵 N, 복잡성, 웨이브 수, 누적 g 시작·끝, 웨이브별 몬스터 수
    public static IEnumerable<Row> Table()
    {
        yield return R(1, 1, 7, 1, 1, 1, 1, 10);
        yield return R(2, 1, 7, 1, 1, 2, 2, 10);
        yield return R(3, 2, 9, 2, 2, 3, 4, 12, 12);
        yield return R(4, 2, 9, 2, 2, 5, 6, 14, 14);
        yield return R(5, 3, 11, 3, 3, 7, 9, 16, 16, 18);
        yield return R(6, 3, 11, 3, 3, 10, 12, 18, 20, 20);
        yield return R(7, 4, 11, 4, 4, 13, 16, 22, 22, 24, 24);   // 맵 확장은 3단계(11×11)까지
        yield return R(8, 4, 11, 4, 4, 17, 20, 26, 26, 28, 28);
        yield return R(9, 5, 11, 5, 5, 21, 25, 30, 30, 32, 32, 34);
        yield return R(10, 5, 11, 5, 5, 26, 30, 34, 36, 36, 38, 38);
    }

    GameConfig config;

    [OneTimeSetUp]
    public void LoadConfig()
    {
        config = AssetDatabase.LoadAssetAtPath<GameConfig>(ConfigPath);
        Assert.IsNotNull(config, ConfigPath + " 에셋이 없습니다.");
    }

    // 이 층 첫 웨이브의 누적 번호 g = (이전 층 웨이브 수 합) + 1.
    int FirstG(int floor)
    {
        int g = 1;
        for (int f = 1; f < floor; f++)
            g += LevelRules.GetWaveCount(config, f);
        return g;
    }

    [TestCaseSource(nameof(Table))]
    public void FloorPlan_MatchesTable(Row row)
    {
        var plan = LevelRules.GetFloorPlan(config, row.Floor);

        Assert.AreEqual(row.Floor, plan.Floor, "floor");
        Assert.AreEqual(row.Tier, plan.Tier, "Tier");
        Assert.AreEqual(row.N, plan.N, "맵 크기 N");
        Assert.AreEqual(row.Complexity, plan.Complexity, "복잡성");
        Assert.AreEqual(row.Waves, plan.Waves, "웨이브 수");
        Assert.AreEqual(row.Floor == 10, plan.IsFinal, "마지막 층 여부");

        int gFirst = FirstG(row.Floor);
        int gLast = gFirst + plan.Waves - 1;
        Assert.AreEqual(row.GFirst, gFirst, "누적 g 시작");
        Assert.AreEqual(row.GLast, gLast, "누적 g 끝");

        if (row.Counts != null)
        {
            var actual = new int[plan.Waves];
            for (int i = 0; i < plan.Waves; i++)
                actual[i] = LevelRules.MonsterCount(config, gFirst + i);
            CollectionAssert.AreEqual(row.Counts, actual, "웨이브별 몬스터 수");
        }
        else
        {
            Assert.AreEqual(row.FirstCount, LevelRules.MonsterCount(config, gFirst), "첫 웨이브 몬스터 수");
            Assert.AreEqual(row.LastCount, LevelRules.MonsterCount(config, gLast), "마지막 웨이브 몬스터 수");
        }
    }

    [Test]
    public void TotalWaves_ThroughFinalFloor_Is30()
    {
        Assert.AreEqual(10, config.finalFloor, "진행은 10층까지");
        int total = 0;
        for (int f = 1; f <= config.finalFloor; f++)
            total += LevelRules.GetWaveCount(config, f);
        Assert.AreEqual(30, total);
    }

    [Test]
    public void OnlyFinalFloor_IsFinal()
    {
        for (int f = 1; f <= config.finalFloor; f++)
            Assert.AreEqual(f == 10, LevelRules.IsFinalFloor(config, f), $"{f}층");
    }

    [Test]
    public void Caps_MapSize11_Waves5_Complexity5()
    {
        // 표 밖 층·Tier로 가도 최대값을 넘지 않는다 (레벨디자인으로 층을 늘릴 때 대비)
        Assert.AreEqual(11, LevelRules.GetMapSize(config, 9));
        Assert.AreEqual(5, LevelRules.GetWaveCount(config, 30));
        Assert.AreEqual(5, LevelRules.GetComplexity(config, 9));
    }

    [Test]
    public void WaveComposition_IsNormalMonsters_OnePerInterval()
    {
        var buffer = new WaveSpawn[4];
        int lines = LevelRules.GetWaveComposition(config, 6, 3, 13, buffer);

        Assert.AreEqual(3, lines, "6층 마지막(3) 웨이브: 일반 + 돌격조 + 탱커");
        Assert.AreEqual("normal", buffer[0].MonsterId);
        Assert.AreEqual(22, buffer[0].Count);
        Assert.AreEqual(1f, buffer[0].IntervalSec);
        Assert.AreEqual(0f, buffer[0].DelaySec);
        Assert.AreEqual("fast", buffer[1].MonsterId);
        Assert.AreEqual(4, buffer[1].Count, "5·6층 (Tier 3) 돌격조 = 4마리");
        Assert.AreEqual(0.5f, buffer[1].IntervalSec);
        Assert.AreEqual(1f, buffer[1].DelaySec);
        Assert.AreEqual("tank", buffer[2].MonsterId);
        Assert.AreEqual(2, buffer[2].Count, "5·6층 (Tier 3) 탱커 = 2마리");
        Assert.AreEqual(3f, buffer[2].IntervalSec);
        Assert.AreEqual(2f, buffer[2].DelaySec);

        // 마지막 웨이브가 아니면 일반 + 돌격조
        Assert.AreEqual(2, LevelRules.GetWaveComposition(config, 6, 2, 12, buffer));
        // 1층은 일반 몬스터만
        Assert.AreEqual(1, LevelRules.GetWaveComposition(config, 1, 1, 1, buffer));
    }

    // 돌격조(해머헤드): 2층부터 모든 웨이브, 2층 2 → 9·10층 6마리
    [TestCase(1, 0)]
    [TestCase(2, 2)]
    [TestCase(3, 3)]
    [TestCase(4, 3)]
    [TestCase(5, 4)]
    [TestCase(7, 5)]
    [TestCase(9, 6)]
    [TestCase(10, 6)]
    public void RunnerCount_FromFloor2_EveryWave(int floor, int expected)
    {
        Assert.AreEqual(expected, LevelRules.RunnerCount(config, floor));
    }

    // 탱커(바스티온): 3층부터 층의 마지막 웨이브에만, 3·4층 1 → 9·10층 4마리
    [TestCase(1, 1, 0)]
    [TestCase(2, 1, 0)]
    [TestCase(3, 1, 0)]
    [TestCase(3, 2, 1)]
    [TestCase(4, 2, 1)]
    [TestCase(5, 3, 2)]
    [TestCase(6, 3, 2)]
    [TestCase(7, 4, 3)]
    [TestCase(8, 4, 3)]
    [TestCase(9, 5, 4)]
    [TestCase(10, 4, 0)]
    [TestCase(10, 5, 4)]
    public void TankerCount_FromFloor3_LastWaveOnly(int floor, int wave, int expected)
    {
        Assert.AreEqual(expected, LevelRules.TankerCount(config, floor, wave));
    }

    [Test]
    public void HpScale_MatchesLevelDesignTable()
    {
        // Docs/HpLevelDesign.md 3장: 층별 웨이브마다 일반 몬스터(기본 체력 4) 체력
        float[][] expected =
        {
            new[] { 4f }, new[] { 5f }, new[] { 6.5f, 7f }, new[] { 8f, 8.5f },
            new[] { 11.5f, 12f, 13f }, new[] { 14f, 15f, 16.5f },
            new[] { 15f, 16.5f, 17.5f, 19f }, new[] { 19f, 20.5f, 22f, 23.5f },  // 맵 배율 M = 1 · 1.8 · 3.0 · 4.4 · 6.0 (2026-11 상향: 7층 이후에도 계속 오른다)
            new[] { 22.5f, 24.5f, 26f, 28f, 30f }, new[] { 28f, 30.5f, 32.5f, 35f, 37f },
        };
        for (int floor = 1; floor <= 10; floor++)
        {
            Assert.AreEqual(expected[floor - 1].Length, LevelRules.GetWaveCount(config, floor), $"{floor}층 웨이브 수");
            for (int w = 1; w <= expected[floor - 1].Length; w++)
                Assert.AreEqual(expected[floor - 1][w - 1], LevelRules.ScaleHp(config, 4f, LevelRules.GetHpScale(config, floor, w)), $"{floor}층 {w}웨이브");
        }
    }

    [Test]
    public void HpScale_RisesEveryFloor()
    {
        for (int floor = 2; floor <= 10; floor++)
            Assert.Greater(LevelRules.GetHpScale(config, floor, 1), LevelRules.GetHpScale(config, floor - 1, 1), $"{floor}층 첫 웨이브가 앞 층보다 강해야 한다");
    }

    static WaveSpawn[] Wave() => new[]
    {
        new WaveSpawn("normal", 7, 1f, 0f),     // 7마리, 1초 간격
        new WaveSpawn("fast", 4, 0.5f, 1f),     // 1초 쉬고 4마리, 0.5초 간격
    };

    static int Total(WaveSpawn[] lines, int n, int spawn)
    {
        int t = 0;
        for (int i = 0; i < n; i++)
            if (lines[i].SpawnIndex == spawn)
                t += lines[i].Count;
        return t;
    }

    [Test]
    public void ArrangeSpawns_SingleSpawn_Unchanged()
    {
        var dst = new WaveSpawn[24];
        int n = LevelRules.ArrangeSpawns(config, Wave(), 2, 1, 0.1, 0.1, dst, out var order);
        Assert.AreEqual(2, n);
        Assert.AreEqual(SpawnOrder.Single, order);
        Assert.AreEqual(-1, dst[0].SpawnIndex);
        Assert.IsFalse(dst[0].Parallel);
    }

    [Test]
    public void ArrangeSpawns_TwoSpawns_Sequential_S2StartsTwoSecondsAfterS1Ends()
    {
        var dst = new WaveSpawn[24];
        int n = LevelRules.ArrangeSpawns(config, Wave(), 2, 2, 0.5, 0.0, dst, out var order); // 0.5 < 0.67 → 순서대로
        Assert.AreEqual(SpawnOrder.Sequential, order);
        Assert.AreEqual(6, Total(dst, n, 0), "s1 몫: 일반 4 + 빠른 2");
        Assert.AreEqual(5, Total(dst, n, 1), "s2 몫: 일반 3 + 빠른 2 (총 11마리 그대로)");
        // s1: 일반 4마리 0~3초, 1초 쉬고 빠른 2마리 5초·5.5초 → 마지막 5.5초. s2는 5.5 + 2 = 7.5초에 시작
        Assert.AreEqual(0f, dst[0].DelaySec, 1e-4f);
        Assert.AreEqual(5f, dst[1].DelaySec, 1e-4f);
        Assert.AreEqual(1, dst[2].SpawnIndex);
        Assert.AreEqual(7.5f, dst[2].DelaySec, 1e-4f);
        for (int i = 0; i < n; i++)
        {
            Assert.IsTrue(dst[i].Parallel);
            Assert.AreEqual(1f, dst[i].HpMul, 1e-6f, "순서대로는 체력 그대로");
        }
    }

    [Test]
    public void ArrangeSpawns_TwoSpawns_Simultaneous_HpMinus10()
    {
        var dst = new WaveSpawn[24];
        int n = LevelRules.ArrangeSpawns(config, Wave(), 2, 2, 0.9, 0.0, dst, out var order); // 0.9 ≥ 0.67 → 동시
        Assert.AreEqual(SpawnOrder.S1S2, order);
        for (int i = 0; i < n; i++)
            Assert.AreEqual(0.9f, dst[i].HpMul, 1e-6f, "동시 출현 체력 −10%");
        Assert.AreEqual(0f, dst[0].DelaySec, 1e-4f);
        Assert.AreEqual(1, dst[2].SpawnIndex);
        Assert.AreEqual(0f, dst[2].DelaySec, 1e-4f, "s2도 웨이브 시작과 함께");
    }

    [TestCase(0.1, SpawnOrder.S1_S2S3)]
    [TestCase(0.5, SpawnOrder.S1S2_S3)]
    [TestCase(0.9, SpawnOrder.S1S3_S2)]
    public void ArrangeSpawns_ThreeSpawns_NeverAllThreeTogether(double pair, SpawnOrder expected)
    {
        var dst = new WaveSpawn[24];
        int n = LevelRules.ArrangeSpawns(config, Wave(), 2, 3, 0.9, pair, dst, out var order);
        Assert.AreEqual(expected, order);
        // 정확히 두 곳이 같은 때 시작(체력 −10%)하고, 나머지 한 곳은 다른 때 (세 곳이 한꺼번에 나오지 않는다)
        var firstAt = new float[] { float.MaxValue, float.MaxValue, float.MaxValue };
        for (int i = 0; i < n; i++)
            firstAt[dst[i].SpawnIndex] = System.Math.Min(firstAt[dst[i].SpawnIndex], dst[i].DelaySec);
        int pairs = 0;
        var together = new bool[3];
        for (int a = 0; a < 3; a++)
            for (int b = a + 1; b < 3; b++)
                if (System.Math.Abs(firstAt[a] - firstAt[b]) < 1e-4f)
                {
                    pairs++;
                    together[a] = together[b] = true;
                }
        Assert.AreEqual(1, pairs, "두 곳만 동시에");
        for (int i = 0; i < n; i++)
            Assert.AreEqual(together[dst[i].SpawnIndex] ? 0.9f : 1f, dst[i].HpMul, 1e-6f, "동시 출현한 두 곳만 체력 −10%");
        int alone = !together[0] ? 0 : !together[1] ? 1 : 2;
        int mate = alone == 0 ? 1 : 0;
        Assert.AreNotEqual(firstAt[alone], firstAt[mate], "혼자 나오는 곳은 앞이나 뒤 (2초 간격)");
        Assert.AreEqual(11, Total(dst, n, 0) + Total(dst, n, 1) + Total(dst, n, 2));
    }

    [Test]
    public void ArrangeSpawns_ThreeSpawns_Sequential()
    {
        var dst = new WaveSpawn[24];
        int n = LevelRules.ArrangeSpawns(config, Wave(), 2, 3, 0.2, 0.0, dst, out var order);
        Assert.AreEqual(SpawnOrder.Sequential, order);
        float s1 = float.MaxValue, s2 = float.MaxValue, s3 = float.MaxValue;
        for (int i = 0; i < n; i++)
        {
            if (dst[i].SpawnIndex == 0) s1 = System.Math.Min(s1, dst[i].DelaySec);
            if (dst[i].SpawnIndex == 1) s2 = System.Math.Min(s2, dst[i].DelaySec);
            if (dst[i].SpawnIndex == 2) s3 = System.Math.Min(s3, dst[i].DelaySec);
        }
        Assert.Less(s1, s2);
        Assert.Less(s2, s3);
    }

    [Test]
    public void HpScale_FirstWaveIsOne_RoundsToHalf()
    {
        Assert.AreEqual(1f, LevelRules.GetHpScale(config, 1, 1), 1e-6f);
        Assert.AreEqual(2.5f, LevelRules.ScaleHp(config, 2f, 1.25f), "빠른 몬스터 2층: 2 × 1.25 = 2.5");
        Assert.AreEqual(0.5f, LevelRules.ScaleHp(config, 0.1f, 1f), "최소 0.5");
    }
}
