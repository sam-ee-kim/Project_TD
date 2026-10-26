using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace TowerDefence.Tests
{
    // 화염방사 범위 공격: 조준 방향 90° 부채꼴(길이 coneLength) 히트박스 안의 몬스터 모두에게 피해.
    public class ConeAttackTests
    {
        // ---------- 히트박스 판정 ----------

        static bool In(float angleDeg, float dist) =>
            TowerManager.InCone(Quaternion.Euler(0f, angleDeg, 0f) * Vector3.forward * dist, Vector3.forward, 1.6f, 45f, 0.25f);

        [Test]
        public void InCone_FanShape()
        {
            Assert.IsTrue(In(0f, 1f), "정면");
            Assert.IsTrue(In(40f, 1.5f), "부채꼴 안 (반각 45°)");
            Assert.IsTrue(In(-40f, 1.5f), "반대쪽도");
            Assert.IsTrue(In(0f, 1.8f), "끝에 몸통이 걸침 (1.6 + 0.25)");
            Assert.IsFalse(In(0f, 1.9f), "불길 끝 너머");
            Assert.IsFalse(In(70f, 1.5f), "부채꼴 밖 (옆)");
            Assert.IsFalse(In(180f, 0.8f), "뒤");
            Assert.IsTrue(In(120f, 0.2f), "타워에 겹쳐 있으면 맞음");
        }

        [Test]
        public void InCone_IgnoresHeight()
        {
            Assert.IsTrue(TowerManager.InCone(new Vector3(0f, 5f, 1f), Vector3.forward, 1.6f, 45f, 0.25f), "바닥 평면으로만 판정");
        }

        // ---------- 실제 발사 ----------

        GameConfig config;
        Board board;
        MonsterManager monsters;
        TowerManager towers;
        PlayerState player;
        TowerDef flame;

        [SetUp]
        public void SetUp()
        {
            config = AssetDatabase.LoadAssetAtPath<GameConfig>("Assets/Data/GameConfig.asset");
            flame = AssetDatabase.LoadAssetAtPath<TowerDef>("Assets/Data/Towers/flamethrower.asset");
            if (flame == null || flame.attackType != AttackType.Cone)
                Assert.Ignore("flamethrower 에셋이 없거나 범위 공격이 아님");
            var data = new GameData();
            data.Init(config, AssetDatabase.LoadAssetAtPath<MapGenRules>("Assets/Data/MapGenRules.asset"),
                new[] { AssetDatabase.LoadAssetAtPath<MonsterDef>("Assets/Data/Monsters/normal.asset") }, new[] { flame });
            board = new Board();
            monsters = new MonsterManager();
            towers = new TowerManager();
            player = new PlayerState();
            board.Init(data);
            player.Init(data);
            monsters.Init(data, board);
            towers.Init(data, board, monsters, player); // 표시 없음: 판정만
            towers.TowerChanged += board.HandleTowerChanged;
            var run = RunState.CreateNew(config);
            run.Cash = 10000;
            board.ResetForNewRun(run);
            player.ResetForNewRun(run);
            monsters.ResetForNewRun(run);
            towers.ResetForNewRun(run);
            board.Load(new FixedTestMapGenerator().Generate(default));
            monsters.HandleStateChanged(new StateChange(GameState.Wave, 1, 1));
        }

        [Test]
        public void Fire_DamagesEveryMonsterInsideFan_Only()
        {
            // 몬스터를 경로를 따라 0.25초 간격으로 늘어놓는다
            monsters.BeginWave(new[] { new WaveSpawn("normal", 12, 0.25f, 0f) }, 1);
            for (int i = 0; i < 40; i++)
            {
                monsters.TickSpawn(0.25f);
                monsters.TickMove(0.25f);
            }

            // 몬스터가 가장 많이 몰린 경로 근처 부지에 화염방사 타워
            var site = BestSite();
            Assert.IsTrue(towers.Build(site, flame));
            var tower = towers.GetTowerAt(site);

            var before = new Dictionary<MonsterInstance, float>();
            foreach (var m in AliveMonsters())
                before[m] = m.Hp;
            Assert.Greater(before.Count, 2);

            towers.TickCombat(0.01f); // 즉시 1발
            Assert.IsFalse(tower.Locked.IsNone, "대상을 잡고 발사");

            var center = board.CellToWorld(site);
            var forward = monsters.GetGroundPoint(monsters.Get(tower.Locked)) - center;
            forward.y = 0f;
            forward.Normalize();
            int hit = 0, missed = 0;
            foreach (var pair in before)
            {
                var m = pair.Key;
                bool inside = TowerManager.InCone(monsters.GetGroundPoint(m) - center, forward, flame.coneLength, flame.coneAngle * 0.5f, TowerManager.ConeHitRadius);
                if (inside)
                {
                    Assert.AreEqual(pair.Value - flame.damage, m.Hp, "부채꼴 안: 피해");
                    hit++;
                }
                else
                {
                    Assert.AreEqual(pair.Value, m.Hp, "부채꼴 밖: 피해 없음");
                    missed++;
                }
            }
            Assert.Greater(hit, 1, "범위 공격: 2마리 이상 맞음");
            TestContext.WriteLine($"맞음 {hit}, 안 맞음 {missed}");
        }

        [Test]
        public void Fire_KillsSeveralAtOnce_WithoutSkippingOthers()
        {
            monsters.DebugHpOverride = 1; // 한 번에 여러 마리가 죽어도 칸 목록을 끝까지 돈다
            monsters.BeginWave(new[] { new WaveSpawn("normal", 12, 0.25f, 0f) }, 1);
            for (int i = 0; i < 40; i++)
            {
                monsters.TickSpawn(0.25f);
                monsters.TickMove(0.25f);
            }
            var site = BestSite();
            towers.Build(site, flame);
            var tower = towers.GetTowerAt(site);

            // 대상 = 사거리 칸 안에서 경로를 가장 많이 간 몬스터 (TargetRule.First) → 발사 전에 부채꼴을 미리 계산
            MonsterInstance target = null;
            foreach (var m in AliveMonsters())
                if (tower.CoversCell[m.CellId] && (target == null || m.Remaining < target.Remaining))
                    target = m;
            Assert.IsNotNull(target);
            var center = board.CellToWorld(site);
            var forward = monsters.GetGroundPoint(target) - center;
            forward.y = 0f;
            forward.Normalize();
            var inside = new List<MonsterInstance>();
            var outside = new List<MonsterInstance>();
            foreach (var m in AliveMonsters())
                (TowerManager.InCone(monsters.GetGroundPoint(m) - center, forward, flame.coneLength, flame.coneAngle * 0.5f, TowerManager.ConeHitRadius) ? inside : outside).Add(m);
            Assert.Greater(inside.Count, 1, "부채꼴 안에 2마리 이상");

            towers.TickCombat(0.01f);                  // 1틱 0.5
            towers.TickCombat(flame.interval + 0.001f); // 2틱 0.5 → 체력 1 처치
            foreach (var m in inside)
                Assert.AreNotEqual(MonsterState.Alive, m.State, "부채꼴 안은 한 번에 모두 처치 (칸 목록을 건너뛰지 않음)");
            foreach (var m in outside)
                Assert.AreEqual(MonsterState.Alive, m.State, "부채꼴 밖은 그대로");
        }

        List<MonsterInstance> AliveMonsters()
        {
            var list = new List<MonsterInstance>();
            for (int i = 0; i < board.GetPathLength(0); i++)
                foreach (var h in board.MonstersAtPath(i))
                {
                    var m = monsters.Get(h);
                    if (m != null && m.State == MonsterState.Alive)
                        list.Add(m);
                }
            return list;
        }

        // 1.6칸 안에 살아 있는 몬스터가 가장 많은 부지
        Vector2Int BestSite()
        {
            var best = default(Vector2Int);
            int bestCount = -1;
            var alive = AliveMonsters();
            for (int x = 0; x < board.N; x++)
                for (int y = 0; y < board.N; y++)
                {
                    var cell = new Vector2Int(x, y);
                    if (!board.IsBuildable(cell))
                        continue;
                    int count = 0;
                    foreach (var m in alive)
                        if ((monsters.GetGroundPoint(m) - board.CellToWorld(cell)).magnitude <= flame.coneLength)
                            count++;
                    if (count > bestCount)
                    {
                        bestCount = count;
                        best = cell;
                    }
                }
            return best;
        }
    }
}
