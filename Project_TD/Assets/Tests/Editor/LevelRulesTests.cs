using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using TowerDefence;

// GDD 3-2 검수표와 LevelRules 결과를 비교한다. 실제 에셋(Assets/Data/GameConfig.asset)의 값으로 계산.
public class LevelRulesTests
{
    const string ConfigPath = "Assets/Data/GameConfig.asset";

    // 검수표 한 행. Counts가 null이면 문서가 범위만 적은 행이라 첫·마지막 웨이브만 비교한다.
    public class Row
    {
        public int Floor, Tier, N, Waves, GFirst, GLast;
        public int[] Counts;
        public int FirstCount, LastCount;

        public override string ToString() => $"{Floor}층";
    }

    static Row R(int floor, int tier, int n, int waves, int gFirst, int gLast, params int[] counts) =>
        new Row { Floor = floor, Tier = tier, N = n, Waves = waves, GFirst = gFirst, GLast = gLast, Counts = counts };

    static Row Range(int floor, int tier, int n, int waves, int gFirst, int gLast, int firstCount, int lastCount) =>
        new Row { Floor = floor, Tier = tier, N = n, Waves = waves, GFirst = gFirst, GLast = gLast, FirstCount = firstCount, LastCount = lastCount };

    // GDD 3-2 검수표 그대로 옮김.
    public static IEnumerable<Row> Table()
    {
        yield return R(1, 1, 7, 2, 1, 2, 10, 10);
        yield return R(2, 1, 7, 2, 3, 4, 12, 12);
        yield return R(3, 1, 7, 2, 5, 6, 14, 14);
        yield return R(4, 2, 9, 2, 7, 8, 16, 16);
        yield return R(5, 2, 9, 2, 9, 10, 18, 18);
        yield return R(6, 2, 9, 3, 11, 13, 20, 20, 22);
        yield return R(7, 3, 11, 3, 14, 16, 22, 24, 24);
        yield return R(8, 3, 11, 3, 17, 19, 26, 26, 28);
        yield return R(9, 3, 11, 3, 20, 22, 28, 30, 30);
        yield return R(10, 4, 13, 3, 23, 25, 32, 32, 34);
        yield return R(11, 4, 13, 4, 26, 29, 34, 36, 36, 38);
        yield return R(12, 4, 13, 4, 30, 33, 38, 40, 40, 42);
        yield return R(13, 5, 15, 4, 34, 37, 42, 44, 44, 46);
        yield return R(14, 5, 15, 4, 38, 41, 46, 48, 48, 50);
        yield return R(15, 5, 15, 4, 42, 45, 50, 52, 52, 54);
        yield return R(16, 6, 17, 5, 46, 50, 54, 56, 56, 58, 58);
        yield return Range(31, 11, 27, 8, 136, 143, 144, 152);
        yield return Range(46, 16, 37, 11, 271, 281, 280, 290);
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
        Assert.AreEqual(row.Waves, plan.Waves, "웨이브 수");
        Assert.AreEqual(row.Floor == 46, plan.IsFinal, "마지막 층 여부");

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
    public void TotalWaves_ThroughFinalFloor_Is281()
    {
        int total = 0;
        for (int f = 1; f <= config.finalFloor; f++)
            total += LevelRules.GetWaveCount(config, f);
        Assert.AreEqual(281, total);
    }

    [Test]
    public void OnlyFinalFloor_IsFinal()
    {
        for (int f = 1; f <= config.finalFloor; f++)
            Assert.AreEqual(f == 46, LevelRules.IsFinalFloor(config, f), $"{f}층");
    }

    [Test]
    public void WaveComposition_IsNormalMonsters_OnePerInterval()
    {
        var buffer = new WaveSpawn[4];
        int lines = LevelRules.GetWaveComposition(config, 6, 3, 13, buffer);

        Assert.AreEqual(1, lines);
        Assert.AreEqual("normal", buffer[0].MonsterId);
        Assert.AreEqual(22, buffer[0].Count);
        Assert.AreEqual(1f, buffer[0].IntervalSec);
        Assert.AreEqual(0f, buffer[0].DelaySec);
    }
}
