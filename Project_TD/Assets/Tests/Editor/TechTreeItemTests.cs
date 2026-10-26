using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace TowerDefence.Tests
{
    // 테크트리(진화)와 층 클리어 아이템. 기본 GameConfig 복사본에서 두 기능을 켜고 표시 없이 판정만 본다.
    // 고정 7×7 맵 경로: 0(3,0) 1(3,1) 2(3,2) 3(4,2) 4(5,2) 5(5,3) 6(5,4) 7(5,5) 8(5,6)
    public class TechTreeItemTests
    {
        GameConfig config;
        GameData data;
        Board board;
        MonsterManager monsters;
        TowerManager towers;
        PlayerState player;
        RunState run;
        TowerDef t1, t2, t3;

        static readonly Vector2Int Site = new Vector2Int(2, 0);

        static TowerDef MakeDef(string id, int tier, int totalCost, float damage, string next, int unlockFloor)
        {
            var d = ScriptableObject.CreateInstance<TowerDef>();
            d.id = id;
            d.tier = tier;
            d.buildCost = totalCost;
            d.damage = damage;
            d.interval = 1f;
            d.range = 2;
            d.attackType = AttackType.Hitscan;
            d.upgradeTo = next != null ? new[] { next } : new string[0];
            d.unlockFloor = unlockFloor;
            return d;
        }

        [SetUp]
        public void SetUp()
        {
            config = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameConfig>("Assets/Data/GameConfig.asset"));
            config.upgradeEnabled = true;
            config.floorItems.enabled = true;
            config.floorItems.damagePerItem = 0.10f;
            config.floorItems.speedPerItem = 0.08f;
            t1 = MakeDef("t1", 1, 50, 1f, "t2", 1);
            t2 = MakeDef("t2", 2, 120, 2f, "t3", 3);
            t3 = MakeDef("t3", 3, 270, 4f, null, 7);

            data = new GameData();
            data.Init(config, AssetDatabase.LoadAssetAtPath<MapGenRules>("Assets/Data/MapGenRules.asset"),
                new[] { TestAssets.NoIdleCopy("Assets/Data/Monsters/normal.asset") }, new[] { t1, t2, t3 });
            board = new Board();
            monsters = new MonsterManager();
            towers = new TowerManager();
            player = new PlayerState();
            board.Init(data);
            player.Init(data);
            monsters.Init(data, board);
            towers.Init(data, board, monsters, player);
            towers.TowerChanged += board.HandleTowerChanged;
            run = RunState.CreateNew(config);
            board.ResetForNewRun(run);
            player.ResetForNewRun(run);
            monsters.ResetForNewRun(run);
            towers.ResetForNewRun(run);
            board.Load(new FixedTestMapGenerator().Generate(default));
            player.Earn(1000, CashReason.Kill);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(t1);
            Object.DestroyImmediate(t2);
            Object.DestroyImmediate(t3);
            Object.DestroyImmediate(config);
        }

        [Test]
        public void Upgrade_PaysDifference_ReplacesTowerInPlace()
        {
            run.Floor = 3;
            Assert.IsTrue(towers.Build(Site, t1));
            int cash = player.Cash;
            Assert.AreEqual(t2, towers.GetUpgrade(t1));
            Assert.AreEqual(70, towers.GetUpgradeCost(t1, t2));

            Assert.IsTrue(towers.Upgrade(towers.GetTowerAt(Site)));
            Assert.AreEqual(cash - 70, player.Cash, "차액 70만 낸다");
            Assert.AreEqual(t2, towers.GetTowerAt(Site).Def, "같은 칸에 2단계");
            Assert.AreEqual(1, towers.Towers.Count);
            Assert.IsFalse(board.IsBuildable(Site), "칸 점유 유지");
            Assert.AreEqual(Mathf.RoundToInt(120 * config.sellRefundRate), towers.GetSellRefund(towers.GetTowerAt(Site).Def), "판매는 누적 비용 기준");
        }

        [Test]
        public void Upgrade_BeforeUnlockFloor_Refused()
        {
            run.Floor = 2;
            Assert.IsTrue(towers.Build(Site, t1));
            int cash = player.Cash;
            Assert.IsFalse(towers.IsUpgradeUnlocked(t2));
            Assert.IsFalse(towers.Upgrade(towers.GetTowerAt(Site)));
            Assert.AreEqual(cash, player.Cash);
            Assert.AreEqual(t1, towers.GetTowerAt(Site).Def);
        }

        [Test]
        public void Upgrade_LastTier_OrDisabled_HasNoNext()
        {
            Assert.IsNull(towers.GetUpgrade(t3), "마지막 단계");
            config.upgradeEnabled = false;
            Assert.IsNull(towers.GetUpgrade(t1), "테크트리 꺼짐 (SampleScene)");
        }

        [Test]
        public void Upgrade_NotEnoughCash_Refused()
        {
            run.Floor = 3;
            Assert.IsTrue(towers.Build(Site, t1));
            player.Spend(player.Cash - 69, CashReason.Build);
            Assert.IsFalse(towers.Upgrade(towers.GetTowerAt(Site)));
            Assert.AreEqual(69, player.Cash);
        }

        [Test]
        public void FloorClear_GrantsAlternatingItems_NotOnGameClear()
        {
            int granted = 0;
            towers.ItemGranted += e => granted++;
            towers.HandleStateChanged(new StateChange(GameState.FloorTransition, 1, 1));
            Assert.AreEqual(1.10f, run.ItemDamageMul, 1e-5f, "1층 클리어 = 화력 +10%");
            towers.HandleStateChanged(new StateChange(GameState.FloorTransition, 2, 1));
            Assert.AreEqual(1.08f, run.ItemSpeedMul, 1e-5f, "2층 클리어 = 연사 +8%");
            towers.HandleStateChanged(new StateChange(GameState.GameClear, 10, 5));
            Assert.AreEqual(2, granted);
            Assert.AreEqual(2, run.ItemCount);
            Assert.AreEqual(1f / 1.08f, towers.GetInterval(t1), 1e-5f);
        }

        [Test]
        public void FloorItems_Disabled_NoChange()
        {
            config.floorItems.enabled = false;
            towers.HandleStateChanged(new StateChange(GameState.FloorTransition, 1, 1));
            Assert.AreEqual(1f, run.ItemDamageMul);
            Assert.AreEqual(0, run.ItemCount);
        }

        [Test]
        public void DamageItem_AppliesToShots()
        {
            towers.HandleStateChanged(new StateChange(GameState.FloorTransition, 1, 1)); // 화력 ×1.1
            Assert.IsTrue(towers.Build(Site, t1));
            monsters.BeginWave(new[] { new WaveSpawn("normal", 1, 1f, 0f) }, 1);
            monsters.TickSpawn(0f);
            var h = board.MonstersAtPath(0)[0];
            float hp = monsters.Get(h).Hp;
            towers.TickCombat(0f);
            Assert.AreEqual(hp - 1.1f, monsters.Get(h).Hp, 1e-4f, "피해 1 × 1.1");
        }

        [Test]
        public void NewRun_ResetsItems()
        {
            towers.HandleStateChanged(new StateChange(GameState.FloorTransition, 1, 1));
            var fresh = RunState.CreateNew(config);
            Assert.AreEqual(1f, fresh.ItemDamageMul);
            Assert.AreEqual(1f, fresh.ItemSpeedMul);
        }
    }
}
