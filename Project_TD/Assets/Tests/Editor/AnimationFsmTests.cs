using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace TowerDefence.Tests
{
    // 몬스터 표시 상태머신(Idle/Walk/Die)과 타워 상태머신(Idle/Aiming/Reloading).
    // 실제 normal 에셋(스폰 대기 0.5초, Saberfang 모델)과 고정 7×7 맵(경로 9칸)을 쓴다.
    public class AnimationFsmTests
    {
        GameConfig config;
        GameData data;
        Board board;
        MonsterManager monsters;
        TowerManager towers;
        PlayerState player;
        MonsterDef normal;
        TowerDef basic;
        GameObject viewRoot;

        [SetUp]
        public void SetUp()
        {
            config = AssetDatabase.LoadAssetAtPath<GameConfig>("Assets/Data/GameConfig.asset");
            normal = AssetDatabase.LoadAssetAtPath<MonsterDef>("Assets/Data/Monsters/normal.asset");
            basic = AssetDatabase.LoadAssetAtPath<TowerDef>("Assets/Data/Towers/basic.asset");
            data = new GameData();
            data.Init(config, AssetDatabase.LoadAssetAtPath<MapGenRules>("Assets/Data/MapGenRules.asset"), new[] { normal }, new[] { basic });

            viewRoot = new GameObject("TestMonsterViews");
            board = new Board();
            monsters = new MonsterManager();
            towers = new TowerManager();
            player = new PlayerState();
            board.Init(data);
            player.Init(data);
            monsters.Init(data, board, viewRoot.transform); // 표시 있음
            towers.Init(data, board, monsters, player);     // 타워는 표시 없음 (상태만 확인)
            towers.TowerChanged += board.HandleTowerChanged;

            var run = RunState.CreateNew(config);
            board.ResetForNewRun(run);
            player.ResetForNewRun(run);
            monsters.ResetForNewRun(run);
            towers.ResetForNewRun(run);
            board.Load(new FixedTestMapGenerator().Generate(default));
            monsters.HandleStateChanged(new StateChange(GameState.Wave, 1, 1));
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(viewRoot);

        MonsterInstance SpawnNormal()
        {
            monsters.BeginWave(new[] { new WaveSpawn("normal", 1, 1f, 0f) }, 1);
            monsters.TickSpawn(0f);
            var cell = board.MonstersAtPath(0);
            return monsters.Get(cell[cell.Count - 1]);
        }

        [Test]
        public void Spawn_IdleFirst_ThenWalk_AndDoesNotMoveWhileIdle()
        {
            Assert.AreEqual(0.5f, normal.spawnIdleSec, 1e-4f, "normal 에셋의 스폰 대기");
            var m = SpawnNormal();
            Assert.IsNotNull(m.View);
            Assert.AreEqual(MonsterViewState.Idle, m.View.State, "스폰 직후 Idle");
            Assert.IsTrue(m.View.Root.gameObject.activeSelf);

            monsters.TickMove(0.4f);
            monsters.TickViews(0.4f);
            Assert.AreEqual(0f, m.PathProgress, "대기 중에는 움직이지 않음");
            Assert.AreEqual(MonsterViewState.Idle, m.View.State);

            monsters.TickMove(0.2f);
            monsters.TickViews(0.2f);
            Assert.AreEqual(0.1f, m.PathProgress, 1e-4f, "대기가 끝난 0.1초만큼 이동");
            Assert.AreEqual(MonsterViewState.Walk, m.View.State, "경로를 따라 이동 중 Walk");
        }

        [Test]
        public void Killed_PlaysDie_ThenHidesAndReturnsToPool()
        {
            if (normal.viewPrefab == null)
                Assert.Ignore("Monster_Saberfang 모델이 없어 Die 길이(1.47초) 검사를 건너뜀");
            var m = SpawnNormal();
            monsters.TickMove(1f);
            var view = m.View;
            Assert.AreEqual(DamageResult.Killed, monsters.ApplyDamage(m.Handle, new DamageInfo(1000, DamageType.Normal)));
            monsters.Cleanup(); // 게임 데이터는 바로 풀로

            Assert.AreEqual(MonsterViewState.Die, view.State, "표시는 Die 재생");
            Assert.AreEqual(1, monsters.DyingViewCount);
            Assert.IsTrue(view.Root.gameObject.activeSelf);

            monsters.TickViews(1f);
            Assert.AreEqual(MonsterViewState.Die, view.State, "Die 애니메이션(1.47초) 도중");
            monsters.TickViews(0.6f);
            Assert.AreEqual(MonsterViewState.Hidden, view.State, "끝나면 숨김");
            Assert.AreEqual(0, monsters.DyingViewCount);
            Assert.IsFalse(view.Root.gameObject.activeSelf);
        }

        [Test]
        public void ReachedExit_HidesImmediately()
        {
            var m = SpawnNormal();
            var view = m.View;
            monsters.TickMove(0.5f); // 대기
            monsters.TickMove(8.1f); // 8칸 → 출구
            Assert.AreEqual(MonsterState.Exited, m.State);
            monsters.Cleanup();
            Assert.AreEqual(MonsterViewState.Hidden, view.State, "출구에 들어가면 Die 없이 사라짐");
            Assert.AreEqual(0, monsters.DyingViewCount);
        }

        [Test]
        public void NotInWave_RemainingMonstersIdle()
        {
            var m = SpawnNormal();
            monsters.TickMove(1f);
            monsters.TickViews(0.02f);
            Assert.AreEqual(MonsterViewState.Walk, m.View.State);

            monsters.HandleStateChanged(new StateChange(GameState.GameOver, 1, 1));
            monsters.TickViews(0.02f);
            Assert.AreEqual(MonsterViewState.Idle, m.View.State, "대기 상태(게임오버)에서는 Idle");
        }

        [Test]
        public void Views_PrewarmedInBreak_NoCreationDuringWave()
        {
            monsters.PrewarmViews(normal, 10);
            int created = monsters.ViewsCreated;
            Assert.AreEqual(10, created);

            monsters.BeginWave(new[] { new WaveSpawn("normal", 10, 0.1f, 0f) }, 1);
            for (int i = 0; i < 20; i++)
                monsters.TickSpawn(0.1f);
            Assert.AreEqual(created, monsters.ViewsCreated, "웨이브 중 표시를 새로 만들지 않음");
            Assert.AreEqual(0, monsters.ViewsCreatedDuringWave);
        }

        [Test]
        public void TowerFsm_IdleToReloadingAndBack()
        {
            player.Earn(1000, CashReason.Kill);
            Assert.IsTrue(towers.Build(new Vector2Int(2, 0), basic));
            var t = towers.GetTowerAt(new Vector2Int(2, 0));
            Assert.AreEqual(TowerState.Idle, t.State, "설치 직후 Idle");

            towers.TickCombat(0.02f);
            Assert.AreEqual(TowerState.Idle, t.State, "대상 없으면 Idle");

            var m = SpawnNormal();
            towers.TickCombat(0.02f);
            Assert.AreEqual(TowerState.Reloading, t.State, "대상 → 발사 → 재장전");
            Assert.AreEqual(m.Handle, t.Locked);

            monsters.ApplyDamage(m.Handle, new DamageInfo(1000, DamageType.Normal));
            monsters.Cleanup();
            towers.TickCombat(0.5f);
            Assert.AreEqual(TowerState.Reloading, t.State, "재장전 중");
            towers.TickCombat(0.6f);
            Assert.AreEqual(TowerState.Idle, t.State, "재장전이 끝났는데 대상 없음 → Idle");
        }
    }
}
