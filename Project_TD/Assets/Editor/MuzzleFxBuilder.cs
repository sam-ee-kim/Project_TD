using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace TowerDefence.EditorTools
{
    // 총구 발사 이펙트 프리팹을 만든다 (메뉴: TowerDefence/Build Muzzle FX).
    // 실제 총 발사 때 보이는 현상만: 총구 섬광(+ 순간 조명), 앞으로 뿜는 화염, 불티, 화약 연기.
    // 프리팹 원점 = 총구 끝, +Z = 포신 방향. 파티클 시스템은 엔진이 돌리므로 스크립트 Update가 없다.
    public static partial class MuzzleFxBuilder
    {
        const string ArtFolder = "Assets/Art/FX";
        const string PrefabFolder = "Assets/Prefabs/FX";
        const string TexturePath = ArtFolder + "/FX_SoftDot.png";
        const string AdditiveMatPath = ArtFolder + "/FX_Additive.mat";
        const string SmokeMatPath = ArtFolder + "/FX_Smoke.mat";
        public const string PrefabPath = PrefabFolder + "/FX_MuzzleFlash.prefab";

        [MenuItem("TowerDefence/Build Muzzle FX")]
        public static void Build()
        {
            EnsureFolder("Assets/Art");
            EnsureFolder(ArtFolder);
            EnsureFolder("Assets/Prefabs");
            EnsureFolder(PrefabFolder);

            var texture = BuildSoftDotTexture();
            var additive = BuildMaterial(AdditiveMatPath, texture, true);
            var smoke = BuildMaterial(SmokeMatPath, texture, false);

            var root = new GameObject("FX_MuzzleFlash");
            try
            {
                var flash = AddFlash(root, additive);
                AddFlame(root, additive);
                AddSparks(root, additive);
                AddSmoke(root, smoke);
                AddFlashLight(root, flash);
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[MuzzleFxBuilder] " + PrefabPath + " 생성");
        }

        // --- 화염방사 타워: FX_FlameSpray90 모델 (90° 부채꼴 불길) ---
        // 모델 구성: 불꽃 조각 3종(Chunk_Core 노랑 / Mid 주황 / Outer 빨강, 각자 재질) + 범위 안내 호(Shape_Core/Mid/Outer).
        // 안내 호의 반지름(약 0.1 / 1.0 / 2.28)과 90° 폭대로, 노즐에서 조각을 뿜어 안쪽은 노랑·가운데는 주황·끝은 빨강이 되게 한다.
        // 프리팹 원점 = 노즐, +Z = 뿜는 방향. 길이는 모델 단위 2.28 (TowerDef.coneFxReach) → 게임에서 coneLength에 맞춰 크기를 정한다.
        // 한 번 발사(0.2초 간격) 동안 이어서 뿜으므로 연달아 쏘면 끊기지 않는 불길이 된다.

        const string FlameSprayModelPath = "Assets/Models/FX/FX_FlameSpray90.fbx";
        public const string FlamePrefabPath = PrefabFolder + "/FX_FlameSpray90.prefab";
        const float FlameArc = 90f;         // 부채꼴 각도 (히트박스 coneAngle과 같게)
        const float FlameGravity = 0.6f;    // 날아가며 바닥 쪽으로 처져 끝이 바닥 히트박스에 닿게

        [MenuItem("TowerDefence/Build Flame Spray FX")]
        public static void BuildFlameSpray()
        {
            EnsureFolder("Assets/Prefabs");
            EnsureFolder(PrefabFolder);
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(FlameSprayModelPath);
            if (model == null)
            {
                Debug.LogError("[MuzzleFxBuilder] " + FlameSprayModelPath + " 없음");
                return;
            }

            var root = new GameObject("FX_FlameSpray90");
            try
            {
                // 노즐 가까이: 노랑 (안쪽 호까지 ~1.0)
                var core = AddChunkStream(root, model, "Chunk_Core", 0.16f, 0.22f, 4.5f, 5.5f, 0.7f, 1.0f, 70f);
                SizeCurve(core, 0.6f, 1.3f, 1.1f);
                // 가운데: 주황 (~1.75)
                var mid = AddChunkStream(Child(root, "Mid"), model, "Chunk_Mid", 0.24f, 0.3f, 5.5f, 6.5f, 1.0f, 1.4f, 55f);
                SizeCurve(mid, 0.7f, 1.5f, 1.0f);
                // 끝: 빨강, 바깥 호(~2.28)에서 사그라짐
                var outer = AddChunkStream(Child(root, "Outer"), model, "Chunk_Outer", 0.32f, 0.38f, 6.0f, 6.6f, 1.3f, 1.8f, 45f);
                SizeCurve(outer, 0.6f, 1.4f, 0.15f);
                PrefabUtility.SaveAsPrefabAsset(root, FlamePrefabPath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[MuzzleFxBuilder] " + FlamePrefabPath + " 생성");
        }

        // 모델의 불꽃 조각 메시를 그 재질 그대로 파티클로 뿜는다 (90° 부채꼴, 위아래 얇게, 이리저리 굴러가며).
        static ParticleSystem AddChunkStream(GameObject go, GameObject model, string chunk, float lifeMin, float lifeMax,
            float speedMin, float speedMax, float sizeMin, float sizeMax, float perSecond)
        {
            Renderer source = null;
            foreach (var r in model.GetComponentsInChildren<MeshRenderer>(true))
                if (r.name.EndsWith(chunk))
                    source = r;
            var ps = AddSystem(go, source != null ? source.sharedMaterial : null, ParticleSystemRenderMode.Mesh, ParticleSystemSimulationSpace.World);
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            if (source != null)
                renderer.mesh = source.GetComponent<MeshFilter>().sharedMesh;
            renderer.alignment = ParticleSystemRenderSpace.Local;

            var main = ps.main;
            main.duration = 0.2f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(lifeMin, lifeMax);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speedMin, speedMax);
            main.startSize = new ParticleSystem.MinMaxCurve(sizeMin, sizeMax);
            main.startRotation3D = true;
            main.startRotationX = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startRotationY = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startRotationZ = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.maxParticles = 30;
            Stream(ps, perSecond);

            // 평평한 90° 호에서 바깥으로: 각도마다 고르게 퍼진다 (원뿔은 가운데로 몰림).
            // 원(XY 평면, +X에서 시작하는 호)을 눕히고(X 90°) 호 가운데가 +Z(뿜는 방향)에 오게 돌린다(Y −45°).
            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.arc = FlameArc;
            shape.arcMode = ParticleSystemShapeMultiModeValue.Random;
            shape.radius = 0.03f;
            shape.rotation = new Vector3(90f, -FlameArc * 0.5f, 0f);
            shape.randomDirectionAmount = 0.08f;
            main.gravityModifier = FlameGravity;

            var spin = ps.rotationOverLifetime;
            spin.enabled = true;
            spin.separateAxes = true;
            spin.x = new ParticleSystem.MinMaxCurve(-6f, 6f);
            spin.y = new ParticleSystem.MinMaxCurve(-6f, 6f);
            spin.z = new ParticleSystem.MinMaxCurve(-6f, 6f);
            return ps;
        }

        // 크기 변화: 시작 → 중간 → 끝
        static void SizeCurve(ParticleSystem ps, float start, float middle, float end)
        {
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, start), new Keyframe(0.55f, middle), new Keyframe(1f, end)));
        }

        // 1) 총구 섬광: 아주 짧고 밝은 빛 덩어리 (루트 시스템, 자식까지 함께 재생)
        static ParticleSystem AddFlash(GameObject root, Material mat)
        {
            var ps = AddSystem(root, mat, ParticleSystemRenderMode.Billboard, ParticleSystemSimulationSpace.Local);
            var main = ps.main;
            main.duration = 0.1f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.06f, 0.08f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.45f, 0.6f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new Color(1f, 0.92f, 0.65f, 1f);
            main.maxParticles = 4;
            Burst(ps, 2, 3);
            var shape = ps.shape;
            shape.enabled = false;
            Fade(ps, new Color(1f, 0.95f, 0.75f), new Color(1f, 0.55f, 0.15f));
            Shrink(ps, 1f, 0.3f);
            return ps;
        }

        // 2) 화염: 포신 방향으로 길게 뿜어지는 불꽃
        static void AddFlame(GameObject root, Material mat)
        {
            var ps = AddSystem(Child(root, "Flame"), mat, ParticleSystemRenderMode.Stretch, ParticleSystemSimulationSpace.Local);
            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.velocityScale = 0.05f;
            renderer.lengthScale = 2.5f;
            var main = ps.main;
            main.duration = 0.1f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.05f, 0.08f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(3f, 5.5f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.16f, 0.24f);
            main.startColor = new Color(1f, 0.75f, 0.3f, 1f);
            main.maxParticles = 8;
            Burst(ps, 4, 6);
            Cone(ps, 7f, 0.01f);
            Fade(ps, new Color(1f, 0.85f, 0.45f), new Color(1f, 0.35f, 0.05f));
            Shrink(ps, 1f, 0.2f);
        }

        // 3) 불티: 앞으로 튀어 나가 떨어지는 작은 불꽃 입자
        static void AddSparks(GameObject root, Material mat)
        {
            var ps = AddSystem(Child(root, "Sparks"), mat, ParticleSystemRenderMode.Stretch, ParticleSystemSimulationSpace.World);
            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.velocityScale = 0.04f;
            renderer.lengthScale = 1f;
            var main = ps.main;
            main.duration = 0.3f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.15f, 0.35f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(2.5f, 6f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.025f, 0.045f);
            main.startColor = new Color(1f, 0.7f, 0.25f, 1f);
            main.gravityModifier = 0.6f;
            main.maxParticles = 20;
            Burst(ps, 8, 14);
            Cone(ps, 22f, 0.01f);
            Fade(ps, new Color(1f, 0.85f, 0.4f), new Color(1f, 0.3f, 0.05f));
        }

        // 4) 화약 연기: 회색 연기가 천천히 퍼지며 위로 흩어진다
        static void AddSmoke(GameObject root, Material mat)
        {
            var ps = AddSystem(Child(root, "Smoke"), mat, ParticleSystemRenderMode.Billboard, ParticleSystemSimulationSpace.World);
            var main = ps.main;
            main.duration = 0.2f;
            main.startDelay = 0.02f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.8f, 1.3f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.15f, 0.5f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.22f, 0.32f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new Color(0.82f, 0.82f, 0.8f, 0.85f);
            main.gravityModifier = -0.04f; // 천천히 떠오름
            main.maxParticles = 12;
            Burst(ps, 4, 6);
            Cone(ps, 18f, 0.02f);

            var velocity = ps.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;
            velocity.x = new ParticleSystem.MinMaxCurve(-0.05f, 0.05f);
            velocity.y = new ParticleSystem.MinMaxCurve(0.05f, 0.15f);
            velocity.z = new ParticleSystem.MinMaxCurve(-0.05f, 0.05f);

            var limit = ps.limitVelocityOverLifetime; // 처음엔 빠르게 밀려나다 금방 느려진다
            limit.enabled = true;
            limit.limit = 0.25f;
            limit.dampen = 0.35f;

            var rotation = ps.rotationOverLifetime;
            rotation.enabled = true;
            rotation.z = new ParticleSystem.MinMaxCurve(-0.8f, 0.8f);

            var color = ps.colorOverLifetime;
            color.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(new Color(0.95f, 0.93f, 0.9f), 0f), new GradientColorKey(new Color(0.72f, 0.72f, 0.72f), 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.85f, 0.08f), new GradientAlphaKey(0.45f, 0.55f), new GradientAlphaKey(0f, 1f) });
            color.color = gradient;

            var size = ps.sizeOverLifetime; // 퍼지면서 커진다
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 4.5f)));
        }

        // 섬광 순간 조명: 섬광 입자에 붙어 주변을 짧게 밝힌다 (Lights 모듈)
        static void AddFlashLight(GameObject root, ParticleSystem flash)
        {
            var lightGo = Child(root, "FlashLightTemplate");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.75f, 0.4f);
            light.range = 2f;
            light.intensity = 4f;
            light.shadows = LightShadows.None;
            // URP 조명 데이터를 미리 붙인다. 없으면 파티클이 만든 조명이 처음 그려질 때마다 URP가 AddComponent를 해서
            // 웨이브 중 GC 할당이 생긴다 (작업 10 프로파일 측정: 이펙트 인스턴스마다 1번씩).
            lightGo.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalLightData>();
            lightGo.SetActive(false); // 템플릿: 실제 조명은 파티클이 살아 있는 동안만 생긴다

            var lights = flash.lights;
            lights.enabled = true;
            lights.light = light;
            lights.ratio = 1f;
            lights.maxLights = 1;
            lights.useParticleColor = false;
            lights.sizeAffectsRange = false;
            lights.alphaAffectsIntensity = true;
            lights.intensityMultiplier = 1f;
            lights.rangeMultiplier = 1f;
        }

        // --- 도우미 ---

        static ParticleSystem AddSystem(GameObject go, Material mat, ParticleSystemRenderMode mode, ParticleSystemSimulationSpace space)
        {
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.playOnAwake = false;
            main.loop = false;
            main.simulationSpace = space;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            var emission = ps.emission;
            emission.rateOverTime = 0f;
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = mode;
            renderer.sharedMaterial = mat;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return ps;
        }

        static GameObject Child(GameObject root, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root.transform, false);
            return go;
        }

        static void Burst(ParticleSystem ps, int min, int max)
        {
            var emission = ps.emission;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)min, (short)max) });
        }

        static void Stream(ParticleSystem ps, float perSecond)
        {
            var emission = ps.emission;
            emission.rateOverTime = perSecond;
        }

        static void Cone(ParticleSystem ps, float angle, float radius)
        {
            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = angle;
            shape.radius = radius;
        }

        static void Fade(ParticleSystem ps, Color start, Color end)
        {
            var color = ps.colorOverLifetime;
            color.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(start, 0f), new GradientColorKey(end, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.8f, 0.4f), new GradientAlphaKey(0f, 1f) });
            color.color = gradient;
        }

        static void Shrink(ParticleSystem ps, float from, float to)
        {
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, from), new Keyframe(1f, to)));
        }

        // 가운데가 밝고 가장자리로 부드럽게 사라지는 원형 텍스처 (섬광·불티·연기 공용)
        static Texture2D BuildSoftDotTexture()
        {
            const int size = 64;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f) / size * 2f - 1f;
                    float dy = (y + 0.5f) / size * 2f - 1f;
                    float d = Mathf.Clamp01(Mathf.Sqrt(dx * dx + dy * dy));
                    float a = Mathf.Pow(1f - d, 2.2f);
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            }
            File.WriteAllBytes(TexturePath, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(TexturePath);

            var importer = (TextureImporter)AssetImporter.GetAtPath(TexturePath);
            importer.textureType = TextureImporterType.Default;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath);
        }

        // URP 파티클 Unlit, 투명. additive = 빛(섬광·불꽃), 아니면 알파 블렌드(연기).
        static Material BuildMaterial(string path, Texture2D texture, bool additive)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.SetTexture("_BaseMap", texture);
            mat.SetColor("_BaseColor", Color.white);
            mat.SetFloat("_Surface", 1f);                 // Transparent
            mat.SetFloat("_Blend", additive ? 2f : 0f);    // 2 = Additive, 0 = Alpha
            mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            mat.SetFloat("_DstBlend", additive ? (float)BlendMode.One : (float)BlendMode.OneMinusSrcAlpha);
            mat.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            mat.SetFloat("_DstBlendAlpha", additive ? (float)BlendMode.One : (float)BlendMode.OneMinusSrcAlpha);
            mat.SetFloat("_ZWrite", 0f);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.renderQueue = (int)RenderQueue.Transparent;
            EditorUtility.SetDirty(mat);
            return mat;
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;
            var parent = Path.GetDirectoryName(path).Replace('\\', '/');
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
