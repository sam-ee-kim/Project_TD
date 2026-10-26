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
            var cell = board.MonstersAtPath(0);
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
            Assert.IsTrue(a.CoversPathIndex(board, 3), "(dx 2, dy 2) 포함");
            Assert.IsFalse(a.CoversPathIndex(board, 4), "(5,2): dx 3 제외");
            CollectionAssert.AreEquivalent(new[] { board.PathCellId(0), board.PathCellId(1), board.PathCellId(2), board.PathCellId(3) },
                a.Covered, "사거리 안 경로 칸 = 칸 번호 0~3번 칸");

            var b = BuildAt(SiteB, basic); // (2,2)
            // 경로 칸 4 = (5,2): dx=3, dy=0 → 제외 (원형이면 거리 3이라 어차피 제외지만, 정사각형 판정 확인)
            Assert.IsFalse(b.CoversPathIndex(board, 4), "(3, 0) 제외");
            Assert.IsTrue(b.CoversPathIndex(board, 3), "(2, 0) 포함");
            Assert.IsTrue(b.CoversPathIndex(board, 0), "(1, −2) 포함");
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
            Assert.IsFalse(tower.CoversCell[monsters.Get(normal).CellId]);
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
            Assert.AreEqual(4f, monsters.Get(fresh).Hp, "옛 핸들의 투사체는 새 몬스터를 맞히지 않음");
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
            Assert.AreEqual(3f, monsters.Get(target).Hp, "판매 뒤에도 명중 (피해 1)");
        }

        [Test]
        public void BasicTower_KillsInTwoBursts_RewardFive()
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
            Assert.That(guard * 0.01f, Is.InRange(1.1f, 1.4f), "1초 간격 이점사(1 피해 × 2발) × 2번 = 체력 4 (0초, 0.12초, 1초, 1.12초 + 비행)");
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
        public void BasicTower_Burst_FiresTwoBullets_OneDamageEach()
        {
            Assert.AreEqual(2, basic.burstCount, "총 타워 = 이점사");
            BuildAt(SiteA, basic);
            var target = Spawn("normal");
            towers.TickCombat(0f);
            Assert.AreEqual(1, towers.ProjectileCount, "첫 발");
            for (int i = 0; i < 50; i++) // 0.5초: 재장전(1초) 전, 둘째 발은 burstGap(0.12초) 뒤
                towers.TickCombat(0.01f);
            Assert.AreEqual(0, towers.ProjectileCount, "2발 모두 명중");
            Assert.AreEqual(2f, monsters.Get(target).Hp, "2발 × 1 피해");
        }

        [Test]
        public void BasicTower_Burst_TargetDiesAfterFirst_SecondNotFired()
        {
            BuildAt(SiteA, basic);
            var target = Spawn("normal");
            towers.TickCombat(0f);
            Assert.AreEqual(1, towers.ProjectileCount);
            monsters.ApplyDamage(target, new DamageInfo(100, DamageType.Normal)); // 다른 원인으로 사망
            monsters.Cleanup();
            var fresh = Spawn("normal");
            for (int i = 0; i < 50; i++) // 0.5초: 재장전(1초) 전
                towers.TickCombat(0.01f);
            Assert.AreEqual(4f, monsters.Get(fresh).Hp, "대상이 죽으면 남은 발은 쏘지 않음 (다른 몬스터로 넘어가지 않음)");
        }

        // 번개 타워처럼 차지가 있는 타워: 조준이 맞으면 차지 → chargeTime 뒤에 발사, 차지 중 대상이 죽으면 다른 대상으로 이어 간다.
        [Test]
        public void Charge_FiresAfterChargeTime_KeepsChargeOnRetarget()
        {
            var charger = MakeDef("charger", TargetLock.Lock, new string[0], AttackType.Projectile, 4);
            charger.chargeTime = 1f;
            var d2 = new GameData();
            d2.Init(config, AssetDatabase.LoadAssetAtPath<MapGenRules>("Assets/Data/MapGenRules.asset"),
                new[] { TestAssets.NoIdleCopy("Assets/Data/Monsters/normal.asset") }, new[] { charger });
            var b2 = new Board(); var m2 = new MonsterManager(); var t2 = new TowerManager(); var p2 = new PlayerState();
            b2.Init(d2); p2.Init(d2); m2.Init(d2, b2); t2.Init(d2, b2, m2, p2);
            t2.TowerChanged += b2.HandleTowerChanged;
            var r2 = RunState.CreateNew(config);
            b2.ResetForNewRun(r2); p2.ResetForNewRun(r2); m2.ResetForNewRun(r2); t2.ResetForNewRun(r2);
            b2.Load(new FixedTestMapGenerator().Generate(default));
            p2.Earn(1000, CashReason.Kill);
            Assert.IsTrue(t2.Build(SiteA, charger));
            var tower = t2.GetTowerAt(SiteA);

            m2.BeginWave(new[] { new WaveSpawn("normal", 2, 0f, 0f) }, 1);
            m2.TickSpawn(0f);
            m2.TickSpawn(0f);
            t2.TickCombat(0f);
            Assert.AreEqual(TowerState.Charging, tower.State, "조준 즉시 차지 시작");
            Assert.AreEqual(0, t2.ProjectileCount, "차지 중에는 쏘지 않음");

            m2.ApplyDamage(tower.Locked, new DamageInfo(100, DamageType.Normal)); // 차지 중 대상 사망
            m2.Cleanup();
            t2.TickCombat(0.6f);
            Assert.AreEqual(TowerState.Charging, tower.State, "다른 대상으로 이어서 차지");
            Assert.IsFalse(tower.Locked.IsNone);
            Assert.AreEqual(0, t2.ProjectileCount);

            t2.TickCombat(0.39f); // 누적 0.99초: 아직
            Assert.AreEqual(0, t2.ProjectileCount);
            t2.TickCombat(0.02f); // 누적 1.01초 ≥ 1초 (비행은 짧게만 진행)
            Assert.AreEqual(1, t2.ProjectileCount, "차지가 끝나면 발사");
            Assert.AreEqual(TowerState.Reloading, tower.State);
            UnityEngine.Object.DestroyImmediate(charger);
        }

        [Test]
        public void Hitscan_DamagesImmediately()
        {
            BuildAt(SiteA, lockSpecial); // Hitscan, 피해 1
            var target = Spawn("normal");
            towers.TickCombat(0f);
            Assert.AreEqual(0, towers.ProjectileCount);
            Assert.AreEqual(3f, monsters.Get(target).Hp);
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
