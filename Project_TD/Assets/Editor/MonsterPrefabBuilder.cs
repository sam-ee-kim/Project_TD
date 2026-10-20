using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace TowerDefence.EditorTools
{
    // 몬스터 모델(FBX) → 게임용 프리팹 + Animator Controller (메뉴: TowerDefence/Build Monster Prefabs).
    // Controller에는 Idle / Walk / Die 상태만 두고 전환(Transition)은 만들지 않는다.
    // 어떤 상태를 재생할지는 코드의 유한상태머신(MonsterView)이 정한다.
    public static class MonsterPrefabBuilder
    {
        const string ModelFolder = "Assets/Models/Monsters";
        const string ControllerFolder = "Assets/Art/Monsters";
        const string PrefabFolder = "Assets/Prefabs/Monsters";

        [MenuItem("TowerDefence/Build Monster Prefabs")]
        public static void BuildAll()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { ModelFolder }))
                Build(AssetDatabase.GUIDToAssetPath(guid));
            AssetDatabase.SaveAssets();
        }

        // 모델 이름 = 프리팹·컨트롤러 이름. 클립 이름은 "…Idle", "…Walk", "…Die"로 끝나야 한다.
        public static GameObject Build(string modelPath)
        {
            EnsureFolder("Assets/Art");
            EnsureFolder(ControllerFolder);
            EnsureFolder("Assets/Prefabs");
            EnsureFolder(PrefabFolder);
            string name = Path.GetFileNameWithoutExtension(modelPath);
            ConfigureImporter(modelPath);

            AnimationClip idle = null, walk = null, die = null;
            foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(modelPath))
            {
                if (!(asset is AnimationClip clip) || clip.name.StartsWith("__preview"))
                    continue;
                if (clip.name.EndsWith("Idle")) idle = clip;
                else if (clip.name.EndsWith("Walk")) walk = clip;
                else if (clip.name.EndsWith("Die")) die = clip;
            }
            if (idle == null || walk == null || die == null)
            {
                Debug.LogError($"[MonsterPrefabBuilder] {modelPath}: Idle/Walk/Die 클립을 모두 찾지 못했습니다.");
                return null;
            }

            var controller = BuildController($"{ControllerFolder}/{name}.controller", idle, walk, die);

            var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            var root = (GameObject)PrefabUtility.InstantiatePrefab(model);
            try
            {
                var animator = root.GetComponent<Animator>();
                if (animator == null)
                    animator = root.AddComponent<Animator>();
                animator.runtimeAnimatorController = controller;
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms; // 화면 밖이면 뼈 계산 생략
                foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>())
                    smr.updateWhenOffscreen = false;
                var prefab = PrefabUtility.SaveAsPrefabAsset(root, $"{PrefabFolder}/{name}.prefab");
                Debug.Log($"[MonsterPrefabBuilder] {name}: 프리팹·컨트롤러 생성 (Idle {idle.length:F2}s, Walk {walk.length:F2}s, Die {die.length:F2}s)");
                return prefab;
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        // Idle·Walk는 반복, Die는 한 번. Generic 리그는 이 모델로 Avatar를 만든다.
        static void ConfigureImporter(string modelPath)
        {
            var importer = (ModelImporter)AssetImporter.GetAtPath(modelPath);
            importer.animationType = ModelImporterAnimationType.Generic;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            var clips = importer.clipAnimations;
            if (clips == null || clips.Length == 0)
                clips = importer.defaultClipAnimations;
            for (int i = 0; i < clips.Length; i++)
            {
                bool loop = clips[i].name.EndsWith("Idle") || clips[i].name.EndsWith("Walk");
                clips[i].loopTime = loop;
                clips[i].loopPose = loop;
            }
            importer.clipAnimations = clips;
            importer.SaveAndReimport();
        }

        static AnimatorController BuildController(string path, AnimationClip idle, AnimationClip walk, AnimationClip die)
        {
            if (AssetDatabase.LoadAssetAtPath<AnimatorController>(path) != null)
                AssetDatabase.DeleteAsset(path);
            var controller = AnimatorController.CreateAnimatorControllerAtPath(path);
            var sm = controller.layers[0].stateMachine;
            var idleState = sm.AddState("Idle");
            idleState.motion = idle;
            var walkState = sm.AddState("Walk");
            walkState.motion = walk;
            var dieState = sm.AddState("Die");
            dieState.motion = die;
            sm.defaultState = idleState;
            return controller;
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
