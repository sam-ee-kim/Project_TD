using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Tile = TowerDefence.MapBuilderScene.Tile;

namespace TowerDefence.Tests
{
    // 수치 조정 메커니즘: 화염 부채꼴 공격 범위(인식 범위와 따로)·최대 5마리, 분진 폭발 번짐, 미니건 과열·집중 스택, 정지 2회.
    public class TowerMechanicsTests
    {
        GameConfig config;
        MonsterDef normal;
        Board board;
        MonsterManager monsters;
        TowerManager towers;
        PlayerState player;
        readonly List<Object> copies = new List<Object>();

        // 가로 경로: 스타트 (0,1) → 넥서스 (6,1). 위·아래 줄은 잔디 (맵 좌표 = 타일 좌표가 되도록 y = 0부터).
        void SetUp(params TowerDef[] defs)
        {
            config = AssetDatabase.LoadAssetAtPath<GameConfig>("Assets/Data/GameConfig.asset");
            normal = TestAssets.NoIdleCopy("Assets/Data/Monsters/normal.asset");
            normal.hp = 100f;
            copies.Add(normal);
            var tiles = new Dictionary<Vector2Int, Tile>();
            for (int x = 0; x <= 6; x++)
            {
                tiles[new Vector2Int(x, 1)] = x == 0 ? Tile.Spawn : x == 6 ? Tile.Nexus : Tile.Path;
                tiles[new Vector2Int(x, 0)] = Tile.Grass;
                tiles[new Vector2Int(x, 2)] = Tile.Grass;
            }
            var spawns = new List<MapBuilderScene.SpawnSetting> { new MapBuilderScene.SpawnSetting { cell = new Vector2Int(0, 1) } };
            var map = MapBuilderScene.BuildMap(tiles, spawns, out _);

            var data = new GameData();
            data.Init(config, AssetDatabase.LoadAssetAtPath<MapGenRules>("Assets/Data/MapGenRules.asset"), new[] { normal }, defs);
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
            run.Cash = 99999;
            board.ResetForNewRun(run);
            player.ResetForNewRun(run);
            monsters.ResetForNewRun(run);
            towers.ResetForNewRun(run);
            board.Load(map);
            monsters.HandleStateChanged(new StateChange(GameState.Wave, 1, 1));
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in copies)
                Object.DestroyImmediate(o);
            copies.Clear();
        }

        TowerDef Copy(string id)
        {
            var d = Object.Instantiate(AssetDatabase.LoadAssetAtPath<TowerDef>("Assets/Data/Towers/" + id + ".asset"));
            d.name = id;
            d.viewPrefab = null;
            d.projectilePrefab = null;
            d.muzzleFxPrefab = null;
            d.chargeFxPrefab = null;
            d.hitFxPrefab = null;
            copies.Add(d);
            return d;
        }

        // 스타트 칸(0,1)에 몬스터 count마리 (이동하지 않고 그 자리)
        List<MonsterHandle> SpawnAtStart(int count)
        {
            monsters.BeginWave(new[] { new WaveSpawn("normal", count, 0f, 0f) }, 1);
            monsters.TickSpawn(0f);
            var list = new List<MonsterHandle>(board.MonstersAt(board.CellId(new Vector2Int(0, 1))));
            Assert.AreEqual(count, list.Count);
            return list;
        }

        int Damaged(List<MonsterHandle> list)
        {
            int n = 0;
            foreach (var h in list)
                if (monsters.Get(h) != null && monsters.Get(h).Hp < 100f)
                    n++;
            return n;
        }

        [Test]
        public void Flame_ConeHitsAtMostFiveMonsters()
        {
            var flame = Copy("flamethrower");
            Assert.IsTrue(flame.coneWholeArea);
            Assert.AreEqual(5, flame.maxTargets);
            SetUp(flame);
            Assert.IsTrue(towers.Build(new Vector2Int(1, 2), flame, 2)); // 아래(경로 쪽)를 본다: 인식 = (0,1)(1,1)(2,1)
            var list = SpawnAtStart(8);
            towers.TickCombat(0f);
            Assert.AreEqual(5, Damaged(list), "부채꼴 안 8마리 중 가까운 5마리만");
        }

        [Test]
        public void Flame_AttackAreaFollowsCone_NotDetectionRow()
        {
            var flame = Copy("flamethrower");
            SetUp(flame);
            Assert.IsTrue(towers.Build(new Vector2Int(1, 2), flame, 2));
            var tower = towers.GetTowerAt(new Vector2Int(1, 2));
            Assert.IsNotNull(tower.AttackCells);
            Assert.Greater(tower.AttackCells.Length, tower.Covered.Length, "공격 범위(불길 길이 1.6칸 원) ⊃ 인식 범위(앞 한 줄 3칸)");
        }

