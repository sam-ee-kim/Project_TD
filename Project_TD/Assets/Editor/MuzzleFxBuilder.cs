using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace TowerDefence.EditorTools
{
    // 총구 발사 이펙트 프리팹을 만든다 (메뉴: TowerDefence/Build Muzzle FX).
    // 실제 총 발사 때 보이는 현상만: 총구 섬광(+ 순간 조명), 앞으로 뿜는 화염, 불티, 화약 연기.
    // 프리팹 원점 = 총구 끝, +Z = 포신 방향. 파티클 시스템은 엔진이 돌리므로 스크립트 Update가 없다.
    public static class MuzzleFxBuilder
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
