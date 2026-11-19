using UnityEditor;
using UnityEngine;

namespace TowerDefence.EditorTools
{
    // 이상효과 표시 (메뉴: TowerDefence/Build Status FX). 몬스터 몸통 가운데에서 1초 동안 재생하도록 만들고,
    // 정지 시간이 길면(생물 얼음·기계 감전 2초) TowerManager가 재생 속도를 늦춘다. 모두 파티클이라 스크립트 Update가 없다.
    //   얼음: 하얗게 번쩍인 뒤 얼음 껍질이 몸을 감싸고, 얼음 가시가 돋고, 발밑에 서리, 반짝임 → 끝에 얼음 조각이 튄다
    //   감전: 몸 둘레로 노랑·하늘색 전기 줄기가 지지직 튀고, 몸이 번쩍번쩍, 불꽃이 튄다
    public static partial class MuzzleFxBuilder
    {
        public const string FrozenFxPath = PrefabFolder + "/FX_StatusFrozen.prefab";
        public const string ShockedFxPath = PrefabFolder + "/FX_StatusShocked.prefab";

        static readonly Color Ice = new Color(0.72f, 0.92f, 1f, 1f);
        static readonly Color IceDeep = new Color(0.42f, 0.72f, 1f, 1f);

        [MenuItem("TowerDefence/Build Status FX")]
        public static void BuildStatusFx()
        {
            EnsureFolder("Assets/Art");
            EnsureFolder(ArtFolder);
            EnsureFolder("Assets/Prefabs");
            EnsureFolder(PrefabFolder);
            var dot = AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath);
            if (dot == null)
                dot = BuildSoftDotTexture();
            var add = BuildMaterial(AdditiveMatPath, dot, true);
            var smoke = BuildMaterial(SmokeMatPath, dot, false);
            var chunk = AssetDatabase.LoadAssetAtPath<Material>(ChunkMatPath);       // 불투명 정점색 Unlit (화염 2단계 이펙트와 같은 재질)
            var shard = AssetDatabase.LoadAssetAtPath<Mesh>(StreakMeshPath);         // 길쭉한 마름모 = 얼음 결정
            if (chunk == null || shard == null)
            {
                BuildFlameTier2();
                chunk = AssetDatabase.LoadAssetAtPath<Material>(ChunkMatPath);
                shard = AssetDatabase.LoadAssetAtPath<Mesh>(StreakMeshPath);
            }
            Save("FX_StatusFrozen", FrozenFxPath, r => BuildFrozen(r, add, smoke, chunk, shard));
            var spark = AssetDatabase.LoadAssetAtPath<Mesh>(SparkMeshPath);           // 가는 막대 = 전기 조각
            Save("FX_StatusShocked", ShockedFxPath, r => BuildShocked(r, add, chunk, spark));
            AssetDatabase.SaveAssets();
            Debug.Log("[MuzzleFxBuilder] 이상효과 이펙트 2종 생성 (얼음·감전)");
        }

        // ---------- 얼음 ----------

