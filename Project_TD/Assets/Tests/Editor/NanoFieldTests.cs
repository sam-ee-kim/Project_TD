using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace TowerDefence.Tests
{
    // 나노봇 타워: 연발 7발, 첫 발이 맞은 자리에 감속 나노필드. 피해는 탄이 맞은 몬스터만.
    // 고정 7×7 맵 경로: 0(3,0) 1(3,1) 2(3,2) 3(4,2) 4(5,2) 5(5,3) 6(5,4) 7(5,5) 8(5,6)
    public class NanoFieldTests
    {
        GameConfig config;
        GameData data;
        Board board;
        MonsterManager monsters;
        TowerManager towers;
        PlayerState player;
        TowerDef nano;

        static readonly Vector2Int Site = new Vector2Int(2, 0);

        [SetUp]
        public void SetUp()
        {
            config = AssetDatabase.LoadAssetAtPath<GameConfig>("Assets/Data/GameConfig.asset");
            nano = ScriptableObject.CreateInstance<TowerDef>();
            nano.id = "nanoTest";
            nano.buildCost = 50;
            nano.damage = 0.3f;
            nano.interval = 2.3f;
            nano.range = 2;
            nano.burstCount = 7;
            nano.burstGap = 0.045f;
            nano.attackType = AttackType.Projectile;
            nano.projectileSpeed = 9f;
            nano.projectileTurnSpeed = 1080f;
            nano.fieldRadius = 1f;
            nano.fieldDuration = 3f;
            nano.fieldSlow = 0.6f;

            data = new GameData();
            data.Init(config, AssetDatabase.LoadAssetAtPath<MapGenRules>("Assets/Data/MapGenRules.asset"),
                new[] { TestAssets.NoIdleCopy("Assets/Data/Monsters/normal.asset") }, new[] { nano });
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
            monsters.HandleStateChanged(new StateChange(GameState.Wave, 1, 1));
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(nano);

        MonsterInstance SpawnOne()
        {
            monsters.BeginWave(new[] { new WaveSpawn("normal", 1, 0f, 0f) }, 1);
            monsters.TickSpawn(0f);
            var cell = board.MonstersAtPath(0);
            return monsters.Get(cell[cell.Count - 1]);
        }

        [Test]
        public void SlowField_InsideMovesAt40Percent_ExpiresAfterDuration()
        {
            var m = SpawnOne();
            monsters.AddSlowField(monsters.GetGroundPoint(m), 1f, 0.6f, 3f);
            Assert.AreEqual(0.4f, monsters.GetSpeedMultiplier(m), 1e-5f, "60% 감속 → 40% 속도");
            monsters.TickMove(0.5f);
            Assert.AreEqual(0.2f, m.PathProgress, 1e-4f, "속도 1 × 0.4 × 0.5초");

            // 원 밖 (반지름 1칸)으로 나가면 원래 속도
            for (int i = 0; i < 30; i++) monsters.TickMove(0.1f); // 누적 3.5초 → 필드 만료
            Assert.AreEqual(0, monsters.ActiveSlowFields, "3초 뒤 사라짐");
            float before = m.PathProgress;
            monsters.TickMove(0.5f);
            Assert.AreEqual(before + 0.5f, m.PathProgress, 1e-4f, "만료 뒤 원래 속도");
        }

        [Test]
        public void SlowFields_DoNotStack_StrongestWins()
        {
            var m = SpawnOne();
            var p = monsters.GetGroundPoint(m);
            monsters.AddSlowField(p, 1f, 0.6f, 3f);
            monsters.AddSlowField(p, 1f, 0.3f, 3f);
            Assert.AreEqual(0.4f, monsters.GetSpeedMultiplier(m), 1e-5f);
        }

        [Test]
        public void Volley_SevenShots_OneField_DamageOnlyToHitMonster()
        {
            Assert.IsTrue(towers.Build(Site, nano));
            monsters.BeginWave(new[] { new WaveSpawn("normal", 2, 0f, 0f) }, 1);
            monsters.TickSpawn(0f);
            monsters.TickSpawn(0f);
            var cell = board.MonstersAtPath(0);
            Assert.AreEqual(2, cell.Count);
            var a = monsters.Get(cell[0]);
            var b = monsters.Get(cell[1]);

            int guard = 0;
            towers.TickCombat(0f); // 첫 발
            while ((towers.Towers[0].BurstLeft > 0 || towers.ProjectileCount > 0) && guard++ < 500)
                towers.TickCombat(0.01f);

            Assert.AreEqual(1, monsters.ActiveSlowFields, "연발 7발이라도 필드는 첫 발 자리에 1개");
            float hpA = a.Hp, hpB = b.Hp;
            float full = 4f;
            Assert.IsTrue((Mathf.Abs(hpA - (full - 7 * 0.3f)) < 1e-3f && hpB == full) || (Mathf.Abs(hpB - (full - 7 * 0.3f)) < 1e-3f && hpA == full),
                $"피해는 고정 대상 1마리에게만 7 × 0.3 (a {hpA}, b {hpB})");
            Assert.AreEqual(0.4f, monsters.GetSpeedMultiplier(a), 1e-5f, "필드 안: 둘 다 느려짐");
            Assert.AreEqual(0.4f, monsters.GetSpeedMultiplier(b), 1e-5f);
        }

        [Test]
        public void FloorTransition_ClearsFields()
        {
            var m = SpawnOne();
            monsters.AddSlowField(monsters.GetGroundPoint(m), 1f, 0.6f, 3f);
            monsters.HandleStateChanged(new StateChange(GameState.FloorTransition, 1, 1));
            Assert.AreEqual(0, monsters.ActiveSlowFields);
            Assert.AreEqual(1f, monsters.GetSpeedMultiplier(m));
        }
    }
}
