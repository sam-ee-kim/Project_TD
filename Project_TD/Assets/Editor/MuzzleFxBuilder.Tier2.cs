using UnityEditor;
using UnityEngine;

namespace TowerDefence.EditorTools
{
    // 2단계 타워 이펙트 (메뉴: TowerDefence/Build Tier2 FX). 모두 파티클이라 스크립트 Update가 없다.
    //   미니건 : 가벼운 총구 섬광(연기 없음, 초당 5발이라) / 노란 예광탄 / 작은 불티
    //   전기   : 노랑·하늘색 전기 섬광 / 지지직 플라즈마 구슬 / 맞은 자리 전기 폭발
    //   냉기   : 서리 안개가 뿜어지는 총구 / 얼음 조각 탄 + 눈가루 꼬리 / 맞은 자리 얼음 파편과 냉기 고리
    public static partial class MuzzleFxBuilder
    {
        public const string MinigunFlashPath = PrefabFolder + "/FX_MinigunFlash.prefab";
        public const string MinigunTracerPath = PrefabFolder + "/FX_MinigunTracer.prefab";
        public const string MinigunHitPath = PrefabFolder + "/FX_MinigunHit.prefab";
        public const string ElectroMuzzlePath = PrefabFolder + "/FX_ElectroMuzzle.prefab";
        public const string ElectroBoltPath = PrefabFolder + "/FX_ElectroBolt.prefab";
        public const string ElectroHitPath = PrefabFolder + "/FX_ElectroHit.prefab";
        public const string CryoMuzzlePath = PrefabFolder + "/FX_CryoMuzzle.prefab";
        public const string CryoShardPath = PrefabFolder + "/FX_CryoShard.prefab";
        public const string CryoHitPath = PrefabFolder + "/FX_CryoHit.prefab";

        static readonly Color TracerCore = new Color(1f, 0.95f, 0.7f, 1f);
        static readonly Color TracerHot = new Color(1f, 0.7f, 0.25f, 1f);
        static readonly Color VoltCore = new Color(1f, 0.98f, 0.8f, 1f);
        static readonly Color Volt = new Color(1f, 0.88f, 0.3f, 1f);   // 노란 전기
        static readonly Color VoltBlue = new Color(0.45f, 0.85f, 1f, 1f);
        static readonly Color FrostCore = new Color(0.92f, 0.98f, 1f, 1f);
        static readonly Color Frost = new Color(0.55f, 0.88f, 1f, 1f);
        static readonly Color FrostDeep = new Color(0.35f, 0.6f, 1f, 1f);

        [MenuItem("TowerDefence/Build Tier2 FX")]
        public static void BuildTier2()
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

            Save("FX_MinigunFlash", MinigunFlashPath, r => BuildMinigunFlash(r, add));
            Save("FX_MinigunTracer", MinigunTracerPath, r => BuildTracer(r, add));
            Save("FX_MinigunHit", MinigunHitPath, r => BuildBulletHit(r, add));
            Save("FX_ElectroMuzzle", ElectroMuzzlePath, r => BuildVoltBurst(r, add, 0.5f, 6));
            Save("FX_ElectroBolt", ElectroBoltPath, r => BuildVoltBolt(r, add));
            Save("FX_ElectroHit", ElectroHitPath, r => BuildVoltBurst(r, add, 0.75f, 10));
            Save("FX_CryoMuzzle", CryoMuzzlePath, r => BuildCryoMuzzle(r, add, smoke));
            Save("FX_CryoShard", CryoShardPath, r => BuildCryoShard(r, add, smoke));
            Save("FX_CryoHit", CryoHitPath, r => BuildCryoHit(r, add, smoke));
            AssetDatabase.SaveAssets();
            Debug.Log("[MuzzleFxBuilder] 2단계 타워 이펙트 9종 생성");
        }

        // ---------- 미니건 ----------

