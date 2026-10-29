using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace TowerDefence.EditorTools
{
    // 밸런스 테스트 씬 3개 (Docs/LevelDesign_Balance.md): 테크트리 / 아이템 / 둘 다.
    // SampleScene을 복사해 GameLoop의 GameConfig·타워 목록만 바꾼다. SampleScene과 기본 GameConfig는 그대로 둔다.
    // 여러 번 눌러도 된다: 진화 단계 타워·설정 에셋 값을 다시 맞추고, 씬은 SampleScene에서 새로 복사한다.
    public static class BalanceTestScenes
    {
        const string SampleScenePath = "Assets/Scenes/SampleScene.unity";
        const string TowerFolder = "Assets/Data/Towers";
        const string ConfigFolder = "Assets/Data/BalanceTest";

        // 진화 단계 수치 = BalanceSimulator.TechTree()와 같다 (기준점과 이유는 문서 4-1).
        struct TierSpec
        {
            public string Id, Name, Description, Base, Next;
            public int Tier, TotalCost, Range, Burst, UnlockFloor;
            public float Damage, Interval, ConeLength, ViewScaleMul;
        }

        static readonly TierSpec[] Tiers =
        {
            new TierSpec { Id = "gun2", Base = "basic", Next = "gun3", Name = "쌍열 기관총", Tier = 2, TotalCost = 120, Damage = 1.5f, Interval = 0.6f, Burst = 2, Range = 2, UnlockFloor = 3, ViewScaleMul = 1.12f,
                Description = "총 타워 2단계. 더 빠르게 이점사합니다." },
            new TierSpec { Id = "gun3", Base = "basic", Next = null, Name = "레일 캐논", Tier = 3, TotalCost = 270, Damage = 3f, Interval = 0.5f, Burst = 2, Range = 3, UnlockFloor = 7, ViewScaleMul = 1.25f,
                Description = "총 타워 3단계. 사거리가 1칸 늘어납니다." },
            new TierSpec { Id = "flame2", Base = "flamethrower", Next = "flame3", Name = "화염포", Tier = 2, TotalCost = 170, Damage = 1.4f, Interval = 0.2f, Burst = 1, Range = 2, ConeLength = 2.0f, UnlockFloor = 3, ViewScaleMul = 1.12f,
                Description = "화염방사 2단계. 불길이 더 멀리, 더 뜨겁게 닿습니다." },
            new TierSpec { Id = "flame3", Base = "flamethrower", Next = null, Name = "플라즈마 방사기", Tier = 3, TotalCost = 370, Damage = 3.2f, Interval = 0.2f, Burst = 1, Range = 3, ConeLength = 2.4f, UnlockFloor = 7, ViewScaleMul = 1.25f,
                Description = "화염방사 3단계. 가장 넓은 부채꼴로 태웁니다." },
        };

        static readonly (string scene, string config, bool tech, bool items)[] Variants =
        {
            ("Assets/Scenes/Test_TechTree.unity", "GameConfig_TechTree", true, false),
            ("Assets/Scenes/Test_Items.unity", "GameConfig_Items", false, true),
            ("Assets/Scenes/Test_TechTreeItems.unity", "GameConfig_TechTreeItems", true, true),
        };

        [MenuItem("TowerDefence/Setup/Balance Test Scenes")]
        public static void Build()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;
            SetupTierDefs();
            SetupConfigs();
            AssetDatabase.SaveAssets();

            foreach (var v in Variants)
            {
                if (AssetDatabase.LoadAssetAtPath<SceneAsset>(v.scene) != null)
                    AssetDatabase.DeleteAsset(v.scene);
                AssetDatabase.CopyAsset(SampleScenePath, v.scene);
                var scene = EditorSceneManager.OpenScene(v.scene);
                // 씬을 열면 참조 없는 에셋 인스턴스가 내려가므로 연 뒤에 다시 불러온다.
                var config = AssetDatabase.LoadAssetAtPath<GameConfig>($"{ConfigFolder}/{v.config}.asset");
                var loop = Object.FindFirstObjectByType<GameLoop>();
                var so = new SerializedObject(loop);
                so.FindProperty("config").objectReferenceValue = config;
                var ids = v.tech ? new[] { "basic", "flamethrower", "lightning", "nano", "emp", "minigun", "electro", "cryo", "dust", "phosphorus", "plasma", "gun2", "gun3", "flame2", "flame3" } : new[] { "basic", "flamethrower", "lightning", "nano", "emp", "minigun", "electro", "cryo", "dust", "phosphorus", "plasma" };
                var tows = so.FindProperty("towerDefs");
                tows.arraySize = ids.Length;
                for (int i = 0; i < ids.Length; i++)
                    tows.GetArrayElementAtIndex(i).objectReferenceValue = AssetDatabase.LoadAssetAtPath<TowerDef>($"{TowerFolder}/{ids[i]}.asset");
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }

            EditorSceneManager.OpenScene(SampleScenePath);
            Debug.Log("[BalanceTestScenes] Test_TechTree / Test_Items / Test_TechTreeItems 씬을 만들었습니다 (SampleScene은 그대로).");
        }

        // 진화 단계 TowerDef (기본 타워를 복사해 모델·이펙트·건축 연출은 그대로, 크기만 조금 키운다).
        static void SetupTierDefs()
        {
            foreach (var t in Tiers)
            {
                var baseDef = AssetDatabase.LoadAssetAtPath<TowerDef>($"{TowerFolder}/{t.Base}.asset");
                string path = $"{TowerFolder}/{t.Id}.asset";
                var def = AssetDatabase.LoadAssetAtPath<TowerDef>(path);
                if (def == null)
                {
                    def = Object.Instantiate(baseDef);
                    AssetDatabase.CreateAsset(def, path);
                }
                else
                {
                    EditorUtility.CopySerialized(baseDef, def);
                }
                def.name = t.Id;
                def.id = t.Id;
                def.displayName = t.Name;
                def.description = t.Description;
                def.tier = t.Tier;
                def.unlockFloor = t.UnlockFloor;
                def.buildCost = t.TotalCost;
                def.damage = t.Damage;
                def.interval = t.Interval;
                def.burstCount = t.Burst;
                def.range = t.Range;
                if (t.ConeLength > 0f)
                    def.coneLength = t.ConeLength;
                def.viewScale = baseDef.viewScale * t.ViewScaleMul;
                def.upgradeTo = t.Next != null ? new[] { t.Next } : new string[0];
                EditorUtility.SetDirty(def);
            }
            // 1단계 → 2단계 연결 (테크트리가 꺼진 SampleScene에서는 쓰이지 않는다)
            SetUpgrade("basic", "gun2");
            SetUpgrade("flamethrower", "flame2");
        }

        static void SetUpgrade(string id, string next)
        {
            var def = AssetDatabase.LoadAssetAtPath<TowerDef>($"{TowerFolder}/{id}.asset");
            def.upgradeTo = new[] { next };
            def.tier = 1;
            def.unlockFloor = 1;
            EditorUtility.SetDirty(def);
        }

        // 기본 GameConfig를 복사해 테크트리·아이템 플래그만 켠다.
        static void SetupConfigs()
        {
            if (!AssetDatabase.IsValidFolder(ConfigFolder))
                AssetDatabase.CreateFolder("Assets/Data", "BalanceTest");
            var baseConfig = AssetDatabase.LoadAssetAtPath<GameConfig>("Assets/Data/GameConfig.asset");
            foreach (var v in Variants)
            {
                string path = $"{ConfigFolder}/{v.config}.asset";
                var config = AssetDatabase.LoadAssetAtPath<GameConfig>(path);
                if (config == null)
                {
                    config = Object.Instantiate(baseConfig);
                    AssetDatabase.CreateAsset(config, path);
                }
                else
                {
                    EditorUtility.CopySerialized(baseConfig, config);
                }
                config.name = v.config;
                config.upgradeEnabled = v.tech;
                config.floorItems.enabled = v.items;
                config.floorItems.damagePerItem = 0.10f;
                config.floorItems.speedPerItem = 0.08f;
                foreach (var b in config.ui.bottomButtons)
                    if (b.id == "upgrade")
                    {
                        b.enabled = v.tech;
                        b.label = "타워 진화";
                    }
                EditorUtility.SetDirty(config);
            }
        }
    }
}
