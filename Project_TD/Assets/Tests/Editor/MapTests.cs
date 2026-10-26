using System;
using System.Diagnostics;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

// namespace 안에 두어 TileType이 기존 생성기의 전역 TileType이 아니라 TowerDefence.TileType을 가리키게 한다.
namespace TowerDefence.Tests
{
// 맵 연결(작업 3): 생성기 어댑터, 검사기 어댑터, 고정 맵, Board.
public class MapTests
{
    MapGenRules rules;
    GameConfig config;
    readonly LayoutMapGenerator generator = new LayoutMapGenerator();
    readonly LayoutMapValidator validator = new LayoutMapValidator();

    static readonly int[] Sizes = { 7, 9, 11, 13, 15, 17, 21, 27, 37 };

    [OneTimeSetUp]
    public void Load()
    {
        rules = AssetDatabase.LoadAssetAtPath<MapGenRules>("Assets/Data/MapGenRules.asset");
        config = AssetDatabase.LoadAssetAtPath<GameConfig>("Assets/Data/GameConfig.asset");
        Assert.IsNotNull(rules);
        Assert.IsNotNull(config);
    }

    MapData Gen(int n, int seed) => generator.Generate(new MapSpec(n, 1, seed, rules));

    [Test]
    public void SameSeed_GivesSameMap([ValueSource(nameof(Sizes))] int n)
    {
        for (int seed = 0; seed < 20; seed++)
        {
            var a = Gen(n, seed);
            var b = Gen(n, seed);
            CollectionAssert.AreEqual(a.Tiles, b.Tiles, $"N={n} seed={seed} Tiles");
            CollectionAssert.AreEqual(a.Sites, b.Sites, $"N={n} seed={seed} Sites");
            CollectionAssert.AreEqual(a.MonsterPaths[0], b.MonsterPaths[0], $"N={n} seed={seed} 경로");
            Assert.AreEqual(seed, a.Seed);
        }
    }

    [Test]
    public void DifferentSeeds_GiveDifferentMaps()
    {
        int distinct = 0;
        var first = Gen(11, 1);
        for (int seed = 2; seed < 22; seed++)
            if (!SamePath(first, Gen(11, seed)))
                distinct++;
        Assert.Greater(distinct, 15, "시드가 달라도 맵이 거의 같음");
    }

    [Test]
    public void GeneratedMaps_PassValidator_AndConvertCorrectly([ValueSource(nameof(Sizes))] int n)
    {
        for (int seed = 0; seed < 50; seed++)
        {
            var map = Gen(n, seed);
            Assert.IsNotNull(map, $"N={n} seed={seed} 생성 실패");
            AssertWellFormed(map, n);
            var report = validator.Validate(map, rules);
            Assert.IsTrue(report.IsValid, $"N={n} seed={seed}: {(report.IsValid ? "" : report.Violations[0].RuleId + " " + report.Violations[0].Detail)}");
        }
    }

    [Test]
    public void FixedTestMap_Is7x7_AndPassesValidator()
    {
        var map = new FixedTestMapGenerator().Generate(default);
        Assert.AreEqual(7, map.N);
        AssertWellFormed(map, 7);
        Assert.IsTrue(validator.Validate(map, rules).IsValid);
        Assert.AreEqual(SiteGrade.TStar, map.Sites[4, 1]);
        Assert.AreEqual(SiteGrade.TStar, map.Sites[4, 3]);
    }

    [Test]
    public void Validator_ReportsBrokenMap()
    {
        var map = Gen(9, 3);
        map.MonsterPaths[0].RemoveAt(map.MonsterPaths[0].Count / 2); // 경로를 끊는다
        Assert.IsFalse(validator.Validate(map, rules).IsValid);
    }

    [Test]
    public void Generator_RunsOnBackgroundThread()
    {
        var task = Task.Run(() => Gen(37, 12345));
        Assert.IsTrue(task.Wait(5000), "백그라운드 생성 시간 초과");
        Assert.IsNotNull(task.Result);
        Assert.IsTrue(SamePath(task.Result, Gen(37, 12345)), "스레드에서 만든 맵이 메인 스레드 결과와 다름");
    }

