using UnityEngine;
using UnityEngine.UI;

namespace TowerDefence
{
    // 화면에 보이는 것 전부: HudView, MapView, 패널 슬라이드, 팝업, 회수 목록 패널, 캐시 로그 자리 (GDD 8-1).
    // 상태를 바꾸지 않는다. 값 표시는 이벤트로 갱신, Tick은 애니메이션만.
    // 버튼은 매니저(GameFlow·BuildController)에 요청만 보낸다 (판정·캐시 변경은 매니저 몫).
    public class GameUI
    {
        const string BlockedTooltipText = "설치할 수 없는 위치";
        const string NotEnoughCashText = "캐시가 부족합니다";

        GameData data;
        RunState run;
        Board board;
        BuildController build;
        GameFlow flow;
        Camera camera;

        RectTransform tooltip;
        readonly ConfirmPopup popup = new ConfirmPopup();

        GameState state = GameState.Ready;
        PopupKind popupKind;
        int popupCost;
        int cash;
        int hp;

        public HudView Hud { get; } = new HudView();
        public MapView Map { get; } = new MapView();
        public PanelSlider Panels { get; } = new PanelSlider();

        public void Init(GameData data, Canvas canvas, Board board, BuildController build, GameFlow flow, Camera camera,
            MapGenerator mapRenderer, CameraController cameraRig)
        {
            this.data = data;
            this.board = board;
            this.build = build;
            this.flow = flow;
            this.camera = camera;
            var config = data.Config;

            Map.Init(mapRenderer, cameraRig, board, config != null ? config.ui : null);
            Panels.Init(config != null ? config.ui.panelSlideSec : 0.25f);

            if (canvas == null)
                return;
            SetupScaler(canvas);
            Hud.Init(canvas, config != null ? config.finalFloor : 0,
                flow.StartNow, flow.RequestRestart,
                config != null ? config.ui.bottomButtons : null, HandleBottomButton,
                config != null && config.cashLog.enabled);

            // BuildMode 슬라이드 (GDD 5-2): 각 패널은 자기가 붙은 쪽으로, 캐시·타이머는 안내 문구 아래로
            Panels.Register(Hud.TopBar, ExitDirection.Up);
            Panels.Register(Hud.BottomBar, ExitDirection.Down);
            Panels.Register(Hud.RecoveryPanel, ExitDirection.Left);
            Panels.Register(Hud.CashLogPanel, ExitDirection.Right); // 보류 기능: 자리만 (cashLog.enabled = false면 숨김)
            Panels.Register(Hud.CashTimer, ExitDirection.None, HudView.CashTimerBuildPosition);
            Panels.RegisterFadeIn(Hud.Guide);

            // 툴팁·확인 팝업은 HUD 위에 그린다 (나중에 만든 것이 위)
            tooltip = BasicUi.Rect("BlockedTooltip", canvas.transform, Vector2.zero, new Vector2(0.5f, 0f), Vector2.zero, new Vector2(300, 46));
            BasicUi.OwnCanvas(tooltip);
            BasicUi.Panel(tooltip, new Color(0.1f, 0.05f, 0.05f, 0.85f));
            BasicUi.Label(tooltip, BlockedTooltipText, 24, TextAnchor.MiddleCenter, new Color(1f, 0.75f, 0.75f));
            tooltip.gameObject.SetActive(false);
            popup.Init(canvas.transform, build.ConfirmPopup, build.CancelPopup);
        }

        // 새 판: 팝업·패널·오버레이를 처음 상태로, HUD에 시작 값.
        public void ResetForNewRun(RunState run)
        {
            this.run = run;
            cash = run.Cash;
            hp = run.NexusHp;
            popupKind = PopupKind.None;
            popup.Hide();
            SetTooltip(false, default);
            Panels.SnapIn();
            Hud.HideResult();
            Hud.HideBanner();
            Hud.ClearRecovery();
            Hud.SetCash(cash);
            Hud.SetNexusHp(hp);
            Hud.SetFloor(run.Floor);
        }

        // GameLoop 7단계: 애니메이션만 (패널 슬라이드, 맵 타일 솟아오르기, 카메라).
        public void Tick(float dt)
        {
            Panels.Tick(dt);
            Map.Tick(dt);
        }

        // --- 이벤트 구독 (GDD 8-4) ---

        public void HandleStateChanged(StateChange e)
        {
            state = e.State;
            int waves = data.Config != null ? LevelRules.GetWaveCount(data.Config, e.Floor) : 0;
            Hud.SetFloor(e.Floor);
            Hud.SetWave(e.Wave, waves);

            bool playing = e.State == GameState.Break || e.State == GameState.Wave || e.State == GameState.FloorTransition;
            Hud.SetHudVisible(e.State != GameState.Ready);
            Hud.SetStartPromptVisible(e.State == GameState.Ready);
            Hud.SetBottomBarVisible(playing);
            Hud.SetButtonInteractable("build", e.State == GameState.Break || e.State == GameState.Wave);
            Hud.SetButtonInteractable("upgrade", data.Config != null && data.Config.upgradeEnabled); // GDD 5-5: 지금은 비활성
            Hud.SetStartNowVisible(e.State == GameState.Break);

            switch (e.State)
            {
                case GameState.Wave:
                    Hud.SetTimerWave();
                    Hud.HideBanner();
                    break;
                case GameState.Break:
                    Hud.HideBanner(); // 휴식 초는 TimerSecondChanged로 표시
                    break;
                case GameState.FloorTransition:
                    Hud.SetTimerText("층 이동 중");
                    Hud.ShowBanner($"{e.Floor}층 클리어!");
                    break;
                case GameState.GameOver:
                    Hud.SetTimerText("");
                    Hud.ShowResult($"게임 오버\n도달 층: {e.Floor}층");
                    break;
                case GameState.GameClear:
                    Hud.SetTimerText("");
                    Hud.ShowResult($"게임 클리어!\n도달 층: {e.Floor}층, 남은 체력: {hp}");
                    break;
                case GameState.Ready:
                    Hud.HideResult();
                    Hud.HideBanner();
                    break;
            }
        }

