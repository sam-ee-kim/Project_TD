using System;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace TowerDefence.Tests
{
    // 작업 6: TowerManager 건축·판매·전투 (표시 없이 동작).
    // 고정 7×7 맵 경로: 0(3,0) 1(3,1) 2(3,2) 3(4,2) 4(5,2) 5(5,3) 6(5,4) 7(5,5) 8(5,6)
    public class TowerTests
    {
        GameConfig config;
        GameData data;
        Board board;
        MonsterManager monsters;
        TowerManager towers;
        PlayerState player;
        RunState run;
        int killedEvents;

        TowerDef basic;      // 실제 에셋: Lock / First / 종류 무관 / Projectile
        TowerDef lockSpecial;   // Lock, ["Special"], Hitscan, 피해 1
        TowerDef preferSpecial; // PreferType, ["Special"], Hitscan, 피해 1
        TowerDef retargetSpecial; // Retarget, ["Special"], Hitscan, 피해 1

        static readonly Vector2Int SiteA = new Vector2Int(2, 0); // 경로 칸 0~3을 덮음
        static readonly Vector2Int SiteB = new Vector2Int(2, 2);

        static TowerDef MakeDef(string id, TargetLock lockMode, string[] priority, AttackType attack, int damage)
        {
            var d = ScriptableObject.CreateInstance<TowerDef>();
            d.id = id;
            d.buildCost = 50;
            d.damage = damage;
            d.interval = 1f;
            d.range = 2;
            d.lockMode = lockMode;
            d.targetRule = TargetRule.First;
            d.typePriority = priority;
            d.attackType = attack;
            d.projectileSpeed = 8f;
            return d;
        }

        [SetUp]
        public void SetUp()
        {
            config = AssetDatabase.LoadAssetAtPath<GameConfig>("Assets/Data/GameConfig.asset");
            basic = AssetDatabase.LoadAssetAtPath<TowerDef>("Assets/Data/Towers/basic.asset");
            lockSpecial = MakeDef("lockSpecial", TargetLock.Lock, new[] { "Special" }, AttackType.Hitscan, 1);
            preferSpecial = MakeDef("preferSpecial", TargetLock.PreferType, new[] { "Special" }, AttackType.Hitscan, 1);
            retargetSpecial = MakeDef("retargetSpecial", TargetLock.Retarget, new[] { "Special" }, AttackType.Hitscan, 1);

            data = new GameData();
            data.Init(config, AssetDatabase.LoadAssetAtPath<MapGenRules>("Assets/Data/MapGenRules.asset"),
                new[]
                {
                    TestAssets.NoIdleCopy("Assets/Data/Monsters/normal.asset"), // 이동 시간 계산용: 스폰 대기 0
                    AssetDatabase.LoadAssetAtPath<MonsterDef>("Assets/Data/Monsters/fast.asset"),
                    AssetDatabase.LoadAssetAtPath<MonsterDef>("Assets/Data/Monsters/tank.asset"),
                },
                new[] { basic, lockSpecial, preferSpecial, retargetSpecial });

            board = new Board();
            monsters = new MonsterManager();
            towers = new TowerManager();
            player = new PlayerState();
            board.Init(data);
            player.Init(data);
            monsters.Init(data, board);
            towers.Init(data, board, monsters, player); // 표시 없음

            killedEvents = 0;
            monsters.Killed += player.HandleKilled;
            monsters.Killed += e => killedEvents++;
            towers.TowerChanged += board.HandleTowerChanged;

            run = RunState.CreateNew(config);
            board.ResetForNewRun(run);
            player.ResetForNewRun(run);
            monsters.ResetForNewRun(run);
            towers.ResetForNewRun(run);
            board.Load(new FixedTestMapGenerator().Generate(default));
            player.Earn(10000, CashReason.Kill); // 건축 테스트용 넉넉한 캐시
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(lockSpecial);
            UnityEngine.Object.DestroyImmediate(preferSpecial);
            UnityEngine.Object.DestroyImmediate(retargetSpecial);
        }

        MonsterHandle Spawn(string id)
        {
            monsters.BeginWave(new[] { new WaveSpawn(id, 1, 1f, 0f) }, 1);
            monsters.TickSpawn(0f);
            var cell = board.MonstersAt(0);
            return cell[cell.Count - 1];
        }

        TowerInstance BuildAt(Vector2Int cell, TowerDef def)
        {
            Assert.IsTrue(towers.Build(cell, def), $"{def.id} 설치 실패 {cell}");
            return towers.GetTowerAt(cell);
        }

        [Test]
        public void Range_IsSquare_DiagonalCornerIncluded_ThreeStraightExcluded()
        {
            var a = BuildAt(SiteA, basic); // (2,0)
            // 경로 칸 3 = (4,2): dx=2, dy=2 → 대각선 모서리, 포함
            Assert.IsTrue(a.CoversPath[3], "(dx 2, dy 2) 포함");
            Assert.IsFalse(a.CoversPath[4], "(5,2): dx 3 제외");
            CollectionAssert.AreEqual(new[] { 3, 2, 1, 0 }, a.Covered, "큰 칸 번호부터");

            var b = BuildAt(SiteB, basic); // (2,2)
            // 경로 칸 4 = (5,2): dx=3, dy=0 → 제외 (원형이면 거리 3이라 어차피 제외지만, 정사각형 판정 확인)
            Assert.IsFalse(b.CoversPath[4], "(3, 0) 제외");
            Assert.IsTrue(b.CoversPath[3], "(2, 0) 포함");
            Assert.IsTrue(b.CoversPath[0], "(1, −2) 포함");
        }

        [Test]
        public void Lock_KeepsTarget_WhenTankEnters_RetargetsWhenOutOfRange()
        {
            var tower = BuildAt(SiteA, lockSpecial);
            var normal = Spawn("normal");
            towers.TickCombat(0f);
            Assert.AreEqual(normal, tower.Locked);

            var tank = Spawn("tank"); // 우선 종류(Special)가 사거리에 들어옴
            towers.TickCombat(1f);
            Assert.AreEqual(normal, tower.Locked, "Lock: 고정 중에는 바꾸지 않음");

            monsters.TickMove(4.1f); // normal 4.1칸(사거리 밖), tank 2.05칸(사거리 안)
            Assert.IsFalse(tower.CoversPath[monsters.Get(normal).PathIndex]);
            towers.TickCombat(1f);
            Assert.AreEqual(tank, tower.Locked, "사거리 밖으로 나가면 새 대상");
        }

        [Test]
        public void Lock_Retargets_WhenTargetDies()
        {
            var tower = BuildAt(SiteA, lockSpecial);
            var first = Spawn("normal");
            towers.TickCombat(0f);
            var second = Spawn("normal");
            monsters.ApplyDamage(first, new DamageInfo(1000, DamageType.Normal));
            towers.TickCombat(1f);
            Assert.AreEqual(second, tower.Locked);
        }

        [Test]
        public void PreferType_SwitchesToHigherTypeOnNextAttack()
        {
            var tower = BuildAt(SiteA, preferSpecial);
            var normal = Spawn("normal");
            towers.TickCombat(0f);
            Assert.AreEqual(normal, tower.Locked);

            var tank = Spawn("tank");
            towers.TickCombat(0.5f);
            Assert.AreEqual(normal, tower.Locked, "재장전 전에는 그대로");
            towers.TickCombat(0.5f);
            Assert.AreEqual(tank, tower.Locked, "다음 공격에 더 높은 종류로 교체");
        }

        [Test]
        public void TypePriority_Special_PicksTankEvenIfNormalIsCloserToExit()
        {
            var normal = Spawn("normal");
            monsters.TickMove(2f); // normal 2칸 진행
            var tank = Spawn("tank"); // tank 0칸

            var special = BuildAt(SiteA, retargetSpecial);
            towers.TickCombat(0f);
            Assert.AreEqual(tank, special.Locked, "종류 우선도가 경로 진행도보다 먼저");

            var plain = BuildAt(SiteB, basic); // 종류 무관, First
            towers.TickCombat(0f);
            Assert.AreEqual(normal, plain.Locked, "종류 무관이면 출구에 가까운 쪽");
        }

        [Test]
        public void Projectile_TargetDiesInFlight_ReturnedWithoutDamage()
        {
            BuildAt(SiteA, basic);
            var target = Spawn("normal");
            towers.TickCombat(0f); // 발사
            Assert.AreEqual(1, towers.ProjectileCount);

            monsters.ApplyDamage(target, new DamageInfo(100, DamageType.Normal)); // 다른 원인으로 사망
            Assert.AreEqual(1, killedEvents);
            towers.TickCombat(0.01f);
            Assert.AreEqual(0, towers.ProjectileCount, "피해 없이 반납");
            Assert.AreEqual(1, killedEvents);
        }

        [Test]
        public void Projectile_TargetReusedInFlight_DoesNotHitNewMonster()
        {
            BuildAt(SiteA, basic);
            var target = Spawn("normal");
            towers.TickCombat(0f);
            monsters.ApplyDamage(target, new DamageInfo(100, DamageType.Normal));
            monsters.Cleanup();
            var fresh = Spawn("normal"); // 같은 객체 재사용
            Assert.AreEqual(target.Index, fresh.Index);

            for (int i = 0; i < 20; i++)
                towers.TickCombat(0.01f);
            Assert.AreEqual(100, monsters.Get(fresh).Hp, "옛 핸들의 투사체는 새 몬스터를 맞히지 않음");
        }

        [Test]
        public void Projectile_TowerSoldInFlight_StillHits()
        {
            var tower = BuildAt(SiteA, basic);
            var target = Spawn("normal");
            towers.TickCombat(0f);
            Assert.AreEqual(1, towers.ProjectileCount);

            Assert.IsTrue(towers.Sell(tower));
            int guard = 0;
            while (towers.ProjectileCount > 0 && guard++ < 1000)
                towers.TickCombat(0.01f);
            Assert.AreEqual(80, monsters.Get(target).Hp, "판매 뒤에도 명중 (피해 20)");
        }

        [Test]
        public void BasicTower_KillsInFiveHits_RewardFive()
        {
            BuildAt(SiteA, basic);
            int cash = player.Cash;
            var target = Spawn("normal");
            int guard = 0;
            while (monsters.IsValid(target) && monsters.Get(target).State == MonsterState.Alive && guard++ < 10000)
                towers.TickCombat(0.01f); // 몬스터는 제자리 (이동 없이 공격만)
            Assert.AreEqual(MonsterState.Dying, monsters.Get(target).State);
            Assert.AreEqual(1, killedEvents);
            Assert.AreEqual(cash + 5, player.Cash, "처치 캐시 +5");
            Assert.That(guard * 0.01f, Is.InRange(4f, 4.2f), "1초 간격 20 피해 × 5발 (첫 발 0초)");
        }

        [Test]
        public void Build_ChecksSiteAndCash_OccupiesBoard()
        {
            Assert.IsFalse(towers.Build(new Vector2Int(3, 1), basic), "경로에는 설치 불가");
            Assert.IsFalse(towers.Build(new Vector2Int(0, 6), basic), "부지가 아닌 칸");

            int cash = player.Cash;
            Assert.IsTrue(towers.Build(SiteA, basic));
            Assert.AreEqual(cash - 50, player.Cash);
            Assert.IsFalse(board.IsBuildable(SiteA), "Board 점유");
            Assert.IsFalse(towers.Build(SiteA, basic), "이미 타워가 있는 칸");

            player.Spend(player.Cash - 10, CashReason.Build);
            Assert.IsFalse(towers.Build(SiteB, basic), "캐시 부족");
            Assert.AreEqual(10, player.Cash, "실패 시 캐시 그대로");
        }

        [Test]
        public void Sell_Refunds35_ReleasesCell()
        {
            var tower = BuildAt(SiteA, basic);
            int cash = player.Cash;
            Assert.AreEqual(35, towers.GetSellRefund(basic));
            Assert.IsTrue(towers.Sell(tower));
            Assert.AreEqual(cash + 35, player.Cash, "50 × 0.7 = 35");
            Assert.IsTrue(board.IsBuildable(SiteA), "칸이 다시 설치 가능");
            Assert.IsNull(towers.GetTowerAt(SiteA));
            Assert.IsFalse(towers.Sell(tower), "두 번 판매 불가");
        }

        [Test]
        public void Hitscan_DamagesImmediately()
        {
            BuildAt(SiteA, lockSpecial); // Hitscan, 피해 1
            var target = Spawn("normal");
            towers.TickCombat(0f);
            Assert.AreEqual(0, towers.ProjectileCount);
            Assert.AreEqual(99, monsters.Get(target).Hp);
        }

        [Test]
        public void CombatTick_AllocatesNothing()
        {
            BuildAt(SiteA, basic);
            BuildAt(SiteB, basic);
            BuildAt(new Vector2Int(6, 4), basic);
            monsters.BeginWave(new[] { new WaveSpawn("normal", 60, 0.1f, 0f) }, 1);
            // 예열
            monsters.TickSpawn(0.02f);
            monsters.TickMove(0.02f);
            towers.TickCombat(0.02f);
            monsters.Cleanup();

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int frame = 0; frame < 500; frame++)
            {
                monsters.TickSpawn(0.02f);
                monsters.TickMove(0.02f);
                towers.TickCombat(0.02f);
                monsters.Cleanup();
            }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.AreEqual(0, allocated, "전투 Tick 할당");
            Assert.Greater(killedEvents, 0, "실제로 싸웠는지");
        }
    }
}
