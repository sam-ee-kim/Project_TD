using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace TowerDefence.Tests
{
    // 작업 10: 최적화 규칙 검사 (GDD 8-5).
    public class OptimizationTests
    {
        // ---------- Update는 GameLoop 하나 ----------

        [Test]
        public void OnlyGameLoop_HasUpdateMethods()
        {
            var names = new[] { "Update", "LateUpdate", "FixedUpdate" };
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            var offenders = new List<string>();
            foreach (var type in typeof(GameLoop).Assembly.GetTypes())
            {
                if (!typeof(MonoBehaviour).IsAssignableFrom(type))
                    continue;
                foreach (var method in type.GetMethods(flags))
                    if (names.Contains(method.Name) && method.GetParameters().Length == 0)
                        offenders.Add($"{type.FullName}.{method.Name}");
            }
            CollectionAssert.AreEquivalent(new[] { "TowerDefence.GameLoop.Update" }, offenders,
                "프로젝트에서 Update 계열 메서드를 가진 MonoBehaviour는 GameLoop 하나여야 한다");
        }

        // ---------- 특수 몬스터 혼합은 데이터만으로 ----------

        [Test]
        public void DesignedWave_MixesSpecialMonsters_FromDataOnly()
        {
            var config = ScriptableObject.CreateInstance<GameConfig>();
            try
            {
                config.waves = new[]
                {
                    new GameConfig.DesignedWave
                    {
                        floor = 46, wave = 3,
                        lines = new[]
                        {
                            new GameConfig.SpawnLine { monsterId = "fast", count = 145, intervalSec = 0.1f },
                            new GameConfig.SpawnLine { monsterId = "tank", count = 145, intervalSec = 0.1f, delaySec = 2f },
                        },
                    },
                };
                var buffer = new WaveSpawn[8];

                int lines = LevelRules.GetWaveComposition(config, 46, 3, 300, buffer);
                Assert.AreEqual(2, lines);
                Assert.AreEqual("fast", buffer[0].MonsterId);
                Assert.AreEqual(145, buffer[0].Count);
                Assert.AreEqual("tank", buffer[1].MonsterId);
                Assert.AreEqual(2f, buffer[1].DelaySec);

                // 표에 없는 웨이브는 공식 규칙 그대로 (일반 몬스터 + 돌격조)
                lines = LevelRules.GetWaveComposition(config, 46, 1, 300, buffer);
                Assert.AreEqual(2, lines);
                Assert.AreEqual(config.rules.runnerMonsterId, buffer[1].MonsterId);
                Assert.AreEqual(LevelRules.RunnerCount(config, 46), buffer[1].Count);
                Assert.AreEqual(config.rules.defaultMonsterId, buffer[0].MonsterId);
                Assert.AreEqual(LevelRules.MonsterCount(config, 300), buffer[0].Count);
            }
            finally
            {
                Object.DestroyImmediate(config);
            }
        }

        // ---------- 다시 시작 10번 ----------

        GameObject loopGo, canvasGo;
        GameLoop loop;

        [TearDown]
        public void TearDown()
        {
            if (loopGo != null) Object.DestroyImmediate(loopGo);
            if (canvasGo != null) Object.DestroyImmediate(canvasGo);
            foreach (var name in new[] { "Monsters", "Towers", "BuildOverlay" })
            {
                var go = GameObject.Find(name);
                while (go != null)
                {
                    Object.DestroyImmediate(go);
                    go = GameObject.Find(name);
                }
            }
        }

        [Test]
        public void Restart10Times_NoDuplicateHandlers_NoGrowth()
        {
            CreateLoop();
            var flow = Field<GameFlow>("flow");
            var board = Field<Board>("board");
            var monsters = Field<MonsterManager>("monsters");
            var towers = Field<TowerManager>("towers");
            var player = Field<PlayerState>("player");
            var managers = new object[]
            {
                flow, Field<MapProvider>("maps"), board, monsters, towers, player,
                Field<BuildController>("build"), Field<GameUI>("ui"),
            };

            Snapshot first = default, second = default;
            for (int cycle = 1; cycle <= 11; cycle++)
            {
                PlayOneRun(flow, board, monsters, towers, player);
                flow.RequestRestart(); // GameOver → [다시 시작] → GameLoop.StartNewRun
                Assert.AreEqual(GameState.Ready, flow.State, $"{cycle}번째 다시 시작 후 Ready");

                var snap = TakeSnapshot(managers, monsters, towers);
                if (cycle == 1)
                {
                    first = snap; // 첫 판에서 풀이 다음 웨이브 수만큼 채워지는 것은 허용
                    continue;
                }
                if (cycle == 2)
                    second = snap;

                Assert.AreEqual(first.Handlers, snap.Handlers, $"{cycle}번째: 이벤트 핸들러 수가 늘었다 (중복 구독)");
                Assert.AreEqual(first.Monsters, snap.Monsters, $"{cycle}번째: 몬스터 데이터 풀이 늘었다");
                Assert.AreEqual(first.Views, snap.Views, $"{cycle}번째: 표시 오브젝트(몬스터·타워·투사체·이펙트)가 늘었다");
                Assert.AreEqual(first.SceneObjects, snap.SceneObjects, $"{cycle}번째: 씬 GameObject 수가 늘었다");
                if (cycle == 11)
                {
                    long growth = snap.Memory - second.Memory;
                    TestContext.WriteLine($"관리 메모리 2→11번째: {second.Memory / 1024}KB → {snap.Memory / 1024}KB ({growth / 1024}KB)");
                    Assert.Less(growth, 1024 * 1024, "다시 시작 10번 동안 관리 메모리가 1MB 넘게 늘었다");
                }
            }
        }

        struct Snapshot
        {
            public int Handlers, Monsters, Views, SceneObjects;
            public long Memory;
        }

        static Snapshot TakeSnapshot(object[] managers, MonsterManager monsters, TowerManager towers)
        {
            int handlers = 0;
            foreach (var m in managers)
                foreach (var f in m.GetType().GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
                    if (typeof(Delegate).IsAssignableFrom(f.FieldType) && f.GetValue(m) is Delegate d)
                        handlers += d.GetInvocationList().Length;
            return new Snapshot
            {
                Handlers = handlers,
                Monsters = monsters.CreatedCount,
                Views = monsters.ViewsCreated + towers.TowerViewsCreated + towers.ProjectileViewsCreated + towers.EffectViewsCreated,
                SceneObjects = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length,
                Memory = GC.GetTotalMemory(true),
            };
        }

        // 한 판: 맵 준비 → 시작 → 타워 2개 → 웨이브(스폰·이동·전투) → 넥서스 체력 0 → GameOver.
        static void PlayOneRun(GameFlow flow, Board board, MonsterManager monsters, TowerManager towers, PlayerState player)
        {
            var until = DateTime.Now.AddSeconds(5);
            while (board.Map == null && DateTime.Now < until)
            {
                flow.Tick(0f); // MapProvider.Poll → TierReady → Board.Load
                System.Threading.Thread.Sleep(5);
            }
            Assert.IsNotNull(board.Map, "맵 준비");

            flow.RequestStartGame();
            Assert.AreEqual(GameState.Break, flow.State);
            var basic = AssetDatabase.LoadAssetAtPath<TowerDef>("Assets/Data/Towers/basic.asset");
            int built = 0;
            for (int x = 0; x < board.N && built < 2; x++)
                for (int y = 0; y < board.N && built < 2; y++)
                    if (towers.Build(new Vector2Int(x, y), basic))
                        built++;

            flow.StartNow();
            Assert.AreEqual(GameState.Wave, flow.State);
            monsters.TickSpawn(3f);
            monsters.TickMove(0.5f);
            towers.TickCombat(0.1f);
            monsters.Cleanup();
            monsters.TickViews(0.1f);
            towers.TickViews(0.1f);

            player.TakeNexusDamage(player.NexusHp);
            Assert.AreEqual(GameState.GameOver, flow.State);
        }

        void CreateLoop()
        {
            canvasGo = new GameObject("TestCanvas", typeof(Canvas), typeof(UnityEngine.UI.CanvasScaler));
            loopGo = new GameObject("TestGameLoop");
            loop = loopGo.AddComponent<GameLoop>(); // 에디터 모드라 Awake가 자동으로 불리지 않는다
            var so = new SerializedObject(loop);
            so.FindProperty("config").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameConfig>("Assets/Data/GameConfig.asset");
            so.FindProperty("mapGenRules").objectReferenceValue = AssetDatabase.LoadAssetAtPath<MapGenRules>("Assets/Data/MapGenRules.asset");
            var mons = so.FindProperty("monsterDefs");
            mons.arraySize = 1;
            mons.GetArrayElementAtIndex(0).objectReferenceValue = AssetDatabase.LoadAssetAtPath<MonsterDef>("Assets/Data/Monsters/normal.asset");
            var tows = so.FindProperty("towerDefs");
            tows.arraySize = 1;
            tows.GetArrayElementAtIndex(0).objectReferenceValue = AssetDatabase.LoadAssetAtPath<TowerDef>("Assets/Data/Towers/basic.asset");
            so.FindProperty("canvas").objectReferenceValue = canvasGo.GetComponent<Canvas>();
            so.FindProperty("useFixedTestMap").boolValue = true;
            so.ApplyModifiedPropertiesWithoutUndo();
            typeof(GameLoop).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(loop, null);
            Field<GameFlow>("flow").LogStates = false;

            // 에디터 모드에서는 풀이 미리 만든 타워 표시의 Awake(TowerAim 시작 자세 저장)도 불리지 않으므로 직접 부른다.
            var aimAwake = typeof(TowerAim).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic);
            foreach (var aim in Object.FindObjectsByType<TowerAim>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                aimAwake.Invoke(aim, null);
        }

        T Field<T>(string name) =>
            (T)typeof(GameLoop).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(loop);
    }
}
