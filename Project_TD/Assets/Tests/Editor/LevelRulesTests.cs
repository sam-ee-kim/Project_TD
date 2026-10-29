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
        yield return R(7, 4, 13, 4, 4, 13, 16, 22, 22, 24, 24);
        yield return R(8, 4, 13, 4, 4, 17, 20, 26, 26, 28, 28);
        yield return R(9, 5, 15, 5, 5, 21, 25, 30, 30, 32, 32, 34);
        yield return R(10, 5, 15, 5, 5, 26, 30, 34, 36, 36, 38, 38);
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
    public void Caps_MapSize15_Waves5_Complexity5()
    {
        // 표 밖 층·Tier로 가도 최대값을 넘지 않는다 (레벨디자인으로 층을 늘릴 때 대비)
        Assert.AreEqual(15, LevelRules.GetMapSize(config, 9));
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
            new[] { 4f }, new[] { 5f }, new[] { 5.5f, 6f }, new[] { 7f, 7.5f },
            new[] { 10f, 10.5f, 11.5f }, new[] { 12f, 13f, 14f },
            new[] { 14f, 15f, 16f, 17f }, new[] { 17f, 18.5f, 20f, 21.5f },
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
    public void HpScale_FirstWaveIsOne_RoundsToHalf()
    {
        Assert.AreEqual(1f, LevelRules.GetHpScale(config, 1, 1), 1e-6f);
        Assert.AreEqual(2.5f, LevelRules.ScaleHp(config, 2f, 1.25f), "빠른 몬스터 2층: 2 × 1.25 = 2.5");
        Assert.AreEqual(0.5f, LevelRules.ScaleHp(config, 0.1f, 1f), "최소 0.5");
    }
}
