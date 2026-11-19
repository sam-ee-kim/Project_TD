using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace TowerDefence
{
    // 맵 생성 규칙 조절 씬 (Assets/Scenes/Test_MapGenRules, GameLoop.mapGenRulesMode). 게임 진행(몬스터·타워·HUD)은 없다.
    // 오른쪽 패널에서 크기·꺾임·스타트 지점·직진 길이 같은 생성 규칙 값을 바꿔 정본 생성기(MapLayoutGenerator)를 바로 돌려 본다.
    // 값은 이 씬 안에서만 쓰고 에셋(MapGenRules)은 바꾸지 않는다. 마음에 드는 값은 패널 숫자를 MapGenRules 복잡성 표에 옮긴다.
    // GameLoop가 이 모드에서는 게임 대신 이것만 만들고 Tick한다 (Update는 GameLoop 하나 규칙 그대로).
    //   F: 새 시드로 생성   R: 같은 시드로 다시   100회 생성: 이 규칙으로 성공률·평균 시간
    public class MapGenRulesScene
    {
        // 조절 항목: 이름, 최소, 최대, 증감 단위
        enum Field { Size, TurnsMin, TurnsMax, Spawns, BranchTurns, MinRun, SpawnDistance, Retries, Count }
        static readonly string[] Names = { "맵 크기 (N×N)", "본선 꺾임 최소", "본선 꺾임 최대", "스타트 지점 수", "지선 꺾임 최대", "꺾은 뒤 최소 직진", "스타트 지점 최소 거리", "재시도 횟수" };
        static readonly int[] Min = { MapLayoutGenerator.MinSize, 0, 0, 1, 0, 1, 1, 10 };
        static readonly int[] Max = { 25, 16, 16, 4, 6, 6, 12, 2000 };
        static readonly int[] Step = { 1, 1, 1, 1, 1, 1, 1, 50 };

        GameConfig config;
        MapGenRules rules;
        MapGenerator mapRenderer;
        CameraController cameraRig;
        readonly System.Random seedRng = new System.Random();
        readonly int[] values = new int[(int)Field.Count];
        readonly Text[] valueLabels = new Text[(int)Field.Count];
        Text info;
        Text result;
        int seed;
        int count;

        public void Init(GameConfig config, MapGenRules rules, MapGenerator mapRenderer, CameraController cameraRig, Canvas canvas)
        {
            this.config = config;
            this.rules = rules;
            this.mapRenderer = mapRenderer;
            this.cameraRig = cameraRig;
            LoadComplexity(1);
            if (canvas != null)
                BuildPanel(canvas);
            seed = NewSeed();
            Generate();
        }

        public void Tick(float dt)
        {
            var keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard.fKey.wasPressedThisFrame)
                {
                    seed = NewSeed();
                    Generate();
                }
                if (keyboard.rKey.wasPressedThisFrame)
                    Generate();
            }
            if (mapRenderer != null)
                mapRenderer.Tick(dt);
            if (cameraRig != null)
                cameraRig.Tick(dt);
        }

        int NewSeed() => seedRng.Next(1, int.MaxValue);

        // 게임의 맵 단계 값 불러오기: 크기는 LevelRules(층 → 맵 크기), 꺾임·스폰은 MapGenRules 복잡성 표.
        void LoadComplexity(int level)
        {
            values[(int)Field.Size] = config != null ? LevelRules.GetMapSize(config, level) : 5 + 2 * level;
            var lv = rules != null ? rules.GetLevel(level) : new MapGenRules.ComplexityLevel();
            values[(int)Field.TurnsMin] = lv.mainTurnsMin;
            values[(int)Field.TurnsMax] = lv.mainTurnsMax;
            values[(int)Field.Spawns] = lv.spawns;
            values[(int)Field.BranchTurns] = lv.branchMaxTurns;
            values[(int)Field.MinRun] = rules != null && rules.minRunLength > 0 ? rules.minRunLength : MapLayoutGenerator.MinRunLength;
            values[(int)Field.SpawnDistance] = rules != null ? rules.spawnMinManhattan : 3;
            values[(int)Field.Retries] = rules != null && rules.maxRetries > 0 ? rules.maxRetries : 200;
            RefreshValues();
        }

        MapLayoutParams Params() => new MapLayoutParams
        {
            MainTurnsMin = values[(int)Field.TurnsMin],
            MainTurnsMax = values[(int)Field.TurnsMax],
            Spawns = values[(int)Field.Spawns],
            BranchMaxTurns = values[(int)Field.BranchTurns],
            MinRun = values[(int)Field.MinRun],
            SpawnMinDistance = values[(int)Field.SpawnDistance],
        };

        public void Generate()
        {
            if (mapRenderer == null)
            {
                Debug.LogWarning("[MapGenRulesScene] MapGenerator(3D 맵 표시)를 연결하세요.");
                return;
            }
            int n = values[(int)Field.Size];
            var p = Params();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var layout = MapLayoutGenerator.Generate(n, p, new System.Random(seed), values[(int)Field.Retries]);
            sw.Stop();
            count++;
            if (layout == null)
            {
                SetInfo($"#{count}  {n}×{n} · 시드 {seed} · 실패: 재시도 {values[(int)Field.Retries]}번 안에 이 규칙에 맞는 맵을 만들지 못함 ({sw.Elapsed.TotalMilliseconds:0.#}ms). 이전 맵을 그대로 둡니다.");
                SetResult("생성 실패\n규칙이 이 크기에 너무 빡빡합니다.\n꺾임을 줄이거나 맵을 키우거나\n최소 직진·스폰 거리를 줄여 보세요.");
                return;
            }
            var map = LayoutMapGenerator.Convert(layout, seed);
            mapRenderer.Show(map);
            SetInfo($"#{count}  {n}×{n} · 시드 {seed} · 생성 {sw.Elapsed.TotalMilliseconds:0.#}ms     F 새 시드 · R 같은 시드 · Tab 카메라");
            SetResult(Describe(layout, map, p));
        }

        // 이 규칙으로 100번 만들어 성공률과 평균 시간을 본다 (마지막 성공 맵을 보여 준다).
        void RunBatch()
        {
            const int Runs = 100;
            int n = values[(int)Field.Size];
            var p = Params();
            int ok = 0, turnsSum = 0, lengthSum = 0;
            double ms = 0, worst = 0;
            MapLayout last = null;
            int lastSeed = 0;
            for (int i = 0; i < Runs; i++)
            {
                int s = NewSeed();
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var layout = MapLayoutGenerator.Generate(n, p, new System.Random(s), values[(int)Field.Retries]);
                sw.Stop();
                double t = sw.Elapsed.TotalMilliseconds;
                ms += t;
                if (t > worst)
                    worst = t;
                if (layout == null)
                    continue;
                ok++;
                turnsSum += layout.TurnCount;
                lengthSum += layout.Path.Count;
                last = layout;
                lastSeed = s;
            }
            if (last != null)
            {
                seed = lastSeed;
                mapRenderer.Show(LayoutMapGenerator.Convert(last, seed));
            }
            SetInfo($"{Runs}회 생성  {n}×{n} · 성공 {ok}/{Runs} · 평균 {ms / Runs:0.#}ms · 최대 {worst:0.#}ms · 마지막 성공 맵 시드 {seed}");
            SetResult(ok == 0
                ? $"{Runs}회 모두 실패\n이 규칙으로는 맵이 나오지 않습니다."
                : $"성공률 {ok}%\n평균 본선 꺾임 {(float)turnsSum / ok:0.0}회\n평균 본선 길이 {(float)lengthSum / ok:0.0}칸\n평균 생성 {ms / Runs:0.#}ms (최대 {worst:0.#}ms)");
        }

        string Describe(MapLayout layout, MapData map, MapLayoutParams p)
        {
            int t1 = 0, star = 0, empty = 0;
            for (int x = 0; x < map.N; x++)
                for (int y = 0; y < map.N; y++)
                    if (map.Sites[x, y] == SiteGrade.T1) t1++;
                    else if (map.Sites[x, y] == SiteGrade.TStar) star++;
                    else if (map.Tiles[x, y] == TileType.Empty) empty++;
            var sb = new StringBuilder();
            sb.Append("본선 꺾임 ").Append(layout.TurnCount).Append("회 (요청 ").Append(p.MainTurnsMin).Append('~').Append(p.MainTurnsMax).Append(")\n");
            sb.Append("스타트 지점 ").Append(layout.SpawnCount).Append("곳\n");
            for (int k = 0; k < map.MonsterPaths.Count; k++)
                sb.Append(k == 0 ? "본선 " : "지선 " + k + " ").Append(map.MonsterPaths[k].Count).Append("칸\n");
            sb.Append("부지 T1 ").Append(t1).Append(" · 명당 T★ ").Append(star).Append(" · 빈 땅 ").Append(empty).Append('\n');
            bool valid = MapLayoutGenerator.ValidatePaths(map.MonsterPaths, map.N, p, out string reason);
            sb.Append(valid ? "규칙 검사 통과" : "규칙 위반: " + reason);
            return sb.ToString();
        }

        void SetInfo(string text)
        {
            if (info != null)
                info.text = "  " + text;
            Debug.Log("[MapGenRulesScene] " + text);
        }

        void SetResult(string text)
        {
            if (result != null)
                result.text = text;
        }

        // ---------- 패널 (오른쪽) ----------

        void BuildPanel(Canvas canvas)
        {
            var top = BasicUi.Rect("MapGenInfo", canvas.transform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(24f, -20f), new Vector2(1300f, 60f));
            BasicUi.Panel(top, new Color(0f, 0f, 0f, 0.55f)).raycastTarget = false;
            info = BasicUi.Label(top, "", 24, TextAnchor.MiddleLeft, Color.white);

            const float W = 430f, RowH = 50f;
            var panel = BasicUi.Rect("MapGenRules", canvas.transform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-20f, -20f), new Vector2(W, 1040f));
            BasicUi.Panel(panel, new Color(0.06f, 0.07f, 0.09f, 0.88f));
            float y = -16f;
            Title(panel, "생성 규칙 (이 씬에서만 적용)", ref y);

            // 게임 맵 단계 불러오기
            var levels = BasicUi.Rect("Levels", panel, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, y), new Vector2(W - 32f, 44f));
            for (int i = 1; i <= 5; i++)
            {
                int level = i;
                BasicUi.Button(levels, "Level" + i, "단계 " + i, new Vector2(0f, 0.5f), new Vector2(36f + (i - 1) * 79f, 0f), new Vector2(74f, 42f),
                    () => { LoadComplexity(level); Generate(); }, out var l);
                l.fontSize = 20;
            }
            y -= 52f;
            var hint = BasicUi.Rect("LevelHint", panel, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, y), new Vector2(W - 32f, 28f));
            BasicUi.Label(hint, "게임의 맵 단계 값 불러오기 (단계 1 = 1~2층)", 18, TextAnchor.MiddleLeft, new Color(0.7f, 0.72f, 0.76f));
            y -= 40f;

            for (int i = 0; i < (int)Field.Count; i++)
            {
                int field = i;
                var row = BasicUi.Rect("Row_" + (Field)i, panel, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, y), new Vector2(W - 32f, RowH - 6f));
                var nameRt = BasicUi.Rect("Name", row, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(220f, RowH - 6f));
                BasicUi.Label(nameRt, Names[i], 21, TextAnchor.MiddleLeft, Color.white);
                BasicUi.Button(row, "Minus", "−", new Vector2(1f, 0.5f), new Vector2(-155f, 0f), new Vector2(44f, 40f), () => Change(field, -1), out _);
                var valueRt = BasicUi.Rect("Value", row, new Vector2(1f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-98f, 0f), new Vector2(70f, 40f));
                valueLabels[i] = BasicUi.Label(valueRt, "", 24, TextAnchor.MiddleCenter, new Color(1f, 0.85f, 0.5f));
                BasicUi.Button(row, "Plus", "+", new Vector2(1f, 0.5f), new Vector2(-30f, 0f), new Vector2(44f, 40f), () => Change(field, +1), out _);
                y -= RowH;
            }
            y -= 8f;

            var actions = BasicUi.Rect("Actions", panel, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, y), new Vector2(W - 32f, 46f));
            BasicUi.Button(actions, "New", "새 시드 (F)", new Vector2(0f, 0.5f), new Vector2(64f, 0f), new Vector2(124f, 44f), () => { seed = NewSeed(); Generate(); }, out var a1);
            BasicUi.Button(actions, "Again", "같은 시드 (R)", new Vector2(0.5f, 0.5f), new Vector2(0f, 0f), new Vector2(124f, 44f), Generate, out var a2);
            BasicUi.Button(actions, "Batch", "100회 생성", new Vector2(1f, 0.5f), new Vector2(-64f, 0f), new Vector2(124f, 44f), RunBatch, out var a3);
            a1.fontSize = a2.fontSize = a3.fontSize = 20;
            y -= 60f;

            var res = BasicUi.Rect("Result", panel, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, y), new Vector2(W - 32f, 250f));
            BasicUi.Panel(res, new Color(1f, 1f, 1f, 0.06f)).raycastTarget = false;
            var resText = BasicUi.Rect("Text", res, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(W - 56f, 230f));
            result = BasicUi.Label(resText, "", 20, TextAnchor.UpperLeft, Color.white);
            RefreshValues();
        }

        void Title(RectTransform panel, string text, ref float y)
        {
            var rt = BasicUi.Rect("Title", panel, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, y), new Vector2(400f, 40f));
            BasicUi.Label(rt, text, 24, TextAnchor.MiddleLeft, new Color(1f, 0.75f, 0.45f));
            y -= 48f;
        }

        void Change(int field, int dir)
        {
            values[field] = Mathf.Clamp(values[field] + dir * Step[field], Min[field], Max[field]);
            // 꺾임 최소 ≤ 최대 유지
            if (field == (int)Field.TurnsMin && values[field] > values[(int)Field.TurnsMax])
                values[(int)Field.TurnsMax] = values[field];
            if (field == (int)Field.TurnsMax && values[field] < values[(int)Field.TurnsMin])
                values[(int)Field.TurnsMin] = values[field];
            RefreshValues();
            Generate(); // 같은 시드로 바로 다시 만들어 값의 영향을 본다
        }

        void RefreshValues()
        {
            for (int i = 0; i < valueLabels.Length; i++)
                if (valueLabels[i] != null)
                    valueLabels[i].text = values[i].ToString();
        }
    }
}
