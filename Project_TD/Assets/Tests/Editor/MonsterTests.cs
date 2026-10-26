using System;
using NUnit.Framework;
using UnityEditor;

namespace TowerDefence.Tests
{
    // 작업 5: MonsterManager (표시 없이 동작, 고정 7×7 맵 = 경로 9칸).
    public class MonsterTests
    {
        GameConfig config;
        GameData data;
        MapProvider maps;
        Board board;
        MonsterManager monsters;
        TowerManager towers;
        PlayerState player;
        GameFlow flow;
        RunState run;
        int killedEvents;
        int defeatedEvents;

        [SetUp]
        public void SetUp()
        {
            config = AssetDatabase.LoadAssetAtPath<GameConfig>("Assets/Data/GameConfig.asset");
            data = new GameData();
            data.Init(config, AssetDatabase.LoadAssetAtPath<MapGenRules>("Assets/Data/MapGenRules.asset"),
                new[]
                {
                    TestAssets.NoIdleCopy("Assets/Data/Monsters/normal.asset"), // 이동 시간 계산용: 스폰 대기 0
                    AssetDatabase.LoadAssetAtPath<MonsterDef>("Assets/Data/Monsters/fast.asset"),
                    AssetDatabase.LoadAssetAtPath<MonsterDef>("Assets/Data/Monsters/tank.asset"),
                },
                new TowerDef[0]);

            maps = new MapProvider();
            board = new Board();
            monsters = new MonsterManager();
            towers = new TowerManager();
            player = new PlayerState();
            flow = new GameFlow { LogStates = false };
            maps.Init(data, null, null);
            board.Init(data);
            player.Init(data);
            monsters.Init(data, board); // 표시 없음
            towers.Init(data, board, monsters, player);
            flow.Init(data, maps, board, monsters, towers);

            killedEvents = 0;
            defeatedEvents = 0;
            monsters.Killed += player.HandleKilled;
            monsters.Killed += e => killedEvents++;
            monsters.ReachedExit += player.HandleReachedExit;
            player.Defeated += flow.HandleDefeated;
            player.Defeated += () => defeatedEvents++;
            flow.StateChanged += monsters.HandleStateChanged;

            run = RunState.CreateNew(config);
            maps.ResetForNewRun(run);
            board.ResetForNewRun(run);
            player.ResetForNewRun(run);
            monsters.ResetForNewRun(run);
            towers.ResetForNewRun(run);
            flow.ResetForNewRun(run);
            board.Load(new FixedTestMapGenerator().Generate(default)); // 경로 9칸, 출구 = 8번 칸
        }

        // GameLoop.Update의 2~6단계와 같은 순서.
        void Frame(float dt)
        {
            flow.Tick(dt);
            if (flow.State != GameState.Wave)
                return;
            monsters.TickSpawn(dt);
            monsters.TickMove(dt);
            bool stillWave = flow.State == GameState.Wave;
            monsters.Cleanup();
            if (stillWave)
                flow.CheckWaveClear();
        }

        void BeginWave(int count, float interval = 1f)
        {
            monsters.BeginWave(new[] { new WaveSpawn("normal", count, interval, 0f) }, 1);
        }

        MonsterHandle SpawnOne()
        {
            BeginWave(1);
            monsters.TickSpawn(0f);
            Assert.AreEqual(1, board.MonstersAtPath(0).Count);
            return board.MonstersAtPath(0)[0];
        }

        [Test]
        public void Spawn_TenNormals_OneSecondApart()
        {
            BeginWave(10);
            monsters.TickSpawn(0f);
            Assert.AreEqual(1, monsters.AliveCount, "첫 마리는 바로");
            monsters.TickSpawn(0.5f);
            Assert.AreEqual(1, monsters.AliveCount);
            monsters.TickSpawn(0.5f);
            Assert.AreEqual(2, monsters.AliveCount, "1초 뒤 두 번째");
            for (int i = 0; i < 8; i++)
                monsters.TickSpawn(1f);
            Assert.AreEqual(10, monsters.AliveCount);
            Assert.IsTrue(monsters.SpawnFinished);
            monsters.TickSpawn(5f);
            Assert.AreEqual(10, monsters.AliveCount, "구성표 수보다 더 나오지 않음");

            var m = monsters.Get(board.MonstersAtPath(0)[0]);
            Assert.AreEqual("normal", m.Def.id);
            Assert.AreEqual(4f, m.Hp);
            Assert.AreEqual(MonsterState.Alive, m.State);
        }

