using Unity.Profiling;
using UnityEngine;
using UnityEngine.InputSystem;

namespace TowerDefence
{
    // 유일한 Update (GDD 8-3, 8-5 #1). 씬에 1개.
    // Awake에서 매니저를 1번 만들고, 서로 주입하고, 이벤트를 1번 구독한다.
    // 새 판은 RunState만 새로 만들어 각 매니저 ResetForNewRun으로 넘긴다 (매니저는 다시 만들지 않음).
    public class GameLoop : MonoBehaviour
    {
        [Header("데이터 (ScriptableObject)")]
        [SerializeField] GameConfig config;
        [SerializeField] MapGenRules mapGenRules;
        [SerializeField] MonsterDef[] monsterDefs;
        [SerializeField] TowerDef[] towerDefs;

        [Header("씬 연결")]
        [SerializeField] Camera mainCamera;
        [SerializeField] Canvas canvas;
        [SerializeField] MapGenerator mapRenderer;      // 3D 맵 표시 (타일이 아래에서 솟아오름)
        [SerializeField] CameraController cameraRig;    // 탑다운/탑 카메라 (Tab, WASD)
        [SerializeField] LobbyView lobby;               // 로비 도시 화면 (Ready). 없으면 맵 위 안내 문구만

        [Header("디버그")]
        [Tooltip("켜면 생성기 대신 고정 7×7 테스트 맵을 쓴다 (생성기 연결 전후 비교용).")]
        [SerializeField] bool useFixedTestMap;
        [Tooltip("0이 아니면 판 시드를 고정한다 (같은 시드 = 같은 맵).")]
        [SerializeField] int debugRunSeed;
        [Tooltip("테스트용(임시): 0보다 크면 판 시작 캐시를 이 값으로. 데이터 에셋은 바꾸지 않는다.")]
        [SerializeField] int debugStartCash;
        [Tooltip("테스트용(임시): 0보다 크면 모든 몬스터 체력을 이 값으로. 데이터 에셋은 바꾸지 않는다.")]
        [SerializeField] int debugMonsterHp;
        [Tooltip("맵 생성 테스트 씬(Test_MapGen): 게임 대신 F키 랜덤 맵 생성만 한다.")]
        [SerializeField] bool mapGenTestMode;
        MapGenTestScene mapGenTest;
        [Tooltip("맵 생성 규칙 조절 씬(Test_MapGenRules): 게임 대신 생성 규칙 값을 바꿔 가며 맵만 만든다.")]
        [SerializeField] bool mapGenRulesMode;
        MapGenRulesScene mapGenRulesScene;
        [Tooltip("맵 제작 · 레벨 테스트 씬(Test_MapBuilder): 타일을 놓아 맵을 만들고, 그 맵으로 실제 게임을 돌려 본다.")]
        [SerializeField] bool mapBuilderMode;
        MapBuilderScene mapBuilder;
        [Tooltip("시작 화면 씬(Test_TitleScreen): 게임 대신 타이틀 반복 애니메이션만 돌린다.")]
        [SerializeField] TitleScreenView titleScreen;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        [Header("스트레스 테스트 (에디터·개발 빌드 전용)")]
        [Tooltip("지정하면 마지막 층 조건 스트레스 테스트를 자동 진행하고 측정 보고서를 남긴다 (StressTest 씬 전용).")]
        [SerializeField] StressTestSettings stressTest;
        StressTestDriver stress;
#endif

        // 프로파일러 구간 (Unity Profiler·스트레스 측정용). Auto()는 구조체 스코프라 할당이 없다.
        static readonly ProfilerMarker UpdateMarker = new ProfilerMarker("TD.GameLoop");
        static readonly ProfilerMarker SpawnMarker = new ProfilerMarker("TD.TickSpawn");
        static readonly ProfilerMarker MoveMarker = new ProfilerMarker("TD.TickMove");
        static readonly ProfilerMarker CombatMarker = new ProfilerMarker("TD.TickCombat");
        static readonly ProfilerMarker CleanupMarker = new ProfilerMarker("TD.Cleanup");
        static readonly ProfilerMarker ViewsMarker = new ProfilerMarker("TD.TickViews");

        GameData data;
        GameFlow flow;
        MapProvider maps;
        Board board;
        MonsterManager monsters;
        TowerManager towers;
        PlayerState player;
        BuildController build;
        GameUI ui;

        RunState run;