        static void BuildMinigunFlash(GameObject root, Material mat)
        {
            var flash = AddSystem(root, mat, ParticleSystemRenderMode.Billboard, ParticleSystemSimulationSpace.Local);
            var main = flash.main;
            main.duration = 0.05f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.03f, 0.05f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.22f, 0.3f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = TracerCore;
            main.maxParticles = 3;
            Burst(flash, 1, 2);
            NoShape(flash);
            Fade(flash, TracerCore, TracerHot);

            var tongue = AddSystem(Child(root, "Tongue"), mat, ParticleSystemRenderMode.Stretch, ParticleSystemSimulationSpace.Local);
            var tr = tongue.GetComponent<ParticleSystemRenderer>();
            tr.velocityScale = 0.05f;
            tr.lengthScale = 2f;
            main = tongue.main;
            main.duration = 0.05f;
            main.startLifetime = 0.04f;
            main.startSpeed = new ParticleSystem.MinMaxCurve(3f, 4.5f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.1f, 0.14f);
            main.startColor = TracerHot;
            main.maxParticles = 4;
            Burst(tongue, 2, 3);
            Cone(tongue, 6f, 0.01f);
            Fade(tongue, TracerCore, TracerHot);

            var sparks = AddSystem(Child(root, "Sparks"), mat, ParticleSystemRenderMode.Stretch, ParticleSystemSimulationSpace.World);
            sparks.GetComponent<ParticleSystemRenderer>().velocityScale = 0.03f;
            main = sparks.main;
            main.duration = 0.05f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.08f, 0.16f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(2f, 4f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.015f, 0.025f);
            main.startColor = TracerHot;
            main.gravityModifier = 0.5f;
            main.maxParticles = 8;
            Burst(sparks, 2, 4);
            Cone(sparks, 25f, 0.01f);
        }

        // 예광탄: 노랗게 빛나는 짧은 줄 (투사체 표시, 계속 재생)
        static void BuildTracer(GameObject root, Material mat)
        {
            var head = AddSystem(root, mat, ParticleSystemRenderMode.Billboard, ParticleSystemSimulationSpace.Local);
            var main = head.main;
            main.loop = true;
            main.playOnAwake = true;
            main.duration = 1f;
            main.startLifetime = 0.04f;
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.09f, 0.12f);
            main.startColor = TracerCore;
            main.maxParticles = 6;
            Stream(head, 80f);
            NoShape(head);

