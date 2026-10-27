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
        readonly TowerPaletteView palette = new TowerPaletteView();
        readonly TowerInfoView info = new TowerInfoView();
        readonly TowerDragView dragView = new TowerDragView();
        bool dragOverlay; // 일반 상태에서 타워를 옮기는 동안만 켠 오버레이

        GameState state = GameState.Ready;
        PopupKind popupKind;
        int popupCost;
        int popupUnlockFloor; // 진화 팝업: 이 층부터 진화 가능
        int cash;
        int hp;

        public HudView Hud { get; } = new HudView();
        public MapView Map { get; } = new MapView();
        public PanelSlider Panels { get; } = new PanelSlider();
        public PanelSlider PaletteSlider { get; } = new PanelSlider(); // 건축 BuildMode에서만 올라오는 타워 목록
        public TowerPaletteView Palette => palette;
        public TowerInfoView Info => info;

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
            PaletteSlider.Init(config != null ? config.ui.panelSlideSec : 0.25f);

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

            // 타워 목록 (하단 바가 내려간 자리로 올라옴) + 타워 정보 패널
            palette.Init(canvas.transform, BuildableTowers(), HandleSlotClicked, build.BeginNewTowerDrag);
            PaletteSlider.RegisterAppear(palette.Root, ExitDirection.Down);
            info.Init(canvas.transform, HideInfo);

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
            PaletteSlider.SnapIn();
            HideInfo();
            palette.RefreshAfford(cash);
            dragOverlay = false;
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
            PaletteSlider.Tick(dt);
            Map.Tick(dt);
            dragView.Tick(dt, build.HasDragPoint, build.DragPoint); // 끌고 있는 타워가 포인터를 따라 공중에 떠 있다
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
            Map.SetPathPreviewWanted(e.State == GameState.Ready || e.State == GameState.Break); // 라운드 시작 전 몬스터 경로 미리보기
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
            palette.RefreshAfford(cash);
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
            dragOverlay = false;
            if (on)
                Panels.SlideOut();
            else
            {
                Panels.SlideIn();
                SetTooltip(false, default);
            }
            // 타워 목록은 건축 모드에서만 (회수 타워 재건축은 놓을 타워가 정해져 있다)
            if (e.Mode == BuildMode.Build)
                PaletteSlider.SlideOut();
            else
            {
                PaletteSlider.SlideIn();
                HideInfo();
            }
        }

        // 드래그 시작·끝: 들고 있는 타워 표시. 일반 상태에서 타워를 옮기는 동안에도 설치 가능 칸 오버레이를 보여 준다
        // (놓일 칸 = 떠 있는 타워 바로 아래 타일이 하이라이트된다).
        public void HandleDragChanged(TowerDrag e)
        {
            dragView.Handle(e);
            bool dragging = e.Kind != DragKind.None;
            if (dragging && build.Mode == BuildMode.None && !dragOverlay)
            {
                dragOverlay = true;
                Map.ShowBuildOverlay(true);
            }
            else if (!dragging && dragOverlay)
            {
                dragOverlay = false;
                Map.SetHover(false, default);
                Map.ShowBuildOverlay(false);
                SetTooltip(false, default);
            }
        }

        // 타워 목록 아이콘 클릭: 정보 패널 + 그 타워를 칸 클릭 건축 대상으로.
        void HandleSlotClicked(TowerDef def)
        {
            info.Show(def, palette.GetIcon(def));
            palette.SetSelected(def);
            build.SelectBuildDef(def);
        }

        void HideInfo()
        {
            info.Hide();
            palette.SetSelected(null);
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
                case PopupKind.Upgrade:
                    popupCost = e.Amount;
                    popupUnlockFloor = e.Def.unlockFloor;
                    popup.Show($"{e.From.displayName} → {e.Def.displayName}\n진화 비용 {e.Amount} 캐시 · 초당 피해 {Dps(e.From):0.#} → {Dps(e.Def):0.#}",
                        "진화", $"판매 (+{e.Refund})");
                    RefreshAfford();
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
                    if (data.Config != null && data.Config.upgradeEnabled)
                        Hud.ShowBanner("진화할 타워를 클릭하세요");
                    break;
            }
        }

        // 캐시 부족: [설치]·[진화] 비활성 + "캐시가 부족합니다". 진화는 해금 층 전이면 "N층부터 진화 가능".
        void RefreshAfford()
        {
            if (popupKind == PopupKind.Upgrade && flow.Floor < popupUnlockFloor)
            {
                popup.SetLeftEnabled(false, $"{popupUnlockFloor}층부터 진화 가능");
                return;
            }
            bool afford = cash >= popupCost;
            popup.SetLeftEnabled(afford, afford ? "" : NotEnoughCashText);
        }

        // 층 클리어 아이템 안내.
        public void HandleItemGranted(FloorItemGrant e)
        {
            string what = e.Damage ? $"화력 +{e.Bonus * 100f:0}%" : $"연사 +{e.Bonus * 100f:0}%";
            Hud.ShowBanner($"{e.Floor}층 클리어 보상: {what}  (공격력 ×{e.DamageMul:0.##} · 공격 속도 ×{e.SpeedMul:0.##})");
        }

        // 타워 목록에는 1단계 타워만 (진화 단계는 설치된 타워에서 진화로만).
        System.Collections.Generic.List<TowerDef> BuildableTowers()
        {
            var list = new System.Collections.Generic.List<TowerDef>();
            foreach (var def in data.Towers)
                if (def.tier <= 1 || def.showInPalette) // 2단계 이상은 테스트 표시를 켠 것만
                    list.Add(def);
            return list;
        }

        static float Dps(TowerDef d) => d.damage * Mathf.Max(1, d.burstCount) / Mathf.Max(0.01f, d.interval);

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