        void Awake()
        {
            if (titleScreen != null)
            {
                // 시작 화면: 몬스터 행진·타워 발사·폭발 반복 (게임 모듈은 만들지 않는다)
                titleScreen.Init();
                return;
            }
            if (mapGenRulesMode)
            {
                // 맵 생성 규칙 조절: 맵 표시·카메라·규칙 패널만 (게임 모듈은 만들지 않는다)
                mapGenRulesScene = new MapGenRulesScene();
                mapGenRulesScene.Init(config, mapGenRules, mapRenderer, cameraRig, canvas);
                return;
            }
            if (mapGenTestMode)
            {
                // 맵 생성 테스트: 맵 표시·카메라만 (게임 모듈은 만들지 않는다)
                mapGenTest = new MapGenTestScene();
                mapGenTest.Init(config, mapGenRules, mapRenderer, cameraRig, canvas);
                return;
            }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (stressTest != null)
                stressTest.ApplyTo(ref config, ref monsterDefs, ref towerDefs, ref debugStartCash, ref debugMonsterHp, ref useFixedTestMap);
#endif
            if (mapBuilderMode)
            {
                // 맵 제작 씬: 판 설정(층·웨이브 표·넥서스 체력)을 바꿔 쓰므로 런타임 복사본을 만든다 (에셋은 그대로)
                config = Instantiate(config);
                mapBuilder = new MapBuilderScene();
            }

            // 1) 생성 (1번)
            data = new GameData();
            flow = new GameFlow();
            maps = new MapProvider();
            board = new Board();
            monsters = new MonsterManager();
            towers = new TowerManager();
            player = new PlayerState();
            build = new BuildController();
            ui = new GameUI();

            // 2) 주입 (데이터 → 조회·계산·흐름 → 게임 시스템 → 입력·표현)
            data.Init(config, mapGenRules, monsterDefs, towerDefs);
            IMapGenerator generator = mapBuilder != null ? mapBuilder
                : useFixedTestMap ? new FixedTestMapGenerator() : (IMapGenerator)new LayoutMapGenerator();
            maps.Init(data, generator, mapBuilder != null ? null : new LayoutMapValidator()); // 직접 만든 맵은 생성 규칙 검사를 하지 않는다
            maps.SeedOverride = debugRunSeed;
            board.Init(data);
            player.Init(data);
            string metaPath = System.IO.Path.Combine(Application.persistentDataPath, "meta.json");
            player.InitMeta(MetaProgress.Load(metaPath), metaPath); // 공학연구소: M1반도체·해금 타워 (판을 넘어 남음)
            var monsterRoot = new GameObject("Monsters").transform; // 몬스터 표시 오브젝트 부모 (풀, 스크립트 없음)
            monsters.Init(data, board, monsterRoot);
            monsters.DebugHpOverride = debugMonsterHp;
            var towerRoot = new GameObject("Towers").transform; // 타워·투사체 표시 오브젝트 부모 (풀)
            towers.Init(data, board, monsters, player, towerRoot);
            flow.Init(data, maps, board, monsters, towers);
            build.Init(data, flow, board, towers, player, mainCamera);
            build.ClickAnywhereToStart = lobby == null; // 로비(도시 화면)가 있으면 우측 하단 [방어 시작]으로만 시작
            ui.Init(data, canvas, board, build, flow, mainCamera, mapRenderer, cameraRig, lobby, player);

            // 3) 이벤트 구독 (1번, GDD 8-4)
            flow.StateChanged += ui.HandleStateChanged;
            flow.StateChanged += monsters.HandleStateChanged;
            flow.StateChanged += towers.HandleStateChanged;
            flow.StateChanged += build.HandleStateChanged;
            flow.TimerSecondChanged += ui.HandleTimerSecond;
            flow.MapWaitChanged += ui.HandleMapWaitChanged;
            flow.RestartRequested += StartNewRun;  // [다시 시작] → 새 RunState로 모든 매니저 초기화 → Ready
            maps.TierReady += flow.HandleTierReady; // 첫 맵 준비 → Board에 올림
            board.MapLoaded += ui.HandleMapLoaded;  // Board 맵 교체 → 3D 맵 다시 그림
            monsters.Killed += player.HandleKilled;
            monsters.ReachedExit += player.HandleReachedExit;
            player.CashChanged += ui.HandleCashChanged;
            player.HpChanged += ui.HandleHpChanged;
            player.HpChanged += flow.HandleHpChanged;
            player.Defeated += flow.HandleDefeated;
            flow.StateChanged += player.HandleStateChanged; // 판 끝 → M1반도체 (ui.HandleStateChanged가 결과창을 먼저 띄운 뒤)
            player.MetaChanged += ui.HandleMetaChanged;
            towers.TowerChanged += board.HandleTowerChanged;
            towers.TowerChanged += ui.HandleTowerChanged;
            towers.RecoveryChanged += ui.HandleRecoveryChanged;
            towers.RecoveryExpired += ui.HandleRecoveryExpired;
            towers.RecoveryExpired += player.HandleRecoveryExpired;
            towers.RecoveryExpired += build.HandleRecoveryExpired;
            towers.ItemGranted += ui.HandleItemGranted;
            build.ModeChanged += ui.HandleModeChanged;
            build.HoverChanged += ui.HandleHoverChanged;
            build.SelectionChanged += ui.HandleSelectionChanged;
            build.PopupRequested += ui.HandlePopupRequested;
            build.DragChanged += ui.HandleDragChanged;
            build.PreviewChanged += ui.HandlePreviewChanged;
            build.RangeDisplayChanged += ui.HandleRangeDisplayChanged;
            build.TechTreeChanged += ui.HandleTechTreeChanged;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (stressTest != null)
                stress = new StressTestDriver(stressTest, data, flow, maps, board, monsters, towers);
#endif

            if (mapBuilder != null)
            {
                // 편집부터: 판은 [테스트 시작]을 누를 때 시작한다
                mapBuilder.Init(config, data, flow, player, ui, mapRenderer, cameraRig, canvas, mainCamera, StartNewRun);
                return;
            }
            StartNewRun();
        }

