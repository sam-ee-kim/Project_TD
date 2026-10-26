using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace TowerDefence.EditorTools
{
    // 번개 타워 이펙트 3종 (메뉴: TowerDefence/Build Lightning FX).
    //   FX_LightningCharge : 차지 동안(약 1초) 구슬 둘레에서 전기 줄기가 안쪽으로 모이고 빛이 커진다
    //   FX_LightningBolt   : 날아가는 번개 (투사체 표시). 밝은 머리 + 지그재그로 떨리는 꼬리 + 곁가지 전기
    //   FX_LightningBurst  : 발사 순간 구슬, 명중 순간 몬스터에서 터지는 섬광과 전기 불티
    // 전기 줄기 = 노이즈를 강하게 건 파티클의 트레일 (꺾인 선처럼 보인다). 모두 파티클 시스템이라 스크립트 Update가 없다.
    public static partial class MuzzleFxBuilder
    {
        public const string LightningChargePath = PrefabFolder + "/FX_LightningCharge.prefab";
        public const string LightningBoltPath = PrefabFolder + "/FX_LightningBolt.prefab";
        public const string LightningBurstPath = PrefabFolder + "/FX_LightningBurst.prefab";

        static readonly Color ElectricCore = new Color(0.92f, 0.98f, 1f, 1f);
        static readonly Color Electric = new Color(0.45f, 0.8f, 1f, 1f);
        static readonly Color ElectricDeep = new Color(0.35f, 0.45f, 1f, 1f);

        [MenuItem("TowerDefence/Build Lightning FX")]
        public static void BuildLightning() => BuildLightning(0.96f);

        // chargeDuration = 차지 클립 길이 (Tower_Lightning@Charge)
        public static void BuildLightning(float chargeDuration)
        {
            EnsureFolder("Assets/Art");
            EnsureFolder(ArtFolder);
            EnsureFolder("Assets/Prefabs");
            EnsureFolder(PrefabFolder);
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath);
            if (texture == null)
                texture = BuildSoftDotTexture();
            var mat = BuildMaterial(AdditiveMatPath, texture, true);

            Save("FX_LightningCharge", LightningChargePath, root => BuildCharge(root, mat, chargeDuration));
            Save("FX_LightningBolt", LightningBoltPath, root => BuildBolt(root, mat));
            Save("FX_LightningBurst", LightningBurstPath, root => BuildBurst(root, mat));
            AssetDatabase.SaveAssets();
            Debug.Log("[MuzzleFxBuilder] 번개 이펙트 3종 생성");
        }

        static void Save(string name, string path, System.Action<GameObject> build)
        {
            var root = new GameObject(name);
            try
            {
                build(root);
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        // --- 차지: 구슬 둘레 전기가 모이며 빛이 커진다 (루트 = 빛 덩어리) ---
        static void BuildCharge(GameObject root, Material mat, float duration)
        {
            var glow = AddSystem(root, mat, ParticleSystemRenderMode.Billboard, ParticleSystemSimulationSpace.Local);
            var main = glow.main;
            main.duration = duration;
            main.startLifetime = duration;
            main.startSpeed = 0f;
            main.startSize = 0.32f;
            main.startColor = ElectricCore;
            main.maxParticles = 2;
            Burst(glow, 1, 1);
            NoShape(glow);
            var size = glow.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.5f), new Keyframe(0.85f, 2.2f), new Keyframe(1f, 2.7f)));
            var color = glow.colorOverLifetime;
            color.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Electric, 0f), new GradientColorKey(ElectricCore, 1f) },
                new[] { new GradientAlphaKey(0.55f, 0f), new GradientAlphaKey(1f, 0.7f), new GradientAlphaKey(1f, 1f) });
            color.color = g;
            AddElectricLight(root, glow, 3.5f);

            // 구슬 중심에서 깜빡이는 하얀 심
            var core = AddSystem(Child(root, "Core"), mat, ParticleSystemRenderMode.Billboard, ParticleSystemSimulationSpace.Local);
            main = core.main;
            main.duration = duration;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.04f, 0.07f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.12f, 0.2f);
            main.startColor = ElectricCore;
            main.maxParticles = 6;
            Stream(core, 40f);
            NoShape(core);

            // 전기 줄기: 둘레 구면에서 안쪽(구슬)으로, 노이즈로 꺾인 트레일. 차지가 끝날수록 많아진다
            var arcs = AddSystem(Child(root, "Arcs"), mat, ParticleSystemRenderMode.Billboard, ParticleSystemSimulationSpace.Local);
            main = arcs.main;
            main.duration = duration;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.1f, 0.18f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(-2.2f, -1.4f); // 안쪽으로
            main.startSize = new ParticleSystem.MinMaxCurve(0.015f, 0.03f);
            main.startColor = ElectricCore;
            main.maxParticles = 90;
            var emission = arcs.emission;
            emission.rateOverTime = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 30f), new Keyframe(1f, 110f)));
            Sphere(arcs, 0.3f);
            ElectricTrail(arcs, mat, 0.035f, 1f);
            Noise(arcs, 0.9f, 8f);

            // 튀는 불티
            var sparks = AddSystem(Child(root, "Sparks"), mat, ParticleSystemRenderMode.Stretch, ParticleSystemSimulationSpace.Local);
            sparks.GetComponent<ParticleSystemRenderer>().velocityScale = 0.03f;
            main = sparks.main;
            main.duration = duration;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.06f, 0.14f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.6f, 1.6f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.015f, 0.03f);
            main.startColor = ElectricCore;
            main.maxParticles = 40;
            Stream(sparks, 40f);
            Sphere(sparks, 0.12f);
            Fade(sparks, ElectricCore, Electric);
        }

        // --- 날아가는 번개 (투사체 표시, 계속 재생). 원점 = 번개 머리 ---
        static void BuildBolt(GameObject root, Material mat)
        {
            var head = AddSystem(root, mat, ParticleSystemRenderMode.Billboard, ParticleSystemSimulationSpace.Local);
            var main = head.main;
            main.loop = true;
            main.playOnAwake = true;
            main.duration = 1f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.05f, 0.08f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.32f, 0.42f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = ElectricCore;
            main.maxParticles = 10;
            Stream(head, 60f);
            NoShape(head);
            Fade(head, ElectricCore, Electric);
            AddElectricLight(root, head, 3f);

            // 지나간 자리: 지그재그로 떨리는 꼬리 (월드 공간, 이동 거리마다)
            var streak = AddSystem(Child(root, "Streak"), mat, ParticleSystemRenderMode.Billboard, ParticleSystemSimulationSpace.World);
            main = streak.main;
            main.loop = true;
            main.playOnAwake = true;
            main.duration = 1f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.08f, 0.14f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.09f, 0.13f);
            main.startColor = Electric;
            main.maxParticles = 120;
            var emission = streak.emission;
            emission.rateOverTime = 0f;
            emission.rateOverDistance = 90f;
            NoShape(streak);
            Fade(streak, ElectricCore, ElectricDeep);
            Shrink(streak, 1f, 0.2f);
            Noise(streak, 0.6f, 12f);

            // 곁가지 전기: 짧게 튀어 나가는 꺾인 줄기
            var arcs = AddSystem(Child(root, "Arcs"), mat, ParticleSystemRenderMode.Billboard, ParticleSystemSimulationSpace.World);
            main = arcs.main;
            main.loop = true;
            main.playOnAwake = true;
            main.duration = 1f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.06f, 0.12f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1f, 2.4f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.01f, 0.02f);
            main.startColor = ElectricCore;
            main.maxParticles = 60;
            Stream(arcs, 45f);
            Sphere(arcs, 0.03f);
            ElectricTrail(arcs, mat, 0.028f, 1f);
            Noise(arcs, 3f, 9f);
        }

        // --- 섬광 + 전기 불티 (발사·명중) ---
        static void BuildBurst(GameObject root, Material mat)
        {
            var flash = AddSystem(root, mat, ParticleSystemRenderMode.Billboard, ParticleSystemSimulationSpace.Local);
            var main = flash.main;
            main.duration = 0.2f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.07f, 0.1f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.7f, 0.9f);
            main.startColor = ElectricCore;
            main.maxParticles = 3;
            Burst(flash, 1, 2);
            NoShape(flash);
            Fade(flash, ElectricCore, Electric);
            Shrink(flash, 1f, 0.3f);
            AddElectricLight(root, flash, 4f);

            var sparks = AddSystem(Child(root, "Sparks"), mat, ParticleSystemRenderMode.Stretch, ParticleSystemSimulationSpace.World);
            sparks.GetComponent<ParticleSystemRenderer>().velocityScale = 0.04f;
            main = sparks.main;
            main.duration = 0.2f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.1f, 0.25f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 3.5f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.02f, 0.035f);
            main.startColor = ElectricCore;
            main.maxParticles = 24;
            Burst(sparks, 12, 18);
            Sphere(sparks, 0.05f);
            Fade(sparks, ElectricCore, Electric);

            var arcs = AddSystem(Child(root, "Arcs"), mat, ParticleSystemRenderMode.Billboard, ParticleSystemSimulationSpace.World);
            main = arcs.main;
            main.duration = 0.2f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.08f, 0.14f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(2f, 3.2f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.012f, 0.02f);
            main.startColor = ElectricCore;
            main.maxParticles = 10;
            Burst(arcs, 5, 7);
            Sphere(arcs, 0.04f);
            ElectricTrail(arcs, mat, 0.02f, 1f);
            Noise(arcs, 3f, 9f);
        }

        // --- 도우미 ---

        static void NoShape(ParticleSystem ps)
        {
            var shape = ps.shape;
            shape.enabled = false;
        }

        static void Sphere(ParticleSystem ps, float radius)
        {
            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = radius;
            shape.radiusThickness = 1f;
        }

        static void Noise(ParticleSystem ps, float strength, float frequency)
        {
            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = strength;
            noise.frequency = frequency;
            noise.scrollSpeed = 3f;
            noise.damping = true;
            noise.quality = ParticleSystemNoiseQuality.Medium;
        }

        // 꺾인 전기 줄기: 파티클이 지나간 길을 가는 선으로 남긴다 (끝으로 갈수록 가늘게)
        static void ElectricTrail(ParticleSystem ps, Material mat, float width, float ratio)
        {
            var trails = ps.trails;
            trails.enabled = true;
            trails.mode = ParticleSystemTrailMode.PerParticle;
            trails.ratio = ratio;
            trails.lifetime = 0.6f;
            trails.minVertexDistance = 0.008f;
            trails.dieWithParticles = true;
            trails.sizeAffectsWidth = false;
            trails.widthOverTrail = new ParticleSystem.MinMaxCurve(width, new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0f)));
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(ElectricCore, 0f), new GradientColorKey(Electric, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
            trails.colorOverTrail = g;
            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.trailMaterial = mat;
        }

        // 파란 순간 조명 (Lights 모듈). URP 조명 데이터를 미리 붙여 웨이브 중 AddComponent(GC)를 막는다.
        static void AddElectricLight(GameObject root, ParticleSystem ps, float intensity)
        {
            var lightGo = Child(root, "LightTemplate");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(0.5f, 0.75f, 1f);
            light.range = 1.6f;
            light.intensity = intensity;
            light.shadows = LightShadows.None;
            lightGo.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalLightData>();
            lightGo.SetActive(false);
            var lights = ps.lights;
            lights.enabled = true;
            lights.light = light;
            lights.ratio = 1f;
            lights.maxLights = 1;
            lights.useParticleColor = false;
            lights.sizeAffectsRange = false;
            lights.alphaAffectsIntensity = true;
        }
    }
}
