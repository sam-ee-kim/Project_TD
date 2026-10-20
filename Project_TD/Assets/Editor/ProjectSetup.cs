using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace TowerDefence.EditorTools
{
    // 프로젝트 복구용 원클릭 셋업. 여러 번 눌러도 이미 있는 에셋은 덮어쓰지 않고 빠진 것만 채운다.
    // 데이터 에셋 → 타일·타워·이펙트·몬스터 프리팹 → SampleScene 배치 순서로 만든다.
    public static class ProjectSetup
    {
        const string DataFolder = "Assets/Data";
        const string TileModelFolder = "Assets/Models/Tiles";
        const string TowerModelFolder = "Assets/Models/Towers";
        const string PlaceholderFolder = "Assets/Prefabs/Tiles";
        const string ScenePath = "Assets/Scenes/SampleScene.unity";

        [MenuItem("TowerDefence/Setup Project (All)")]
        public static void SetupAll()
        {
            SetupData();
            SetupTowerPrefabs();
            if (AssetDatabase.LoadAssetAtPath<GameObject>(MuzzleFxBuilder.PrefabPath) == null)
                MuzzleFxBuilder.Build();
            if (AssetDatabase.IsValidFolder("Assets/Models/Monsters"))
                MonsterPrefabBuilder.BuildAll();
            LinkViewPrefabs();
            SetupScene();
            AssetDatabase.SaveAssets();
            Debug.Log("[ProjectSetup] 완료");
        }

        // ---------- 데이터 에셋 ----------

        [MenuItem("TowerDefence/Setup/Data Assets")]
        public static void SetupData()
        {
            EnsureFolder(DataFolder);
            EnsureFolder(DataFolder + "/Monsters");
            EnsureFolder(DataFolder + "/Towers");

            GetOrCreate<GameConfig>(DataFolder + "/GameConfig.asset", null);
            GetOrCreate<MapGenRules>(DataFolder + "/MapGenRules.asset", null);

            GetOrCreate<MonsterDef>(DataFolder + "/Monsters/normal.asset", d =>
            {
                d.id = "normal"; d.displayName = "일반 몬스터"; d.category = MonsterCategory.Normal;
                d.hp = 100; d.speed = 1f; d.reward = 5; d.nexusDamage = 1;
                d.spawnIdleSec = 0.5f; d.viewScale = 0.25f; d.aimHeight = 0.25f;
            });
            GetOrCreate<MonsterDef>(DataFolder + "/Monsters/fast.asset", d =>
            {
                d.id = "fast"; d.displayName = "빠른 몬스터"; d.category = MonsterCategory.Special;
                d.hp = 50; d.speed = 2f; d.reward = 4; d.nexusDamage = 1;
            });
            GetOrCreate<MonsterDef>(DataFolder + "/Monsters/tank.asset", d =>
            {
                d.id = "tank"; d.displayName = "탱크 몬스터"; d.category = MonsterCategory.Special;
                d.hp = 300; d.speed = 0.5f; d.reward = 10; d.nexusDamage = 2;
            });
            GetOrCreate<TowerDef>(DataFolder + "/Towers/basic.asset", d =>
            {
                d.id = "basic"; d.displayName = "기본 타워";
            });
            AssetDatabase.SaveAssets();
        }

        // ---------- 타워 프리팹 (TowerAim 설정) ----------

        [MenuItem("TowerDefence/Setup/Tower Prefabs")]
        public static void SetupTowerPrefabs()
        {
            EnsureFolder("Assets/Prefabs");
            EnsureFolder("Assets/Prefabs/Towers");
            MakeTower("Tower_TwinCannon", "TT", "TT_Head_Eye",
                new[] { "TT_Gun_Muzzle_L", "TT_Gun_Muzzle_R" }, new[] { "TT_Gun_Recoil_L", "TT_Gun_Recoil_R" }, -25f);
            MakeTower("Tower_Flamethrower", "FT", "FT_Flame_Emitter",
                new[] { "FT_Flame_Emitter" }, null, -30f);
        }

        static void MakeTower(string model, string prefix, string muzzleName, string[] fireNames, string[] recoilNames, float minPitch)
        {
            string outPath = "Assets/Prefabs/Towers/" + model + ".prefab";
            if (AssetDatabase.LoadAssetAtPath<GameObject>(outPath) != null)
                return;
            var src = AssetDatabase.LoadAssetAtPath<GameObject>(TowerModelFolder + "/" + model + ".fbx");
            if (src == null)
            {
                Debug.LogWarning("[ProjectSetup] 타워 모델 없음: " + model);
                return;
            }
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(src);
            try
            {
                var aim = inst.AddComponent<TowerAim>();
                aim.yawPivot = Find(inst.transform, prefix + "_Rotation_Plate");
                aim.pitchPivot = Find(inst.transform, prefix + "_Neck_Joint");
                aim.muzzle = Find(inst.transform, muzzleName);
                aim.firePoints = FindAll(inst.transform, fireNames);
                if (recoilNames != null)
                    aim.recoilPivots = FindAll(inst.transform, recoilNames);
                aim.barrelAxis = Vector3.up;
                aim.minPitch = minPitch;
                PrefabUtility.SaveAsPrefabAsset(inst, outPath);
            }
            finally
            {
                Object.DestroyImmediate(inst);
            }
        }

        // 데이터 에셋에 모델·이펙트 프리팹 연결 (비어 있을 때만).
        static void LinkViewPrefabs()
        {
            var basic = AssetDatabase.LoadAssetAtPath<TowerDef>(DataFolder + "/Towers/basic.asset");
            if (basic != null)
            {
                if (basic.viewPrefab == null)
                    basic.viewPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Towers/Tower_TwinCannon.prefab");
                if (basic.projectilePrefab == null)
                    basic.projectilePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(TowerModelFolder + "/Projectile_TwinCannon_Bullet.fbx");
                if (basic.muzzleFxPrefab == null)
                    basic.muzzleFxPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(MuzzleFxBuilder.PrefabPath);
                EditorUtility.SetDirty(basic);
            }

            // 몬스터 모델이 있으면 일반 몬스터에 연결 (없으면 캡슐로 대신 표시된다).
            var normal = AssetDatabase.LoadAssetAtPath<MonsterDef>(DataFolder + "/Monsters/normal.asset");
            var saberfang = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Monsters/Monster_Saberfang.prefab");
            if (normal != null && normal.viewPrefab == null && saberfang != null)
            {
                normal.viewPrefab = saberfang;
                EditorUtility.SetDirty(normal);
            }
        }

        // ---------- 타일 (FBX가 없으면 임시 큐브 타일) ----------

        static GameObject TileModel(string name)
        {
            return AssetDatabase.LoadAssetAtPath<GameObject>(TileModelFolder + "/" + name + ".fbx");
        }

        static void ResolveTiles(out GameObject path, out GameObject buildable, out GameObject[] blocked)
        {
            path = TileModel("DirtPathTile");
            buildable = TileModel("GrassTile");
            var list = new List<GameObject>();
            for (int i = 1; i <= 5; i++)
            {
                var c = TileModel("GrassTile_Center" + i);
                if (c != null) list.Add(c);
                var t = TileModel("GrassTile_Tree" + i);
                if (t != null) list.Add(t);
            }
            blocked = list.ToArray();

            if (path == null) path = Placeholder("Placeholder_Path", new Color(0.55f, 0.45f, 0.33f), false);
            if (buildable == null) buildable = Placeholder("Placeholder_Grass", new Color(0.45f, 0.72f, 0.38f), false);
            if (blocked.Length == 0) blocked = new[] { Placeholder("Placeholder_GrassTree", new Color(0.3f, 0.55f, 0.28f), true) };
        }

        // 1×0.4×1, 피벗이 바닥인 임시 타일. 콜라이더 없음 (물리 사용 금지 규칙).
        static GameObject Placeholder(string name, Color color, bool tree)
        {
            EnsureFolder("Assets/Prefabs");
            EnsureFolder(PlaceholderFolder);
            string path = PlaceholderFolder + "/" + name + ".prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null)
                return existing;

            var root = new GameObject(name);
            try
            {
                var body = Primitive(PrimitiveType.Cube, root.transform, new Vector3(0f, 0.2f, 0f), new Vector3(1f, 0.4f, 1f),
                    Material(name, color));
                body.name = "Body";
                if (tree)
                {
                    Primitive(PrimitiveType.Cylinder, root.transform, new Vector3(0f, 0.55f, 0f), new Vector3(0.12f, 0.15f, 0.12f),
                        Material("Placeholder_Trunk", new Color(0.45f, 0.3f, 0.18f))).name = "Trunk";
                    Primitive(PrimitiveType.Sphere, root.transform, new Vector3(0f, 0.9f, 0f), new Vector3(0.5f, 0.5f, 0.5f),
                        Material("Placeholder_Leaves", new Color(0.2f, 0.45f, 0.2f))).name = "Leaves";
                }
                return PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        static GameObject Primitive(PrimitiveType type, Transform parent, Vector3 pos, Vector3 scale, Material mat)
        {
            var go = GameObject.CreatePrimitive(type);
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            return go;
        }

        static Material Material(string name, Color color)
        {
            string path = PlaceholderFolder + "/" + name + ".mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat != null)
                return mat;
            mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            mat.SetColor("_BaseColor", color);
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        // ---------- SampleScene 배치 ----------

        [MenuItem("TowerDefence/Setup/SampleScene")]
        public static void SetupScene()
        {
            var scene = EditorSceneManager.GetActiveScene();
            if (scene.path != ScenePath)
            {
                if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                    return;
                scene = EditorSceneManager.OpenScene(ScenePath);
            }

            // 3D 맵 (타일이 아래에서 솟아오르는 MapGenerator)
            var mapGen = Object.FindFirstObjectByType<MapGenerator>();
            if (mapGen == null)
                mapGen = new GameObject("MapGenerator").AddComponent<MapGenerator>();
            ResolveTiles(out var pathTile, out var buildTile, out var blockedTiles);
            var soMap = new SerializedObject(mapGen);
            soMap.FindProperty("pathTilePrefab").objectReferenceValue = pathTile;
            soMap.FindProperty("buildableTilePrefab").objectReferenceValue = buildTile;
            var blockedProp = soMap.FindProperty("blockedTilePrefabs");
            blockedProp.arraySize = blockedTiles.Length;
            for (int i = 0; i < blockedTiles.Length; i++)
                blockedProp.GetArrayElementAtIndex(i).objectReferenceValue = blockedTiles[i];
            soMap.ApplyModifiedPropertiesWithoutUndo();

            // 카메라 (탑다운/탑, Tab 전환, WASD)
            var cam = Camera.main;
            if (cam == null)
            {
                var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
                cam = camGo.AddComponent<Camera>();
                camGo.AddComponent<AudioListener>();
            }
            var camCtl = cam.GetComponent<CameraController>();
            if (camCtl == null)
                camCtl = cam.gameObject.AddComponent<CameraController>();
            var soCam = new SerializedObject(camCtl);
            soCam.FindProperty("map").objectReferenceValue = mapGen;
            soCam.ApplyModifiedPropertiesWithoutUndo();

            // UI 캔버스
            var canvas = Object.FindFirstObjectByType<Canvas>();
            if (canvas == null)
            {
                var canvasGo = new GameObject("Canvas") { layer = LayerMask.NameToLayer("UI") };
                canvas = canvasGo.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                var scaler = canvasGo.AddComponent<UnityEngine.UI.CanvasScaler>();
                scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920f, 1080f);
                scaler.matchWidthOrHeight = 0.5f;
                canvasGo.AddComponent<UnityEngine.UI.GraphicRaycaster>();
            }

            if (Object.FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>() == null)
            {
                var es = new GameObject("EventSystem");
                es.AddComponent<UnityEngine.EventSystems.EventSystem>();
                es.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
            }

            // GameLoop (유일한 Update)
            var loop = Object.FindFirstObjectByType<GameLoop>();
            if (loop == null)
                loop = new GameObject("GameLoop").AddComponent<GameLoop>();
            var so = new SerializedObject(loop);
            so.FindProperty("config").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameConfig>(DataFolder + "/GameConfig.asset");
            so.FindProperty("mapGenRules").objectReferenceValue = AssetDatabase.LoadAssetAtPath<MapGenRules>(DataFolder + "/MapGenRules.asset");
            var ids = new[] { "normal", "fast", "tank" };
            var mons = so.FindProperty("monsterDefs");
            mons.arraySize = ids.Length;
            for (int i = 0; i < ids.Length; i++)
                mons.GetArrayElementAtIndex(i).objectReferenceValue = AssetDatabase.LoadAssetAtPath<MonsterDef>(DataFolder + "/Monsters/" + ids[i] + ".asset");
            var tows = so.FindProperty("towerDefs");
            tows.arraySize = 1;
            tows.GetArrayElementAtIndex(0).objectReferenceValue = AssetDatabase.LoadAssetAtPath<TowerDef>(DataFolder + "/Towers/basic.asset");
            so.FindProperty("mainCamera").objectReferenceValue = cam;
            so.FindProperty("canvas").objectReferenceValue = canvas;
            so.FindProperty("mapRenderer").objectReferenceValue = mapGen;
            so.FindProperty("cameraRig").objectReferenceValue = camCtl;
            // 층 전환 테스트용 임시 값 (데이터 에셋은 그대로 둔다)
            so.FindProperty("debugStartCash").intValue = 99999;
            so.FindProperty("debugMonsterHp").intValue = 1;
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        // ---------- 공용 ----------

        static T GetOrCreate<T>(string path, System.Action<T> init) where T : ScriptableObject
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null)
                return asset;
            asset = ScriptableObject.CreateInstance<T>();
            init?.Invoke(asset);
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        static Transform Find(Transform root, string name)
        {
            if (root.name == name)
                return root;
            foreach (Transform c in root)
            {
                var r = Find(c, name);
                if (r != null)
                    return r;
            }
            return null;
        }

        static Transform[] FindAll(Transform root, string[] names)
        {
            var result = new Transform[names.Length];
            for (int i = 0; i < names.Length; i++)
                result[i] = Find(root, names[i]);
            return result;
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;
            int slash = path.LastIndexOf('/');
            EnsureFolder(path.Substring(0, slash));
            AssetDatabase.CreateFolder(path.Substring(0, slash), path.Substring(slash + 1));
        }
    }
}
