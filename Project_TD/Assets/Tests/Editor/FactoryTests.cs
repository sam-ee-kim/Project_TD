using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace TowerDefence.Tests
{
    // 반도체 공장: 작은 반도체 지급(판 끝, 층당 2개), 단계 강화 가격 1·2·3·4·5, 최대 5단계, 무료 초기화(전액 환급),
    // 효과(공격력·공격 속도 +5%/단계, 설치·진화 비용 −5%/단계, 시작 캐시·넥서스 체력 +5%/단계), 저장.
    public class FactoryTests
    {
        GameConfig config;
        GameData data;
        Board board;
        MonsterManager monsters;
        TowerManager towers;
        PlayerState player;
        TowerDef def;
        string path;

        [SetUp]
        public void SetUp()
        {
            config = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameConfig>("Assets/Data/GameConfig.asset"));
            config.factory = new GameConfig.FactorySettings(); // 기본값: 5%, 5단계, 1·2·3·4·5, 층당 2개
            config.startCash = 100;
            config.nexusHp = 20;
            def = ScriptableObject.CreateInstance<TowerDef>();
            def.id = "t1";
            def.tier = 1;
            def.buildCost = 100;
            def.damage = 2f;
            def.interval = 1f;
            def.range = 2;
            def.attackType = AttackType.Hitscan;
            data = new GameData();
            data.Init(config, AssetDatabase.LoadAssetAtPath<MapGenRules>("Assets/Data/MapGenRules.asset"),
                new[] { TestAssets.NoIdleCopy("Assets/Data/Monsters/normal.asset") }, new[] { def });
            board = new Board();
            monsters = new MonsterManager();
            towers = new TowerManager();
            player = new PlayerState();
            board.Init(data);
            player.Init(data);
            path = Path.Combine(Path.GetTempPath(), "td_factory_test.json");
            if (File.Exists(path))
                File.Delete(path);
            player.InitMeta(MetaProgress.Load(path), path);
            monsters.Init(data, board);
            towers.Init(data, board, monsters, player);
            NewRun();
        }

        RunState NewRun()
        {
            var run = RunState.CreateNew(config);
            board.ResetForNewRun(run);
            player.ResetForNewRun(run);
            monsters.ResetForNewRun(run);
            towers.ResetForNewRun(run);
            return run;
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(def);
            Object.DestroyImmediate(config);
            if (File.Exists(path))
                File.Delete(path);
        }

        [Test]
        public void RunEnd_GivesTwoPerClearedFloor_Once()
        {
            int granted = -1;
            player.MetaChanged += e => granted = e.SmallGranted;
            player.HandleStateChanged(new StateChange(GameState.GameOver, 6, 1));
            Assert.AreEqual(10, granted, "6층에서 게임 오버 = 5층 클리어 × 2");
            player.HandleStateChanged(new StateChange(GameState.GameOver, 6, 1));
            Assert.AreEqual(10, player.SmallChips, "한 판에 한 번만");
        }

        [Test]
        public void Upgrade_CostsOneToFive_MaxFive()
        {
            player.HandleStateChanged(new StateChange(GameState.GameClear, 10, 5)); // 20개
            for (int i = 0; i < 5; i++)
                Assert.IsTrue(player.TryUpgradeFactory(FactoryNode.Damage));
            Assert.AreEqual(5, player.FactoryLevel(FactoryNode.Damage));
            Assert.AreEqual(20 - 15, player.SmallChips, "1+2+3+4+5 = 15");
            Assert.IsFalse(player.TryUpgradeFactory(FactoryNode.Damage), "최대 5단계");
            Assert.AreEqual(5, player.SmallChips);
        }

        [Test]
        public void Upgrade_NotEnoughChips_Refused()
        {
            Assert.IsFalse(player.TryUpgradeFactory(FactoryNode.Speed));
            Assert.AreEqual(0, player.FactoryLevel(FactoryNode.Speed));
        }

        [Test]
        public void Reset_IsFree_RefundsAll()
        {
            player.HandleStateChanged(new StateChange(GameState.GameClear, 10, 5)); // 20개
            player.TryUpgradeFactory(FactoryNode.Damage); // 1
            player.TryUpgradeFactory(FactoryNode.Damage); // 2
            player.TryUpgradeFactory(FactoryNode.Cost);   // 1
            Assert.AreEqual(16, player.SmallChips);
            player.ResetFactory();
            Assert.AreEqual(20, player.SmallChips, "쓴 재화 전액 환급");
            Assert.AreEqual(0, player.FactoryLevel(FactoryNode.Damage));
            Assert.AreEqual(0, player.FactoryLevel(FactoryNode.Cost));
        }

        [Test]
        public void Effects_DamageSpeedCost()
        {
            player.HandleStateChanged(new StateChange(GameState.GameClear, 10, 5));
            player.TryUpgradeFactory(FactoryNode.Damage);
            player.TryUpgradeFactory(FactoryNode.Damage);   // +10%
            player.TryUpgradeFactory(FactoryNode.Speed);    // +5%
            player.TryUpgradeFactory(FactoryNode.Cost);
            player.TryUpgradeFactory(FactoryNode.Cost);     // −10%
            Assert.AreEqual(2f * 1.1f, towers.GetDamage(def), 1e-4f);
            Assert.AreEqual(1f / 1.05f, towers.GetInterval(def), 1e-4f);
            Assert.AreEqual(90, towers.GetBuildCost(def));
            Assert.AreEqual(Mathf.RoundToInt(100 * config.sellRefundRate), towers.GetSellRefund(def), "환급은 원래 비용 기준");
        }

        [Test]
        public void Effects_StartCashAndNexusHp_NextRun()
        {
            player.HandleStateChanged(new StateChange(GameState.GameClear, 10, 5)); // 20개
            for (int i = 0; i < 3; i++)
                player.TryUpgradeFactory(FactoryNode.StartCash); // +15%  (1+2+3 = 6)
            for (int i = 0; i < 4; i++)
                player.TryUpgradeFactory(FactoryNode.NexusHp);   // +20%  (1+2+3+4 = 10)
            NewRun();
            Assert.AreEqual(115, player.Cash);
            Assert.AreEqual(24, player.NexusHp, "20 + 20×20%");
        }

        [Test]
        public void Progress_SavedAndLoaded()
        {
            player.HandleStateChanged(new StateChange(GameState.GameClear, 10, 5));
            player.TryUpgradeFactory(FactoryNode.Speed);
            var loaded = MetaProgress.Load(path);
            Assert.AreEqual(19, loaded.smallChips);
            Assert.AreEqual(1, loaded.FactoryLevel(FactoryNode.Speed));
        }
    }
}
