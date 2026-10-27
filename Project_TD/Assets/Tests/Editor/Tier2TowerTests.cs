using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace TowerDefence.Tests
{
    // 2단계 타워: 미니건 예열(쏠수록 빨라져 초당 5발), 단발 타워(전기·냉기)는 기존 투사체 규칙 그대로.
    // 고정 7×7 맵 경로: 0(3,0) 1(3,1) 2(3,2) 3(4,2) 4(5,2) 5(5,3) 6(5,4) 7(5,5) 8(5,6)
    public class Tier2TowerTests
    {
        GameConfig config;
        GameData data;
        Board board;
        MonsterManager monsters;
        TowerManager towers;
        PlayerState player;
        TowerDef minigun;

        static readonly Vector2Int Site = new Vector2Int(2, 0);

        [SetUp]
        public void SetUp()
        {
            config = AssetDatabase.LoadAssetAtPath<GameConfig>("Assets/Data/GameConfig.asset");
            minigun = ScriptableObject.CreateInstance<TowerDef>();
            minigun.id = "minigunTest";
            minigun.tier = 2;
            minigun.buildCost = 50;
            minigun.damage = 1f;
            minigun.interval = 0.2f;
            minigun.spinUpTime = 1.5f;
            minigun.spinUpStartInterval = 0.6f;
            minigun.range = 2;
            minigun.attackType = AttackType.Hitscan; // 비행 시간 없이 발수만 센다

            data = new GameData();
            data.Init(config, AssetDatabase.LoadAssetAtPath<MapGenRules>("Assets/Data/MapGenRules.asset"),
                new[] { TestAssets.NoIdleCopy("Assets/Data/Monsters/normal.asset") }, new[] { minigun });
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
            monsters.DebugHpOverride = 1000; // 죽지 않고 계속 맞는 과녁
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(minigun);

        [Test]
        public void Minigun_SpinsUpTo5ShotsPerSecond_CoolsDownWhenIdle()
        {
            Assert.IsTrue(towers.Build(Site, minigun));
            var tower = towers.Towers[0];
            monsters.BeginWave(new[] { new WaveSpawn("normal", 1, 0f, 0f) }, 1);
            monsters.TickSpawn(0f);
            var target = monsters.Get(board.MonstersAtPath(0)[0]);

            const float dt = 0.01f;
            float hp0 = target.Hp;
            for (int i = 0; i < 100; i++) towers.TickCombat(dt); // 0~1초: 예열 중
            float firstSecond = hp0 - target.Hp;
            for (int i = 0; i < 100; i++) towers.TickCombat(dt); // 1~2초: 예열 끝 (1.5초)
            Assert.AreEqual(1f, tower.SpinUp, 1e-4f, "1.5초 쏘면 예열 완료");
            float hp2 = target.Hp;
            for (int i = 0; i < 100; i++) towers.TickCombat(dt); // 2~3초: 최고 연사
            float thirdSecond = hp2 - target.Hp;

            Assert.Less(firstSecond, thirdSecond, "쏠수록 빨라진다");
            Assert.That(thirdSecond, Is.InRange(4.5f, 5.5f), "최고 연사 초당 5발 × 1 피해");
            Assert.AreEqual(0.2f, towers.GetInterval(tower), 1e-4f);

            // 대상이 없으면 식는다 (예열 시간의 절반에 걸쳐)
            monsters.ApplyDamage(tower.Locked, new DamageInfo(100000, DamageType.Normal));
            monsters.Cleanup();
            for (int i = 0; i < 80; i++) towers.TickCombat(dt);
            Assert.AreEqual(0f, tower.SpinUp, 1e-4f, "0.75초 쉬면 완전히 식음");
            Assert.AreEqual(0.6f, towers.GetInterval(tower), 1e-4f, "다시 느린 간격부터");
        }

        [Test]
        public void Tier2_ShowInPalette_IsTestFlagOnly()
        {
            Assert.AreEqual(2, minigun.tier);
            Assert.IsFalse(minigun.showInPalette, "기본은 진화 전용 (건축 목록에 안 나옴)");
            var asset = AssetDatabase.LoadAssetAtPath<TowerDef>("Assets/Data/Towers/minigun.asset");
            if (asset != null)
            {
                Assert.AreEqual(2, asset.tier);
                Assert.IsTrue(asset.showInPalette, "테스트 환경: 건축 목록에 표시");
            }
        }
    }
}