        public void HandleTimerSecond(TimerSecond e)
        {
            if (state == GameState.Break)
                Hud.SetTimerSeconds(e.Seconds);
        }

        public void HandleCashChanged(CashChange e)
        {
            cash = e.Cash;
            Hud.SetCash(cash);
            if (popupKind == PopupKind.Build)
                RefreshAfford(); // 웨이브 중 처치 캐시로 [설치]가 다시 켜질 수 있다
        }

        public void HandleHpChanged(HpChange e)
        {
            hp = e.Hp;
            Hud.SetNexusHp(hp);
        }

        // 타워 설치·판매로 칸 상태가 바뀌면 그 칸만 다시 칠한다.
        public void HandleTowerChanged(TowerChange e) => Map.RefreshCell(e.Cell);

        // 좌측 타워 회수 목록: 바뀐 종류의 줄만 갱신 ("기본 타워 ×3"). 줄 클릭 → [재건축]/[철거] 팝업 요청.
        public void HandleRecoveryChanged(RecoveryChange e)
        {
            var def = e.Def;
            Hud.SetRecoveryCount(def, e.Count, () => build.OpenRecoveryPopup(def));
        }

        // 만료(70% 반환) 또는 게임오버 소멸: 목록과 패널 제거.
        public void HandleRecoveryExpired(RecoveryExpire e) => Hud.ClearRecovery();

        // Board에 새 맵이 올라오면 3D 맵을 다시 그린다 (타일이 아래에서 솟아오름).
        public void HandleMapLoaded(MapData map) => Map.Rebuild(map);

        // 층 전환 중 다음 맵 대기: 로딩 표시.
        public void HandleMapWaitChanged(bool waiting)
        {
            if (waiting)
                Hud.ShowBanner("다음 맵 준비 중…");
        }

        public void HandleModeChanged(BuildModeChange e)
        {
            bool on = e.Mode != BuildMode.None;
            Hud.SetGuideText(e.Mode == BuildMode.Rebuild ? HudView.RebuildGuideText : HudView.BuildGuideText);
            Map.ShowBuildOverlay(on);
            if (on)
                Panels.SlideOut();
            else
            {
                Panels.SlideIn();
                SetTooltip(false, default);
            }
        }

        public void HandleHoverChanged(CellHover e)
        {
            Map.SetHover(e.HasCell, e.Cell);
            SetTooltip(e.HasCell && !e.Buildable, e.Cell);
        }

        public void HandleSelectionChanged(CellSelection e) => Map.SetSelected(e.HasCell, e.Cell);

        public void HandlePopupRequested(PopupRequest e)
        {
            popupKind = e.Kind;
            switch (e.Kind)
            {
                case PopupKind.Build:
                    popupCost = e.Amount;
                    popup.Show("설치하시겠습니까?", "설치", "취소");
                    RefreshAfford();
                    break;
                case PopupKind.Sell:
                    popup.Show($"판매하시겠습니까? (+{e.Amount} 캐시)", "판매", "취소");
                    break;
                case PopupKind.Recovery:
                    popup.Show($"회수한 {e.Def.displayName}", "재건축", $"철거 (+{e.Amount})");
                    break;
                default:
                    popup.Hide();
                    break;
            }
        }

        // 하단 바 버튼 (GameConfig.ui.bottomButtons의 id). UI는 요청만 보낸다.
        void HandleBottomButton(string id)
        {
            switch (id)
            {
                case "build":
                    if (data.Towers.Count > 0)
                        build.EnterBuildMode(data.Towers[0]); // 타워 종류가 2개 이상이 되면 종류 선택 단계 추가 (GDD 5-7)
                    break;
                case "upgrade":
                    break; // upgradeEnabled = false: 반응 없음 (GDD 5-5)
            }
        }

        // 캐시 부족: [설치] 비활성 + "캐시가 부족합니다".
        void RefreshAfford()
        {
            bool afford = cash >= popupCost;
            popup.SetLeftEnabled(afford, afford ? "" : NotEnoughCashText);
        }

        // 설치 불가 칸 툴팁: 그 칸 위에 띄운다 (마우스 아래 칸이 바뀔 때만 위치 갱신).
        void SetTooltip(bool show, Vector2Int cell)
        {
            if (tooltip == null)
                return;
            if (show && camera != null)
                tooltip.position = camera.WorldToScreenPoint(board.CellToWorld(cell) + Vector3.up * 0.6f);
            tooltip.gameObject.SetActive(show);
        }

        static void SetupScaler(Canvas canvas)
        {
            var scaler = canvas.GetComponent<CanvasScaler>();
            if (scaler == null)
                return;
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
        }
    }
}
