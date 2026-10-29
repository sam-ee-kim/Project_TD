using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace TowerDefence.Tests
{
    // 복잡성 가중치 맵 생성 (꺾임 수·스타트 지점 1~3개)과 여러 경로에서의 게임 동작.
    public class MultiPathTests
    {
        static readonly int[] SizeOf = { 0, 7, 9, 11, 13, 15 }; // 복잡성 → 맵 크기 (진행 표)

        MapGenRules rules;
        GameConfig config;
        LayoutMapGenerator generator;

        [SetUp]
        public void SetUp()
        {
            rules = AssetDatabase.LoadAssetAtPath<MapGenRules>("Assets/Data/MapGenRules.asset");
            config = AssetDatabase.LoadAssetAtPath<GameConfig>("Assets/Data/GameConfig.asset");
            generator = new LayoutMapGenerator();
        }

        MapData Gen(int complexity, int seed) => generator.Generate(new MapSpec(SizeOf[complexity], complexity, seed, rules));

        static int Turns(List<Vector2Int> path)
        {
            int turns = 0;
            for (int i = 2; i < path.Count; i++)
                if (path[i] - path[i - 1] != path[i - 1] - path[i - 2])
                    turns++;
            return turns;
        }

        // ---------- 생성 ----------

        [Test]
        public void Generate_EveryComplexity_FollowsTable([NUnit.Framework.Range(1, 5)] int complexity)
        {
            var level = rules.GetLevel(complexity);
            var validator = new LayoutMapValidator();
            for (int seed = 1; seed <= 50; seed++)
            {
                var map = Gen(complexity, seed);
                Assert.IsNotNull(map, $"복잡성 {complexity} seed {seed} 생성 실패");
                var report = validator.Validate(map, rules);
                Assert.IsTrue(report.IsValid, $"복잡성 {complexity} seed {seed}: {(report.IsValid ? "" : report.Violations[0].Detail)}");

                Assert.AreEqual(level.spawns, map.Spawns.Length, "스타트 지점 수");
                Assert.AreEqual(level.spawns, map.MonsterPaths.Count, "스타트 지점마다 경로");
                Assert.AreEqual(level.spawns - 1, map.Junctions.Count, "지선마다 합류 지점");
                int turns = Turns(map.MonsterPaths[0]);
                Assert.That(turns, Is.InRange(level.mainTurnsMin, level.mainTurnsMax), $"본선 꺾임 seed {seed}");

                for (int k = 0; k < map.MonsterPaths.Count; k++)
                {
                    var path = map.MonsterPaths[k];
                    Assert.AreEqual(map.Spawns[k], path[0], "경로 첫 칸 = 그 스타트 지점");
                    Assert.AreEqual(map.Exit, path[path.Count - 1], "모든 경로는 같은 출구로");
                    Assert.AreEqual(TileType.Spawn, map.Tiles[path[0].x, path[0].y]);
                }
                foreach (var j in map.Junctions)
                    CollectionAssert.Contains(map.MonsterPaths[0], j, "합류 지점은 본선 위");
            }
        }

        // 넥서스는 스타트 지점이 몰린 변의 반대쪽. 상하·좌우 비율이 같으면 본선이 최대 길이(2N−3칸)로 돌아가고
        // 모든 스타트 지점이 넥서스에서 N−1칸 이상 떨어진다.
        [Test]
        public void Nexus_OppositeSpawnMajority_OrMaxDetourWhenBalanced([NUnit.Framework.Range(1, 5)] int complexity)
        {
            int n = SizeOf[complexity], balanced = 0;
            for (int seed = 0; seed < 100; seed++)
            {
                var map = Gen(complexity, 300 + seed);
                Assert.IsNotNull(map);
                int vx = 0, vy = 0;
                foreach (var sp in map.Spawns)
                {
                    if (sp.x == 0) vx++; else if (sp.x == n - 1) vx--;
                    else if (sp.y == 0) vy++; else if (sp.y == n - 1) vy--;
                }
                var exit = map.Exit;
                if (System.Math.Abs(vx) != System.Math.Abs(vy))
                {
                    bool opposite = System.Math.Abs(vx) > System.Math.Abs(vy)
                        ? (vx > 0 ? exit.x == n - 1 : exit.x == 0)
                        : (vy > 0 ? exit.y == n - 1 : exit.y == 0);
                    Assert.IsTrue(opposite, $"복잡성 {complexity} 시드 {seed}: 넥서스 {exit}가 스타트 지점 쪽 반대가 아님");
                    continue;
                }
                balanced++;
                Assert.GreaterOrEqual(map.MonsterPaths[0].Count, 2 * n - 3, $"복잡성 {complexity} 시드 {seed}: 비슷한 비율인데 최대로 돌아가지 않음");
                foreach (var sp in map.Spawns)
                    Assert.GreaterOrEqual(System.Math.Abs(sp.x - exit.x) + System.Math.Abs(sp.y - exit.y), n - 1, $"스타트 지점 {sp}가 넥서스와 가까움");
            }
            TestContext.WriteLine($"복잡성 {complexity}: 비슷한 비율(최대 우회) {balanced}/100");
        }

        [Test]
        public void Generate_SameSeed_SameMap()
        {
            for (int c = 1; c <= 5; c++)
            {
                var a = Gen(c, 77);
                var b = Gen(c, 77);
                CollectionAssert.AreEqual(a.Tiles, b.Tiles, $"복잡성 {c}");
                for (int k = 0; k < a.MonsterPaths.Count; k++)
                    CollectionAssert.AreEqual(a.MonsterPaths[k], b.MonsterPaths[k]);
            }
        }

        [Test]
        public void Complexity_AddsSpawns_At2And4()
        {
            Assert.AreEqual(new[] { 1, 2, 2, 3, 3 },
                new[] { rules.GetLevel(1).spawns, rules.GetLevel(2).spawns, rules.GetLevel(3).spawns, rules.GetLevel(4).spawns, rules.GetLevel(5).spawns });
        }

        // ---------- 게임 동작 ----------

        Board board;
        MonsterManager monsters;
        TowerManager towers;
        PlayerState player;
        TowerDef basic;

        void SetUpGame(MapData map)
        {
            basic = AssetDatabase.LoadAssetAtPath<TowerDef>("Assets/Data/Towers/basic.asset");
            var normal = TestAssets.NoIdleCopy("Assets/Data/Monsters/normal.asset");
            var data = new GameData();
            data.Init(config, rules, new[] { normal }, new[] { basic });
            board = new Board();
            monsters = new MonsterManager();
            towers = new TowerManager();
            player = new PlayerState();
            board.Init(data);
            player.Init(data);
            monsters.Init(data, board);
            towers.Init(data, board, monsters, player);
            towers.TowerChanged += board.HandleTowerChanged;
            var run = RunState.CreateNew(config);
            run.Cash = 100000;
            board.ResetForNewRun(run);
            player.ResetForNewRun(run);
            monsters.ResetForNewRun(run);
            towers.ResetForNewRun(run);
            board.Load(map);
            monsters.HandleStateChanged(new StateChange(GameState.Wave, 1, 1));
        }

        List<MonsterInstance> Alive()
        {
            var list = new List<MonsterInstance>();
            var seen = new HashSet<int>();
            for (int k = 0; k < board.PathCount; k++)
                for (int i = 0; i < board.GetPathLength(k); i++)
                    foreach (var h in board.MonstersAt(board.PathCellId(i, k)))
                    {
                        var m = monsters.Get(h);
                        if (m != null && m.State == MonsterState.Alive && seen.Add(m.Index))
                            list.Add(m);
                    }
            return list;
        }

        [Test]
        public void Spawns_Alternate_BetweenStartPoints()
        {
            var map = Gen(4, 3); // 스타트 지점 3개
            SetUpGame(map);
            monsters.BeginWave(new[] { new WaveSpawn("normal", 6, 0.1f, 0f) }, 1);
            monsters.TickSpawn(10f); // 6마리 모두 스폰
            var perSpawn = new int[3];
            foreach (var m in Alive())
            {
                perSpawn[m.PathId]++;
                Assert.AreEqual(map.Spawns[m.PathId], board.GetPathCell(m.PathId, 0));
            }
            CollectionAssert.AreEqual(new[] { 2, 2, 2 }, perSpawn, "스타트 지점 3개에 번갈아 2마리씩");
        }

        [Test]
        public void MonstersFromDifferentPaths_ShareCellBucket_AfterJunction()
        {
            var map = Gen(2, 5);
            SetUpGame(map);
            monsters.BeginWave(new[] { new WaveSpawn("normal", 40, 0.25f, 0f) }, 1);
            bool shared = false;
            for (int step = 0; step < 400 && !shared; step++)
            {
                monsters.TickSpawn(0.05f);
                monsters.TickMove(0.05f);
                monsters.Cleanup();
                foreach (var cell in map.MonsterPaths[0])
                {
                    var bucket = board.MonstersAt(board.CellId(cell));
                    var ids = new HashSet<int>();
                    foreach (var h in bucket)
                        ids.Add(monsters.Get(h).PathId);
                    if (ids.Count >= 2)
                        shared = true;
                }
            }
            Assert.IsTrue(shared, "합류 뒤 본선 칸에 서로 다른 스타트 지점 몬스터가 함께 담김");
        }

        [Test]
        public void Tower_CoversBranchAndMainCells_TargetsClosestToExit()
        {
            var map = Gen(2, 5);
            SetUpGame(map);
            var junction = map.Junctions[0];
            var branchCells = new HashSet<Vector2Int>(map.MonsterPaths[1]);
            branchCells.ExceptWith(map.MonsterPaths[0]);

            // 합류 지점 근처 부지 (사거리 안에 지선 칸과 본선 칸이 모두 있는 곳)
            Vector2Int site = default;
            bool found = false;
            for (int x = 0; x < map.N && !found; x++)
                for (int y = 0; y < map.N && !found; y++)
                {
                    var c = new Vector2Int(x, y);
                    if (board.IsBuildable(c) && Mathf.Max(Mathf.Abs(c.x - junction.x), Mathf.Abs(c.y - junction.y)) <= 1)
                    {
                        site = c;
                        found = true;
                    }
                }
            Assert.IsTrue(found, "합류 지점 옆 부지");
            Assert.IsTrue(towers.Build(site, basic));
            var tower = towers.GetTowerAt(site);

            bool coversBranch = false, coversMainAfter = false;
            foreach (int id in tower.Covered)
            {
                var cell = new Vector2Int(id / map.N, id % map.N);
                if (branchCells.Contains(cell)) coversBranch = true;
                int idx = map.MonsterPaths[0].IndexOf(cell);
                if (idx > map.MonsterPaths[0].IndexOf(junction)) coversMainAfter = true;
            }
            Assert.IsTrue(coversBranch, "지선 칸도 사거리에 포함");
            Assert.IsTrue(coversMainAfter, "합류 뒤 본선 칸도 포함");

            // 두 경로에서 몬스터를 흘려 보낸 뒤 대상 = 사거리 안에서 출구까지 가장 적게 남은 몬스터
            // (사거리는 타워가 바라보는 쪽 앞 칸만이라, 몬스터가 그 칸들에 2마리 이상 들어올 때까지 흘려 보낸다)
            monsters.BeginWave(new[] { new WaveSpawn("normal", 30, 0.2f, 0f) }, 1);
            var inRange = new List<MonsterInstance>();
            for (int step = 0; step < 400 && inRange.Count < 2; step++)
            {
                monsters.TickSpawn(0.05f);
                monsters.TickMove(0.05f);
                inRange = Alive().FindAll(m => tower.CoversCell[m.CellId]);
            }
            Assume.That(inRange.Count, Is.GreaterThan(1), "사거리 안 몬스터 2마리 이상일 때만 의미 있음");
            towers.TickCombat(0f);
            var target = monsters.Get(tower.Locked);
            Assert.IsNotNull(target);
            foreach (var m in inRange)
                Assert.LessOrEqual(target.Remaining, m.Remaining + 1e-4f, "First = 출구에 가장 가까운 몬스터 (경로가 달라도)");
        }
    }
}
