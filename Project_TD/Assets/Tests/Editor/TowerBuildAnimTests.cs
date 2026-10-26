using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace TowerDefence.Tests
{
    // 타워 건축 연출 (모델@Build 클립): 조립이 끝나야 공격, 판매하면 거꾸로 분해된 뒤 풀로.
    // 실제 basic 에셋(트윈캐논 모델 + Build 클립 1.92초)과 고정 7×7 맵을 쓴다.
    public class TowerBuildAnimTests
    {
        static readonly Vector2Int Site = new Vector2Int(2, 2); // 경로 칸과 맞닿은 부지

        GameConfig config;
        Board board;
        MonsterManager monsters;
        TowerManager towers;
        PlayerState player;
        TowerDef basic;
        GameObject towerRoot;

        [SetUp]
        public void SetUp()
        {
            config = AssetDatabase.LoadAssetAtPath<GameConfig>("Assets/Data/GameConfig.asset");
            basic = AssetDatabase.LoadAssetAtPath<TowerDef>("Assets/Data/Towers/basic.asset");
            if (basic.buildClip == null || basic.viewPrefab == null)
                Assert.Ignore("basic 타워에 모델·Build 클립이 연결되지 않음");

            var data = new GameData();
            data.Init(config, AssetDatabase.LoadAssetAtPath<MapGenRules>("Assets/Data/MapGenRules.asset"),
                new[] { AssetDatabase.LoadAssetAtPath<MonsterDef>("Assets/Data/Monsters/normal.asset") }, new[] { basic });
            board = new Board();
            monsters = new MonsterManager();
            towers = new TowerManager();
            player = new PlayerState();
            towerRoot = new GameObject("TestTowerViews");
            board.Init(data);
            player.Init(data);
            monsters.Init(data, board);
            towers.Init(data, board, monsters, player, towerRoot.transform); // 표시 있음
            towers.TowerChanged += board.HandleTowerChanged;

            // 에디터 모드에서는 풀이 만든 표시의 Awake(TowerAim 시작 자세)가 불리지 않으므로 직접 부른다
            var awake = typeof(TowerAim).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic);
            foreach (var aim in towerRoot.GetComponentsInChildren<TowerAim>(true))
                awake.Invoke(aim, null);

            var run = RunState.CreateNew(config);
            run.Cash = 10000;
            board.ResetForNewRun(run);
            player.ResetForNewRun(run);
            monsters.ResetForNewRun(run);
            towers.ResetForNewRun(run);
            board.Load(new FixedTestMapGenerator().Generate(default));
            monsters.HandleStateChanged(new StateChange(GameState.Wave, 1, 1));
        }

        [TearDown]
        public void TearDown()
        {
            if (towerRoot != null)
                Object.DestroyImmediate(towerRoot);
        }

        static Transform Part(TowerInstance t, string name)
        {
            foreach (var child in t.View.GetComponentsInChildren<Transform>(true))
                if (child.name == name)
                    return child;
            return null;
        }

        [Test]
        public void Build_PlaysAssembly_ThenAttacks()
        {
            float length = basic.buildClip.length;
            Assert.IsTrue(towers.Build(Site, basic));
            var t = towers.GetTowerAt(Site);
            Assert.AreEqual(TowerState.Building, t.State, "설치 직후 조립 연출");
            Assert.IsFalse(t.Parts.IsMerged, "조립 중에는 부품을 따로 그린다");

            // 사거리 안에 몬스터가 있어도 조립 중에는 쏘지 않는다
            monsters.BeginWave(new[] { new WaveSpawn("normal", 1, 1f, 0f) }, 1);
            monsters.TickSpawn(0f);
            monsters.TickMove(1f);
            towers.TickCombat(0.5f);
            Assert.AreEqual(0, towers.ProjectileCount);
            Assert.IsTrue(t.Locked.IsNone, "대상도 잡지 않음");

            towers.TickViews(length * 0.5f);
            Assert.AreEqual(TowerState.Building, t.State, "연출 도중");
            towers.TickViews(length * 0.6f);
            Assert.AreEqual(TowerState.Idle, t.State, "연출이 끝나면 공격 대기");
            Assert.IsTrue(t.Parts.IsMerged, "끝나면 합친 메시로");

            towers.TickCombat(0.1f);
            Assert.IsFalse(t.Locked.IsNone, "이제 대상을 잡는다");
        }

        [Test]
        public void Build_EndsInExactRestPose()
        {
            var prefabBase = basic.viewPrefab.transform.Find("TT_Base");
            towers.Build(Site, basic);
            var t = towers.GetTowerAt(Site);
            towers.TickViews(basic.buildClip.length * 0.3f);
            var basePart = Part(t, "TT_Base");
            towers.TickViews(basic.buildClip.length);
            Assert.AreEqual(prefabBase.localPosition, basePart.localPosition, "완성 자세 = 모델 기본 자세 (합친 메시와 어긋나지 않음)");
            Assert.AreEqual(prefabBase.localScale, basePart.localScale);
        }

        [Test]
        public void Sell_PlaysReverse_ThenReturnsToPool()
        {
            float length = basic.buildClip.length;
            towers.Build(Site, basic);
            var t = towers.GetTowerAt(Site);
            towers.TickViews(length + 0.1f);
            Assert.AreEqual(TowerState.Idle, t.State);

            Assert.IsTrue(towers.Sell(t));
            Assert.IsNull(towers.GetTowerAt(Site), "게임에서는 바로 빠진다");
            Assert.IsFalse(board.IsOccupied(Site), "칸도 바로 비운다");
            Assert.IsTrue(t.View.gameObject.activeSelf, "분해 연출 동안은 보인다");

            towers.TickViews(length * 0.5f);
            Assert.IsTrue(t.View.gameObject.activeSelf);
            towers.TickViews(length * 0.6f);
            Assert.IsFalse(t.View.gameObject.activeSelf, "분해가 끝나면 숨기고 풀로");
            Assert.IsTrue(t.Parts.IsMerged, "다음에 꺼낼 때를 위해 원래대로");
        }

        [Test]
        public void SellWhileBuilding_ReversesFromCurrentPoint()
        {
            float length = basic.buildClip.length;
            towers.Build(Site, basic);
            var t = towers.GetTowerAt(Site);
            towers.TickViews(length * 0.25f);
            towers.Sell(t);

            towers.TickViews(length * 0.3f); // 25%까지만 조립됐으니 25%만큼 거꾸로 가면 끝
            Assert.IsFalse(t.View.gameObject.activeSelf);
        }

        [Test]
        public void MoveAllToRecovery_DemolishesEveryTower()
        {
            towers.Build(Site, basic);
            towers.Build(new Vector2Int(2, 0), basic);
            towers.TickViews(basic.buildClip.length + 0.1f);
            var views = new[] { towers.Towers[0].View, towers.Towers[1].View };

            towers.MoveAllTowersToRecovery(4);
            Assert.AreEqual(0, towers.Towers.Count);
            Assert.IsTrue(views[0].gameObject.activeSelf && views[1].gameObject.activeSelf, "층 전환 연출 동안 분해");
            towers.TickViews(basic.buildClip.length + 0.1f);
            Assert.IsFalse(views[0].gameObject.activeSelf || views[1].gameObject.activeSelf);
        }
    }
}