            var trail = AddSystem(Child(root, "Trail"), mat, ParticleSystemRenderMode.Billboard, ParticleSystemSimulationSpace.World);
            main = trail.main;
            main.loop = true;
            main.playOnAwake = true;
            main.duration = 1f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.05f, 0.08f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.06f);
            main.startColor = TracerHot;
            main.maxParticles = 60;
            var emission = trail.emission;
            emission.rateOverTime = 0f;
            emission.rateOverDistance = 80f;
            NoShape(trail);
            Fade(trail, TracerCore, TracerHot);
            Shrink(trail, 1f, 0.2f);
        }

        static void BuildBulletHit(GameObject root, Material mat)
        {
            var sparks = AddSystem(root, mat, ParticleSystemRenderMode.Stretch, ParticleSystemSimulationSpace.World);
            sparks.GetComponent<ParticleSystemRenderer>().velocityScale = 0.03f;
            var main = sparks.main;
            main.duration = 0.1f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.08f, 0.16f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 3f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.015f, 0.03f);
            main.startColor = TracerHot;
            main.gravityModifier = 0.6f;
            main.maxParticles = 10;
            Burst(sparks, 4, 7);
            Sphere(sparks, 0.03f);
            Fade(sparks, TracerCore, TracerHot);

            var puff = AddSystem(Child(root, "Puff"), mat, ParticleSystemRenderMode.Billboard, ParticleSystemSimulationSpace.Local);
            main = puff.main;
            main.duration = 0.1f;
            main.startLifetime = 0.06f;
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.14f, 0.2f);
            main.startColor = TracerCore;
            main.maxParticles = 2;
            Burst(puff, 1, 1);
            NoShape(puff);
            Fade(puff, TracerCore, TracerHot);
        }

        // ---------- 전기 ----------

        // 노랑·하늘 전기 섬광 + 꺾인 전기 줄기 (총구 섬광과 명중 폭발 공용, size·arcs로 크기 조절)
        static void BuildVoltBurst(GameObject root, Material mat, float size, int arcs)
        {
            var flash = AddSystem(root, mat, ParticleSystemRenderMode.Billboard, ParticleSystemSimulationSpace.Local);
            var main = flash.main;
            main.duration = 0.15f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.06f, 0.1f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(size * 0.85f, size);
            main.startColor = VoltCore;
            main.maxParticles = 3;
            Burst(flash, 1, 2);
            NoShape(flash);
            Fade(flash, VoltCore, Volt);
            Shrink(flash, 1f, 0.3f);
            AddElectricLight(root, flash, 3.5f);

            var a = AddSystem(Child(root, "Arcs"), mat, ParticleSystemRenderMode.Billboard, ParticleSystemSimulationSpace.World);
            main = a.main;
            main.duration = 0.15f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.08f, 0.15f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.8f, 3.2f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.012f, 0.02f);
            main.startColor = VoltCore;
            main.maxParticles = arcs + 4;
            Burst(a, (short)arcs, (short)(arcs + 2));
            Sphere(a, 0.05f);
            VoltTrail(a, mat, 0.03f);
            Noise(a, 3f, 10f);

            var sparks = AddSystem(Child(root, "Sparks"), mat, ParticleSystemRenderMode.Stretch, ParticleSystemSimulationSpace.World);
            sparks.GetComponent<ParticleSystemRenderer>().velocityScale = 0.03f;
            main = sparks.main;
            main.duration = 0.15f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.1f, 0.22f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 3.5f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.015f, 0.03f);
            main.startColor = VoltBlue;
            main.maxParticles = 20;
            Burst(sparks, 8, 14);
            Sphere(sparks, 0.04f);
            Fade(sparks, VoltCore, VoltBlue);
        }

        // 플라즈마 구슬: 노란 심 + 둘레 지지직 + 지나간 자리 꼬리 (투사체 표시)
        static void BuildVoltBolt(GameObject root, Material mat)
        {
            var core = AddSystem(root, mat, ParticleSystemRenderMode.Billboard, ParticleSystemSimulationSpace.Local);
            var main = core.main;
            main.loop = true;
            main.playOnAwake = true;
            main.duration = 1f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.05f, 0.08f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.26f, 0.34f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = VoltCore;
            main.maxParticles = 10;
            Stream(core, 60f);
            NoShape(core);
            Fade(core, VoltCore, Volt);
            AddElectricLight(root, core, 2.5f);

            var crackle = AddSystem(Child(root, "Crackle"), mat, ParticleSystemRenderMode.Billboard, ParticleSystemSimulationSpace.Local);
            main = crackle.main;
            main.loop = true;
            main.playOnAwake = true;
            main.duration = 1f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.05f, 0.1f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.2f, 2f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.01f, 0.018f);
            main.startColor = VoltCore;
            main.maxParticles = 30;
            Stream(crackle, 70f);
            Sphere(crackle, 0.06f);
            VoltTrail(crackle, mat, 0.022f);
            Noise(crackle, 2.5f, 12f);

            var tail = AddSystem(Child(root, "Tail"), mat, ParticleSystemRenderMode.Billboard, ParticleSystemSimulationSpace.World);
            main = tail.main;
            main.loop = true;
            main.playOnAwake = true;
            main.duration = 1f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.1f, 0.18f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.07f, 0.11f);
            main.startColor = Volt;
            main.maxParticles = 100;
            var emission = tail.emission;
            emission.rateOverTime = 0f;
            emission.rateOverDistance = 70f;
            NoShape(tail);
            Fade(tail, VoltCore, Volt); // 노랑끼리 (노랑+파랑은 섞여 탁한 초록이 된다)
            Shrink(tail, 1f, 0.15f);
            Noise(tail, 0.5f, 12f);
        }

        // 노랑 → 하늘 전기 줄기 트레일
        static void VoltTrail(ParticleSystem ps, Material mat, float width)
        {
            ElectricTrail(ps, mat, width, 1f);
            var trails = ps.trails;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(VoltCore, 0f), new GradientColorKey(Volt, 0.5f), new GradientColorKey(VoltBlue, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
            trails.colorOverTrail = g;
        }

        // ---------- 냉기 ----------

        // 총구: 하얀 서리 안개가 앞으로 뿜어지고 얼음 가루가 반짝인다 (원점 = 총구, +Z = 발사 방향)
        static void BuildCryoMuzzle(GameObject root, Material add, Material smoke)
        {
            var flash = AddSystem(root, add, ParticleSystemRenderMode.Billboard, ParticleSystemSimulationSpace.Local);
            var main = flash.main;
            main.duration = 0.12f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.07f, 0.1f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.35f, 0.45f);
            main.startColor = FrostCore;
            main.maxParticles = 2;
            Burst(flash, 1, 1);
            NoShape(flash);
            Fade(flash, FrostCore, Frost);
            Shrink(flash, 1f, 0.4f);

            var mist = AddSystem(Child(root, "Mist"), smoke, ParticleSystemRenderMode.Billboard, ParticleSystemSimulationSpace.World);
            main = mist.main;
            main.duration = 0.15f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.6f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.8f, 1.6f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.15f, 0.25f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new Color(0.88f, 0.96f, 1f, 0.75f);
            main.maxParticles = 14;
            Burst(mist, 6, 9);
            Cone(mist, 18f, 0.02f);
            var limit = mist.limitVelocityOverLifetime;
            limit.enabled = true;
            limit.limit = 0.2f;
            limit.dampen = 0.3f;
            var grow = mist.sizeOverLifetime;
            grow.enabled = true;
            grow.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.6f), new Keyframe(1f, 2.4f)));
            FadeSoft(mist, new Color(0.9f, 0.97f, 1f), new Color(0.75f, 0.88f, 1f));

            var glints = AddSystem(Child(root, "Glints"), add, ParticleSystemRenderMode.Billboard, ParticleSystemSimulationSpace.World);
            main = glints.main;
            main.duration = 0.12f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.2f, 0.4f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.8f, 2.2f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.02f, 0.04f);
            main.startColor = FrostCore;
            main.maxParticles = 16;
            Burst(glints, 8, 12);
            Cone(glints, 25f, 0.02f);
            Fade(glints, FrostCore, Frost);
        }

        // 얼음 탄: 하얀 심 + 둘레 서리빛 + 눈가루 꼬리 (투사체 표시)
        static void BuildCryoShard(GameObject root, Material add, Material smoke)
        {
            var core = AddSystem(root, add, ParticleSystemRenderMode.Billboard, ParticleSystemSimulationSpace.Local);
            var main = core.main;
            main.loop = true;
            main.playOnAwake = true;
            main.duration = 1f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.06f, 0.1f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.2f, 0.26f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = FrostCore;
            main.maxParticles = 10;
            Stream(core, 50f);
            NoShape(core);
            Fade(core, FrostCore, Frost);

            var snow = AddSystem(Child(root, "Snow"), add, ParticleSystemRenderMode.Billboard, ParticleSystemSimulationSpace.World);
            main = snow.main;
            main.loop = true;
            main.playOnAwake = true;
            main.duration = 1f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.45f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0f, 0.25f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.02f, 0.04f);
            main.startColor = FrostCore;
            main.gravityModifier = 0.15f;
            main.maxParticles = 80;
            var emission = snow.emission;
            emission.rateOverTime = 0f;
            emission.rateOverDistance = 45f;
            Sphere(snow, 0.05f);
            Fade(snow, FrostCore, Frost);

            var vapor = AddSystem(Child(root, "Vapor"), smoke, ParticleSystemRenderMode.Billboard, ParticleSystemSimulationSpace.World);
            main = vapor.main;
            main.loop = true;
            main.playOnAwake = true;
            main.duration = 1f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.18f, 0.3f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.1f, 0.16f);
            main.startColor = new Color(0.85f, 0.95f, 1f, 0.55f);
            main.maxParticles = 60;
            emission = vapor.emission;
            emission.rateOverTime = 0f;
            emission.rateOverDistance = 25f;
            NoShape(vapor);
            FadeSoft(vapor, new Color(0.9f, 0.97f, 1f), new Color(0.7f, 0.85f, 1f));
        }

        // 명중: 얼음 파편이 튀고 바닥에 냉기 고리가 퍼진다
        static void BuildCryoHit(GameObject root, Material add, Material smoke)
        {
            var flash = AddSystem(root, add, ParticleSystemRenderMode.Billboard, ParticleSystemSimulationSpace.Local);
            var main = flash.main;
            main.duration = 0.15f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.08f, 0.12f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.5f, 0.65f);
            main.startColor = FrostCore;
            main.maxParticles = 2;
            Burst(flash, 1, 1);
            NoShape(flash);
            Fade(flash, FrostCore, Frost);

            var shards = AddSystem(Child(root, "Shards"), add, ParticleSystemRenderMode.Stretch, ParticleSystemSimulationSpace.World);
            shards.GetComponent<ParticleSystemRenderer>().velocityScale = 0.04f;
            main = shards.main;
            main.duration = 0.15f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.2f, 0.4f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 3f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.05f);
            main.startColor = FrostCore;
            main.gravityModifier = 0.8f;
            main.maxParticles = 20;
            Burst(shards, 10, 14);
            Sphere(shards, 0.05f);
            Fade(shards, FrostCore, FrostDeep);

            var ring = AddSystem(Child(root, "ColdRing"), smoke, ParticleSystemRenderMode.Billboard, ParticleSystemSimulationSpace.World);
            main = ring.main;
            main.duration = 0.15f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.55f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.8f, 1.3f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.12f, 0.2f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new Color(0.88f, 0.96f, 1f, 0.7f);
            main.maxParticles = 20;
            Burst(ring, 12, 16);
            FlatCircle(ring, 0.05f);
            var limit = ring.limitVelocityOverLifetime;
            limit.enabled = true;
            limit.limit = 0.2f;
            limit.dampen = 0.25f;
            FadeSoft(ring, new Color(0.9f, 0.97f, 1f), new Color(0.7f, 0.85f, 1f));
        }

        // 안개용: 서서히 나타났다 사라지는 반투명
        static void FadeSoft(ParticleSystem ps, Color start, Color end)
        {
            var color = ps.colorOverLifetime;
            color.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(start, 0f), new GradientColorKey(end, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.8f, 0.15f), new GradientAlphaKey(0.4f, 0.6f), new GradientAlphaKey(0f, 1f) });
            color.color = g;
        }
    }
}