        [Test]
        public void Dust_BurstSplashesToNearbyMonsters()
        {
            var dust = Copy("dust");
            Assert.AreEqual(1f, dust.status.splashRadius);
            SetUp(dust);
            var list = SpawnAtStart(3);
            var info = new DamageInfo(1f, DamageType.Normal, null, 1f, dust.status);
            for (int i = 0; i < dust.status.procStacks; i++)
                monsters.ApplyDamage(list[0], info);
            float burst = 1f * dust.status.procDamageRatio;
            Assert.AreEqual(100f - dust.status.procStacks - burst, monsters.Get(list[0]).Hp, 1e-4f, "맞은 몬스터: 5타 + 폭발");
            Assert.AreEqual(100f - burst, monsters.Get(list[1]).Hp, 1e-4f, "옆 몬스터도 폭발 피해");
            Assert.AreEqual(100f - burst, monsters.Get(list[2]).Hp, 1e-4f);
        }

        [Test]
        public void NanoField_MonstersInsideTakeMoreDamage()
        {
            var nano = Copy("nano");
            Assert.AreEqual(0.2f, nano.fieldDamageBonus, 1e-4f);
            SetUp(nano);
            var list = SpawnAtStart(1);
            var m = monsters.Get(list[0]);
            monsters.ApplyDamage(list[0], new DamageInfo(10f, DamageType.Normal));
            Assert.AreEqual(90f, m.Hp, 1e-4f, "필드 밖: 그대로");
            monsters.AddSlowField(monsters.GetGroundPoint(m), nano.fieldRadius, nano.fieldSlow, nano.fieldDuration, nano.fieldDamageBonus);
            monsters.ApplyDamage(list[0], new DamageInfo(10f, DamageType.Normal));
            Assert.AreEqual(78f, m.Hp, 1e-4f, "필드 안: 피해 +20%");
        }

        [Test]
        public void Cryo_StunsAfterTwoHits()
        {
            var cryo = Copy("cryo");
            Assert.AreEqual(2, cryo.status.procStacks);
            SetUp(cryo);
            var list = SpawnAtStart(1);
            var info = new DamageInfo(1f, DamageType.Normal, null, 1f, cryo.status);
            monsters.ApplyDamage(list[0], info);
            Assert.AreEqual(0f, monsters.Get(list[0]).StunTime);
            monsters.ApplyDamage(list[0], info);
            Assert.Greater(monsters.Get(list[0]).StunTime, 0f, "두 번째 타에 얼음");
        }

        [Test]
        public void Minigun_SpinTwo_FireFive_OverheatThree()
        {
            var mg = Copy("minigun");
            Assert.AreEqual((2f, 5f, 3f), (mg.spinUpTime, mg.overheatAfter, mg.overheatRest));
            SetUp(mg);
            Assert.IsTrue(towers.Build(new Vector2Int(1, 2), mg, 2));
            var tower = towers.GetTowerAt(new Vector2Int(1, 2));
            var target = SpawnAtStart(1)[0];
            monsters.Get(target).Hp = 100000f;
            float time = 0f;
            const float Dt = 0.01f;
            while (tower.OverheatTime <= 0f && time < 20f)
            {
                towers.TickCombat(Dt);
                time += Dt;
            }
            Assert.That(time, Is.InRange(6.9f, 7.2f), "예열 2초 + 최고 연사 5초 뒤 과열");
            for (int i = 0; i < 50; i++) towers.TickCombat(Dt); // 날던 탄 명중
            float hp = monsters.Get(target).Hp;
            for (int i = 0; i < 240; i++) towers.TickCombat(Dt); // 과열 휴식 3초 중 2.4초
            Assert.AreEqual(hp, monsters.Get(target).Hp, "과열 중에는 쏘지 않는다");
            for (int i = 0; i < 100; i++) towers.TickCombat(Dt);
            Assert.Less(monsters.Get(target).Hp, hp, "휴식이 끝나면 예열부터 다시 쏜다");
            Assert.Less(tower.SpinUp, 1f);
        }

        [Test]
        public void Minigun_FocusStack_AddsPerSecond_ResetsOnNewTarget()
        {
            var mg = Copy("minigun");
            mg.spinUpTime = 0f;      // 예열·과열 없이 집중 스택만 본다
            mg.overheatAfter = 0f;
            mg.interval = 1f;
            mg.damage = 1f;
            SetUp(mg);
            Assert.IsTrue(towers.Build(new Vector2Int(1, 2), mg, 2));
            var tower = towers.GetTowerAt(new Vector2Int(1, 2));
            var target = SpawnAtStart(1)[0];
            float time = 0f;
            while (time < 2.6f)
            {
                towers.TickCombat(0.01f);
                time += 0.01f;
            }
            // 0초 1 + 1초 1.2 + 2초 1.4 = 3.6
            Assert.AreEqual(100f - 3.6f, monsters.Get(target).Hp, 0.01f);
            Assert.That(tower.FocusTime, Is.GreaterThan(2f));

            monsters.ApplyDamage(target, new DamageInfo(1000f, DamageType.Normal)); // 대상 사망
            monsters.Cleanup();
            towers.TickCombat(0.01f);
            Assert.That(tower.FocusTime, Is.LessThan(0.05f), "새 대상은 스택 0부터");
        }
    }
}