        static void BuildFrozen(GameObject root, Material add, Material smoke, Material chunk, Mesh shard)
        {
            // 얼음 껍질: 몸을 감싸는 반투명 하늘색 덩어리, 1초 내내 유지 (루트)
            var shell = AddSystem(root, smoke, ParticleSystemRenderMode.Billboard, ParticleSystemSimulationSpace.Local);
            var main = shell.main;
            main.duration = 1f;
            main.startLifetime = 1f;
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.8f, 0.95f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new Color(0.62f, 0.86f, 1f, 0.85f);
            main.maxParticles = 4;
            Burst(shell, 3, 3);
            Sphere(shell, 0.08f);
            HoldFade(shell, new Color(0.85f, 0.97f, 1f), Ice);

            // 얼음 결정: 몸을 둘러싼 저폴리 얼음 덩어리 (불투명, 하늘색~흰색). 돋아난 뒤 그대로 있다가 끝에 줄어든다
            var crystals = AddSystem(Child(root, "Crystals"), chunk, ParticleSystemRenderMode.Mesh, ParticleSystemSimulationSpace.Local);
            crystals.GetComponent<ParticleSystemRenderer>().mesh = shard;
            crystals.GetComponent<ParticleSystemRenderer>().alignment = ParticleSystemRenderSpace.Local;
            main = crystals.main;
            main.duration = 1f;
            main.startLifetime = 1f;
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.45f, 0.75f);
            main.startRotation3D = true;
            main.startRotationX = new ParticleSystem.MinMaxCurve(-0.6f, 0.6f);
            main.startRotationY = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startRotationZ = new ParticleSystem.MinMaxCurve(-0.6f, 0.6f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.75f, 0.93f, 1f), new Color(0.5f, 0.8f, 1f));
            main.maxParticles = 12;
            Burst(crystals, 9, 11);
            Sphere(crystals, 0.2f);
            var cgrow = crystals.sizeOverLifetime;
            cgrow.enabled = true;
            cgrow.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(0.1f, 1.1f), new Keyframe(0.16f, 1f), new Keyframe(0.88f, 1f), new Keyframe(1f, 0f)));

            // 얼음 가시: 몸에서 바깥으로 뾰족하게 돋아 그대로 멈춘다
            var spikes = AddSystem(Child(root, "Spikes"), add, ParticleSystemRenderMode.Stretch, ParticleSystemSimulationSpace.Local);
            var sr = spikes.GetComponent<ParticleSystemRenderer>();
            sr.velocityScale = 0f;
            sr.lengthScale = 4f;
            main = spikes.main;
            main.duration = 1f;
            main.startLifetime = 1f;
            main.startSpeed = 0.02f; // 방향만 (늘어나는 쪽)
            main.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.12f);
            main.startColor = FrostCore;
            main.maxParticles = 16;
            Burst(spikes, 12, 14);
            Sphere(spikes, 0.2f);
            HoldFade(spikes, FrostCore, Ice);
            var grow = spikes.sizeOverLifetime;
            grow.enabled = true;
            grow.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.2f), new Keyframe(0.12f, 1.1f), new Keyframe(0.2f, 1f), new Keyframe(1f, 1f)));

            // 발밑 서리: 바닥에 눕힌 하얀 원 (몸통 가운데 기준 아래)
            var floor = AddSystem(Child(root, "FloorFrost"), smoke, ParticleSystemRenderMode.HorizontalBillboard, ParticleSystemSimulationSpace.Local);
            floor.transform.localPosition = new Vector3(0f, -0.22f, 0f);
            main = floor.main;
            main.duration = 1f;
            main.startLifetime = 1f;
            main.startSpeed = 0f;
            main.startSize = 1.1f;
            main.startColor = new Color(0.85f, 0.95f, 1f, 0.85f);
            main.maxParticles = 1;
            Burst(floor, 1, 1);
            NoShape(floor);
            HoldFade(floor, new Color(0.92f, 0.98f, 1f), Ice);

            // 처음: 하얀 번쩍임
            var flash = AddSystem(Child(root, "Flash"), add, ParticleSystemRenderMode.Billboard, ParticleSystemSimulationSpace.Local);
            main = flash.main;
            main.duration = 1f;
            main.startLifetime = 0.15f;
            main.startSpeed = 0f;
            main.startSize = 0.8f;
            main.startColor = FrostCore;
            main.maxParticles = 1;
            Burst(flash, 1, 1);
            NoShape(flash);
            Fade(flash, FrostCore, Frost);

            // 반짝임
            var glints = AddSystem(Child(root, "Glints"), add, ParticleSystemRenderMode.Billboard, ParticleSystemSimulationSpace.Local);
            main = glints.main;
            main.duration = 1f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.15f, 0.3f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.06f);
            main.startColor = FrostCore;
            main.maxParticles = 12;
            Stream(glints, 18f);
            Sphere(glints, 0.25f);
            Fade(glints, FrostCore, Ice);

            // 끝: 얼음이 깨지며 조각이 튄다
            var chips = AddSystem(Child(root, "Shatter"), add, ParticleSystemRenderMode.Stretch, ParticleSystemSimulationSpace.Local);
            var cr = chips.GetComponent<ParticleSystemRenderer>();
            cr.velocityScale = 0.04f;
            cr.lengthScale = 1.2f;
            main = chips.main;
            main.duration = 1f;
            main.startDelay = 0.9f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.4f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1f, 2.2f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.06f);
            main.startColor = FrostCore;
            main.gravityModifier = 1.2f;
            main.maxParticles = 16;
            Burst(chips, 10, 14);
            Sphere(chips, 0.2f);
            Fade(chips, FrostCore, IceDeep);
        }

        // 1초 동안 거의 그대로 보이다가 끝에서 사라짐 (얼음은 정지 시간 내내 보여야 한다)
        static void HoldFade(ParticleSystem ps, Color start, Color end)
        {
            var color = ps.colorOverLifetime;
            color.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(start, 0f), new GradientColorKey(end, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.06f), new GradientAlphaKey(1f, 0.85f), new GradientAlphaKey(0f, 1f) });
            color.color = g;
        }

        // ---------- 감전 ----------

        static void BuildShocked(GameObject root, Material add, Material chunk, Mesh spark)
        {
            // 몸이 번쩍번쩍 (루트)
            var glow = AddSystem(root, add, ParticleSystemRenderMode.Billboard, ParticleSystemSimulationSpace.Local);
            var main = glow.main;
            main.duration = 1f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.05f, 0.09f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.7f, 1f);
            main.startColor = VoltCore;
            main.maxParticles = 4;
            Stream(glow, 18f);
            NoShape(glow);
            Fade(glow, VoltCore, Volt);

            // 전기 조각: 불투명한 노랑·하늘색 막대가 몸 둘레에서 지그재그로 튄다 (밝은 바닥에서도 보이게)
            var bolts = AddSystem(Child(root, "Bolts"), chunk, ParticleSystemRenderMode.Mesh, ParticleSystemSimulationSpace.Local);
            var br = bolts.GetComponent<ParticleSystemRenderer>();
            br.mesh = spark;
            br.alignment = ParticleSystemRenderSpace.Velocity;
            main = bolts.main;
            main.duration = 1f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.06f, 0.11f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.8f, 1.6f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.8f, 1.3f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.95f, 0.35f), new Color(0.55f, 0.9f, 1f));
            main.maxParticles = 12;
            Stream(bolts, 60f);
            Sphere(bolts, 0.22f);
            Noise(bolts, 2f, 8f);

            // 전기 줄기: 몸 둘레에서 짧게 꺾이며 튄다 (노랑, 가끔 하늘색)
            var arcs = AddSystem(Child(root, "Arcs"), add, ParticleSystemRenderMode.Billboard, ParticleSystemSimulationSpace.Local);
            main = arcs.main;
            main.duration = 1f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.08f, 0.14f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.8f, 1.6f);
            main.startSize = 0.03f;
            main.startColor = new ParticleSystem.MinMaxGradient(Volt, VoltBlue);
            main.maxParticles = 24;
            Stream(arcs, 60f);
            Sphere(arcs, 0.22f);
            Noise(arcs, 2.5f, 6f);
            VoltTrail(arcs, add, 0.05f);

            // 불꽃: 몸에서 튀어 떨어진다
            var sparks = AddSystem(Child(root, "Sparks"), add, ParticleSystemRenderMode.Stretch, ParticleSystemSimulationSpace.Local);
            var sr = sparks.GetComponent<ParticleSystemRenderer>();
            sr.velocityScale = 0.04f;
            sr.lengthScale = 1f;
            main = sparks.main;
            main.duration = 1f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.15f, 0.3f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1f, 2.5f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.02f, 0.035f);
            main.startColor = VoltCore;
            main.gravityModifier = 0.8f;
            main.maxParticles = 16;
            Stream(sparks, 20f);
            Sphere(sparks, 0.2f);
            Fade(sparks, VoltCore, Volt);

            // 발밑에 지지직 원
            var ring = AddSystem(Child(root, "FloorGlow"), add, ParticleSystemRenderMode.HorizontalBillboard, ParticleSystemSimulationSpace.Local);
            ring.transform.localPosition = new Vector3(0f, -0.22f, 0f);
            main = ring.main;
            main.duration = 1f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.08f, 0.15f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.6f, 0.8f);
            main.startColor = new Color(1f, 0.9f, 0.4f, 0.6f);
            main.maxParticles = 3;
            Stream(ring, 10f);
            NoShape(ring);
            Fade(ring, Volt, VoltBlue);
        }
    }
}
