using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace TowerDefence.EditorTools
{
    // 로비 도시 (메뉴: TowerDefence/Setup/Lobby City). 여러 번 눌러도 된다 (만든 것을 지우고 다시 만든다).
    //
    // 입력: Tools/Blender/export_lobby_city.py가 SF_City.blend에서 만든 결과
    //   Assets/Models/Lobby/SF_City_Lobby.fbx      : 바닥을 구워 없애고 가로등·신호등 면 수를 줄인 도시 (삼각형 62만 → 28만)
    //   Assets/Models/Lobby/SF_City_Materials.json : 블렌더 재질 색 (Principled BSDF 값)
    //   Assets/Art/Lobby/LobbyCity_Ground.png      : 위에서 찍은 도로·차선·보도 텍스처
    //
    // 렌더링 최적화 (이 도구가 하는 일):
    //   1) 객체 390개를 "건물 묶음 × 재질"로 합친다. 넓게 퍼진 도시 묶음(도로·주변 건물·초고층)은 칸으로 나눠
    //      합쳐서(320m 칸), 화면 밖 칸은 카메라 절두체 컬링으로 빠지게 한다. → 렌더러 수백 개 → 수십 개 (드로우 콜 감소)
    //   2) 재질은 URP Lit으로 새로 만든다 (FBX 기본 재질 변환은 발광·금속값이 어긋난다). 같은 재질은 1개만.
    //   3) 바닥·도로 소품·발광 재질은 그림자를 드리우지 않는다 (그림자 패스 드로우 콜 절약).
    //   4) 합친 메시는 에셋 1개에 모아 저장하고 읽기 전용(isReadable 끔)으로 둔다 (CPU 메모리 복사본 없음).
    //   5) 게임 중에는 도시를 통째로 끈다 (LobbyView.Hide). 그리기·컬링 비용 0.
    public static class LobbyCityBuilder
    {
        const string FbxPath = "Assets/Models/Lobby/SF_City_Lobby.fbx";
        const string MaterialJsonPath = "Assets/Models/Lobby/SF_City_Materials.json";
        const string GroundTexPath = "Assets/Art/Lobby/LobbyCity_Ground.png";
        const string PinPath = "Assets/Art/Lobby/UI_Pin.png";
        const string PinDotPath = "Assets/Art/Lobby/UI_PinDot.png";
        const string PinShadowPath = "Assets/Art/Lobby/UI_PinShadow.png";
        const string MaterialFolder = "Assets/Models/Lobby/Materials";
        const string MeshAssetPath = "Assets/Models/Lobby/Generated/LobbyCity_Meshes.asset";
        const string PrefabPath = "Assets/Prefabs/Lobby/LobbyCity.prefab";
        const string ScenePath = "Assets/Scenes/SampleScene.unity";

        const float ChunkSize = 320f;          // 넓은 도시 묶음을 나누는 칸 (m)
        const int ChunkMinTriangles = 500;     // 이보다 작은 메시는 나누지 않는다 (바닥 사각형 등)
        static readonly Vector3 LobbyOrigin = new Vector3(0f, 0f, -3000f); // 게임 맵(원점)과 겹치지 않게 멀리 둔다

        // 탑언더뷰: 위에서 비스듬히 내려다본다
        const float CameraPitch = 62f;
        const float CameraYaw = 180f;     // 이 방향에서 보면 초고층 빌딩이 카메라 앞을 가리지 않는다
        const float CameraFov = 32f;
        const float CameraMargin = 1.5f;  // 아래쪽 시작 안내 문구에 건물이 가리지 않게 여유

        static readonly string[] SpreadGroups = { "City_Streets", "City_Fillers", "City_Supertalls" };
        static readonly string[] NoShadowGroups = { "City_Streets" };

        struct PinSpec
        {
            public string Group, Label;
            public Color Color;
            public PinSpec(string group, string label, Color color) { Group = group; Label = label; Color = color; }
        }

        static readonly PinSpec[] Pins =
        {
            new PinSpec("SF_SemiconFab", "반도체 공장", new Color(0.62f, 0.42f, 1f)),   // 건물 발광색(보라)
            new PinSpec("SF_Warehouse", "물류창고", new Color(0.18f, 0.78f, 0.78f)),    // 컨테이너 청록
            new PinSpec("SF_MechLab", "공학연구소", new Color(1f, 0.55f, 0.15f)),        // 발광 주황
        };

        class MatInfo
        {
            public Color Base = Color.white;
            public float Metallic, Roughness = 0.5f, EmissionStrength, Alpha = 1f;
            public Color Emission = Color.black;
            public string Texture;
        }

        class Acc
        {
            public string Group, Name;
            public Material Material;
            public bool Shadow;
            public readonly List<Vector3> Vertices = new List<Vector3>();
            public readonly List<Vector3> Normals = new List<Vector3>();
            public readonly List<Vector2> Uvs = new List<Vector2>();
            public readonly List<int> Indices = new List<int>();
            public bool HasUv;
        }

        [MenuItem("TowerDefence/Setup/Lobby City")]
        public static void Build()
        {
            if (!File.Exists(FbxPath) || !File.Exists(MaterialJsonPath))
            {
                Debug.LogError("[LobbyCity] " + FbxPath + " 이 없습니다. Tools/Blender/export_lobby_city.py를 먼저 실행하세요.");
                return;
            }
            ConfigureImporters();
            var infos = ReadMaterials(File.ReadAllText(MaterialJsonPath));
            var materials = new Dictionary<string, Material>();
            foreach (var kv in infos)
                materials[kv.Key] = CreateMaterial(kv.Key, kv.Value);
            var prefab = BuildPrefab(materials, out int renderers, out int triangles);
            PlaceInScene(prefab);
            Debug.Log($"[LobbyCity] 완료: 렌더러 {renderers}개, 삼각형 {triangles:N0}개, 재질 {materials.Count}개");
        }

        // ---------- 가져오기 설정 ----------

        static void ConfigureImporters()
        {
            var model = (ModelImporter)AssetImporter.GetAtPath(FbxPath);
            model.importCameras = false;
            model.importLights = false;
            model.importAnimation = false;
            model.animationType = ModelImporterAnimationType.None;
            model.importBlendShapes = false;
            model.importVisibility = false;
            model.isReadable = true;                 // 합치기용 (런타임은 합친 메시만 쓰므로 FBX는 빌드에 들어가지 않는다)
            model.meshCompression = ModelImporterMeshCompression.Off;
            model.importNormals = ModelImporterNormals.Import;
            model.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription; // 재질 이름으로 짝을 맞춘다
            model.SaveAndReimport();

            var ground = (TextureImporter)AssetImporter.GetAtPath(GroundTexPath);
            ground.textureType = TextureImporterType.Default;
            ground.sRGBTexture = true;
            ground.mipmapEnabled = true;
            ground.wrapMode = TextureWrapMode.Clamp;
            ground.anisoLevel = 8;                   // 비스듬히 보는 바닥의 차선이 뭉개지지 않게
            ground.maxTextureSize = 4096;
            // 2의 거듭제곱(4096×4096)으로 맞춘다: 4096×3240 그대로 두면 작은 밉맵 단계가 4의 배수가 안 돼 압축이 꺼지고 무압축(약 50MB)이 된다
            ground.npotScale = TextureImporterNPOTScale.ToNearest;
            ground.textureCompression = TextureImporterCompression.Compressed; // 알파 없는 RGB → DXT1 (약 11MB, BC7의 절반)
            ground.SaveAndReimport();

            foreach (var path in new[] { PinPath, PinDotPath, PinShadowPath })
            {
                var t = (TextureImporter)AssetImporter.GetAtPath(path);
                t.textureType = TextureImporterType.Sprite;
                t.spriteImportMode = SpriteImportMode.Single;
                t.alphaIsTransparency = true;
                t.mipmapEnabled = false;
                t.textureCompression = TextureImporterCompression.Uncompressed;
                t.SaveAndReimport();
            }
        }

        // ---------- 재질 ----------

        // SF_City_Materials.json: { "materials": { "이름": { "base": [r,g,b,a], ... }, ... } } (값은 선형 색)
        static Dictionary<string, MatInfo> ReadMaterials(string json)
        {
            var result = new Dictionary<string, MatInfo>();
            foreach (Match m in Regex.Matches(json, "\"(\\w+)\":\\s*\\{([^{}]*)\\}"))
            {
                var body = m.Groups[2].Value;
                var info = new MatInfo
                {
                    Base = ColorOf(body, "base", Color.white),
                    Metallic = FloatOf(body, "metallic", 0f),
                    Roughness = FloatOf(body, "roughness", 0.5f),
                    Emission = ColorOf(body, "emission", Color.black),
                    EmissionStrength = FloatOf(body, "emissionStrength", 0f),
                    Alpha = FloatOf(body, "alpha", 1f),
                };
                var tex = Regex.Match(body, "\"texture\":\\s*\"([^\"]+)\"");
                if (tex.Success)
                    info.Texture = tex.Groups[1].Value;
                result[m.Groups[1].Value] = info;
            }
            return result;
        }

        static float FloatOf(string body, string key, float fallback)
        {
            var m = Regex.Match(body, "\"" + key + "\":\\s*(-?[0-9.eE+-]+)");
            return m.Success ? float.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : fallback;
        }

        static Color ColorOf(string body, string key, Color fallback)
        {
            var m = Regex.Match(body, "\"" + key + "\":\\s*\\[([^\\]]*)\\]");
            if (!m.Success)
                return fallback;
            var p = m.Groups[1].Value.Split(',');
            float F(int i) => i < p.Length ? float.Parse(p[i].Trim(), CultureInfo.InvariantCulture) : 1f;
            return new Color(F(0), F(1), F(2), F(3));
        }

        static Material CreateMaterial(string name, MatInfo info)
        {
            Directory.CreateDirectory(MaterialFolder);
            var path = MaterialFolder + "/" + name + ".mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (mat == null)
            {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.shader = shader;
            // 블렌더 값은 선형 색 → 재질 색(감마)으로
            var baseColor = new Color(info.Base.r, info.Base.g, info.Base.b).gamma;
            mat.SetColor("_BaseColor", baseColor);
            mat.SetFloat("_Metallic", info.Metallic);
            mat.SetFloat("_Smoothness", 1f - info.Roughness);
            mat.SetFloat("_EnvironmentReflections", 1f);
            if (!string.IsNullOrEmpty(info.Texture))
                mat.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(info.Texture));
            if (info.EmissionStrength > 0f)
            {
                // 블렌더 발광 세기(3.5~15)를 그대로 쓰면 하얗게 날아가므로 줄여서 HDR 색으로 (블룸이 켜져 있으면 은은하게 번진다)
                float intensity = Mathf.Clamp(info.EmissionStrength * 0.35f, 0.6f, 4f);
                var e = new Color(info.Emission.r, info.Emission.g, info.Emission.b).gamma * intensity;
                mat.SetColor("_EmissionColor", e);
                mat.EnableKeyword("_EMISSION");
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            }
            else
            {
                mat.SetColor("_EmissionColor", Color.black);
                mat.DisableKeyword("_EMISSION");
            }
            mat.enableInstancing = false; // SRP Batcher를 쓴다
            EditorUtility.SetDirty(mat);
            return mat;
        }

        static bool IsGlow(MatInfo info) => info != null && info.EmissionStrength > 1f;

        // ---------- 메시 합치기 → 프리팹 ----------

        static GameObject BuildPrefab(Dictionary<string, Material> materials, out int rendererCount, out int triangleCount)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(FbxPath);
            var temp = Object.Instantiate(model);
            temp.transform.position = Vector3.zero;
            temp.transform.rotation = Quaternion.identity;
            var infos = ReadMaterials(File.ReadAllText(MaterialJsonPath));

            var accs = new Dictionary<string, Acc>();
            var order = new List<Acc>();
            var groupBounds = new Dictionary<string, Bounds>();
            var toRoot = temp.transform.worldToLocalMatrix;

            foreach (Transform group in temp.transform)
            {
                bool spread = System.Array.IndexOf(SpreadGroups, group.name) >= 0;
                bool noShadowGroup = System.Array.IndexOf(NoShadowGroups, group.name) >= 0;
                foreach (var mf in group.GetComponentsInChildren<MeshFilter>(true))
                {
                    var mesh = mf.sharedMesh;
                    var mr = mf.GetComponent<MeshRenderer>();
                    if (mesh == null || mr == null)
                        continue;
                    var m = toRoot * mf.transform.localToWorldMatrix;
                    var nm = m.inverse.transpose;
                    bool flip = m.determinant < 0f;
                    var verts = mesh.vertices;
                    var normals = mesh.normals;
                    var uvs = mesh.uv;
                    bool hasUv = uvs != null && uvs.Length == verts.Length;
                    var shared = mr.sharedMaterials;

                    var world = new Vector3[verts.Length];
                    for (int i = 0; i < verts.Length; i++)
                        world[i] = m.MultiplyPoint3x4(verts[i]);
                    foreach (var w in world)
                        Encapsulate(groupBounds, group.name, w);

                    for (int sub = 0; sub < mesh.subMeshCount; sub++)
                    {
                        string matName = sub < shared.Length && shared[sub] != null ? shared[sub].name : "";
                        if (!materials.TryGetValue(matName, out var mat))
                        {
                            Debug.LogWarning($"[LobbyCity] 재질 표에 없는 재질 '{matName}' ({mf.name}) → 건너뜀");
                            continue;
                        }
                        infos.TryGetValue(matName, out var info);
                        bool shadow = !noShadowGroup && !IsGlow(info);
                        var tris = mesh.GetTriangles(sub);
                        bool chunked = spread && tris.Length / 3 >= ChunkMinTriangles;
                        var remap = new Dictionary<long, int>();
                        for (int t = 0; t < tris.Length; t += 3)
                        {
                            int a = tris[t], b = tris[t + 1], c = tris[t + 2];
                            string key = group.name + "|" + matName;
                            if (chunked)
                            {
                                var centroid = (world[a] + world[b] + world[c]) / 3f;
                                key += "|" + Mathf.FloorToInt(centroid.x / ChunkSize) + "_" + Mathf.FloorToInt(centroid.z / ChunkSize);
                            }
                            if (!accs.TryGetValue(key, out var acc))
                            {
                                acc = new Acc { Group = group.name, Name = key.Replace('|', '_'), Material = mat, Shadow = shadow, HasUv = hasUv };
                                accs.Add(key, acc);
                                order.Add(acc);
                            }
                            acc.HasUv &= hasUv;
                            int ia = Remap(acc, remap, a, world, normals, uvs, hasUv, nm);
                            int ib = Remap(acc, remap, b, world, normals, uvs, hasUv, nm);
                            int ic = Remap(acc, remap, c, world, normals, uvs, hasUv, nm);
                            if (flip) { acc.Indices.Add(ia); acc.Indices.Add(ic); acc.Indices.Add(ib); }
                            else { acc.Indices.Add(ia); acc.Indices.Add(ib); acc.Indices.Add(ic); }
                        }
                    }
                }
            }
            Object.DestroyImmediate(temp);

            // 합친 메시 저장 (에셋 1개)
            Directory.CreateDirectory(Path.GetDirectoryName(MeshAssetPath));
            AssetDatabase.DeleteAsset(MeshAssetPath);
            var root = new GameObject("LobbyCity");
            var groupRoots = new Dictionary<string, Transform>();
            rendererCount = 0;
            triangleCount = 0;
            bool first = true;
            foreach (var acc in order)
            {
                var mesh = new Mesh { name = acc.Name };
                mesh.indexFormat = acc.Vertices.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
                mesh.SetVertices(acc.Vertices);
                mesh.SetNormals(acc.Normals);
                if (acc.HasUv && acc.Uvs.Count == acc.Vertices.Count)
                    mesh.SetUVs(0, acc.Uvs);
                mesh.SetTriangles(acc.Indices, 0);
                mesh.RecalculateBounds();
                MeshUtility.Optimize(mesh);
                if (first)
                {
                    AssetDatabase.CreateAsset(mesh, MeshAssetPath);
                    first = false;
                }
                else
                {
                    AssetDatabase.AddObjectToAsset(mesh, MeshAssetPath);
                }
                var so = new SerializedObject(mesh);
                so.FindProperty("m_IsReadable").boolValue = false; // 런타임 CPU 사본 없음
                so.ApplyModifiedPropertiesWithoutUndo();

                if (!groupRoots.TryGetValue(acc.Group, out var parent))
                {
                    parent = new GameObject(acc.Group).transform;
                    parent.SetParent(root.transform, false);
                    groupRoots.Add(acc.Group, parent);
                }
                var go = new GameObject(acc.Name);
                go.transform.SetParent(parent, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = acc.Material;
                r.shadowCastingMode = acc.Shadow ? ShadowCastingMode.On : ShadowCastingMode.Off;
                r.receiveShadows = true;
                r.lightProbeUsage = LightProbeUsage.Off;
                r.reflectionProbeUsage = ReflectionProbeUsage.BlendProbes;
                r.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
                rendererCount++;
                triangleCount += acc.Indices.Count / 3;
            }

            // 핀 기준점: 건물 묶음의 꼭대기 가운데 바로 위
            foreach (var pin in Pins)
            {
                if (!groupBounds.TryGetValue(pin.Group, out var b) || !groupRoots.TryGetValue(pin.Group, out var parent))
                {
                    Debug.LogError("[LobbyCity] 핀 건물을 찾지 못함: " + pin.Group);
                    continue;
                }
                var anchor = new GameObject("PinAnchor").transform;
                anchor.SetParent(parent, false);
                anchor.localPosition = new Vector3(b.center.x, b.max.y + 3f, b.center.z);
            }

            AssetDatabase.SaveAssets();
            Directory.CreateDirectory(Path.GetDirectoryName(PrefabPath));
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            return prefab;
        }

        static void Encapsulate(Dictionary<string, Bounds> bounds, string group, Vector3 p)
        {
            if (bounds.TryGetValue(group, out var b))
            {
                b.Encapsulate(p);
                bounds[group] = b;
            }
            else
            {
                bounds[group] = new Bounds(p, Vector3.zero);
            }
        }

        static int Remap(Acc acc, Dictionary<long, int> remap, int src, Vector3[] world, Vector3[] normals, Vector2[] uvs, bool hasUv, Matrix4x4 nm)
        {
            // 같은 원본 정점은 같은 묶음 안에서 1번만 넣는다 (묶음마다 번호가 다르므로 묶음 객체와 함께 키로)
            long key = ((long)acc.GetHashCode() << 32) | (uint)src;
            if (remap.TryGetValue(key, out int index))
                return index;
            index = acc.Vertices.Count;
            acc.Vertices.Add(world[src]);
            acc.Normals.Add(normals != null && normals.Length > src ? nm.MultiplyVector(normals[src]).normalized : Vector3.up);
            acc.Uvs.Add(hasUv ? uvs[src] : Vector2.zero);
            remap.Add(key, index);
            return index;
        }

        // ---------- 씬 배치 ----------

        static void PlaceInScene(GameObject prefab)
        {
            var scene = EditorSceneManager.GetActiveScene();
            if (scene.path != ScenePath)
                scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            var old = GameObject.Find("Lobby");
            if (old != null)
                Object.DestroyImmediate(old);
            var lobbyGo = new GameObject("Lobby");
            lobbyGo.transform.position = LobbyOrigin;
            var view = lobbyGo.AddComponent<LobbyView>();
            var city = (GameObject)PrefabUtility.InstantiatePrefab(prefab, lobbyGo.transform);
            city.transform.localPosition = Vector3.zero;

            // 핀 기준점 → 카메라 맞춤
            var anchors = new Transform[Pins.Length];
            var bounds = new Bounds();
            bool any = false;
            for (int i = 0; i < Pins.Length; i++)
            {
                var g = city.transform.Find(Pins[i].Group);
                anchors[i] = g != null ? g.Find("PinAnchor") : null;
                if (g == null)
                    continue;
                foreach (var r in g.GetComponentsInChildren<Renderer>())
                {
                    if (!any) { bounds = r.bounds; any = true; }
                    else bounds.Encapsulate(r.bounds);
                }
            }

            var mainCamera = Camera.main;
            var camGo = new GameObject("LobbyCamera");
            camGo.transform.SetParent(lobbyGo.transform, false);
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = CameraFov;
            cam.nearClipPlane = 1f;
            cam.farClipPlane = 2500f;
            if (mainCamera != null)
            {
                cam.clearFlags = mainCamera.clearFlags;
                cam.backgroundColor = mainCamera.backgroundColor;
                cam.depth = mainCamera.depth;
            }
            var camData = camGo.AddComponent<UniversalAdditionalCameraData>();
            var mainData = mainCamera != null ? mainCamera.GetComponent<UniversalAdditionalCameraData>() : null;
            camData.renderPostProcessing = mainData != null && mainData.renderPostProcessing;
            FitCamera(cam, bounds);
            cam.enabled = false; // LobbyView.Show가 켠다

            var so = new SerializedObject(view);
            so.FindProperty("lobbyCamera").objectReferenceValue = cam;
            so.FindProperty("city").objectReferenceValue = city;
            so.FindProperty("pinSprite").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Sprite>(PinPath);
            so.FindProperty("pinDotSprite").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Sprite>(PinDotPath);
            so.FindProperty("pinShadowSprite").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Sprite>(PinShadowPath);
            var pins = so.FindProperty("pins");
            pins.arraySize = Pins.Length;
            for (int i = 0; i < Pins.Length; i++)
            {
                var p = pins.GetArrayElementAtIndex(i);
                p.FindPropertyRelative("label").stringValue = Pins[i].Label;
                p.FindPropertyRelative("anchor").objectReferenceValue = anchors[i];
                p.FindPropertyRelative("color").colorValue = Pins[i].Color;
            }
            so.ApplyModifiedPropertiesWithoutUndo();

            var loop = Object.FindFirstObjectByType<GameLoop>();
            if (loop != null)
            {
                var lso = new SerializedObject(loop);
                lso.FindProperty("lobby").objectReferenceValue = view;
                lso.ApplyModifiedPropertiesWithoutUndo();
            }
            else
            {
                Debug.LogWarning("[LobbyCity] 씬에 GameLoop가 없어 로비를 연결하지 못했습니다.");
            }
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        // 세 건물이 화면에 다 들어오도록: 위에서 CameraPitch만큼 비스듬히, CameraYaw 방향에서.
        static void FitCamera(Camera cam, Bounds b)
        {
            var size = b.size;
            var rot = Quaternion.Euler(CameraPitch, CameraYaw, 0f);
            bool sideways = Mathf.Abs(Mathf.DeltaAngle(CameraYaw, 90f)) < 45f || Mathf.Abs(Mathf.DeltaAngle(CameraYaw, 270f)) < 45f;
            float wide = (sideways ? size.z : size.x) * 0.5f; // 화면 가로에 놓이는 폭
            float deep = (sideways ? size.x : size.z) * 0.5f;
            float vHalf = CameraFov * 0.5f * Mathf.Deg2Rad;
            float hHalf = Mathf.Atan(Mathf.Tan(vHalf) * (16f / 9f));
            // 가로 폭과 세로 깊이(기울어진 만큼 줄어 보임) 중 더 먼 거리
            float dist = Mathf.Max(wide / Mathf.Tan(hHalf), deep * Mathf.Sin(CameraPitch * Mathf.Deg2Rad) / Mathf.Tan(vHalf)) * CameraMargin;
            var target = new Vector3(b.center.x, b.min.y, b.center.z);
            cam.transform.SetPositionAndRotation(target - rot * Vector3.forward * dist, rot);
        }
    }
}
