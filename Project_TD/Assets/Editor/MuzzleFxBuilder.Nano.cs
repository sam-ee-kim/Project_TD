using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace TowerDefence.EditorTools
{
    // 나노봇 타워 이펙트 (메뉴: TowerDefence/Build Nano FX).
    //   FX_NanoMuzzle   : 발사구마다 터지는 작은 청록 섬광과 나노 입자
    //   Projectile_Nano : 날아가는 나노 탄 (모델 Projectile_Nano_Shot + 청록 꼬리 파티클, 루트 = 꼬리 파티클)
    //   FX_NanoField    : 명중 자리에 깔리는 나노 무리 (모델 FX_NanoSwarm_Field의 2초 물결 애니메이션 반복 + 떠오르는 입자)
    // 필드 모델은 루트 위치·회전도 키로 갖고 있어서, 배치용 빈 루트 아래 자식으로 둔다 (게임이 루트를 옮기고 크기를 바꾼다).
    public static partial class MuzzleFxBuilder
    {
        public const string NanoMuzzlePath = PrefabFolder + "/FX_NanoMuzzle.prefab";
        public const string NanoProjectilePath = PrefabFolder + "/Projectile_Nano.prefab";
        public const string NanoFieldPath = PrefabFolder + "/FX_NanoField.prefab";
        const string NanoShotModelPath = "Assets/Models/Towers/Projectile_Nano_Shot.fbx";
        const string NanoFieldModelPath = "Assets/Models/FX/FX_NanoSwarm_Field.fbx";
        const string NanoFieldControllerPath = ArtFolder + "/FX_NanoField.controller";
        public const float NanoFieldModelRadius = 1.6f; // 필드 모델 반지름 (나노 무리 3.2 × 3.4 모델 단위)

        static readonly Color NanoGlow = new Color(0.22f, 0.98f, 0.77f, 1f);  // SF_Nano_Glow
        static readonly Color NanoPale = new Color(0.75f, 1f, 0.92f, 1f);

        [MenuItem("TowerDefence/Build Nano FX")]
        public static void BuildNano()
        {
            EnsureFolder("Assets/Art");
            EnsureFolder(ArtFolder);
            EnsureFolder("Assets/Prefabs");
            EnsureFolder(PrefabFolder);
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath);
            if (texture == null)
                texture = BuildSoftDotTexture();
            var mat = BuildMaterial(AdditiveMatPath, texture, true);

            Save("FX_NanoMuzzle", NanoMuzzlePath, root => BuildNanoMuzzle(root, mat));
            Save("Projectile_Nano", NanoProjectilePath, root => BuildNanoProjectile(root, mat));
            var controller = BuildNanoFieldController();
            Save("FX_NanoField", NanoFieldPath, root => BuildNanoField(root, mat, controller));
            AssetDatabase.SaveAssets();
            Debug.Log("[MuzzleFxBuilder] 나노 이펙트 3종 생성");
        }

        // 발사구 섬광: 짧은 청록 빛 + 앞으로 튀는 나노 입자 (원점 = 발사구, +Z = 발사 방향)
        static void BuildNanoMuzzle(GameObject root, Material mat)
        {
            var flash = AddSystem(root, mat, ParticleSystemRenderMode.Billboard, ParticleSystemSimulationSpace.Local);
            var main = flash.main;
            main.duration = 0.1f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.05f, 0.07f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.16f, 0.22f);
            main.startColor = NanoPale;
            main.maxParticles = 3;
            Burst(flash, 1, 2);
            NoShape(flash);
            Fade(flash, NanoPale, NanoGlow);
            Shrink(flash, 1f, 0.4f);

            var bits = AddSystem(Child(root, "Bits"), mat, ParticleSystemRenderMode.Stretch, ParticleSystemSimulationSpace.World);
            bits.GetComponent<ParticleSystemRenderer>().velocityScale = 0.03f;
            main = bits.main;
            main.duration = 0.15f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.1f, 0.22f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.2f, 2.6f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.015f, 0.028f);
            main.startColor = NanoGlow;
            main.maxParticles = 14;
            Burst(bits, 5, 9);
            Cone(bits, 25f, 0.01f);
            Fade(bits, NanoPale, NanoGlow);
        }

        // 나노 탄: 루트 = 지나간 자리에 남는 청록 꼬리, 자식 = 탄 모델
        static void BuildNanoProjectile(GameObject root, Material mat)
        {
            var trail = AddSystem(root, mat, ParticleSystemRenderMode.Billboard, ParticleSystemSimulationSpace.World);
            var main = trail.main;
            main.loop = true;
            main.playOnAwake = true;
            main.duration = 1f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.12f, 0.22f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.035f, 0.06f);
            main.startColor = NanoGlow;
            main.maxParticles = 80;
            var emission = trail.emission;
            emission.rateOverTime = 0f;
            emission.rateOverDistance = 60f;
            NoShape(trail);
            Fade(trail, NanoPale, NanoGlow);
            Shrink(trail, 1f, 0.1f);

            var model = AssetDatabase.LoadAssetAtPath<GameObject>(NanoShotModelPath);
            if (model != null)
            {
                var m = (GameObject)PrefabUtility.InstantiatePrefab(model);
                m.name = "Model";
                m.transform.SetParent(root.transform, false);
                foreach (var r in m.GetComponentsInChildren<Renderer>())
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
        }

        // 필드 애니메이션 (Scene 클립 2초) 반복 재생용 컨트롤러. 모델의 클립을 반복으로 다시 가져온다.
        static AnimatorController BuildNanoFieldController()
        {
            var importer = (ModelImporter)AssetImporter.GetAtPath(NanoFieldModelPath);
            if (importer == null)
                return null;
            var clips = importer.clipAnimations;
            if (clips == null || clips.Length == 0)
                clips = importer.defaultClipAnimations;
            bool changed = false;
            foreach (var c in clips)
                if (!c.loopTime) { c.loopTime = true; changed = true; }
            if (changed || importer.clipAnimations.Length == 0)
            {
                importer.clipAnimations = clips;
                importer.SaveAndReimport();
            }
            AnimationClip clip = null;
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(NanoFieldModelPath))
                if (o is AnimationClip ac && !ac.name.StartsWith("__preview"))
                    clip = ac;
            if (clip == null)
                return null;
            AssetDatabase.DeleteAsset(NanoFieldControllerPath);
            return AnimatorController.CreateAnimatorControllerAtPathWithClip(NanoFieldControllerPath, clip);
        }

        // 필드: 배치용 빈 루트(바닥 중앙) + 나노 무리 모델(반복 애니메이션) + 원 안에서 떠오르는 청록 입자
        static void BuildNanoField(GameObject root, Material mat, AnimatorController controller)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(NanoFieldModelPath);
            if (model != null)
            {
                var m = (GameObject)PrefabUtility.InstantiatePrefab(model);
                m.name = "Swarm";
                m.transform.SetParent(root.transform, false);
                var animator = m.GetComponent<Animator>();
                if (animator == null)
                    animator = m.AddComponent<Animator>();
                animator.runtimeAnimatorController = controller;
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
                foreach (var r in m.GetComponentsInChildren<Renderer>())
                {
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    r.receiveShadows = false;
                }
            }

            var motes = AddSystem(Child(root, "Motes"), mat, ParticleSystemRenderMode.Billboard, ParticleSystemSimulationSpace.Local);
            var main = motes.main;
            main.loop = true;
            main.playOnAwake = true;
            main.duration = 2f;
            main.prewarm = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 1.1f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.11f);
            var rise = motes.velocityOverLifetime;
            rise.enabled = true;
            rise.space = ParticleSystemSimulationSpace.Local;
            rise.x = new ParticleSystem.MinMaxCurve(0f, 0f);
            rise.y = new ParticleSystem.MinMaxCurve(0.25f, 0.55f);
            rise.z = new ParticleSystem.MinMaxCurve(0f, 0f);
            main.startColor = NanoGlow;
            main.maxParticles = 80;
            Stream(motes, 40f);
            var shape = motes.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = NanoFieldModelRadius;
            shape.radiusThickness = 1f;
            shape.rotation = new Vector3(90f, 0f, 0f); // 바닥에 눕힌 원 (속도는 위로)
            Fade(motes, NanoPale, NanoGlow);
            Shrink(motes, 1f, 0.3f);
        }
    }
}
