using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace TowerDefence.EditorTools
{
    // 맵 생성 테스트 씬 (메뉴: TowerDefence/Setup/Map Gen Test Scene): SampleScene을 복사해
    // GameLoop를 맵 생성 테스트 모드로 바꾼다 (F키 = 랜덤 맵, 게임 진행 없음). SampleScene은 그대로 둔다.
    // 맵 생성 규칙 조절 씬 (메뉴: TowerDefence/Setup/Map Gen Rules Scene): 같은 방식, 규칙 값 패널이 있는 모드.
    // 맵 제작 · 레벨 테스트 씬 (메뉴: TowerDefence/Setup/Map Builder Scene): 타일을 놓아 맵을 만들고 그 맵으로 게임을 돌린다.
    //   스타트 타일은 초록, 넥서스 타일은 빨강.
    // 여러 번 눌러도 된다: 매번 SampleScene에서 새로 복사한다.
    public static class MapGenTestSceneBuilder
    {
        const string SampleScenePath = "Assets/Scenes/SampleScene.unity";
        public const string ScenePath = "Assets/Scenes/Test_MapGen.unity";
        public const string RulesScenePath = "Assets/Scenes/Test_MapGenRules.unity";
        public const string BuilderScenePath = "Assets/Scenes/Test_MapBuilder.unity";

        [MenuItem("TowerDefence/Setup/Map Gen Test Scene")]
        public static void Build() => Build(ScenePath, "mapGenTestMode", "F키 = 랜덤 맵");

        [MenuItem("TowerDefence/Setup/Map Gen Rules Scene")]
        public static void BuildRules() => Build(RulesScenePath, "mapGenRulesMode", "생성 규칙 조절 패널");

        [MenuItem("TowerDefence/Setup/Map Builder Scene")]
        public static void BuildBuilder() => Build(BuilderScenePath, "mapBuilderMode", "맵 제작 · 레벨 테스트");

        static void Build(string scenePath, string modeField, string description)
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;
            var previous = EditorSceneManager.GetActiveScene().path;
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath) != null)
                AssetDatabase.DeleteAsset(scenePath);
            AssetDatabase.CopyAsset(SampleScenePath, scenePath);
            var scene = EditorSceneManager.OpenScene(scenePath);

            // 게임 진행 대신 맵 생성 테스트 모드 (GameLoop가 맵 표시·카메라만 돌린다. Update는 GameLoop 하나)
            var loop = Object.FindFirstObjectByType<GameLoop>();
            var so = new SerializedObject(loop);
            so.FindProperty(modeField).boolValue = true;
            var lobby = so.FindProperty("lobby");
            if (lobby != null)
                lobby.objectReferenceValue = null; // 로비 도시는 쓰지 않는다
            var lobbyGo = GameObject.Find("Lobby");
            if (lobbyGo != null)
                Object.DestroyImmediate(lobbyGo); // 복사된 로비 도시(무거운 메시)를 지운다
            so.ApplyModifiedPropertiesWithoutUndo();

            if (modeField == "mapBuilderMode")
            {
                // 스타트 = 초록, 넥서스 = 빨강 (이 씬의 맵 표시만)
                var renderer = Object.FindFirstObjectByType<MapGenerator>();
                if (renderer != null)
                {
                    var rso = new SerializedObject(renderer);
                    rso.FindProperty("spawnColor").colorValue = new Color(0.3f, 0.85f, 0.35f);
                    rso.FindProperty("exitColor").colorValue = new Color(0.95f, 0.25f, 0.22f);
                    rso.ApplyModifiedPropertiesWithoutUndo();
                }
            }

            EditorSceneManager.SaveScene(scene);
            Debug.Log("[MapGenTestSceneBuilder] " + scenePath + " 생성 (" + description + ")");
            if (!string.IsNullOrEmpty(previous) && previous != scenePath)
                EditorSceneManager.OpenScene(previous);
        }
    }
}