        // 새 판 시작 (앱 시작, 다시 시작). RunState를 통째로 새로 만든다.
        void StartNewRun()
        {
            run = RunState.CreateNew(config);
            if (debugStartCash > 0)
                run.Cash = debugStartCash; // 테스트용 임시 값 (에셋은 그대로)
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            stress?.PrepareRun(run);   // 시작 층을 마지막 층 직전으로 (flow가 층 계획을 계산하기 전)
#endif
            maps.ResetForNewRun(run);
            board.ResetForNewRun(run);
            player.ResetForNewRun(run);
            monsters.ResetForNewRun(run);
            towers.ResetForNewRun(run);
            build.ResetForNewRun(run);
            ui.ResetForNewRun(run);
            flow.ResetForNewRun(run); // 마지막: Ready 상태 알림이 다른 모듈 초기화 뒤에 나가도록
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            stress?.AfterRunReset();   // 시작 층 Tier 맵 생성 요청
#endif
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (titleScreen != null)
            {
                titleScreen.Tick(dt);
                if (titleScreen.ReadyToLeave)
                {
                    var next = titleScreen.NextScene;
                    titleScreen = null; // 한 번만
                    UnityEngine.SceneManagement.SceneManager.LoadScene(next); // 시작 화면 → 로비(게임 씬)
                }
                return;
            }
            if (mapGenRulesScene != null)
            {
                mapGenRulesScene.Tick(dt);
                return;
            }
            if (mapBuilder != null && !mapBuilder.Testing)
            {
                mapBuilder.Tick(dt); // 맵 편집 중: 게임 모듈은 멈춰 둔다
                return;
            }
            if (mapGenTest != null)
            {
                mapGenTest.Tick(dt);
                return;
            }
            using var frame = UpdateMarker.Auto();

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            DebugKeys();
            stress?.BeginFrame();
#endif

            build.Tick(dt);                    // 0 입력 (Ready 클릭 → 게임 시작, BuildMode)
            flow.Tick(dt);                     // 1 휴식 타이머, 층 전환(맵 교체), 상태 전환

            if (flow.State == GameState.Wave)
            {
                using (SpawnMarker.Auto())
                    monsters.TickSpawn(dt);    // 2 스폰
                using (MoveMarker.Auto())
                    monsters.TickMove(dt);     // 3 이동, 출구 도착 → 넥서스 피해

                // 3단계에서 체력 0 → GameOver가 되면 전투·클리어 판정은 건너뛴다 (DamageDesign 4-2).
                bool stillWave = flow.State == GameState.Wave;
                if (stillWave)
                {
                    using (CombatMarker.Auto())
                        towers.TickCombat(dt); // 4 전투
                }
                using (CleanupMarker.Auto())
                    monsters.Cleanup();        // 5 Dying·Exited 풀 반납
                if (stillWave)
                    flow.CheckWaveClear();     // 6 웨이브 클리어 판정
            }

            // 7 표현 (모든 상태에서): 몬스터·타워 상태머신 애니메이션, 패널 슬라이드·맵·카메라
            using (ViewsMarker.Auto())
            {
                monsters.TickViews(dt);
                towers.TickViews(dt);
            }
            ui.Tick(dt);
            mapBuilder?.TickTesting(dt);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            stress?.EndFrame();                // 측정 기록·자동 진행 (스트레스 테스트일 때만)
#endif
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        void OnDestroy() => stress?.Dispose(); // 프로파일러 기록기 해제
#endif

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        // --- 디버그 ---
        // F1 디버그 패널 / F3 웨이브 클리어 / F4 즉시 시작 / F5 넥서스 체력 0 / F6 다시 시작 / F7 자동 진행
        // F8 타워 설치(임의 부지) / F9 마지막 타워 판매
        bool debugAutoAdvance;
        DebugPanel debugPanel; // F1로 켤 때만 붙는다 (OnGUI는 있기만 해도 매 프레임 IMGUI 처리·할당이 생기므로 GameLoop에 두지 않음)

        void DebugKeys()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null)
                return;

