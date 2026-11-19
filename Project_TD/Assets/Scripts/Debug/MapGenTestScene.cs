using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace TowerDefence
{
    // 맵 생성 테스트 (Assets/Scenes/Test_MapGen, GameLoop.mapGenTestMode): F키 = 랜덤 맵 생성. 게임 진행(몬스터·타워·UI)은 없다.
    // GameLoop가 이 모드에서는 게임 대신 이것만 만들고 Tick한다 (Update는 GameLoop 하나 규칙 그대로).
    // 맵 단계(1~5)를 무작위로 골라 그 단계의 크기·복잡성과 무작위 시드로 정본 생성기(LayoutMapGenerator)를 돌린다.
    public class MapGenTestScene
    {
        GameConfig config;
        MapGenRules rules;
        MapGenerator mapRenderer;
        CameraController cameraRig;
        readonly System.Random rng = new System.Random();
        Text info;
        int count;

        public void Init(GameConfig config, MapGenRules rules, MapGenerator mapRenderer, CameraController cameraRig, Canvas canvas)
        {
            this.config = config;
            this.rules = rules;
            this.mapRenderer = mapRenderer;
            this.cameraRig = cameraRig;
            if (canvas != null)
            {
                var rt = BasicUi.Rect("MapGenInfo", canvas.transform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(24f, -20f), new Vector2(1300f, 70f));
                BasicUi.Panel(rt, new Color(0f, 0f, 0f, 0.55f)).raycastTarget = false;
                info = BasicUi.Label(rt, "", 26, TextAnchor.MiddleLeft, Color.white);
            }
            Generate();
        }

        public void Tick(float dt)
        {
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.fKey.wasPressedThisFrame)
                Generate();
            if (mapRenderer != null)
                mapRenderer.Tick(dt);
            if (cameraRig != null)
                cameraRig.Tick(dt);
        }

        public void Generate()
        {
            if (config == null || rules == null || mapRenderer == null)
            {
                Debug.LogWarning("[MapGenTestScene] GameConfig · MapGenRules · MapGenerator를 연결하세요.");
                return;
            }
            int floor = rng.Next(1, config.finalFloor + 1);   // 층을 고르면 맵 단계(크기·복잡성)가 정해진다
            var plan = LevelRules.GetFloorPlan(config, floor);
            int seed = rng.Next(1, int.MaxValue);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var map = new LayoutMapGenerator().Generate(new MapSpec(plan.N, plan.Complexity, seed, rules));
            sw.Stop();
            mapRenderer.Show(map);
            count++;
            string text = $"F: 랜덤 맵  #{count}   {plan.N}×{plan.N} · 복잡성 {plan.Complexity} · 스타트 {map.Spawns.Length}곳 · 시드 {seed} · 생성 {sw.Elapsed.TotalMilliseconds:0.#}ms";
            if (info != null)
                info.text = "  " + text;
            Debug.Log("[MapGenTestScene] " + text);
        }
    }
}
