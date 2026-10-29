using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace TowerDefence.Tests
{
    // 타워 방향: 공격은 바라보는 쪽 앞 칸만 (바로 옆 줄 제외), 제자리 회전, 플라즈마 일직선 (헤드 고정).
    // 고정 7×7 맵 경로: 0(3,0) 1(3,1) 2(3,2) 3(4,2) 4(5,2) 5(5,3) 6(5,4) 7(5,5) 8(5,6)
    public class TowerFacingTests
    {
        GameConfig config;
        GameData data;
        Board board;
        MonsterManager monsters;
        TowerManager towers;
        PlayerState player;
        TowerDef basic;
        TowerDef line;

        [SetUp]
        public void SetUp()
        {
            config = AssetDatabase.LoadAssetAtPath<GameConfig>("Assets/Data/GameConfig.asset");
            basic = ScriptableObject.CreateInstance<TowerDef>();
            basic.id = "basicTest";
            basic.buildCost = 10;
            basic.damage = 1f;
            basic.interval = 1f;
            basic.range = 2;
            basic.attackType = AttackType.Hitscan;
            line = ScriptableObject.CreateInstance<TowerDef>();
            line.id = "lineTest";
            line.buildCost = 10;
            line.damage = 1f;
            line.interval = 0.2f;
            line.range = 3;
            line.attackType = AttackType.Line;

            data = new GameData();
            data.Init(config, AssetDatabase.LoadAssetAtPath<MapGenRules>("Assets/Data/MapGenRules.asset"),
                new[] { TestAssets.NoIdleCopy("Assets/Data/Monsters/normal.asset") }, new[] { basic, line });
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
            board.ResetForNewRun(run);
            player.ResetForNewRun(run);
            monsters.ResetForNewRun(run);
            towers.ResetForNewRun(run);
            board.Load(new FixedTestMapGenerator().Generate(default));
            player.Earn(1000, CashReason.Kill);
            monsters.DebugHpOverride = 1000;
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(basic);
            Object.DestroyImmediate(line);
        }

        HashSet<Vector2Int> CoveredCells(TowerInstance t)
        {
            var set = new HashSet<Vector2Int>();
            foreach (int id in t.Covered)
                set.Add(new Vector2Int(id / board.N, id % board.N));
            return set;
        }

        [Test]
        public void SquareRange_CoversOnlyCellsInFront()
        {
            var site = new Vector2Int(2, 0);
            Assert.IsTrue(towers.Build(site, basic, 1)); // +x (경로 쪽)
            var t = towers.GetTowerAt(site);
            CollectionAssert.AreEquivalent(
                new[] { new Vector2Int(3, 0), new Vector2Int(3, 1), new Vector2Int(3, 2), new Vector2Int(4, 2) }, CoveredCells(t));

            Assert.IsTrue(towers.Sell(t));
            Assert.IsTrue(towers.Build(site, basic, 3)); // −x (경로 반대쪽): 공격할 칸 없음
            Assert.AreEqual(0, towers.GetTowerAt(site).Covered.Length);
        }

        [Test]
        public void SideRow_IsExcluded_AndBestFacingCoversMostPath()
        {
            var site = new Vector2Int(2, 0);
            Assert.AreEqual(1, towers.BestFacing(basic, site), "+x가 경로 칸 4개로 가장 많다");
            Assert.IsTrue(towers.Build(site, basic, 0)); // +y: 같은 줄 (3,0)은 바로 옆이라 제외
            var cells = CoveredCells(towers.GetTowerAt(site));
            Assert.IsFalse(cells.Contains(new Vector2Int(3, 0)), "바로 옆 줄은 180° 밖");
            Assert.IsTrue(cells.Contains(new Vector2Int(3, 1)));
        }

        [Test]
        public void MoveInPlace_RotatesAndRecomputesRange()
        {
            var site = new Vector2Int(2, 0);
            Assert.IsTrue(towers.Build(site, basic, 3));
            var t = towers.GetTowerAt(site);
            Assert.AreEqual(0, t.Covered.Length);
            Assert.IsFalse(towers.Move(t, site, 3), "같은 칸·같은 방향이면 바뀌는 것 없음");
            Assert.IsTrue(towers.Move(t, site, 1), "제자리에서 방향만 바꾼다");
            Assert.AreEqual(1, t.Facing);
            Assert.AreEqual(4, t.Covered.Length);
            Assert.IsTrue(board.IsOccupied(site), "점유는 그대로");
        }

        [Test]
        public void LineTower_CoversStraightCells_AndHitsEveryMonsterOnThem()
        {
            var site = new Vector2Int(2, 2);
            Assert.IsTrue(towers.Build(site, line, 1)); // +x: (3,2) (4,2) (5,2)
            var t = towers.GetTowerAt(site);
            CollectionAssert.AreEquivalent(
                new[] { new Vector2Int(3, 2), new Vector2Int(4, 2), new Vector2Int(5, 2) }, CoveredCells(t));

            // 몬스터를 여러 마리 흘려 보내 줄 위에 2마리 이상 올라올 때 1번 쏜다
            monsters.BeginWave(new[] { new WaveSpawn("normal", 12, 0.3f, 0f) }, 1);
            var onLine = new List<MonsterInstance>();
            var all = new List<MonsterInstance>();
            for (int step = 0; step < 400 && onLine.Count < 2; step++)
            {
                monsters.TickSpawn(0.05f);
                monsters.TickMove(0.05f);
                onLine.Clear();
                all.Clear();
                for (int pathIndex = 0; pathIndex < board.GetPathLength(0); pathIndex++)
                    foreach (var h in board.MonstersAt(board.CellId(board.GetPathCell(0, pathIndex))))
                    {
                        var m = monsters.Get(h);
                        all.Add(m);
                        if (t.CoversCell[m.CellId])
                            onLine.Add(m);
                    }
            }
            Assert.GreaterOrEqual(onLine.Count, 2, "줄 위에 몬스터 2마리 이상");
            var hp = new Dictionary<MonsterInstance, float>();
            foreach (var m in all)
                hp[m] = m.Hp;

            towers.TickCombat(0f);

            foreach (var m in all)
            {
                if (onLine.Contains(m))
                    Assert.AreEqual(hp[m] - 1f, m.Hp, 1e-4f, "줄 위 몬스터는 모두 맞는다");
                else
                    Assert.AreEqual(hp[m], m.Hp, 1e-4f, "줄 밖은 안 맞는다");
            }
        }
    }
}
