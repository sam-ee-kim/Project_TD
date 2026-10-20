using NUnit.Framework;
using UnityEditor;
using TowerDefence;

// GameData: 실제 에셋으로 id 조회와 연속 인덱스 부여를 확인한다.
public class GameDataTests
{
    static T Load<T>(string path) where T : UnityEngine.Object
    {
        var asset = AssetDatabase.LoadAssetAtPath<T>(path);
        Assert.IsNotNull(asset, path + " 에셋이 없습니다.");
        return asset;
    }

    [Test]
    public void Init_BuildsLookupAndContinuousIndexes()
    {
        var monsters = new[]
        {
            Load<MonsterDef>("Assets/Data/Monsters/normal.asset"),
            Load<MonsterDef>("Assets/Data/Monsters/fast.asset"),
            Load<MonsterDef>("Assets/Data/Monsters/tank.asset"),
        };
        var towers = new[] { Load<TowerDef>("Assets/Data/Towers/basic.asset") };

        var data = new GameData();
        data.Init(Load<GameConfig>("Assets/Data/GameConfig.asset"), Load<MapGenRules>("Assets/Data/MapGenRules.asset"), monsters, towers);

        for (int i = 0; i < monsters.Length; i++)
        {
            Assert.AreEqual(i, monsters[i].Index, monsters[i].id);
            Assert.AreEqual(i, data.GetMonsterIndex(monsters[i]));
            Assert.IsTrue(data.TryGetMonster(monsters[i].id, out var found));
            Assert.AreSame(monsters[i], found);
        }
        Assert.AreEqual(0, towers[0].Index);
        Assert.IsTrue(data.TryGetTower("basic", out var basic));
        Assert.AreSame(towers[0], basic);
        Assert.IsFalse(data.TryGetMonster("unknown", out _));
    }

    [Test]
    public void Assets_MatchGddTables()
    {
        var normal = Load<MonsterDef>("Assets/Data/Monsters/normal.asset");
        var fast = Load<MonsterDef>("Assets/Data/Monsters/fast.asset");
        var tank = Load<MonsterDef>("Assets/Data/Monsters/tank.asset");
        Assert.AreEqual((MonsterCategory.Normal, 100, 1f, 5, 1), (normal.category, normal.hp, normal.speed, normal.reward, normal.nexusDamage));
        Assert.AreEqual((MonsterCategory.Special, 50, 2f, 4, 1), (fast.category, fast.hp, fast.speed, fast.reward, fast.nexusDamage));
        Assert.AreEqual((MonsterCategory.Special, 300, 0.5f, 10, 2), (tank.category, tank.hp, tank.speed, tank.reward, tank.nexusDamage));

        var basic = Load<TowerDef>("Assets/Data/Towers/basic.asset");
        Assert.AreEqual(50, basic.buildCost);
        Assert.AreEqual(20, basic.damage);
        Assert.AreEqual(1f, basic.interval);
        Assert.AreEqual(2, basic.range);
        Assert.AreEqual(TargetLock.Lock, basic.lockMode);
        Assert.AreEqual(TargetRule.First, basic.targetRule);
        Assert.AreEqual(0, basic.typePriority.Length);
        Assert.AreEqual(AttackType.Projectile, basic.attackType);
        Assert.AreEqual(8f, basic.projectileSpeed);
    }
}