        [Test]
        public void Move_UpdatesPathIndexAndCellBuckets()
        {
            var h = SpawnOne();
            monsters.TickMove(0.5f); // speed 1칸/초
            var m = monsters.Get(h);
            Assert.AreEqual(0, m.PathIndex);
            Assert.AreEqual(0.5f, m.PathProgress, 1e-5f);
            Assert.AreEqual(1, board.MonstersAtPath(0).Count, "칸이 안 바뀌면 그대로");

            monsters.TickMove(0.6f);
            Assert.AreEqual(1, m.PathIndex);
            Assert.AreEqual(0, board.MonstersAtPath(0).Count);
            Assert.AreEqual(1, board.MonstersAtPath(1).Count);
            Assert.AreEqual(h, board.MonstersAtPath(1)[0]);
        }

        [Test]
        public void ReachExit_ExitedThenNexusDamage_NoReward_ThenCleanup()
        {
            var h = SpawnOne();
            for (int i = 0; i < 79; i++)
                monsters.TickMove(0.1f); // 7.9칸
            Assert.AreEqual(20, player.NexusHp);

            monsters.TickMove(0.2f);     // 8칸 = 출구 칸
            Assert.AreEqual(MonsterState.Exited, monsters.Get(h).State);
            Assert.AreEqual(19, player.NexusHp, "nexusDamage 1");
            Assert.AreEqual(100, player.Cash, "도착은 보상 없음");
            Assert.AreEqual(0, monsters.AliveCount);
            for (int i = 0; i < 9; i++)
                Assert.AreEqual(0, board.MonstersAtPath(i).Count, "Board 목록에서 제거");

            monsters.Cleanup();
            Assert.IsFalse(monsters.IsValid(h), "정리 후 핸들 무효");
            Assert.IsNull(monsters.Get(h));
        }

        [Test]
        public void TwentyArrivals_GameOver_DefeatedOnce()
        {
            flow.RequestStartGame();
            flow.StartNow(); // 1층 1웨이브: 10마리
            Assert.AreEqual(10, monsters.RemainingToSpawn);

            int guard = 0;
            while (flow.State == GameState.Wave && guard++ < 10000)
                Frame(0.05f);
            Assert.AreEqual(GameState.FloorTransition, flow.State, "10마리 도착 후 웨이브 클리어 (1층은 1웨이브)");
            Assert.AreEqual(10, player.NexusHp);

            flow.Tick(config.floorTransitionSec + 0.01f); // 같은 Tier → 2층 휴식
            Assert.AreEqual(GameState.Break, flow.State);
            flow.StartNow(); // 2층 1웨이브: 10마리
            guard = 0;
            while (flow.State == GameState.Wave && guard++ < 10000)
                Frame(0.05f);

            Assert.AreEqual(GameState.GameOver, flow.State, "20마리 도착 → GameOver");
            Assert.AreEqual(0, player.NexusHp);
            Assert.AreEqual(1, defeatedEvents);
            Assert.AreEqual(0, monsters.CreatedDuringWave, "웨이브 중 풀 생성 없음");
        }

        [Test]
        public void Spawn_UsesFloorWaveHpScale()
        {
            monsters.HandleStateChanged(new StateChange(GameState.Wave, 10, 5));
            var h = SpawnOne();
            Assert.AreEqual(37f, monsters.Get(h).Hp, "10층 5웨이브 일반 몬스터 = 37 (HpLevelDesign)");

            monsters.HandleStateChanged(new StateChange(GameState.Wave, 1, 1));
            BeginWave(1);
            monsters.TickSpawn(0f);
            var fresh = board.MonstersAtPath(0)[board.MonstersAtPath(0).Count - 1];
            Assert.AreEqual(4f, monsters.Get(fresh).Hp, "1층 1웨이브 = 기본 체력 4");
        }

