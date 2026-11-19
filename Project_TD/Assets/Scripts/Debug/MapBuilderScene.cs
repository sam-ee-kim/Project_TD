using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace TowerDefence
{
    // 맵 제작 · 레벨 테스트 씬 (Assets/Scenes/Test_MapBuilder, GameLoop.mapBuilderMode).
    //
    // 편집: 하단 목록의 타일(나무 장애물 · 경로 · 잔디 · 스타트 · 넥서스)을 끌어 와 격자에 놓는다. 이웃 칸이 있으면 그 옆에 붙는다.
    //       스타트 칸을 누르면 가중치와 웨이브별 몬스터 줄(종류 · 수량 · 간격 · 시작 시각 · 이 지점/가중치 랜덤)을,
    //       넥서스 칸을 누르면 무적·체력을 정한다. 경로는 스타트마다 경로 타일을 따라 넥서스까지 가장 짧은 길로 계산한다.
    // 테스트: [테스트 시작]을 누르면 만든 맵과 몬스터 설정으로 실제 게임 모듈(Board·MonsterManager·TowerManager·GameUI)이 돈다.
    //       타워 건축·판매는 게임과 같다. [편집으로]를 누르면 판을 지우고 편집 화면으로 돌아간다.
    //
    // 게임 설정은 GameLoop가 만든 런타임 복사본(GameConfig)을 바꿔 쓴다: 1층짜리 판, 웨이브 표 = 스타트 지점 줄, 체력 배율 1.
    // 에셋은 바꾸지 않는다. MonoBehaviour가 아니다: GameLoop가 Tick을 부르고 IMapGenerator로 만든 맵을 넘긴다.
    // 편집 내용은 자동 저장된다 (Application.persistentDataPath/map_builder.json).
    public class MapBuilderScene : IMapGenerator
    {
        public const int EditSize = 15;    // 편집 격자 (칸)
        public enum Tool { Select, Tree, Path, Grass, Spawn, Nexus, Erase }
        public enum Tile { Tree, Path, Grass, Spawn, Nexus }

        static readonly string[] ToolNames = { "선택", "나무 장애물", "경로", "잔디 (설치)", "스타트", "넥서스", "지우개" };
        static readonly Color[] ToolColors =
        {
            new Color(0.8f, 0.8f, 0.85f), new Color(0.25f, 0.5f, 0.25f), new Color(0.85f, 0.7f, 0.45f),
            new Color(0.55f, 0.85f, 0.4f), new Color(0.2f, 0.85f, 0.3f), new Color(0.9f, 0.2f, 0.2f), new Color(0.6f, 0.2f, 0.2f),
        };
        static readonly Vector2Int[] Dirs = { Vector2Int.up, Vector2Int.right, Vector2Int.down, Vector2Int.left };

        [Serializable]
        public class Line
        {
            public int wave = 1;
            public string monsterId = "normal";
            public int count = 10;
            public float interval = 1f;
            public float delay = 0f;     // 웨이브 시작부터 (초)
            public bool random;          // true = 가중치 랜덤 (어느 스타트에서 나올지 가중치로), false = 이 스타트에서
        }

        [Serializable]
        public class SpawnSetting
        {
            public Vector2Int cell;
            public float weight = 1f;
            public List<Line> lines = new List<Line>();
        }

        // 맵 파일 (자동 저장 · 레벨디자인 라이브러리 LevelDesign/*.json 공용). 칸 좌표는 편집 격자(0 ~ EditSize−1) 기준.
        [Serializable]
        public class MapFile
        {
            public string title = "";     // 예: "1단계 A03 플라즈마 회랑"
            public string concept = "";   // 컨셉 설명
            public string towers = "";    // 컨셉 타워
            public List<Vector2Int> cells = new List<Vector2Int>();
            public List<int> kinds = new List<int>();
            public List<SpawnSetting> spawns = new List<SpawnSetting>();
            public bool invincible = true;
            public int nexusHp = 20;
        }

        // ---------- 편집 상태 ----------
        readonly Dictionary<Vector2Int, Tile> tiles = new Dictionary<Vector2Int, Tile>();
        readonly Dictionary<Vector2Int, GameObject> tileViews = new Dictionary<Vector2Int, GameObject>();
        readonly List<SpawnSetting> spawns = new List<SpawnSetting>(); // 순서 = 스타트 지점 번호
        bool invincible = true;
        int nexusHp = 20;
        Tool tool = Tool.Select;
        bool paletteDrag, painting, erasingRight;
        Vector2Int lastPaint = new Vector2Int(-99, -99);
        SpawnSetting openSpawn;
        bool nexusOpen;
        readonly System.Random visualRng = new System.Random();

        // ---------- 연결 ----------
        GameConfig config;    // 런타임 복사본 (GameLoop가 만들어 준다)
        GameData data;
        GameFlow flow;
        PlayerState player;
        GameUI ui;
        MapGenerator mapRenderer;
        CameraController cameraRig;
        Canvas gameCanvas;
        Camera camera;
        Action startNewRun;
        volatile MapData currentMap;

        public bool Testing { get; private set; }

        // ---------- 화면 ----------
        Transform worldRoot, tilesRoot;
        Transform ghost;
        Material ghostMaterial;
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        RectTransform editUi, testUi, panel, panelContent;
        Text status, testStatus, libraryLabel;
        string title = "";               // 불러온 라이브러리 맵 이름 (직접 고치면 그대로 둔다)
        string[] library = new string[0];
        int libraryIndex = -1;
        readonly List<Image> paletteFrames = new List<Image>();

        string SavePath => Path.Combine(Application.persistentDataPath, "map_builder.json");

        public void Init(GameConfig runtimeConfig, GameData data, GameFlow flow, PlayerState player, GameUI ui,
            MapGenerator mapRenderer, CameraController cameraRig, Canvas gameCanvas, Camera camera, Action startNewRun)
        {
            config = runtimeConfig;
            this.data = data;
            this.flow = flow;
            this.player = player;
            this.ui = ui;
            this.mapRenderer = mapRenderer;
            this.cameraRig = cameraRig;
            this.gameCanvas = gameCanvas;
            this.camera = camera;
            this.startNewRun = startNewRun;
            nexusHp = config != null ? config.nexusHp : 20;

            worldRoot = new GameObject("MapBuilder").transform;
            tilesRoot = new GameObject("BuilderTiles").transform;
            tilesRoot.SetParent(worldRoot, false);
            CreateGridFloor();
            CreateGhost();
            BuildUi();
            Load();
            EnterEdit();
        }

        // IMapGenerator: 테스트를 시작할 때 만든 맵을 그대로 돌려준다 (MapProvider가 백그라운드에서 불러도 안전한 읽기).
        public MapData Generate(MapSpec spec) => currentMap;

        // ---------- Tick ----------

        // 편집 중 (GameLoop가 게임 모듈 대신 부른다).
        public void Tick(float dt)
        {
            HandleInput();
            if (mapRenderer != null)
                mapRenderer.Tick(dt); // 지난 테스트 맵 정리 + 행성 회전
            if (cameraRig != null)
                cameraRig.Tick(dt);
        }

        // 테스트 중 (게임 루프 뒤에 부른다): 넥서스 상태 표시만.
        public void TickTesting(float dt)
        {
            if (testStatus != null)
                testStatus.text = invincible ? "레벨 테스트 중 · 넥서스 무적" : $"레벨 테스트 중 · 넥서스 체력 {player.NexusHp}";
        }

        // ---------- 입력 ----------

        void HandleInput()
        {
            var keyboard = Keyboard.current;
            var pointer = Pointer.current;
            var mouse = Mouse.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
            {
                SetTool(Tool.Select);
                ClosePanel();
            }
            if (pointer == null || camera == null)
                return;

            bool overUi = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
            var screen = pointer.position.ReadValue();
            bool hasCell = TryPointerCell(screen, out var cell, out var world) && !overUi;
            if (hasCell && tool != Tool.Select && tool != Tool.Erase && !tiles.ContainsKey(cell))
                cell = Snap(cell, world); // 이웃 타일 옆에 붙는다
            UpdateGhost(hasCell && (tool != Tool.Select || erasingRight), cell);

            // 하단 목록에서 끌어 와서 놓기
            if (paletteDrag)
            {
                if (pointer.press.wasReleasedThisFrame)
                {
                    paletteDrag = false;
                    if (hasCell)
                        Apply(cell);
                }
                return;
            }

            // 우클릭(누른 채 끌기) = 지우기
            if (mouse != null)
            {
                if (mouse.rightButton.wasPressedThisFrame && hasCell)
                    erasingRight = true;
                if (!mouse.rightButton.isPressed)
                    erasingRight = false;
                if (erasingRight && hasCell)
                {
                    Remove(cell);
                    return;
                }
            }

            if (pointer.press.wasPressedThisFrame && hasCell)
            {
                if (tool == Tool.Select)
                {
                    Select(cell);
                    return;
                }
                painting = true;
                lastPaint = new Vector2Int(-99, -99);
            }
            if (!pointer.press.isPressed)
                painting = false;
            if (painting && hasCell && cell != lastPaint)
            {
                lastPaint = cell;
                Apply(cell);
            }
        }

        bool TryPointerCell(Vector2 screen, out Vector2Int cell, out Vector3 world)
        {
            cell = default;
            world = default;
            var ray = camera.ScreenPointToRay(screen);
            if (Mathf.Abs(ray.direction.y) < 1e-5f)
                return false;
            float t = -ray.origin.y / ray.direction.y; // 타일 윗면 y = 0
            if (t < 0f)
                return false;
            world = ray.origin + ray.direction * t;
            float half = (EditSize - 1) * 0.5f;
            cell = new Vector2Int(Mathf.RoundToInt(world.x + half), Mathf.RoundToInt(world.z + half));
            return cell.x >= 0 && cell.y >= 0 && cell.x < EditSize && cell.y < EditSize;
        }

        // 빈 칸이고 이웃 타일이 없으면, 포인터 가까이(1칸 안)에서 기존 타일과 맞닿는 빈 칸으로 끌어당긴다.
        Vector2Int Snap(Vector2Int cell, Vector3 world)
        {
            if (tiles.Count == 0 || HasNeighbor(cell))
                return cell;
            float half = (EditSize - 1) * 0.5f;
            var best = cell;
            float bestD = 0.75f * 0.75f + 1f; // 포인터가 후보 칸 중심에서 약 1.1칸 안이면
            for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                {
                    var c = new Vector2Int(cell.x + dx, cell.y + dy);
                    if (!InGrid(c) || tiles.ContainsKey(c) || !HasNeighbor(c))
                        continue;
                    float ox = world.x - (c.x - half), oz = world.z - (c.y - half);
                    float d = ox * ox + oz * oz;
                    if (d < bestD)
                    {
                        bestD = d;
                        best = c;
                    }
                }
            return best;
        }

        bool HasNeighbor(Vector2Int c)
        {
            foreach (var d in Dirs)
                if (tiles.ContainsKey(c + d))
                    return true;
            return false;
        }

        static bool InGrid(Vector2Int c) => c.x >= 0 && c.y >= 0 && c.x < EditSize && c.y < EditSize;

        // ---------- 편집 ----------

        void Apply(Vector2Int cell)
        {
            switch (tool)
            {
                case Tool.Tree: Place(cell, Tile.Tree); break;
                case Tool.Path: Place(cell, Tile.Path); break;
                case Tool.Grass: Place(cell, Tile.Grass); break;
                case Tool.Spawn: Place(cell, Tile.Spawn); break;
                case Tool.Nexus: Place(cell, Tile.Nexus); break;
                case Tool.Erase: Remove(cell); break;
            }
        }

        void Place(Vector2Int cell, Tile kind)
        {
            if (tiles.TryGetValue(cell, out var old) && old == kind)
                return;
            if (kind == Tile.Nexus)
            {
                // 넥서스는 1곳: 이전 넥서스는 경로 타일로 바꾼다
                foreach (var kv in new List<KeyValuePair<Vector2Int, Tile>>(tiles))
                    if (kv.Value == Tile.Nexus && kv.Key != cell)
                        SetTile(kv.Key, Tile.Path);
            }
            SetTile(cell, kind);
            Changed();
        }

        void Remove(Vector2Int cell)
        {
            if (!tiles.ContainsKey(cell))
                return;
            RemoveSpawn(cell);
            tiles.Remove(cell);
            if (tileViews.TryGetValue(cell, out var go))
            {
                UnityEngine.Object.Destroy(go);
                tileViews.Remove(cell);
            }
            Changed();
        }

        void SetTile(Vector2Int cell, Tile kind)
        {
            if (tiles.TryGetValue(cell, out var old) && old == Tile.Spawn && kind != Tile.Spawn)
                RemoveSpawn(cell);
            tiles[cell] = kind;
            if (kind == Tile.Spawn && FindSpawn(cell) == null)
            {
                var s = new SpawnSetting { cell = cell };
                s.lines.Add(new Line { monsterId = DefaultMonster() });
                spawns.Add(s);
            }
            if (tileViews.TryGetValue(cell, out var go))
                UnityEngine.Object.Destroy(go);
            tileViews[cell] = CreateTileView(cell, kind);
        }

        void RemoveSpawn(Vector2Int cell)
        {
            var s = FindSpawn(cell);
            if (s == null)
                return;
            spawns.Remove(s);
            if (openSpawn == s)
                ClosePanel();
        }

        SpawnSetting FindSpawn(Vector2Int cell)
        {
            foreach (var s in spawns)
                if (s.cell == cell)
                    return s;
            return null;
        }

        void Select(Vector2Int cell)
        {
            if (!tiles.TryGetValue(cell, out var kind))
            {
                ClosePanel();
                return;
            }
            if (kind == Tile.Spawn)
                OpenSpawnPanel(FindSpawn(cell));
            else if (kind == Tile.Nexus)
                OpenNexusPanel();
            else
                ClosePanel();
        }

        string DefaultMonster() => data != null && data.Monsters.Count > 0 ? data.Monsters[0].id : "normal";

        void Changed()
        {
            Save();
            RefreshStatus();
        }

        void ClearAll()
        {
            foreach (var go in tileViews.Values)
                UnityEngine.Object.Destroy(go);
            tileViews.Clear();
            tiles.Clear();
            spawns.Clear();
            title = "";
            ClosePanel();
            Changed();
        }

        // ---------- 맵 계산 (편집 타일 → 게임 맵) ----------

        // 칸 종류와 스타트 순서로 게임 맵을 만든다. 경로 = 스타트마다 경로 타일(경로·스타트·넥서스)을 따라 넥서스까지 가장 짧은 길.
        // 맵 크기 = 놓은 타일을 감싸는 정사각형. 그 안의 빈 칸은 나무 장애물로 채운다.
        public static MapData BuildMap(IReadOnlyDictionary<Vector2Int, Tile> tiles, IList<SpawnSetting> spawns, out string error)
        {
            error = null;
            if (tiles.Count == 0) { error = "타일이 없습니다."; return null; }
            Vector2Int nexus = default;
            int nexusCount = 0;
            int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
            foreach (var kv in tiles)
            {
                if (kv.Value == Tile.Nexus) { nexus = kv.Key; nexusCount++; }
                minX = Math.Min(minX, kv.Key.x); minY = Math.Min(minY, kv.Key.y);
                maxX = Math.Max(maxX, kv.Key.x); maxY = Math.Max(maxY, kv.Key.y);
            }
            if (nexusCount != 1) { error = nexusCount == 0 ? "넥서스 타일이 없습니다." : "넥서스는 1곳만 놓을 수 있습니다."; return null; }
            if (spawns.Count == 0) { error = "스타트 타일이 없습니다."; return null; }

            // 가장자리 규칙: 넥서스·스타트는 맵(타일 묶음) 가장자리, 꼭짓점 제외.
            // 첫 스타트는 넥서스 반대 변 (스타트가 1곳이면 끝과 끝). 넥서스와 같은 변에는 스타트를 둘 수 없다
            // (넥서스 가까이 스타트를 두는 건 스타트가 2곳 이상일 때, 두 번째 스타트부터).
            bool Border(Vector2Int c) => c.x == minX || c.x == maxX || c.y == minY || c.y == maxY;
            bool Corner(Vector2Int c) => (c.x == minX || c.x == maxX) && (c.y == minY || c.y == maxY);
            bool SameEdge(Vector2Int a, Vector2Int b) => (a.x == b.x && (a.x == minX || a.x == maxX)) || (a.y == b.y && (a.y == minY || a.y == maxY));
            bool Opposite(Vector2Int a, Vector2Int b) =>
                (a.x == minX && b.x == maxX) || (a.x == maxX && b.x == minX) || (a.y == minY && b.y == maxY) || (a.y == maxY && b.y == minY);
            if (!Border(nexus) || Corner(nexus)) { error = "넥서스는 맵 가장자리(꼭짓점 제외)에만 둘 수 있습니다."; return null; }
            for (int k = 0; k < spawns.Count; k++)
            {
                var c = spawns[k].cell;
                if (!Border(c) || Corner(c)) { error = $"스타트 {k + 1}은(는) 맵 가장자리(꼭짓점 제외)에만 둘 수 있습니다."; return null; }
                if (k == 0 && !Opposite(c, nexus)) { error = "첫 스타트는 넥서스의 반대 변에 있어야 합니다."; return null; }
                if (k > 0 && SameEdge(c, nexus)) { error = $"스타트 {k + 1}이(가) 넥서스와 같은 변에 있습니다."; return null; }
            }

            int n = Math.Max(3, Math.Max(maxX - minX + 1, maxY - minY + 1));
            var origin = new Vector2Int(minX, minY);
            var map = new MapData
            {
                N = n,
                Tiles = new TileType[n, n],
                Sites = new SiteGrade[n, n],
                Spawns = new Vector2Int[spawns.Count],
                Exit = nexus - origin,
                Junctions = new List<Vector2Int>(),
                MonsterPaths = new List<List<Vector2Int>>(),
                SpawnWeights = new float[spawns.Count],
                Seed = 0,
            };
            for (int x = 0; x < n; x++)
                for (int y = 0; y < n; y++)
                    map.Tiles[x, y] = TileType.Empty;
            foreach (var kv in tiles)
            {
                var c = kv.Key - origin;
                switch (kv.Value)
                {
                    case Tile.Path: map.Tiles[c.x, c.y] = TileType.Path; break;
                    case Tile.Spawn: map.Tiles[c.x, c.y] = TileType.Spawn; break;
                    case Tile.Nexus: map.Tiles[c.x, c.y] = TileType.Exit; break;
                    case Tile.Grass: map.Tiles[c.x, c.y] = TileType.BuildSite; map.Sites[c.x, c.y] = SiteGrade.T1; break;
                    default: map.Tiles[c.x, c.y] = TileType.Empty; break;
                }
            }

            for (int k = 0; k < spawns.Count; k++)
            {
                var start = spawns[k].cell;
                var path = ShortestPath(tiles, start, nexus);
                if (path == null) { error = $"스타트 {k + 1} ({start.x}, {start.y})에서 넥서스까지 경로 타일이 이어지지 않습니다."; return null; }
                for (int i = 0; i < path.Count; i++)
                    path[i] -= origin;
                map.MonsterPaths.Add(path);
                map.Spawns[k] = path[0];
                map.SpawnWeights[k] = Mathf.Max(0f, spawns[k].weight);
            }
            return map;
        }

        // 너비 우선 탐색: 다른 스타트 칸은 지나지 않는다. 같은 길이면 위 → 오른쪽 → 아래 → 왼쪽 순서로 앞선 길.
        static List<Vector2Int> ShortestPath(IReadOnlyDictionary<Vector2Int, Tile> tiles, Vector2Int start, Vector2Int goal)
        {
            var prev = new Dictionary<Vector2Int, Vector2Int> { [start] = start };
            var queue = new Queue<Vector2Int>();
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                var c = queue.Dequeue();
                if (c == goal)
                    break;
                foreach (var d in Dirs)
                {
                    var nb = c + d;
                    if (prev.ContainsKey(nb) || !tiles.TryGetValue(nb, out var kind))
                        continue;
                    if (kind != Tile.Path && kind != Tile.Nexus)
                        continue;
                    prev[nb] = c;
                    queue.Enqueue(nb);
                }
            }
            if (!prev.ContainsKey(goal))
                return null;
            var path = new List<Vector2Int>();
            for (var c = goal; ; c = prev[c])
            {
                path.Add(c);
                if (c == start)
                    break;
            }
            path.Reverse();
            return path;
        }

        // 스타트 지점 줄 → 게임 웨이브 표 (1층, 웨이브마다 줄을 동시에 진행). 웨이브 수 = 줄의 가장 큰 웨이브 번호.
        public static GameConfig.DesignedWave[] BuildWaves(IList<SpawnSetting> spawns, out int waveCount)
        {
            waveCount = 0;
            foreach (var s in spawns)
                foreach (var l in s.lines)
                    if (l.count > 0)
                        waveCount = Math.Max(waveCount, l.wave);
            var waves = new GameConfig.DesignedWave[waveCount];
            for (int w = 1; w <= waveCount; w++)
            {
                var lines = new List<GameConfig.SpawnLine>();
                for (int k = 0; k < spawns.Count; k++)
                    foreach (var l in spawns[k].lines)
                        if (l.wave == w && l.count > 0)
                            lines.Add(new GameConfig.SpawnLine { monsterId = l.monsterId, count = l.count, intervalSec = l.interval, delaySec = l.delay, spawnIndex = l.random ? -1 : k });
                waves[w - 1] = new GameConfig.DesignedWave { floor = 1, wave = w, parallel = true, lines = lines.ToArray() };
            }
            return waves;
        }

        // ---------- 테스트 시작 · 편집으로 ----------

        void StartTest()
        {
            var map = BuildMap(tiles, spawns, out string error);
            if (map == null)
            {
                SetStatus("테스트할 수 없음: " + error, true);
                return;
            }
            var waves = BuildWaves(spawns, out int waveCount);
            if (waveCount == 0)
            {
                SetStatus("테스트할 수 없음: 스타트 지점에 몬스터 줄이 없습니다 (스타트 칸을 눌러 추가).", true);
                return;
            }
            ApplyConfig(waves, waveCount);
            ui?.Hud.SetFinalFloor(1);
            currentMap = map;
            player.Invincible = invincible;
            ClosePanel();
            Testing = true;
            worldRoot.gameObject.SetActive(false);
            editUi.gameObject.SetActive(false);
            testUi.gameObject.SetActive(true);
            if (gameCanvas != null)
                gameCanvas.enabled = true;
            startNewRun();             // 새 판: 맵 = 위에서 만든 맵 (MapProvider → Board → 3D 타일)
            flow.RequestStartGame();   // 시작 클릭 없이 바로 휴식 → [즉시 시작]으로 웨이브
            Save();
        }

        void ApplyConfig(GameConfig.DesignedWave[] waves, int waveCount)
        {
            var r = config.rules;
            config.finalFloor = 1;
            config.nexusHp = Mathf.Max(1, nexusHp);
            config.waves = waves;
            config.mapPool.prefetchNextTier = false;
            config.floorItems.enabled = false;
            r.baseWaves = waveCount;
            r.maxWaves = waveCount;
            r.runnerFromFloor = 0;   // 돌격조·탱커 자동 추가 없음 (스타트 지점 줄만)
            r.tankerFromFloor = 0;
            r.hpMapScale = new[] { 1f }; // 체력 = 몬스터 기본 체력 그대로
            r.hpComplexityScale = new[] { 1f };
            r.hpSecondFloorScale = 1f;
            r.hpPerWave = 0f;
        }

        void BackToEdit()
        {
            Testing = false;
            startNewRun();                 // 몬스터·타워·투사체를 풀에 반납
            if (mapRenderer != null)
                mapRenderer.Show(null);    // 테스트 맵 타일 정리 (나눠서 지운다)
            if (ui != null)
                ui.Map.PathPreview.SetVisible(false);
            if (gameCanvas != null)
                gameCanvas.enabled = false;
            worldRoot.gameObject.SetActive(true);
            testUi.gameObject.SetActive(false);
            EnterEdit();
        }

        void EnterEdit()
        {
            if (gameCanvas != null)
                gameCanvas.enabled = false;
            editUi.gameObject.SetActive(true);
            testUi.gameObject.SetActive(false);
            if (cameraRig != null)
                cameraRig.SetFallbackSize(EditSize);
            SetTool(Tool.Select);
            RefreshStatus();
        }

        // ---------- 월드 표시 ----------

        Vector3 CellToWorld(Vector2Int c)
        {
            float half = (EditSize - 1) * 0.5f;
            return new Vector3(c.x - half, 0f, c.y - half);
        }

        GameObject CreateTileView(Vector2Int cell, Tile kind)
        {
            GameObject prefab = null;
            if (mapRenderer != null)
            {
                switch (kind)
                {
                    case Tile.Tree:
                        var trees = mapRenderer.BlockedTilePrefabs;
                        prefab = trees != null && trees.Length > 0 ? trees[visualRng.Next(trees.Length)] : null; // 나무 위치가 다른 타일 중 무작위
                        break;
                    case Tile.Grass: prefab = mapRenderer.BuildableTilePrefab; break;
                    default: prefab = mapRenderer.PathTilePrefab; break;
                }
            }
            float height = mapRenderer != null ? mapRenderer.TileHeight : 0.4f;
            GameObject go;
            if (prefab != null)
            {
                go = UnityEngine.Object.Instantiate(prefab, tilesRoot, false);
            }
            else
            {
                go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                UnityEngine.Object.Destroy(go.GetComponent<Collider>());
                go.transform.SetParent(tilesRoot, false);
                go.transform.localScale = new Vector3(1f, height, 1f);
                height *= 0.5f;
            }
            go.name = $"Edit_{cell.x}_{cell.y}_{kind}";
            go.transform.localPosition = CellToWorld(cell) + Vector3.down * height;
            if (prefab != null)
            {
                // 게임 맵 표시(MapGenerator)와 같게: 크기 1칸, 잔디·나무만 90° 단위 무작위 회전
                go.transform.localScale = Vector3.one;
                go.transform.localRotation = kind == Tile.Tree || kind == Tile.Grass ? Quaternion.Euler(0f, 90f * visualRng.Next(4), 0f) : Quaternion.identity;
            }
            if (kind != Tile.Tree)
            {
                // 평평한 바닥 타일은 그림자를 드리우지 않는다 (게임 맵 표시와 같게, 자기 그림자 얼룩 방지)
                foreach (var r in go.GetComponentsInChildren<Renderer>())
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            if (kind == Tile.Spawn || kind == Tile.Nexus)
            {
                var block = new MaterialPropertyBlock();
                var color = kind == Tile.Spawn ? (mapRenderer != null ? mapRenderer.SpawnColor : Color.green) : (mapRenderer != null ? mapRenderer.ExitColor : Color.red);
                block.SetColor(BaseColorId, color);
                block.SetColor("_Color", color);
                foreach (var r in go.GetComponentsInChildren<Renderer>())
                    r.SetPropertyBlock(block);
            }
            return go;
        }

        void CreateGridFloor()
        {
            // 편집 격자: 빈 칸 위치가 보이도록 어두운 바닥에 칸 선을 그린다 (타일 윗면 아래)
            const int Px = 16;
            var tex = new Texture2D(EditSize * Px, EditSize * Px, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[tex.width * tex.height];
            var fill = new Color32(38, 44, 40, 255);
            var line = new Color32(95, 110, 98, 255);
            for (int y = 0; y < tex.height; y++)
                for (int x = 0; x < tex.width; x++)
                    px[y * tex.width + x] = (x % Px == 0 || y % Px == 0 || x == tex.width - 1 || y == tex.height - 1) ? line : fill;
            tex.SetPixels32(px);
            tex.Apply();
            var floor = GameObject.CreatePrimitive(PrimitiveType.Quad);
            UnityEngine.Object.Destroy(floor.GetComponent<Collider>());
            floor.name = "BuilderGrid";
            floor.transform.SetParent(worldRoot, false);
            // 타일 바닥보다 아래 (흙길 타일은 윗면이 살짝 파여 있어 위에 두면 격자가 길을 덮는다)
            floor.transform.localPosition = new Vector3(0f, -(mapRenderer != null ? mapRenderer.TileHeight : 0.4f) - 0.01f, 0f);
            floor.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            floor.transform.localScale = new Vector3(EditSize, EditSize, 1f);
            var mat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            mat.SetTexture("_BaseMap", tex);
            floor.GetComponent<Renderer>().sharedMaterial = mat;
        }

        void CreateGhost()
        {
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            UnityEngine.Object.Destroy(q.GetComponent<Collider>());
            q.name = "BuilderGhost";
            q.transform.SetParent(worldRoot, false);
            q.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            q.transform.localScale = new Vector3(0.92f, 0.92f, 1f);
            ghostMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            q.GetComponent<Renderer>().sharedMaterial = ghostMaterial;
            q.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ghost = q.transform;
            ghost.gameObject.SetActive(false);
        }

        void UpdateGhost(bool show, Vector2Int cell)
        {
            if (ghost.gameObject.activeSelf != show)
                ghost.gameObject.SetActive(show);
            if (!show)
                return;
            bool occupied = tiles.ContainsKey(cell);
            ghost.localPosition = CellToWorld(cell) + Vector3.up * (occupied ? 0.45f : 0.03f);
            var c = erasingRight ? ToolColors[(int)Tool.Erase] : ToolColors[(int)tool];
            ghostMaterial.SetColor(BaseColorId, c);
        }

        // ---------- UI ----------

        void BuildUi()
        {
            var canvasGo = new GameObject("MapBuilderCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 50;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            var root = canvasGo.transform;

            // 편집 화면
            editUi = BasicUi.Rect("Edit", root, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            Stretch(editUi);
            var top = BasicUi.Rect("Top", editUi, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -16f), new Vector2(1880f, 70f));
            BasicUi.Panel(top, new Color(0.05f, 0.06f, 0.08f, 0.85f));
            var title = BasicUi.Rect("Title", top, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(20f, 0f), new Vector2(330f, 60f));
            BasicUi.Label(title, "맵 제작 · 레벨 테스트", 28, TextAnchor.MiddleLeft, new Color(1f, 0.78f, 0.45f));
            BasicUi.Button(top, "Start", "테스트 시작", new Vector2(1f, 0.5f), new Vector2(-110f, 0f), new Vector2(190f, 52f), StartTest, out _);
            BasicUi.Button(top, "Clear", "맵 비우기", new Vector2(1f, 0.5f), new Vector2(-310f, 0f), new Vector2(170f, 52f), ClearAll, out _);
            var st = BasicUi.Rect("Status", top, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(360f, 0f), new Vector2(1110f, 60f));
            // 레벨디자인 라이브러리 (LevelDesign 폴더의 맵): ◀ 이름 ▶
            var lib = BasicUi.Rect("Library", editUi, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(-300f, -96f), new Vector2(760f, 50f));
            BasicUi.Panel(lib, new Color(0.05f, 0.06f, 0.08f, 0.85f));
            BasicUi.Button(lib, "Prev", "◀", new Vector2(0f, 0.5f), new Vector2(30f, 0f), new Vector2(50f, 42f), () => StepLibrary(-1), out _);
            BasicUi.Button(lib, "Next", "▶", new Vector2(1f, 0.5f), new Vector2(-30f, 0f), new Vector2(50f, 42f), () => StepLibrary(+1), out _);
            var ll = BasicUi.Rect("Name", lib, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(640f, 44f));
            libraryLabel = BasicUi.Label(ll, "레벨디자인 맵 불러오기 (◀ ▶)", 20, TextAnchor.MiddleCenter, new Color(0.85f, 0.87f, 0.9f));
            status = BasicUi.Label(st, "", 21, TextAnchor.MiddleLeft, Color.white);

            // 하단 타일 목록
            var bar = BasicUi.Rect("Palette", editUi, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 16f), new Vector2(1240f, 150f));
            BasicUi.Panel(bar, new Color(0.05f, 0.06f, 0.08f, 0.85f));
            for (int i = 0; i < ToolNames.Length; i++)
                CreateSlot(bar, (Tool)i, i);
            var hint = BasicUi.Rect("Hint", editUi, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 172f), new Vector2(1240f, 34f));
            BasicUi.Label(hint, "타일을 끌어다 놓거나, 고른 뒤 격자를 눌러 칠하기 · 우클릭 = 지우기 · 선택으로 스타트/넥서스를 눌러 설정 · WASD·Tab 카메라 · ESC 선택",
                18, TextAnchor.MiddleCenter, new Color(0.85f, 0.87f, 0.9f));

            // 설정 창 (스타트·넥서스)
            panel = BasicUi.Rect("Panel", editUi, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-20f, -100f), new Vector2(900f, 760f));
            BasicUi.Panel(panel, new Color(0.06f, 0.07f, 0.09f, 0.94f));
            panelContent = BasicUi.Rect("Content", panel, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            Stretch(panelContent);
            panel.gameObject.SetActive(false);

            // 테스트 화면: 편집으로 돌아가기
            testUi = BasicUi.Rect("Test", root, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            Stretch(testUi);
            var box = BasicUi.Rect("Box", testUi, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(20f, 20f), new Vector2(430f, 120f));
            BasicUi.Panel(box, new Color(0.05f, 0.06f, 0.08f, 0.85f));
            var tsRt = BasicUi.Rect("Status", box, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -8f), new Vector2(400f, 40f));
            testStatus = BasicUi.Label(tsRt, "", 20, TextAnchor.MiddleLeft, new Color(1f, 0.85f, 0.5f));
            BasicUi.Button(box, "Back", "편집으로 돌아가기", new Vector2(0.5f, 0f), new Vector2(0f, 36f), new Vector2(400f, 52f), BackToEdit, out _);
            testUi.gameObject.SetActive(false);
        }

        static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        void CreateSlot(RectTransform bar, Tool t, int index)
        {
            const float W = 160f;
            var slot = BasicUi.Rect("Slot_" + t, bar, new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(20f + W * 0.5f + index * (W + 10f), 0f), new Vector2(W, 126f));
            var frame = BasicUi.Panel(slot, new Color(0.22f, 0.24f, 0.28f, 1f));
            paletteFrames.Add(frame);
            var sw = BasicUi.Rect("Swatch", slot, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -12f), new Vector2(70f, 70f));
            BasicUi.Panel(sw, ToolColors[index]).raycastTarget = false;
            if (t == Tool.Tree)
            {
                var tri = BasicUi.Rect("Tree", sw, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(28f, 40f));
                BasicUi.Panel(tri, new Color(0.15f, 0.35f, 0.15f)).raycastTarget = false;
            }
            var lb = BasicUi.Rect("Name", slot, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 6f), new Vector2(W, 36f));
            BasicUi.Label(lb, ToolNames[index], 20, TextAnchor.MiddleCenter, Color.white);
            // 누르면 그 타일을 집는다: 손을 떼는 칸에 놓이고, 이후 격자를 눌러 계속 칠할 수 있다
            var trigger = slot.gameObject.AddComponent<EventTrigger>();
            var entry = new EventTrigger.Entry { eventID = EventTriggerType.PointerDown };
            entry.callback.AddListener(_ =>
            {
                SetTool(t);
                paletteDrag = t != Tool.Select;
            });
            trigger.triggers.Add(entry);
        }

        void SetTool(Tool t)
        {
            tool = t;
            for (int i = 0; i < paletteFrames.Count; i++)
                paletteFrames[i].color = i == (int)t ? new Color(0.85f, 0.45f, 0.2f, 1f) : new Color(0.22f, 0.24f, 0.28f, 1f);
        }

        void SetStatus(string text, bool error = false)
        {
            if (status == null)
                return;
            status.text = text;
            status.color = error ? new Color(1f, 0.55f, 0.5f) : Color.white;
        }

        void RefreshStatus()
        {
            int n = 0;
            foreach (var kv in tiles)
                if (kv.Value == Tile.Nexus)
                    n++;
            var map = BuildMap(tiles, spawns, out string error);
            BuildWaves(spawns, out int waves);
            int total = 0;
            foreach (var s in spawns)
                foreach (var l in s.lines)
                    total += l.count;
            if (map != null && !string.IsNullOrEmpty(title))
                SetStatus($"[{title}]  맵 {map.N}×{map.N} · 스타트 {spawns.Count} · 경로 {string.Join("/", map.MonsterPaths.ConvertAll(p => p.Count + "칸"))} · 웨이브 {waves} · 몬스터 {total}마리");
            else if (map == null)
                SetStatus($"타일 {tiles.Count} · 스타트 {spawns.Count} · 넥서스 {n} — {error}", tiles.Count > 0);
            else
                SetStatus($"맵 {map.N}×{map.N} · 스타트 {spawns.Count} · 경로 {string.Join("/", map.MonsterPaths.ConvertAll(p => p.Count + "칸"))} · 웨이브 {waves} · 몬스터 {total}마리 · 넥서스 {(invincible ? "무적" : "체력 " + nexusHp)}");
        }

        // ---------- 스타트 설정 창 ----------

        void ClosePanel()
        {
            openSpawn = null;
            nexusOpen = false;
            if (panel != null)
                panel.gameObject.SetActive(false);
        }

        void OpenSpawnPanel(SpawnSetting s)
        {
            if (s == null)
                return;
            openSpawn = s;
            nexusOpen = false;
            RebuildPanel();
        }

        void OpenNexusPanel()
        {
            openSpawn = null;
            nexusOpen = true;
            RebuildPanel();
        }

        void RebuildPanel()
        {
            for (int i = panelContent.childCount - 1; i >= 0; i--)
                UnityEngine.Object.Destroy(panelContent.GetChild(i).gameObject);
            panel.gameObject.SetActive(true);
            if (nexusOpen)
                BuildNexusPanel();
            else if (openSpawn != null)
                BuildSpawnPanel(openSpawn);
        }

        void BuildSpawnPanel(SpawnSetting s)
        {
            int index = spawns.IndexOf(s);
            float total = 0f;
            foreach (var o in spawns)
                total += Mathf.Max(0f, o.weight);
            float share = total > 0f ? 100f * Mathf.Max(0f, s.weight) / total : 0f;
            panel.sizeDelta = new Vector2(960f, Mathf.Min(900f, 250f + s.lines.Count * 62f));

            float y = -20f;
            Text(panelContent, $"스타트 {index + 1}  ({s.cell.x}, {s.cell.y})", 26, new Vector2(24f, y), 500f, new Color(0.45f, 1f, 0.55f));
            BasicUi.Button(panelContent, "Close", "닫기", new Vector2(1f, 1f), new Vector2(-70f, y - 22f), new Vector2(110f, 44f), ClosePanel, out _);
            y -= 56f;
            Text(panelContent, "가중치 (랜덤 배치)", 21, new Vector2(24f, y), 220f, Color.white);
            Stepper(panelContent, new Vector2(250f, y), s.weight.ToString("0.#"), () => { s.weight = Mathf.Max(0f, s.weight - 0.5f); Changed(); RebuildPanel(); },
                () => { s.weight = Mathf.Min(20f, s.weight + 0.5f); Changed(); RebuildPanel(); });
            Text(panelContent, $"→ 랜덤 줄의 몬스터 중 {share:0}%가 여기서 나옵니다", 19, new Vector2(420f, y), 460f, new Color(0.75f, 0.78f, 0.82f));
            y -= 56f;

            string[] heads = { "웨이브", "몬스터", "수량", "간격(초)", "시작(초)", "나오는 곳" };
            float[] xs = { 24f, 160f, 380f, 510f, 640f, 770f };
            for (int i = 0; i < heads.Length; i++)
                Text(panelContent, heads[i], 18, new Vector2(xs[i], y), 130f, new Color(0.7f, 0.72f, 0.76f));
            y -= 32f;

            var monsters = data.Monsters;
            foreach (var line in s.lines)
            {
                var l = line;
                Stepper(panelContent, new Vector2(xs[0], y), l.wave.ToString(), () => { l.wave = Mathf.Max(1, l.wave - 1); Changed(); RebuildPanel(); }, () => { l.wave = Mathf.Min(20, l.wave + 1); Changed(); RebuildPanel(); }, 34f, 50f);
                int mi = IndexOfMonster(l.monsterId);
                string mname = mi >= 0 ? monsters[mi].displayName : l.monsterId;
                Stepper(panelContent, new Vector2(xs[1], y), mname, () => { l.monsterId = monsters[(Mathf.Max(0, mi) - 1 + monsters.Count) % monsters.Count].id; Changed(); RebuildPanel(); },
                    () => { l.monsterId = monsters[(mi + 1) % monsters.Count].id; Changed(); RebuildPanel(); }, 34f, 140f, "◀", "▶");
                Stepper(panelContent, new Vector2(xs[2], y), l.count.ToString(), () => { l.count = Mathf.Max(0, l.count - 1); Changed(); RebuildPanel(); }, () => { l.count = Mathf.Min(500, l.count + 1); Changed(); RebuildPanel(); }, 34f, 50f);
                Stepper(panelContent, new Vector2(xs[3], y), l.interval.ToString("0.0"), () => { l.interval = Mathf.Max(0.1f, Round1(l.interval - 0.1f)); Changed(); RebuildPanel(); }, () => { l.interval = Mathf.Min(10f, Round1(l.interval + 0.1f)); Changed(); RebuildPanel(); }, 34f, 50f);
                Stepper(panelContent, new Vector2(xs[4], y), l.delay.ToString("0.0"), () => { l.delay = Mathf.Max(0f, Round1(l.delay - 0.5f)); Changed(); RebuildPanel(); }, () => { l.delay = Mathf.Min(300f, Round1(l.delay + 0.5f)); Changed(); RebuildPanel(); }, 34f, 50f);
                BasicUi.Button(panelContent, "Where", l.random ? "가중치 랜덤" : "이 스타트", new Vector2(0f, 1f), new Vector2(xs[5] + 60f, y - 20f), new Vector2(120f, 40f),
                    () => { l.random = !l.random; Changed(); RebuildPanel(); }, out var wl).GetComponent<Image>().color = l.random ? new Color(0.45f, 0.3f, 0.6f) : new Color(0.22f, 0.24f, 0.28f, 0.95f);
                wl.fontSize = 17;
                BasicUi.Button(panelContent, "Del", "✕", new Vector2(1f, 1f), new Vector2(-30f, y - 20f), new Vector2(40f, 40f), () => { s.lines.Remove(l); Changed(); RebuildPanel(); }, out _);
                y -= 62f;
            }
            BasicUi.Button(panelContent, "Add", "+ 줄 추가", new Vector2(0f, 1f), new Vector2(110f, y - 24f), new Vector2(180f, 46f), () =>
            {
                var last = s.lines.Count > 0 ? s.lines[s.lines.Count - 1] : null;
                s.lines.Add(last != null
                    ? new Line { wave = last.wave, monsterId = last.monsterId, count = last.count, interval = last.interval, delay = last.delay, random = last.random }
                    : new Line { monsterId = DefaultMonster() });
                Changed();
                RebuildPanel();
            }, out _);
            Text(panelContent, "줄마다 웨이브가 시작된 뒤 '시작' 초부터 '간격'마다 1마리씩 나옵니다. 줄끼리는 동시에 진행됩니다.", 17, new Vector2(320f, y - 10f), 560f, new Color(0.7f, 0.72f, 0.76f));
        }

        static float Round1(float v) => Mathf.Round(v * 10f) / 10f;

        int IndexOfMonster(string id)
        {
            for (int i = 0; i < data.Monsters.Count; i++)
                if (data.Monsters[i].id == id)
                    return i;
            return -1;
        }

        void BuildNexusPanel()
        {
            panel.sizeDelta = new Vector2(620f, 260f);
            float y = -20f;
            Text(panelContent, "넥서스", 26, new Vector2(24f, y), 300f, new Color(1f, 0.5f, 0.45f));
            BasicUi.Button(panelContent, "Close", "닫기", new Vector2(1f, 1f), new Vector2(-70f, y - 22f), new Vector2(110f, 44f), ClosePanel, out _);
            y -= 70f;
            BasicUi.Button(panelContent, "Invincible", (invincible ? "☑" : "☐") + " 무적", new Vector2(0f, 1f), new Vector2(110f, y - 22f), new Vector2(180f, 50f),
                () => { invincible = !invincible; Changed(); RebuildPanel(); }, out _).GetComponent<Image>().color = invincible ? new Color(0.6f, 0.35f, 0.15f) : new Color(0.22f, 0.24f, 0.28f, 0.95f);
            Text(panelContent, invincible ? "몬스터가 들어와도 체력이 줄지 않습니다" : "체력이 0이 되면 게임 오버", 19, new Vector2(230f, y - 6f), 360f, new Color(0.75f, 0.78f, 0.82f));
            y -= 80f;
            Text(panelContent, "체력", 22, new Vector2(24f, y), 100f, invincible ? new Color(0.5f, 0.5f, 0.5f) : Color.white);
            BasicUi.Button(panelContent, "M10", "−10", new Vector2(0f, 1f), new Vector2(160f, y - 20f), new Vector2(70f, 40f), () => { nexusHp = Mathf.Max(1, nexusHp - 10); Changed(); RebuildPanel(); }, out _);
            Stepper(panelContent, new Vector2(205f, y), nexusHp.ToString(), () => { nexusHp = Mathf.Max(1, nexusHp - 1); Changed(); RebuildPanel(); }, () => { nexusHp = Mathf.Min(9999, nexusHp + 1); Changed(); RebuildPanel(); });
            BasicUi.Button(panelContent, "P10", "+10", new Vector2(0f, 1f), new Vector2(420f, y - 20f), new Vector2(70f, 40f), () => { nexusHp = Mathf.Min(9999, nexusHp + 10); Changed(); RebuildPanel(); }, out _);
        }

        // [−] 값 [+] 한 묶음. pos = 왼쪽 위.
        void Stepper(RectTransform parent, Vector2 pos, string value, Action minus, Action plus, float btn = 40f, float valueW = 70f, string minusText = "−", string plusText = "+")
        {
            BasicUi.Button(parent, "Minus", minusText, new Vector2(0f, 1f), new Vector2(pos.x + btn * 0.5f, pos.y - 20f), new Vector2(btn, 40f), minus, out _);
            var v = BasicUi.Rect("Value", parent, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(pos.x + btn + 2f, pos.y), new Vector2(valueW, 40f));
            BasicUi.Label(v, value, 20, TextAnchor.MiddleCenter, new Color(1f, 0.85f, 0.5f));
            BasicUi.Button(parent, "Plus", plusText, new Vector2(0f, 1f), new Vector2(pos.x + btn * 1.5f + valueW + 4f, pos.y - 20f), new Vector2(btn, 40f), plus, out _);
        }

        static void Text(RectTransform parent, string text, int size, Vector2 pos, float width, Color color)
        {
            var rt = BasicUi.Rect("Text", parent, new Vector2(0f, 1f), new Vector2(0f, 1f), pos, new Vector2(width, 40f));
            BasicUi.Label(rt, text, size, TextAnchor.MiddleLeft, color);
        }

        // ---------- 저장 ----------

        public static void WriteFile(string path, MapFile file) => File.WriteAllText(path, JsonUtility.ToJson(file, true));

        public static MapFile ReadFile(string path) => JsonUtility.FromJson<MapFile>(File.ReadAllText(path));

        // 타일·스타트 설정 → 파일 (레벨디자인 도구도 쓴다)
        public static MapFile ToFile(IReadOnlyDictionary<Vector2Int, Tile> tiles, List<SpawnSetting> spawns, bool invincible, int nexusHp)
        {
            var d = new MapFile { invincible = invincible, nexusHp = nexusHp, spawns = spawns };
            foreach (var kv in tiles)
            {
                d.cells.Add(kv.Key);
                d.kinds.Add((int)kv.Value);
            }
            return d;
        }

        void Save()
        {
            var d = ToFile(tiles, spawns, invincible, nexusHp);
            d.title = title;
            try { WriteFile(SavePath, d); }
            catch (Exception e) { Debug.LogWarning("[MapBuilder] 저장 실패: " + e.Message); }
        }

        // 레벨디자인 라이브러리: 프로젝트의 LevelDesign 폴더(하위 폴더 포함) json을 이름순으로 넘겨 가며 불러온다.
        void StepLibrary(int dir)
        {
            var folder = Path.Combine(Application.dataPath, "..", "LevelDesign");
            library = Directory.Exists(folder) ? Directory.GetFiles(folder, "*.json", SearchOption.AllDirectories) : new string[0];
            Array.Sort(library, StringComparer.Ordinal);
            if (library.Length == 0)
            {
                libraryLabel.text = "LevelDesign 폴더에 맵이 없습니다";
                return;
            }
            libraryIndex = ((libraryIndex < 0 ? (dir > 0 ? -1 : 0) : libraryIndex) + dir + library.Length) % library.Length;
            ClearAll();
            Load(library[libraryIndex]);
            libraryLabel.text = $"{libraryIndex + 1}/{library.Length}  {title}";
            Changed();
        }

        void Load() => Load(SavePath);

        void Load(string path)
        {
            if (!File.Exists(path))
                return;
            try
            {
                var d = ReadFile(path);
                title = d.title ?? "";
                invincible = d.invincible;
                nexusHp = d.nexusHp > 0 ? d.nexusHp : nexusHp;
                for (int i = 0; i < d.cells.Count && i < d.kinds.Count; i++)
                {
                    var c = d.cells[i];
                    if (!InGrid(c))
                        continue;
                    tiles[c] = (Tile)d.kinds[i];
                    tileViews[c] = CreateTileView(c, (Tile)d.kinds[i]);
                }
                foreach (var s in d.spawns)
                    if (tiles.TryGetValue(s.cell, out var k) && k == Tile.Spawn && FindSpawn(s.cell) == null)
                        spawns.Add(s);
                foreach (var kv in tiles)
                    if (kv.Value == Tile.Spawn && FindSpawn(kv.Key) == null)
                        spawns.Add(new SpawnSetting { cell = kv.Key, lines = new List<Line> { new Line { monsterId = DefaultMonster() } } });
            }
            catch (Exception e)
            {
                Debug.LogWarning("[MapBuilder] 불러오기 실패: " + e.Message);
            }
        }
    }
}