            if (keyboard.f1Key.wasPressedThisFrame)
                ToggleDebugPanel();
            if (keyboard.f3Key.wasPressedThisFrame)
                DebugClearWave();
            if (keyboard.f4Key.wasPressedThisFrame)
                flow.StartNow();
            if (keyboard.f5Key.wasPressedThisFrame && flow.State == GameState.Wave)
                player.TakeNexusDamage(player.NexusHp);
            if (keyboard.f6Key.wasPressedThisFrame)
                flow.RequestRestart();
            if (keyboard.f7Key.wasPressedThisFrame)
                debugAutoAdvance = !debugAutoAdvance;
            if (keyboard.f8Key.wasPressedThisFrame)
                DebugBuildRandomTower();
            if (keyboard.f9Key.wasPressedThisFrame && towers.Towers.Count > 0)
                towers.Sell(towers.Towers[towers.Towers.Count - 1]);

            // 자동 진행: 휴식은 바로 시작, 웨이브는 바로 클리어 (층 전환 연출 시간은 그대로 기다린다).
            if (debugAutoAdvance)
            {
                if (flow.State == GameState.Break)
                    flow.StartNow();
                else if (flow.State == GameState.Wave)
                    DebugClearWave();
            }
        }

        readonly System.Random debugRng = new System.Random();

        void DebugBuildRandomTower()
        {
            if (board.Map == null || data.Towers.Count == 0)
                return;
            var def = data.Towers[0];
            var candidates = new System.Collections.Generic.List<Vector2Int>();
            for (int x = 0; x < board.N; x++)
                for (int y = 0; y < board.N; y++)
                    if (board.IsBuildable(new Vector2Int(x, y)))
                        candidates.Add(new Vector2Int(x, y));
            if (candidates.Count == 0)
                return;
            var cell = candidates[debugRng.Next(candidates.Count)];
            bool built = towers.Build(cell, def);
            Debug.Log(built ? $"[GameLoop] 타워 설치 {cell} (캐시 {player.Cash})" : $"[GameLoop] 타워 설치 실패 {cell}: 캐시 {player.Cash} / 비용 {def.buildCost}");
        }

        // 남은 스폰을 버리고 살아있는 몬스터를 보상 없이 정리한다. 실제 판정은 6단계 CheckWaveClear가 한다.
        void DebugClearWave()
        {
            if (flow.State == GameState.Wave)
                monsters.DebugFinishWave();
        }

        void ToggleDebugPanel()
        {
            if (debugPanel != null)
            {
                Destroy(debugPanel);
                debugPanel = null;
                return;
            }
            debugPanel = gameObject.AddComponent<DebugPanel>();
            debugPanel.Draw = DrawDebugPanel;
        }

        // DebugPanel.OnGUI가 부른다 (패널이 켜져 있을 때만).
        void DrawDebugPanel()
        {
            if (flow == null)
                return;
            GUILayout.BeginArea(new Rect(10, 110, 460, 260), GUI.skin.box);
            GUILayout.Label($"상태 {flow.State}  |  층 {flow.Floor}/{config.finalFloor} (Tier {flow.CurrentPlan.Tier}, {flow.CurrentPlan.N}×{flow.CurrentPlan.N})");
            GUILayout.Label($"웨이브 {flow.Wave}/{flow.CurrentPlan.Waves}  |  누적 g {flow.TotalWaveIndex}  |  남은 스폰 {monsters.RemainingToSpawn}  |  살아있음 {monsters.AliveCount}");
            GUILayout.Label($"몬스터 풀 {monsters.CreatedCount}개  |  웨이브 중 새로 만든 수 {monsters.CreatedDuringWave}");
            GUILayout.Label($"캐시 {player.Cash}  |  넥서스 체력 {player.NexusHp}  |  타워 {towers.Towers.Count}  |  회수 목록 {towers.RecoveryTotalCount}");
            if (debugStartCash > 0 || debugMonsterHp > 0)
                GUILayout.Label($"[테스트 설정] 시작 캐시 {debugStartCash}, 몬스터 체력 {debugMonsterHp}");
            GUILayout.Label($"F3 웨이브 클리어  F4 즉시 시작  F5 체력 0  F6 다시 시작  F7 자동 진행({(debugAutoAdvance ? "켬" : "끔")})");
            GUILayout.Label("F8 타워 설치(임의 부지)  F9 마지막 타워 판매");
            GUILayout.EndArea();
        }
#endif
    }
}
