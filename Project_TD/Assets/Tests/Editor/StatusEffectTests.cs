using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace TowerDefence.Tests
{
    // 이상효과 (MonsterManager.ApplyDamage): 화상·분진·백린·서리(얼음)·정전기(감전). 타워 없이 피해 값만 넣어 본다.
    public class StatusEffectTests
    {
        GameConfig config;
        GameData data;
        Board board;
        MonsterManager monsters;
        PlayerState player;
        MonsterDef normal;
        MonsterInstance target;
        int stunnedEvents;
        float lastStun;

        [SetUp]
        public void SetUp()
        {
            config = AssetDatabase.LoadAssetAtPath<GameConfig>("Assets/Data/GameConfig.asset");
            normal = TestAssets.NoIdleCopy("Assets/Data/Monsters/normal.asset");
            normal.traits = new string[0];
            data = new GameData();
            data.Init(config, AssetDatabase.LoadAssetAtPath<MapGenRules>("Assets/Data/MapGenRules.asset"), new[] { normal }, new TowerDef[0]);
            board = new Board();
            monsters = new MonsterManager();
            player = new PlayerState();
            board.Init(data);
            player.Init(data);
            monsters.Init(data, board);
            var run = RunState.CreateNew(config);
            board.ResetForNewRun(run);
            player.ResetForNewRun(run);
            monsters.ResetForNewRun(run);
            board.Load(new FixedTestMapGenerator().Generate(default));
            monsters.DebugHpOverride = 100;
            monsters.Stunned += e => { stunnedEvents++; lastStun = e.Duration; };
            monsters.BeginWave(new[] { new WaveSpawn("normal", 1, 0f, 0f) }, 1);
            monsters.TickSpawn(0f);
            target = monsters.Get(board.MonstersAtPath(0)[0]);
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(normal);

        void Hit(float damage, StatusEffect s) => monsters.ApplyDamage(target.Handle, new DamageInfo(damage, DamageType.Normal, null, 1f, s));

        [Test]
        public void Burn_FiveHitsPerStack_ThreeStacksDeal20Percent_ThenCooldown()
        {
            var burn = new StatusEffect { kind = StatusKind.Burn, hitsPerStack = 5, procStacks = 3, procDamageRatio = 0.2f, cooldown = 1f };
            for (int i = 0; i < 14; i++)
                Hit(1f, burn);
            Assert.AreEqual(86f, target.Hp, 1e-4f, "14번: 화상 2스택 (발동 전)");
            Hit(1f, burn);
            Assert.AreEqual(100f - 15f - 0.2f, target.Hp, 1e-4f, "15번째: 3스택 → 공격력 20% 추가 피해");
            Assert.AreEqual(0, target.BurnStacks, "스택 제거");
            for (int i = 0; i < 15; i++)
                Hit(1f, burn);
            Assert.AreEqual(0, target.BurnStacks + target.BurnHits, "쿨타임 1초 동안은 쌓이지 않음");
            monsters.TickMove(1.01f);
            Hit(1f, burn);
            Assert.AreEqual(1, target.BurnHits, "쿨타임 뒤 다시 센다");
        }

        [Test]
        public void Dust_FiveStacksDeal50Percent_MechanicalBonus30()
        {
            var dust = new StatusEffect { kind = StatusKind.Dust, procStacks = 5, procDamageRatio = 0.5f, cooldown = 5f };
            for (int i = 0; i < 5; i++)
                Hit(2f, dust);
            Assert.AreEqual(100f - 10f - 1f, target.Hp, 1e-4f, "5스택 → 공격력 50% 추가 피해");

            normal.traits = new[] { "mechanical" };
            float hp = target.Hp;
            monsters.ApplyDamage(target.Handle, new DamageInfo(2f, DamageType.Normal, "mechanical", 1.3f, dust));
            Assert.AreEqual(hp - 2.6f, target.Hp, 1e-4f, "기계 +30%");
        }

        [Test]
        public void Phosphorus_StacksBurnEverySecond_CappedAt20Percent()
        {
            var phos = new StatusEffect { kind = StatusKind.Phosphorus, dotPerStack = 0.001f, dotMax = 0.2f, dotInterval = 1f };
            for (int i = 0; i < 10; i++)
                Hit(1f, phos); // 10스택 = 초당 공격력의 1%
            float hp = target.Hp;
            monsters.TickMove(1f);
            Assert.AreEqual(hp - 0.01f, target.Hp, 1e-5f, "10스택: 1초에 공격력 × 1%");

            for (int i = 0; i < 300; i++)
                Hit(0f, phos); // 공격력 0으로 스택만 (기준 공격력도 0이 되므로 아래에서 다시 1로)
            Hit(1f, phos);
            hp = target.Hp;
            monsters.TickMove(1f);
            Assert.AreEqual(hp - 0.2f, target.Hp, 1e-5f, "최대 20%");
        }

        [Test]
        public void Frost_ThreeStacksFreezeOneSecond_BiologicalTwo()
        {
            var frost = new StatusEffect { kind = StatusKind.Frost, procStacks = 3, stunSec = 1f, stunBonusTrait = "biological", stunBonusSec = 1f };
            Hit(1f, frost);
            Hit(1f, frost);
            Assert.AreEqual(0f, target.StunTime);
            Hit(1f, frost);
            Assert.AreEqual(1f, target.StunTime, 1e-4f, "3스택 → 얼음 1초");
            Assert.AreEqual(1, stunnedEvents, "이펙트 이벤트");

            float progress = target.PathProgress;
            monsters.TickMove(0.5f);
            Assert.AreEqual(progress, target.PathProgress, 1e-5f, "얼어 있는 동안 안 움직인다");
            Hit(1f, frost);
            Assert.AreEqual(0, target.FrostStacks, "얼어 있는 동안은 서리가 안 쌓인다");
            monsters.TickMove(0.6f);
            Assert.Greater(target.PathProgress, progress, "풀리면 다시 걷는다 (남은 0.1초만큼)");

            normal.traits = new[] { "biological" };
            for (int i = 0; i < 3; i++)
                Hit(1f, frost);
            Assert.AreEqual(2f, lastStun, 1e-4f, "생물 +1초");
        }

        [Test]
        public void Static_ThreeStacksShock_MechanicalTwoSeconds()
        {
            var shock = new StatusEffect { kind = StatusKind.Static, procStacks = 3, stunSec = 1f, stunBonusTrait = "mechanical", stunBonusSec = 1f };
            normal.traits = new[] { "mechanical" };
            for (int i = 0; i < 3; i++)
                Hit(1f, shock);
            Assert.AreEqual(2f, target.StunTime, 1e-4f, "기계 감전 2초");
            Assert.AreEqual(0, target.StaticStacks);
        }
    }
}