    [Test]
    public void Board_LoadQueriesAndOccupancy()
    {
        var data = new GameData();
        data.Init(config, rules, new MonsterDef[0], new TowerDef[0]);
        var board = new Board();
        board.Init(data);
        board.ResetForNewRun(new RunState());
        var map = new FixedTestMapGenerator().Generate(default);
        board.Load(map);

        Assert.AreEqual(7, board.N);
        Assert.AreEqual(TileType.Spawn, board.GetTile(new Vector2Int(3, 0)));
        Assert.AreEqual(TileType.Exit, board.GetTile(new Vector2Int(5, 6)));
        Assert.AreEqual(TileType.Empty, board.GetTile(new Vector2Int(-1, 0)));
        Assert.AreEqual(9, board.GetPathLength(0));
        Assert.AreEqual(new Vector2Int(4, 2), board.GetPathCell(0, 3));
        Assert.IsTrue(board.IsPathCell(new Vector2Int(4, 2)));
        Assert.IsFalse(board.IsPathCell(new Vector2Int(0, 0)));
        Assert.AreEqual(4 * 7 + 2, board.CellId(new Vector2Int(4, 2)), "칸 번호 = x × N + y");

        var site = new Vector2Int(2, 0);
        Assert.IsTrue(board.IsBuildable(site));
        Assert.IsFalse(board.IsBuildable(new Vector2Int(3, 1)), "경로에는 설치 불가");
        Assert.IsFalse(board.IsBuildable(new Vector2Int(0, 0)), "부지가 아닌 칸은 설치 불가");
        board.Occupy(site);
        Assert.IsFalse(board.IsBuildable(site));
        board.Release(site);
        Assert.IsTrue(board.IsBuildable(site));

        // 3D 바닥 좌표: 7×7이면 칸 (x, y) 중심 = (x − 3, 0, y − 3)
        Assert.AreEqual(new Vector3(-1f, 0f, -3f), board.CellToWorld(site));
        Assert.AreEqual(Vector3.zero, board.CellToWorld(new Vector2Int(3, 3)), "맵 중앙 = 원점");
        Assert.IsTrue(board.TryWorldToCell(new Vector3(-0.6f, 0f, -2.8f), out var cell));
        Assert.AreEqual(site, cell);
        Assert.IsFalse(board.TryWorldToCell(new Vector3(3.6f, 0f, 0f), out _), "맵 밖");
    }

    [Test]
    public void Board_CellBuckets_AddRemoveWithoutAllocation()
    {
        var data = new GameData();
        data.Init(config, rules, new MonsterDef[0], new TowerDef[0]);
        var board = new Board();
        board.Init(data);
        board.ResetForNewRun(new RunState());
        board.Load(Gen(37, 7));

        // JIT·첫 호출 비용 제외용 예열
        board.AddToCell(0, new MonsterHandle(0, 0));
        board.RemoveFromCell(0, new MonsterHandle(0, 0));

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int round = 0; round < 10; round++)
        {
            for (int i = 0; i < 290; i++)
                board.AddToCell(board.PathCellId(i % board.GetPathLength(0)), new MonsterHandle(i, round));
            for (int i = 0; i < 290; i++)
                board.RemoveFromCell(board.PathCellId(i % board.GetPathLength(0)), new MonsterHandle(i, round));
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.AreEqual(0, allocated, "칸별 몬스터 목록 추가·제거에서 할당 발생");
        for (int i = 0; i < board.GetPathLength(0); i++)
            Assert.AreEqual(0, board.MonstersAtPath(i).Count);
    }

    [Test]
    public void Measure_GenerationTime_37x37()
    {
        Gen(37, 0); // 예열
        const int runs = 50;
        double total = 0, max = 0;
        var sw = new Stopwatch();
        for (int seed = 1; seed <= runs; seed++)
        {
            sw.Restart();
            var map = Gen(37, seed);
            validator.Validate(map, rules);
            sw.Stop();
            double ms = sw.Elapsed.TotalMilliseconds;
            total += ms;
            max = Math.Max(max, ms);
        }
        string result = $"37×37 생성+검사 {runs}회: 평균 {total / runs:F3}ms, 최대 {max:F3}ms";
        UnityEngine.Debug.Log("[MapTests] " + result);
        TestContext.WriteLine(result);
        Assert.Less(max, config.performance.mapGenFrameBudgetMs * 50, "생성이 지나치게 느림");
    }

    static bool SamePath(MapData a, MapData b)
    {
        var pa = a.MonsterPaths[0];
        var pb = b.MonsterPaths[0];
        if (pa.Count != pb.Count)
            return false;
        for (int i = 0; i < pa.Count; i++)
            if (pa[i] != pb[i])
                return false;
        return true;
    }

    static void AssertWellFormed(MapData map, int n)
    {
        Assert.AreEqual(n, map.N);
        Assert.AreEqual(1, map.Spawns.Length);
        var path = map.MonsterPaths[0];
        Assert.AreEqual(map.Spawns[0], path[0], "경로 첫 칸 = 스폰");
        Assert.AreEqual(map.Exit, path[path.Count - 1], "경로 마지막 칸 = 출구");
        Assert.AreEqual(TileType.Spawn, map.Tiles[map.Spawns[0].x, map.Spawns[0].y]);
        Assert.AreEqual(TileType.Exit, map.Tiles[map.Exit.x, map.Exit.y]);
        for (int i = 1; i < path.Count - 1; i++)
            Assert.AreEqual(TileType.Path, map.Tiles[path[i].x, path[i].y]);

        for (int x = 0; x < n; x++)
        {
            for (int y = 0; y < n; y++)
            {
                bool site = map.Tiles[x, y] == TileType.BuildSite;
                var grade = map.Sites[x, y];
                Assert.AreEqual(site, grade != SiteGrade.None, $"({x},{y}) 부지와 등급 불일치");
                Assert.AreNotEqual(SiteGrade.T2, grade, "정본 생성기는 T2를 만들지 않음");
            }
        }
    }
}
}
