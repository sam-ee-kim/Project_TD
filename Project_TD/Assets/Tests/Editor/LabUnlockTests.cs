using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace TowerDefence.Tests
{
    // 공학연구소: M1반도체(판 끝 지급), 2단계 이상 타워 해금, 해금 전 진화 막기, 저장·불러오기.
    public class LabUnlockTests
    {
        GameConfig config;
        GameData data;
        Board board;
        MonsterManager monsters;
        TowerManager towers;
        PlayerState player;
        RunState run;
        TowerDef t1, t2;
        string path;

        static readonly Vector2Int Site = new Vector2Int(2, 0);

        static TowerDef MakeDef(string id, int tier, int cost, string next, int price)
        {
            var d = ScriptableObject.CreateInstance<TowerDef>();
            d.id = id;
            d.tier = tier;
            d.buildCost = cost;
            d.damage = 1f;
            d.interval = 1f;
            d.range = 2;
            d.attackType = AttackType.Hitscan;
            d.upgradeTo = next != null ? new[] { next } : new string[0];
            d.unlockPrice = price;
            return d;
        }

        [SetUp]
        public void SetUp()
        {
            config = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameConfig>("Assets/Data/GameConfig.asset"));
            config.upgradeEnabled = true;
            config.labUnlock = true;
            t1 = MakeDef("t1", 1, 50, "t2", 0);
            t2 = MakeDef("t2", 2, 120, null, 3);
            data = new GameData();
            data.Init(config, AssetDatabase.LoadAssetAtPath<MapGenRules>("Assets/Data/MapGenRules.asset"),
                new[] { TestAssets.NoIdleCopy("Assets/Data/Monsters/normal.asset") }, new[] { t1, t2 });
            board = new Board();
            monsters = new MonsterManager();
            towers = new TowerManager();
            player = new PlayerState();
            board.Init(data);
            player.Init(data);
            path = Path.Combine(Path.GetTempPath(), "td_meta_test.json");
            if (File.Exists(path))
                File.Delete(path);
            player.InitMeta(MetaProgress.Load(path), path);
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
            Object.DestroyImmediate(config);
            if (File.Exists(path))
                File.Delete(path);
        }

        [Test]
        public void GameOver_GivesClearedFloors_Once()
        {
            int granted = -1;
            player.MetaChanged += e => granted = e.Granted;
            player.HandleStateChanged(new StateChange(GameState.GameOver, 5, 2));
            Assert.AreEqual(4, granted, "5층에서 게임 오버 = 4층까지 클리어");
            Assert.AreEqual(4, player.M1Chips);
            player.HandleStateChanged(new StateChange(GameState.GameOver, 5, 2));
            Assert.AreEqual(4, player.M1Chips, "한 판에 한 번만");
        }

        [Test]
        public void GameClear_GivesAllFloors()
        {
            player.HandleStateChanged(new StateChange(GameState.GameClear, 10, 5));
            Assert.AreEqual(10, player.M1Chips);
        }

        [Test]
        public void Upgrade_LockedUntilLabUnlock()
        {
            Assert.IsTrue(towers.Build(Site, t1));
            Assert.IsFalse(player.IsTowerUnlocked(t2));
            Assert.IsFalse(towers.Upgrade(towers.GetTowerAt(Site)), "해금 전에는 진화 불가");

            player.HandleStateChanged(new StateChange(GameState.GameOver, 3, 1)); // 2개
            Assert.IsFalse(player.TryUnlockTower(t2), "가격 3, 보유 2");
            Assert.AreEqual(2, player.M1Chips);

            player.ResetForNewRun(run);
            player.HandleStateChanged(new StateChange(GameState.GameOver, 2, 1)); // +1 = 3
            Assert.IsTrue(player.TryUnlockTower(t2));
            Assert.AreEqual(0, player.M1Chips);
            Assert.IsTrue(player.IsTowerUnlocked(t2));
            Assert.IsFalse(player.TryUnlockTower(t2), "두 번 해금하지 않는다");
            Assert.IsTrue(towers.Upgrade(towers.GetTowerAt(Site)), "해금 뒤 진화");
        }

        [Test]
        public void Progress_SavedAndLoaded()
        {
            player.HandleStateChanged(new StateChange(GameState.GameClear, 10, 5));
            Assert.IsTrue(player.TryUnlockTower(t2));
            var loaded = MetaProgress.Load(path);
            Assert.AreEqual(7, loaded.m1Chips);
            Assert.IsTrue(loaded.IsUnlocked("t2"));
        }

        [Test]
        public void LabOff_EverythingUnlocked_NoChips()
        {
            config.labUnlock = false;
            Assert.IsTrue(player.IsTowerUnlocked(t2));
            player.HandleStateChanged(new StateChange(GameState.GameClear, 10, 5));
            Assert.AreEqual(0, player.M1Chips);
        }
    }
}