        [Test]
        public void ApplyDamage_DamagedThenKilled_RewardOnce()
        {
            var h = SpawnOne();
            Assert.AreEqual(DamageResult.Damaged, monsters.ApplyDamage(h, new DamageInfo(1, DamageType.Normal)));
            Assert.AreEqual(3f, monsters.Get(h).Hp);

            // 같은 프레임에 두 번 맞아 죽음 (오버킬 포함)
            var r1 = monsters.ApplyDamage(h, new DamageInfo(80, DamageType.Normal));
            var r2 = monsters.ApplyDamage(h, new DamageInfo(80, DamageType.Normal));
            Assert.AreEqual(DamageResult.Killed, r1);
            Assert.AreEqual(DamageResult.Ignored, r2, "Dying은 무시");
            Assert.AreEqual(1, killedEvents, "Killed 1번");
            Assert.AreEqual(105, player.Cash, "처치 보상 5, 1번만");
            Assert.AreEqual(MonsterState.Dying, monsters.Get(h).State);
            Assert.AreEqual(0, board.MonstersAtPath(0).Count, "사망 즉시 Board 목록에서 제거");
            Assert.AreEqual(0, monsters.AliveCount);
        }

        [Test]
        public void ApplyDamage_TwoKillingHitsSameFrame_KilledOnce()
        {
            var h = SpawnOne();
            var r1 = monsters.ApplyDamage(h, new DamageInfo(100, DamageType.Normal));
            var r2 = monsters.ApplyDamage(h, new DamageInfo(100, DamageType.Normal));
            Assert.AreEqual((DamageResult.Killed, DamageResult.Ignored), (r1, r2));
            Assert.AreEqual(1, killedEvents);
        }

        [Test]
        public void OldHandle_OnReusedMonster_IsIgnored()
        {
            var old = SpawnOne();
            monsters.ApplyDamage(old, new DamageInfo(100, DamageType.Normal));
            monsters.Cleanup(); // 풀 반납

            var fresh = SpawnOne(); // 같은 객체가 재사용된다 (풀 스택)
            Assert.AreEqual(old.Index, fresh.Index, "같은 객체 재사용");
            Assert.AreNotEqual(old.Generation, fresh.Generation, "Generation +1");
            Assert.IsFalse(monsters.IsValid(old));
            Assert.IsTrue(monsters.IsValid(fresh));

            Assert.AreEqual(DamageResult.Ignored, monsters.ApplyDamage(old, new DamageInfo(100, DamageType.Normal)));
            Assert.AreEqual(4f, monsters.Get(fresh).Hp, "새 몬스터는 피해 없음");
            Assert.AreEqual(1, killedEvents);
        }

        [Test]
        public void InvalidHandles_AreIgnored()
        {
            Assert.AreEqual(DamageResult.Ignored, monsters.ApplyDamage(MonsterHandle.None, new DamageInfo(10, DamageType.Normal)));
            Assert.AreEqual(DamageResult.Ignored, monsters.ApplyDamage(new MonsterHandle(99999, 1), new DamageInfo(10, DamageType.Normal)));
            Assert.IsFalse(monsters.IsValid(new MonsterHandle(0, 0)), "한 번도 안 나온 몬스터");
        }

        [Test]
        public void Pool_PrewarmedAtInit_AndRefilledInBreak()
        {
            Assert.AreEqual(config.performance.monsterPoolPrewarm, monsters.CreatedCount);
            monsters.Prewarm(400); // 휴식 때 부족분 채움
            Assert.AreEqual(400, monsters.CreatedCount);
            monsters.Prewarm(100);
            Assert.AreEqual(400, monsters.CreatedCount, "충분하면 더 만들지 않음");
        }

        [Test]
        public void WaveTicks_AllocateNothing()
        {
            BeginWave(290, 0.01f);
            // 예열 (JIT·첫 호출 비용 제외)
            monsters.TickSpawn(0.01f);
            monsters.TickMove(0.01f);
            monsters.Cleanup();

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int frame = 0; frame < 600; frame++)
            {
                monsters.TickSpawn(0.02f);
                monsters.TickMove(0.02f);
                monsters.Cleanup();
            }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.AreEqual(0, allocated, "웨이브 중 Tick 할당");
            Assert.AreEqual(0, monsters.CreatedDuringWave);
        }
    }
}
