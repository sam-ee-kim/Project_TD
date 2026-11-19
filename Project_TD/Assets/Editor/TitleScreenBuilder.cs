using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace TowerDefence.EditorTools
{
    // 게임 시작 화면(타이틀) 구도 씬: Assets/Scenes/Test_TitleScreen.unity
    // 게임에서 쓰는 타일(흙길·잔디·나무), 타워·몬스터 모델, 공격 이펙트(FX 프리팹)로 한 장면을 짠다.
    //   몬스터 떼: 저 멀리에서 길을 따라 앞으로 몰려오고, 맨 앞 한 마리는 꺾여 오른쪽으로 지나가는 중
    //   타워 여러 대: 길 양옆에서 몬스터를 조준해 일제히 쏘는 중 (발사 섬광 · 날아가는 탄 · 맞은 자리 폭발)
    //   빛: 노을 역광(저 멀리에서 카메라 쪽으로) + 폭발 불빛
    // 지금은 구도 검수용 정지 장면 (파티클은 한 순간으로 시뮬레이션해 찍는다). 검수가 끝나면 반복 애니메이션을 붙인다.
    // 메뉴: TowerDefence/Setup/Title Screen Scene (씬 만들기) · Title Screen Render (Temp/TitleScreen.png로 찍기)
    public static class TitleScreenBuilder
    {
        const string ScenePath = "Assets/Scenes/Title.unity"; // 빌드 첫 씬 (클릭 → SampleScene 로비)
        public const int Seed = 7;

        // 경로: 저 멀리에서 카메라 왼쪽 옆까지 곧게 (칸 좌표 x, z). 길 오른쪽에서 비스듬히 보면 몬스터 떼가 대각선으로 펼쳐진다
        public static readonly Vector2Int[] PathCorners = { new Vector2Int(-3, 34), new Vector2Int(-3, -6) }; // 저 멀리에서 카메라 왼쪽으로 곧게

        public struct MonsterSpot { public string Id; public Vector3 Pos; public float Yaw; public float Phase; }
        public struct TowerSpot { public string Id; public Vector2Int Cell; public int Target; public bool Boom; }

        public static float MonsterScale = 1.5f; // 시작 화면에서는 몬스터를 게임보다 크게 (떼가 화면을 채우게)

        public static MonsterSpot[] Monsters =
        {
            new MonsterSpot { Id = "normal", Pos = new Vector3(-3f, 0f, 2.3f), Yaw = 180f, Phase = 0.3f },     // 맨 앞: 카메라 쪽으로 달려옴
            new MonsterSpot { Id = "tank", Pos = new Vector3(-3.2f, 0f, 4.2f), Yaw = 180f, Phase = 0.1f },   // 바스티온
            new MonsterSpot { Id = "fast", Pos = new Vector3(-2.75f, 0f, 5.3f), Yaw = 180f, Phase = 0.6f },  // 해머헤드
            new MonsterSpot { Id = "normal", Pos = new Vector3(-3.25f, 0f, 6.6f), Yaw = 180f, Phase = 0.85f },
            new MonsterSpot { Id = "urantank", Pos = new Vector3(-2.8f, 0f, 8f), Yaw = 180f, Phase = 0.2f },
            new MonsterSpot { Id = "normal", Pos = new Vector3(-3.2f, 0f, 9.5f), Yaw = 180f, Phase = 0.7f },
            new MonsterSpot { Id = "fast", Pos = new Vector3(-2.8f, 0f, 10.8f), Yaw = 180f, Phase = 0.4f },
            new MonsterSpot { Id = "tank", Pos = new Vector3(-3.1f, 0f, 12.4f), Yaw = 180f, Phase = 0.5f },
            new MonsterSpot { Id = "normal", Pos = new Vector3(-2.85f, 0f, 14f), Yaw = 180f, Phase = 0.15f },
            new MonsterSpot { Id = "fast", Pos = new Vector3(-3.1f, 0f, 15.6f), Yaw = 180f, Phase = 0.9f },
            new MonsterSpot { Id = "urantank", Pos = new Vector3(-2.9f, 0f, 17.4f), Yaw = 180f, Phase = 0.35f },
            new MonsterSpot { Id = "normal", Pos = new Vector3(-3.1f, 0f, 19.2f), Yaw = 180f, Phase = 0.55f },
        };

        public static TowerSpot[] Towers =
        {
            new TowerSpot { Id = "basic", Cell = new Vector2Int(1, 5), Target = 0, Boom = true },
            new TowerSpot { Id = "minigun", Cell = new Vector2Int(0, 9), Target = 4, Boom = true },
            new TowerSpot { Id = "flamethrower", Cell = new Vector2Int(-4, 5), Target = 2 },
            new TowerSpot { Id = "lightning", Cell = new Vector2Int(-1, 13), Target = 7, Boom = true },
            new TowerSpot { Id = "cryo", Cell = new Vector2Int(-5, 9), Target = 5, Boom = true },
            new TowerSpot { Id = "electro", Cell = new Vector2Int(-5, 15), Target = 9, Boom = true },
            new TowerSpot { Id = "basic", Cell = new Vector2Int(-1, 18), Target = 10, Boom = true },
            new TowerSpot { Id = "basic", Cell = new Vector2Int(-1, 4), Target = 1, Boom = true },   // 화면 오른쪽 앞 큰 타워
            new TowerSpot { Id = "plasma", Cell = new Vector2Int(-1, 7), Target = 3 },
            new TowerSpot { Id = "dust", Cell = new Vector2Int(-1, 11), Target = 6 },
            new TowerSpot { Id = "minigun", Cell = new Vector2Int(2, 9), Target = 5, Boom = true },
            new TowerSpot { Id = "phosphorus", Cell = new Vector2Int(-5, 12), Target = 7 },
            new TowerSpot { Id = "emp", Cell = new Vector2Int(1, 15), Target = 8, Boom = true },
            new TowerSpot { Id = "nano", Cell = new Vector2Int(-6, 6), Target = 3 },
            new TowerSpot { Id = "lightning", Cell = new Vector2Int(-6, 18), Target = 11, Boom = true },
            // 길 왼쪽 바로 옆 (인게임처럼 길에 붙은 칸)
            new TowerSpot { Id = "minigun", Cell = new Vector2Int(-4, 2), Target = 1, Boom = true },
            new TowerSpot { Id = "basic", Cell = new Vector2Int(-5, 4), Target = 2, Boom = true },
            new TowerSpot { Id = "lightning", Cell = new Vector2Int(-4, 7), Target = 3, Boom = true },
        };

        public static Vector3 CameraPos = new Vector3(-1.523f, 0.31f, 0.666f); // 씬 뷰에서 고른 각도 (조금 뒤로 빼 오른쪽 타워까지)
        public static Vector3 CameraLook = new Vector3(-1.523f - 4.08f, 0.31f + 1.68f, 0.666f + 8.98f);
        public static float CameraFov = 62f;
        public static float ClearRadius = 3f;

        [MenuItem("TowerDefence/Setup/Title Screen Scene")]
        public static void BuildScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Build();
            EditorSceneManager.SaveScene(scene, ScenePath);
            // 빌드 씬 순서: 시작 화면 → 게임(로비)
            var list = new List<EditorBuildSettingsScene> { new EditorBuildSettingsScene(ScenePath, true) };
            foreach (var s in EditorBuildSettings.scenes)
                if (s.path != ScenePath && !s.path.EndsWith("Test_TitleScreen.unity"))
                    list.Add(s);
            EditorBuildSettings.scenes = list.ToArray();
        }

        [MenuItem("TowerDefence/Setup/Title Screen Render")]
        public static void RenderMenu() => Render(Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Temp", "TitleScreen.png")), 1920, 1080);

        static GameObject Load(string path) => AssetDatabase.LoadAssetAtPath<GameObject>(path);
        static TowerDef TowerDefOf(string id) => AssetDatabase.LoadAssetAtPath<TowerDef>("Assets/Data/Towers/" + id + ".asset");
        static MonsterDef MonsterDefOf(string id) => AssetDatabase.LoadAssetAtPath<MonsterDef>("Assets/Data/Monsters/" + id + ".asset");

        // 나무를 두지 않는 칸: 카메라 둘레, 타워 둘레, 카메라에서 맨 앞 몬스터·가까운 타워를 보는 시선
        static bool Clear(int x, int z)
        {
            var c = new Vector2(x, z);
            var cam = new Vector2(CameraPos.x, CameraPos.z);
            if (Vector2.Distance(c, cam) < ClearRadius)
                return true;
            foreach (var t in Towers)
                if (Mathf.Abs(t.Cell.x - x) <= 1 && Mathf.Abs(t.Cell.y - z) <= 1)
                    return true;
            if (x >= -2 && x <= 2 && z <= 20) // 길과 오른쪽 타워 사이: 트인 전장
                return true;
            if (x <= -5 && x >= -16 && z <= 9) // 길 왼쪽 앞 들판: 왼쪽 타워 줄이 보이게
                return true;
            return false;
        }

        public static GameObject Build()
        {
            var old = GameObject.Find("TitleScreen");
            if (old != null)
                Object.DestroyImmediate(old);
            var oldLoop = GameObject.Find("GameLoop");
            if (oldLoop != null)
                Object.DestroyImmediate(oldLoop);
            var root = new GameObject("TitleScreen").transform;
            BuildGround(root);

            // ---- 몬스터 떼 (걷는 동작의 서로 다른 순간) ----
            var monsterObjs = new List<GameObject>();
            foreach (var m in Monsters)
            {
                var def = MonsterDefOf(m.Id);
                var go = (GameObject)PrefabUtility.InstantiatePrefab(def.viewPrefab, root);
                go.name = "Monster_" + m.Id;
                go.transform.SetPositionAndRotation(m.Pos, Quaternion.Euler(0f, m.Yaw, 0f));
                go.transform.localScale = Vector3.one * def.viewScale * MonsterScale;
                var animator = go.GetComponent<Animator>();
                if (animator != null && animator.runtimeAnimatorController != null)
                {
                    AnimationClip walk = null;
                    foreach (var clip in animator.runtimeAnimatorController.animationClips)
                        if (walk == null || clip.name.ToLower().Contains("walk") || clip.name.ToLower().Contains("run"))
                            walk = clip;
                    if (walk != null)
                        walk.SampleAnimation(go, walk.length * m.Phase);
                }
                monsterObjs.Add(go);
            }

            // ---- 타워: 조준 → 발사 섬광 → 날아가는 탄 → 맞은 자리 폭발 ----
            var fx = new GameObject("StaticFX").transform; // 구도 시안용 정지 이펙트 (재생하면 TitleScreenView가 끈다)
            fx.SetParent(root, false);
            var towerSlots = new List<TitleScreenView.TowerSlot>();
            var flash = Load("Assets/Prefabs/FX/FX_MuzzleFlash.prefab");
            var sparks = Load("Assets/Prefabs/FX/FX_MinigunHit.prefab");
            var tracer = Load("Assets/Prefabs/FX/FX_MinigunTracer.prefab");
            for (int i = 0; i < Towers.Length; i++)
            {
                var spot = Towers[i];
                var def = TowerDefOf(spot.Id);
                var tower = (GameObject)PrefabUtility.InstantiatePrefab(def.viewPrefab, root);
                tower.name = "Tower_" + spot.Id;
                tower.transform.position = new Vector3(spot.Cell.x, 0f, spot.Cell.y);
                tower.transform.localScale = Vector3.one * def.viewScale;
                var targetObj = monsterObjs[spot.Target];
                var target = Center(targetObj);
                var aim = tower.GetComponent<TowerAim>();
                if (aim == null)
                    continue;
                AimAt(aim, target);
                towerSlots.Add(MakeTowerSlot(def, aim, spot, i, flash));
                var fire = aim.firePoints != null && aim.firePoints.Length > 0 ? aim.firePoints[0].position : aim.muzzle.position;
                var dir = (target - fire).normalized;
                var look = Quaternion.LookRotation(dir);

                if (def.attackType == AttackType.Cone || def.attackType == AttackType.Line) // 화염·분진·백린·플라즈마: 뿜는 줄기
                {
                    Fx(def.muzzleFxPrefab, fx, fire, look, 1f, 0.14f);
                }
                else
                {
                    if (def.muzzleFxPrefab != null)
                        Fx(def.muzzleFxPrefab, fx, fire, look, 1.8f, 0.03f);
                    Fx(flash, fx, fire + dir * 0.05f, look, 1.2f, 0.02f);
                }

                // 날아가는 탄 (탄 모델이 있는 타워는 2발, 미니건은 예광탄 줄기)
                if (spot.Id == "minigun")
                    Fx(tracer, fx, fire, look, 1f, 0.35f);
                else if (def.projectilePrefab != null && def.attackType == AttackType.Projectile)
                    for (int k = 0; k < 2; k++)
                    {
                        var p = (GameObject)PrefabUtility.InstantiatePrefab(def.projectilePrefab, fx);
                        p.transform.position = Vector3.Lerp(fire, target, k == 0 ? 0.35f : 0.7f);
                        p.transform.rotation = look * Quaternion.FromToRotation(def.projectileModelForward, Vector3.forward);
                        p.transform.localScale = Vector3.one * def.projectileScale * 0.7f;
                        foreach (var ps in p.GetComponentsInChildren<ParticleSystem>())
                            ps.Simulate(0.3f, false, true);
                    }
                else if (def.projectilePrefab != null)
                    Fx(def.projectilePrefab, fx, Vector3.Lerp(fire, target, 0.5f), look, 1.6f, 0.1f); // 번개 줄기 등

                // 맞은 자리: 타워 고유 피격 이펙트 + 폭발 (섬광 크게 + 불꽃 + 불빛)
                if (def.hitFxPrefab != null)
                    Fx(def.hitFxPrefab, fx, target, Quaternion.LookRotation(-dir), 2.2f, 0.06f);
                if (spot.Id == "dust" || spot.Id == "phosphorus") // 폭발은 분진·백린 스택이 찼을 때만
                {
                    var at = target - dir * 0.15f;
                    if (spot.Target == 0) // 맨 앞 몬스터는 가리지 않게: 바로 옆 땅에 떨어진 탄이 터지는 순간
                        at = Monsters[0].Pos + new Vector3(0.95f, 0.1f, 0.35f); // 몬스터 오른쪽(타워 쪽) 바로 옆
                    Boom(fx, flash, sparks, at, i % 2 == 0 ? 0.75f : 0.55f);
                }
            }

            // ---- 빛: 노을 역광 · 짙은 안개 ----
            var light = new GameObject("Sun").AddComponent<Light>();
            light.transform.SetParent(root, false);
            light.type = LightType.Directional;
            light.transform.rotation = Quaternion.Euler(16f, 200f, 0f); // 저 멀리 왼쪽에서 카메라 쪽으로 낮게
            light.color = new Color(1f, 0.55f, 0.3f);
            light.intensity = 1.15f;
            light.shadows = LightShadows.Soft;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.56f, 0.46f, 0.6f);
            RenderSettings.ambientEquatorColor = new Color(0.46f, 0.36f, 0.38f);
            RenderSettings.ambientGroundColor = new Color(0.1f, 0.08f, 0.08f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(0.95f, 0.62f, 0.45f); // 지평선 하늘색에 맞춤
            RenderSettings.fogStartDistance = 12f;
            RenderSettings.fogEndDistance = 42f;

            var cam = new GameObject("TitleCamera").AddComponent<Camera>();
            cam.transform.SetParent(root, false);
            cam.tag = "MainCamera";
            cam.transform.position = CameraPos;
            cam.transform.rotation = Quaternion.LookRotation(CameraLook - CameraPos) * Quaternion.Euler(0f, 0f, -4f); // 살짝 기울여 긴장감
            cam.fieldOfView = CameraFov;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 80f;
            var sky = SunsetSky();
            RenderSettings.skybox = sky;
            RenderSettings.sun = light;
            cam.clearFlags = CameraClearFlags.Skybox;
            var camData = cam.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>()
                          ?? cam.gameObject.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
            camData.renderPostProcessing = true;
            cam.gameObject.AddComponent<AudioListener>();

            // ---- 반복 애니메이션 연결 (재생하면 GameLoop가 TitleScreenView.Tick을 부른다) ----
            var template = Fireball(root, Vector3.zero, 1f);
            template.name = "FireballTemplate";
            var boomLight = new GameObject("BoomLight").AddComponent<Light>();
            boomLight.transform.SetParent(template, false);
            boomLight.transform.localPosition = Vector3.up * 0.2f;
            boomLight.type = LightType.Point;
            boomLight.color = new Color(1f, 0.45f, 0.15f);
            boomLight.range = 2f;
            boomLight.intensity = 0f;
            template.gameObject.SetActive(false);

            var view = root.gameObject.AddComponent<TitleScreenView>();
            var so = new SerializedObject(view);
            so.FindProperty("cam").objectReferenceValue = cam;
            so.FindProperty("staticFx").objectReferenceValue = fx.gameObject;
            so.FindProperty("fireballTemplate").objectReferenceValue = template.gameObject;
            so.FindProperty("flashPrefab").objectReferenceValue = flash;
            so.FindProperty("sparksPrefab").objectReferenceValue = sparks;
            so.FindProperty("burningPrefab").objectReferenceValue = EnsureBurningFx(true);
            var ms = so.FindProperty("monsters");
            ms.arraySize = monsterObjs.Count;
            for (int i = 0; i < monsterObjs.Count; i++)
            {
                var e = ms.GetArrayElementAtIndex(i);
                e.FindPropertyRelative("root").objectReferenceValue = monsterObjs[i].transform;
                e.FindPropertyRelative("animator").objectReferenceValue = monsterObjs[i].GetComponent<Animator>();
                e.FindPropertyRelative("laneX").floatValue = Monsters[i].Pos.x;
                e.FindPropertyRelative("centerHeight").floatValue = Center(monsterObjs[i]).y;
                e.FindPropertyRelative("hitsToDie").floatValue = 150f + (i * 37) % 100; // 타워 15대가 몰아 쏴도 대부분은 카메라 옆까지 걸어오고, 가끔 쓰러진다
            }
            var ts = so.FindProperty("towers");
            ts.arraySize = towerSlots.Count;
            for (int i = 0; i < towerSlots.Count; i++)
            {
                var t = towerSlots[i];
                var e = ts.GetArrayElementAtIndex(i);
                e.FindPropertyRelative("aim").objectReferenceValue = t.aim;
                e.FindPropertyRelative("kind").enumValueIndex = (int)t.kind;
                e.FindPropertyRelative("interval").floatValue = t.interval;
                e.FindPropertyRelative("muzzleFx").objectReferenceValue = t.muzzleFx;
                e.FindPropertyRelative("muzzleScale").floatValue = t.muzzleScale;
                e.FindPropertyRelative("projectile").objectReferenceValue = t.projectile;
                e.FindPropertyRelative("projectileScale").floatValue = t.projectileScale;
                e.FindPropertyRelative("projectileForward").vector3Value = t.projectileForward;
                e.FindPropertyRelative("hitFx").objectReferenceValue = t.hitFx;
                e.FindPropertyRelative("hitScale").floatValue = t.hitScale;
                e.FindPropertyRelative("burns").boolValue = t.burns;
                e.FindPropertyRelative("stackBoom").boolValue = t.stackBoom;
                e.FindPropertyRelative("boomSize").floatValue = t.boomSize;
                e.FindPropertyRelative("range").floatValue = t.range;
            }
            so.ApplyModifiedPropertiesWithoutUndo();

            if (Object.FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>() == null)
            {
                var es = new GameObject("EventSystem");
                es.AddComponent<UnityEngine.EventSystems.EventSystem>();
                es.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
            }

            var loop = new GameObject("GameLoop").AddComponent<GameLoop>();
            var lso = new SerializedObject(loop);
            lso.FindProperty("titleScreen").objectReferenceValue = view;
            lso.ApplyModifiedPropertiesWithoutUndo();
            return root.gameObject;
        }

        // 타워 종류별 발사 방식: 화염·분진·백린·플라즈마 = 계속 뿜기, EMP = 파동, 나머지 = 탄
        static TitleScreenView.TowerSlot MakeTowerSlot(TowerDef def, TowerAim aim, TowerSpot spot, int index, GameObject flash)
        {
            var t = new TitleScreenView.TowerSlot
            {
                aim = aim,
                muzzleFx = def.muzzleFxPrefab != null ? def.muzzleFxPrefab : flash,
                projectile = def.projectilePrefab,
                projectileScale = def.projectileScale * 0.7f,
                projectileForward = def.projectileModelForward,
                hitFx = def.id == "basic" || def.id == "minigun" || def.id == "gun2" || def.id == "gun3"
                    ? EnsureBloodFx()                                                   // 일반 탄(듀얼샷·미니건): 피가 튄다
                    : def.hitFxPrefab != null ? def.hitFxPrefab : EnsureBloodFx(),
                hitScale = def.id == "basic" || def.id == "minigun" || def.hitFxPrefab == null ? 1.5f : 2.2f, // 피는 1.5배 (멀리서도 보이게)
                burns = def.attackType == AttackType.Cone || def.attackType == AttackType.Line, // 화염·분진·백린·플라즈마
                stackBoom = def.id == "dust" || def.id == "phosphorus",                         // 스택이 차면 폭발
                boomSize = 0.8f,
                range = 9f,
            };
            if (def.attackType == AttackType.Cone || def.attackType == AttackType.Line)
            {
                t.kind = TitleScreenView.FireKind.Spray;
                t.interval = 0.18f;
                t.muzzleScale = 1f;
                t.range = 5f;
            }
            else if (def.attackType == AttackType.Pulse)
            {
                t.kind = TitleScreenView.FireKind.Pulse;
                t.interval = 1.6f;
                t.muzzleScale = 1f;
            }
            else
            {
                t.kind = TitleScreenView.FireKind.Projectile;
                t.interval = def.id == "minigun" ? 0.12f : Mathf.Clamp(def.interval, 0.35f, 1.2f);
                t.muzzleScale = 1.1f; // 크게 하면 총구 연기가 화면을 뿌옇게 덮는다
            }
            return t;
        }

        // 불에 타는 상태 파티클: 화염방사 이펙트(FX_FlameSpray90.fbx)의 큐브 3종(심·중간·겉)을 몸에서 위로 피워 올린다.
        // Assets/Prefabs/FX/FX_StatusBurning.prefab (없을 때만 만든다. 게임의 화상 상태에도 쓸 수 있게 따로 둔다)
        const string BurningPath = "Assets/Prefabs/FX/FX_StatusBurning.prefab";

        public static GameObject EnsureBurningFx(bool rebuild = false)
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(BurningPath);
            if (existing != null && !rebuild)
                return existing;
            Mesh core = null, mid = null, outer = null;
            Material coreMat = null, midMat = null, outerMat = null;
            foreach (var a in AssetDatabase.LoadAllAssetsAtPath("Assets/Models/FX/FX_FlameSpray90.fbx"))
            {
                if (a is Mesh m)
                {
                    if (m.name.EndsWith("Chunk_Core")) core = m;
                    else if (m.name.EndsWith("Chunk_Mid")) mid = m;
                    else if (m.name.EndsWith("Chunk_Outer")) outer = m;
                }
                else if (a is Material mt)
                {
                    if (mt.name.Contains("Core")) coreMat = mt;
                    else if (mt.name.Contains("Mid")) midMat = mt;
                    else if (mt.name.Contains("Outer")) outerMat = mt;
                }
            }
            Mesh smoke = null;
            foreach (var a in AssetDatabase.LoadAllAssetsAtPath("Assets/Models/FX/FX_Phosphorus.fbx"))
                if (a is Mesh m && m.name.EndsWith("Smoke")) smoke = m;
            var smokeMat = Mat("Burn_Smoke", new Color(0.22f, 0.19f, 0.19f), false);

            var root = new GameObject("FX_StatusBurning");
            // 루트 파티클(방출 없음): Play·Stop 한 번으로 아래 층이 함께 켜지고 꺼진다
            var rootPs = root.AddComponent<ParticleSystem>();
            rootPs.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var rm = rootPs.main;
            rm.loop = true;
            rm.playOnAwake = false;
            var rem = rootPs.emission;
            rem.enabled = false;
            root.GetComponent<ParticleSystemRenderer>().enabled = false;
            // 겉(빨강·크고 느림) → 중간(주황) → 심(노랑·작고 빠름) → 연기(어둡고 높이)
            Layer(root.transform, "Outer", outer, outerMat, 14f, new Vector2(0.45f, 0.7f), new Vector2(0.5f, 0.9f), new Vector2(0.7f, 1.05f), 0.3f);
            Layer(root.transform, "Mid", mid, midMat, 18f, new Vector2(0.35f, 0.55f), new Vector2(0.7f, 1.1f), new Vector2(0.55f, 0.85f), 0.24f);
            Layer(root.transform, "Core", core, coreMat, 20f, new Vector2(0.25f, 0.4f), new Vector2(0.9f, 1.4f), new Vector2(0.35f, 0.6f), 0.16f);
            if (smoke != null)
                Layer(root.transform, "Smoke", smoke, smokeMat, 6f, new Vector2(0.9f, 1.3f), new Vector2(0.6f, 0.9f), new Vector2(0.5f, 0.8f), 0.25f);
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, BurningPath);
            Object.DestroyImmediate(root);
            return prefab;
        }

        // 피 튀기기: 일반 탄에 맞은 자리에서 핏덩이(백린 덩어리 메시)·핏방울(불씨 메시)·피 줄기(불꽃 줄기 메시)가 쏜 쪽으로 튀었다가 떨어진다.
        // Assets/Prefabs/FX/FX_HitBlood.prefab. 앞(+Z)이 튀는 방향 (TitleScreenView는 탄이 온 쪽을 향하게 놓는다)
        const string BloodPath = "Assets/Prefabs/FX/FX_HitBlood.prefab";
        static GameObject bloodFx;

        public static GameObject EnsureBloodFx()
        {
            if (bloodFx != null)
                return bloodFx;
            Mesh blob = null, drop = null;
            foreach (var a in AssetDatabase.LoadAllAssetsAtPath("Assets/Models/FX/FX_Phosphorus.fbx"))
                if (a is Mesh m)
                {
                    if (m.name.EndsWith("Phosphor")) blob = m;
                    else if (m.name.EndsWith("Ember")) drop = m;
                }
            var streak = AssetDatabase.LoadAssetAtPath<Mesh>("Assets/Art/FX/FX_SparkMesh.asset");
            var dark = Mat("Blood_Dark", new Color(0.42f, 0.02f, 0.03f), false);
            var bright = Mat("Blood_Bright", new Color(0.72f, 0.05f, 0.06f), true);

            var root = new GameObject("FX_HitBlood");
            var rootPs = root.AddComponent<ParticleSystem>();
            rootPs.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var rm = rootPs.main;
            rm.loop = false;
            rm.playOnAwake = false;
            rm.duration = 0.1f;
            var rem = rootPs.emission;
            rem.enabled = false;
            root.GetComponent<ParticleSystemRenderer>().enabled = false;
            Burst(root.transform, "Blobs", blob, dark, 9, new Vector2(0.35f, 0.6f), new Vector2(1.2f, 2.4f), new Vector2(0.9f, 1.6f), 45f, 3.2f, false);
            Burst(root.transform, "Drops", drop, bright, 14, new Vector2(0.3f, 0.55f), new Vector2(1.8f, 3.4f), new Vector2(1f, 1.8f), 55f, 3.5f, false);
            Burst(root.transform, "Streaks", streak, bright, 8, new Vector2(0.12f, 0.22f), new Vector2(2.5f, 4.5f), new Vector2(1.2f, 2f), 30f, 1f, true);
            bloodFx = PrefabUtility.SaveAsPrefabAsset(root, BloodPath);
            Object.DestroyImmediate(root);
            return bloodFx;
        }

        static void Burst(Transform parent, string name, Mesh mesh, Material mat, int count, Vector2 life, Vector2 speed, Vector2 size, float cone, float gravity, bool alongVelocity)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = false;
            main.playOnAwake = false;
            main.duration = 0.1f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(life.x, life.y);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed.x, speed.y);
            main.startSize = new ParticleSystem.MinMaxCurve(size.x, size.y);
            main.startRotation3D = !alongVelocity;
            if (!alongVelocity)
            {
                main.startRotationX = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
                main.startRotationY = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
                main.startRotationZ = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            }
            main.gravityModifier = gravity;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.maxParticles = count * 3;
            var em = ps.emission;
            em.rateOverTime = 0f;
            em.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = cone;
            shape.radius = 0.05f;
            var sol = ps.sizeOverLifetime;
            sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.7f, 0.8f), new Keyframe(1f, 0f)));
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Mesh;
            r.mesh = mesh;
            r.sharedMaterial = mat;
            r.alignment = alongVelocity ? ParticleSystemRenderSpace.Velocity : ParticleSystemRenderSpace.Local;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        static void Layer(Transform parent, string name, Mesh mesh, Material mat, float rate, Vector2 life, Vector2 speed, Vector2 size, float radius)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true;
            main.playOnAwake = false;
            main.duration = 1f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(life.x, life.y);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed.x, speed.y);
            main.startSize = new ParticleSystem.MinMaxCurve(size.x, size.y);
            main.startRotation3D = true;
            main.startRotationX = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startRotationY = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startRotationZ = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.simulationSpace = ParticleSystemSimulationSpace.World; // 걷는 몬스터 뒤로 불길이 끌린다
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.maxParticles = 40;
            var em = ps.emission;
            em.rateOverTime = rate;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Hemisphere;
            shape.radius = radius;
            shape.rotation = new Vector3(-90f, 0f, 0f); // 반구가 위를 향하게 (불길이 위로)
            var sol = ps.sizeOverLifetime;
            sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.6f), new Keyframe(0.25f, 1f), new Keyframe(1f, 0f)));
            var rol = ps.rotationOverLifetime;
            rol.enabled = true;
            rol.separateAxes = true;
            rol.x = new ParticleSystem.MinMaxCurve(-3f, 3f);
            rol.y = new ParticleSystem.MinMaxCurve(-3f, 3f);
            rol.z = new ParticleSystem.MinMaxCurve(-3f, 3f);
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Mesh;
            r.mesh = mesh;
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        static void BuildGround(Transform root)
        {
            var path = Load("Assets/Models/Tiles/DirtPathTile.fbx");
            var grass = Load("Assets/Models/Tiles/GrassTile.fbx");
            var trees = new GameObject[10];
            for (int i = 0; i < 5; i++)
            {
                trees[i * 2] = Load($"Assets/Models/Tiles/GrassTile_Tree{i + 1}.fbx");
                trees[i * 2 + 1] = Load($"Assets/Models/Tiles/GrassTile_Center{i + 1}.fbx");
            }
            var pathCells = new HashSet<Vector2Int>();
            for (int k = 1; k < PathCorners.Length; k++)
            {
                var a = PathCorners[k - 1];
                var b = PathCorners[k];
                var step = new Vector2Int(System.Math.Sign(b.x - a.x), System.Math.Sign(b.y - a.y));
                for (var c = a; ; c += step)
                {
                    pathCells.Add(c);
                    if (c == b) break;
                }
            }
            var rng = new System.Random(Seed);
            var ground = new GameObject("Ground").transform;
            ground.SetParent(root, false);
            for (int x = -18; x <= 16; x++)
                for (int z = -6; z <= 36; z++)
                {
                    var cell = new Vector2Int(x, z);
                    GameObject prefab;
                    float yaw = 0f;
                    if (pathCells.Contains(cell))
                        prefab = path;
                    else
                    {
                        int near = int.MaxValue;
                        foreach (var p in pathCells)
                            near = Mathf.Min(near, Mathf.Max(Mathf.Abs(p.x - x), Mathf.Abs(p.y - z)));
                        double treeChance = Clear(x, z) || near <= 1 ? 0 : near == 2 ? 0.3 : near <= 4 ? 0.55 : 0.8;
                        prefab = rng.NextDouble() < treeChance ? trees[rng.Next(trees.Length)] : grass;
                        yaw = 90f * rng.Next(4);
                    }
                    var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, ground);
                    go.transform.position = new Vector3(x, -0.4f, z); // 피벗이 바닥: 윗면을 y=0에 (MapGenerator와 같음)
                    go.transform.rotation = Quaternion.Euler(0f, yaw, 0f) * go.transform.rotation;
                }
        }

        static Vector3 Center(GameObject go)
        {
            var b = new Bounds(go.transform.position, Vector3.zero);
            foreach (var r in go.GetComponentsInChildren<Renderer>())
                b.Encapsulate(r.bounds);
            return b.center;
        }

        // 좌우는 회전판, 상하는 목 관절 (TowerAim과 같은 피벗)
        static void AimAt(TowerAim aim, Vector3 target)
        {
            if (aim.yawPivot == null || aim.muzzle == null)
                return;
            var fwd = aim.muzzle.position - aim.yawPivot.position;
            fwd.y = 0f;
            var to = target - aim.yawPivot.position;
            to.y = 0f;
            aim.yawPivot.Rotate(Vector3.up, Vector3.SignedAngle(fwd, to, Vector3.up), Space.World);
            if (aim.pitchPivot != null)
            {
                var from = aim.muzzle.position - aim.pitchPivot.position;
                var want = target - aim.pitchPivot.position;
                var axis = Vector3.Cross(Vector3.up, to).normalized;
                float pitch = Vector3.SignedAngle(Vector3.ProjectOnPlane(from, axis), Vector3.ProjectOnPlane(want, axis), axis);
                aim.pitchPivot.Rotate(axis, Mathf.Clamp(pitch, -25f, 15f), Space.World);
            }
        }

        // 파티클 이펙트를 놓고 t초 지난 순간으로 시뮬레이션 (정지 화면용)
        static GameObject Fx(GameObject prefab, Transform parent, Vector3 pos, Quaternion rot, float scale, float t)
        {
            if (prefab == null)
                return null;
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            go.transform.SetPositionAndRotation(pos, rot);
            go.transform.localScale *= scale;
            foreach (var ps in go.GetComponentsInChildren<ParticleSystem>())
            {
                var main = ps.main;
                main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            }
            foreach (var ps in go.GetComponentsInChildren<ParticleSystem>())
                if (ps.transform.parent == null || ps.transform.parent.GetComponentInParent<ParticleSystem>() == null)
                    ps.Simulate(t, true, true);
            return go;
        }

        // 폭발: 로우폴리 불덩이(노랑 심 → 주황 → 빨강 겉) + 검은 연기 + 사방 불꽃 + 주황 불빛
        static void Boom(Transform parent, GameObject flash, GameObject sparks, Vector3 pos, float size)
        {
            Fireball(parent, pos, size);
            var f = Fx(flash, parent, pos, Quaternion.identity, 2.5f * size, 0.03f);
            foreach (var l in f.GetComponentsInChildren<Light>())
                l.enabled = false; // 불빛은 아래 점광원 하나로 (여러 개가 겹치면 바닥이 노랗게 번진다)
            for (int k = 0; k < 4; k++)
                Fx(sparks, parent, pos, Quaternion.Euler(-30f - k * 20f, k * 90f, 0f), 6f * size, 0.04f);
            var light = new GameObject("BoomLight").AddComponent<Light>();
            light.transform.SetParent(parent, false);
            light.transform.position = pos + Vector3.up * 0.2f;
            light.type = LightType.Point;
            light.color = new Color(1f, 0.45f, 0.15f);
            light.range = 1.8f * size;
            light.intensity = 2.5f * size;
        }

        const string MatFolder = "Assets/Art/Title";
        static Material Mat(string name, Color color, bool unlit)
        {
            string path = $"{MatFolder}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                if (!AssetDatabase.IsValidFolder("Assets/Art")) AssetDatabase.CreateFolder("Assets", "Art");
                if (!AssetDatabase.IsValidFolder(MatFolder)) AssetDatabase.CreateFolder("Assets/Art", "Title");
                m = new Material(Shader.Find(unlit ? "Universal Render Pipeline/Unlit" : "Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(m, path);
            }
            m.SetColor("_BaseColor", color);
            if (!unlit) m.SetFloat("_Smoothness", 0f);
            EditorUtility.SetDirty(m);
            return m;
        }

        static Mesh lowPolySphere;
        // 각진 공 (게임 그림체에 맞춘 로우폴리): 정이십면체를 한 번 나눈 면 그대로 (면마다 꼭짓점을 따로 둬 평면 음영)
        static Mesh LowPolySphere()
        {
            if (lowPolySphere != null)
                return lowPolySphere;
            lowPolySphere = AssetDatabase.LoadAssetAtPath<Mesh>($"{MatFolder}/LowPolySphere.asset");
            if (lowPolySphere != null)
                return lowPolySphere;
            float t = (1f + Mathf.Sqrt(5f)) / 2f;
            var v = new List<Vector3> { new Vector3(-1, t, 0), new Vector3(1, t, 0), new Vector3(-1, -t, 0), new Vector3(1, -t, 0), new Vector3(0, -1, t), new Vector3(0, 1, t),
                new Vector3(0, -1, -t), new Vector3(0, 1, -t), new Vector3(t, 0, -1), new Vector3(t, 0, 1), new Vector3(-t, 0, -1), new Vector3(-t, 0, 1) };
            int[] f = { 0,11,5, 0,5,1, 0,1,7, 0,7,10, 0,10,11, 1,5,9, 5,11,4, 11,10,2, 10,7,6, 7,1,8, 3,9,4, 3,4,2, 3,2,6, 3,6,8, 3,8,9, 4,9,5, 2,4,11, 6,2,10, 8,6,7, 9,8,1 };
            var verts = new List<Vector3>();
            var tris = new List<int>();
            for (int i = 0; i < f.Length; i += 3)
            {
                Vector3 a = v[f[i]].normalized, b = v[f[i + 1]].normalized, c = v[f[i + 2]].normalized;
                Vector3 ab = ((a + b) * 0.5f).normalized, bc = ((b + c) * 0.5f).normalized, ca = ((c + a) * 0.5f).normalized;
                foreach (var tri in new[] { new[] { a, ab, ca }, new[] { ab, b, bc }, new[] { ca, bc, c }, new[] { ab, bc, ca } })
                    foreach (var p in tri) { tris.Add(verts.Count); verts.Add(p * 0.5f); }
            }
            lowPolySphere = new Mesh { name = "LowPolySphere" };
            lowPolySphere.SetVertices(verts);
            lowPolySphere.SetTriangles(tris, 0);
            lowPolySphere.RecalculateNormals();
            lowPolySphere.RecalculateBounds();
            if (!AssetDatabase.IsValidFolder("Assets/Art")) AssetDatabase.CreateFolder("Assets", "Art");
            if (!AssetDatabase.IsValidFolder(MatFolder)) AssetDatabase.CreateFolder("Assets/Art", "Title");
            AssetDatabase.CreateAsset(lowPolySphere, $"{MatFolder}/LowPolySphere.asset"); // 씬에 저장되도록 에셋으로
            return lowPolySphere;
        }

        static void Ball(Transform parent, Vector3 pos, float size, Material mat, int seed)
        {
            var go = new GameObject("Ball");
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            go.transform.rotation = Quaternion.Euler(seed * 37f, seed * 71f, seed * 13f);
            go.transform.localScale = Vector3.one * size;
            go.AddComponent<MeshFilter>().sharedMesh = LowPolySphere();
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        static Transform Fireball(Transform parent, Vector3 pos, float size)
        {
            var root = new GameObject("Fireball").transform;
            root.SetParent(parent, false);
            root.position = pos;
            var core = Mat("Boom_Core", new Color(1f, 0.95f, 0.6f), true);
            var mid = Mat("Boom_Mid", new Color(1f, 0.6f, 0.12f), true);
            var outer = Mat("Boom_Outer", new Color(0.9f, 0.22f, 0.08f), true);
            var smoke = Mat("Boom_Smoke", new Color(0.32f, 0.27f, 0.27f), false);
            var rng = new System.Random(Mathf.RoundToInt(pos.x * 100f + pos.z * 7f));
            Vector3 J(float r) => new Vector3((float)rng.NextDouble() - 0.5f, (float)rng.NextDouble() - 0.3f, (float)rng.NextDouble() - 0.5f) * r;
            // 카메라 쪽으로 갈수록 밝은 층이 보이게: 빨강(뒤·아래) → 주황 → 노랑 심(앞)
            var toCam = (CameraPos - pos);
            toCam.y = 0f;
            toCam = toCam.normalized;
            for (int i = 0; i < 5; i++) Ball(root, pos + J(0.5f) * size - toCam * 0.12f * size, (0.34f + 0.1f * (float)rng.NextDouble()) * size, outer, i);
            for (int i = 0; i < 4; i++) Ball(root, pos + J(0.32f) * size + toCam * 0.08f * size + Vector3.up * 0.05f * size, (0.3f + 0.08f * (float)rng.NextDouble()) * size, mid, i + 5);
            for (int i = 0; i < 3; i++) Ball(root, pos + J(0.16f) * size + toCam * 0.2f * size + Vector3.up * 0.06f * size, (0.17f + 0.05f * (float)rng.NextDouble()) * size, core, i + 9);
            for (int i = 0; i < 3; i++) Ball(root, pos + Vector3.up * (0.38f + 0.14f * i) * size + J(0.35f) * size - toCam * 0.1f * size, (0.22f - 0.03f * i) * size, smoke, i + 12);
            return root;
        }

        // 노을 하늘: 세로 그라데이션 텍스처(위 보라 → 분홍 → 지평선 주황 → 아래 어두운 갈색)를 파노라마 하늘에 쓴다
        static readonly Color SkyTop = new Color(0.2f, 0.17f, 0.36f);
        static readonly Color SkyMid = new Color(0.78f, 0.42f, 0.5f);
        static readonly Color SkyHorizon = new Color(1f, 0.7f, 0.42f);
        static readonly Color SkyBelow = new Color(0.35f, 0.22f, 0.2f);

        static Material SunsetSky()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Art")) AssetDatabase.CreateFolder("Assets", "Art");
            if (!AssetDatabase.IsValidFolder(MatFolder)) AssetDatabase.CreateFolder("Assets/Art", "Title");
            string texPath = $"{MatFolder}/Sky_Sunset.png";
            const int H = 256;
            var tex = new Texture2D(4, H, TextureFormat.RGB24, false);
            for (int y = 0; y < H; y++)
            {
                float v = y / (H - 1f); // 0 = 아래, 0.5 = 지평선, 1 = 위
                Color c = v < 0.5f ? Color.Lerp(SkyBelow, SkyHorizon, Mathf.Pow(v / 0.5f, 6f))
                        : v < 0.62f ? Color.Lerp(SkyHorizon, SkyMid, (v - 0.5f) / 0.12f)
                        : Color.Lerp(SkyMid, SkyTop, Mathf.Pow((v - 0.62f) / 0.38f, 0.7f));
                for (int x = 0; x < 4; x++)
                    tex.SetPixel(x, y, c);
            }
            File.WriteAllBytes(Path.GetFullPath(texPath), tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(texPath);
            var importer = (TextureImporter)AssetImporter.GetAtPath(texPath);
            importer.wrapModeU = TextureWrapMode.Repeat;
            importer.wrapModeV = TextureWrapMode.Clamp;
            importer.mipmapEnabled = false;
            importer.SaveAndReimport();

            string matPath = $"{MatFolder}/Sky_Sunset.mat";
            var sky = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (sky == null || sky.shader.name != "Skybox/Panoramic")
            {
                if (sky != null) AssetDatabase.DeleteAsset(matPath);
                sky = new Material(Shader.Find("Skybox/Panoramic"));
                AssetDatabase.CreateAsset(sky, matPath);
            }
            sky.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture2D>(texPath));
            sky.SetFloat("_Exposure", 1f);
            EditorUtility.SetDirty(sky);
            return sky;
        }

        public static void Render(string file, int w, int h)
        {
            var cam = GameObject.Find("TitleCamera")?.GetComponent<Camera>();
            if (cam == null)
            {
                Debug.LogWarning("[Title] TitleCamera 없음: 먼저 Title Screen Scene을 만드세요");
                return;
            }
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 8 };
            var prev = cam.targetTexture;
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            cam.targetTexture = prev;
            RenderTexture.active = null;
            File.WriteAllBytes(file, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            Object.DestroyImmediate(rt);
        }
    }
}
