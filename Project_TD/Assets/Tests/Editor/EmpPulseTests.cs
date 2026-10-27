using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace TowerDefence.Tests
{
    // EMP 타워: 차지 뒤 원형 전자파. 파면이 지나가는 몬스터마다 1번씩 피해, 반지름 밖은 피해 없음.
    // 고정 7×7 맵 경로: 0(3,0) 1(3,1) 2(3,2) 3(4,2) 4(5,2) 5(5,3) 6(5,4) 7(5,5) 8(5,6)
    public class EmpPulseTests
    {
        GameConfig config;
        GameData data;
        Board board;
        MonsterManager monsters;
        TowerManager towers;
        PlayerState player;
        TowerDef emp;
        MonsterDef mech; // 기계 속성 몬스터 (일반 몬스터 복사본 + "mechanical")

        static readonly Vector2Int Site = new Vector2Int(2, 0);

        [SetUp]
        public void SetUp()
        {
            config = AssetDatabase.LoadAssetAtPath<GameConfig>("Assets/Data/GameConfig.asset");
            emp = ScriptableObject.CreateInstance<TowerDef>();
            emp.id = "empTest";
            emp.buildCost = 50;
            emp.damage = 1.5f;
            emp.interval = 1.6f;
            emp.range = 2;
            emp.chargeTime = 0.42f;
            emp.attackType = AttackType.Pulse;
            emp.pulseRadius = 2f;
            emp.pulseSpeed = 6f;

            data = new GameData();
            mech = TestAssets.NoIdleCopy("Assets/Data/Monsters/normal.asset");
            mech.id = "mech";
            mech.traits = new[] { "mechanical" };
            data.Init(config, AssetDatabase.LoadAssetAtPath<MapGenRules>("Assets/Data/MapGenRules.asset"),
                new[] { TestAssets.NoIdleCopy("Assets/Data/Monsters/normal.asset"), mech }, new[] { emp });
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
        public void TearDown() => Object.DestroyImmediate(emp);

        MonsterInstance SpawnOne(string id = "normal")
        {
            monsters.BeginWave(new[] { new WaveSpawn(id, 1, 0f, 0f) }, 1);
            monsters.TickSpawn(0f);
            var cell = board.MonstersAtPath(0);
            return monsters.Get(cell[cell.Count - 1]);
        }

        [Test]
        public void Pulse_AfterCharge_HitsEveryoneInRadiusOnce_NotOutside()
        {
            var far = SpawnOne();
            monsters.TickMove(4f);                 // 경로 4번 칸 (5,2): 타워 (2,0)에서 약 3.6칸 → 반지름 밖
            var near1 = SpawnOne();                // 0번 칸 (3,0): 1칸
            monsters.TickMove(1.2f);               // near1 → 1.2칸 진행 (3,1) 근처, far는 더 멀리
            var near2 = SpawnOne();                // 0번 칸
            Assert.IsTrue(towers.Build(Site, emp));
            var tower = towers.Towers[0];

            towers.TickCombat(0f);
            Assert.AreEqual(TowerState.Charging, tower.State, "조준 즉시 차지");
            towers.TickCombat(0.4f);
            Assert.AreEqual(0, towers.PulseCount, "차지 중에는 방출 안 함");
            Assert.AreEqual(4f, near1.Hp);

            towers.TickCombat(0.03f);              // 차지 끝 → 방출
            Assert.AreEqual(1, towers.PulseCount, "전자파 1개");
            for (int i = 0; i < 40; i++)
                towers.TickCombat(0.02f);          // 0.8초: 파동이 끝까지 퍼짐 (반지름 2 / 속도 6 ≈ 0.33초)
            Assert.AreEqual(0, towers.PulseCount, "다 퍼지면 사라짐");

            Assert.AreEqual(2.5f, near1.Hp, 1e-4f, "반지름 안: 1번만 1.5 피해");
            Assert.AreEqual(2.5f, near2.Hp, 1e-4f, "반지름 안: 1번만 1.5 피해");
            Assert.AreEqual(4f, far.Hp, "반지름 밖: 피해 없음");
            Assert.AreEqual(TowerState.Reloading, tower.State);
        }

        [Test]
        public void Pulse_ReachesNearBeforeFar()
        {
            var a = SpawnOne();
            monsters.TickMove(1.8f);               // a: 타워에서 조금 더 먼 곳 (3,1)~(3,2) 사이
            var b = SpawnOne();                    // b: (3,0) 바로 옆
            Assert.IsTrue(towers.Build(Site, emp));
            towers.TickCombat(0f);
            towers.TickCombat(0.4f);
            towers.TickCombat(0.03f);              // 차지 0.43초 → 방출, 파면 0.18칸 (아직 아무도 안 닿음)
            Assert.AreEqual(4f, b.Hp);
            towers.TickCombat(0.1f);               // 파면 0.78칸: b(몸 가장자리 0.75칸)만 닿음
            Assert.AreEqual(2.5f, b.Hp, 1e-4f, "가까운 몬스터 먼저");
            Assert.AreEqual(4f, a.Hp, "먼 몬스터는 아직");
            towers.TickCombat(0.3f);
            Assert.AreEqual(2.5f, a.Hp, 1e-4f, "파면이 닿으면 맞음");
        }

        // 기계 속성 몬스터는 2배 (EMP: 공격력 1 → 기계 2)
        [Test]
        public void Pulse_MechanicalTakesDouble()
        {
            emp.damage = 1f;
            emp.bonusTrait = "mechanical";
            emp.bonusMultiplier = 2f;
            var normal = SpawnOne();
            monsters.TickMove(0.6f);
            var robot = SpawnOne("mech");
            Assert.IsTrue(towers.Build(Site, emp));
            towers.TickCombat(0f);
            towers.TickCombat(0.43f);
            for (int i = 0; i < 30; i++)
                towers.TickCombat(0.02f);
            Assert.AreEqual(3f, normal.Hp, 1e-4f, "일반: 1 피해");
            Assert.AreEqual(2f, robot.Hp, 1e-4f, "기계: 2배 = 2 피해");
        }

        [Test]
        public void DamageCalculator_TraitBonus()
        {
            var plain = TestAssets.NoIdleCopy("Assets/Data/Monsters/normal.asset");
            var info = new DamageInfo(1f, DamageType.Normal, "mechanical", 2f);
            Assert.AreEqual(2f, DamageCalculator.Compute(info, mech), "속성 일치 → 2배");
            Assert.AreEqual(1f, DamageCalculator.Compute(info, plain), "속성 없음 → 그대로");
            Assert.AreEqual(1f, DamageCalculator.Compute(new DamageInfo(1f, DamageType.Normal), mech), "상성 없는 타워 → 그대로");
            Object.DestroyImmediate(plain);
        }

        [Test]
        public void ClearCombat_RemovesPulses()
        {
            SpawnOne();
            Assert.IsTrue(towers.Build(Site, emp));
            towers.TickCombat(0f);
            towers.TickCombat(0.4f);
            towers.TickCombat(0.03f);              // 방출 직후 (파면 0.18칸, 아직 퍼지는 중)
            Assert.AreEqual(1, towers.PulseCount);
            towers.ClearCombat();
            Assert.AreEqual(0, towers.PulseCount);
        }
    }
}
