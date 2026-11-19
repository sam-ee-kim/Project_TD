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
        LobbyView lobby; // 게임 시작 전(Ready) 도시 화면 (씬에 없으면 null: 기존처럼 맵 위 안내 문구만)
        PlayerState player; // 공학연구소: 해금 여부 조회와 [해금] 요청만
        readonly LabView lab = new LabView(); // 로비 공학연구소 핀 → 2단계 이상 타워 해금 창
        readonly FactoryView factory = new FactoryView(); // 로비 반도체 공장 핀 → 모든 타워 기본 스탯 강화 창
        string resultText = ""; // 결과창 문구 (판 끝 M1반도체 지급 줄을 덧붙이려고 기억)
        TowerDef popupDef;
        RectTransform placingLabel; // 지금 놓는 타워 이름 (포인터 옆에 따라다님)
        Text placingText;
        RectTransform canvasRect;

        RectTransform tooltip;
        readonly ConfirmPopup popup = new ConfirmPopup();
        readonly TowerPaletteView palette = new TowerPaletteView();
        readonly TowerInfoView info = new TowerInfoView();
        readonly TowerDragView dragView = new TowerDragView();
        readonly TechTreeView tree = new TechTreeView(); // 하단 [타워 업그레이드]: 테크트리 창 (게임은 멈추지 않음)
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
            MapGenerator mapRenderer, CameraController cameraRig, LobbyView lobby = null, PlayerState player = null)
        {
            this.player = player;
            this.data = data;
            this.board = board;
            this.build = build;
            this.flow = flow;
            this.camera = camera;
            this.lobby = lobby;
            var config = data.Config;

            Map.Init(mapRenderer, cameraRig, board, config != null ? config.ui : null);
            Panels.Init(config != null ? config.ui.panelSlideSec : 0.25f);
            PaletteSlider.Init(config != null ? config.ui.panelSlideSec : 0.25f);

            if (canvas == null)
                return;
            SetupScaler(canvas);
            if (lobby != null)
                lobby.Init(canvas, camera); // HUD보다 먼저: 핀이 시작 안내 문구 아래에 그려진다
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
            tree.Init(canvas.transform, data, palette.GetIcon, build.ChooseUpgrade, build.SellTechTreeTower, build.CloseTechTree);
            if (player != null)
            {
                tree.LabUnlocked = player.IsTowerUnlocked;
                tree.CostMod = player.ApplyCost;
                palette.CostOf = def => player.ApplyCost(def.buildCost);
                palette.RefreshCosts();
                factory.Init(canvas.transform, player, data.Config);
                // 공학연구소 창: 로비(Ready)에서 핀을 누르면 열린다. HUD 위에 그린다
                lab.Init(canvas.transform, data, palette.GetIcon, player.IsTowerUnlocked, def => player.TryUnlockTower(def));
                lab.SetChips(player.M1Chips);
                if (lobby != null)
                    lobby.StartClicked += () =>
                    {
                        if (state == GameState.Ready && !lab.Visible && !factory.Visible)
                            flow.RequestStartGame(); // 요청만 (상태 전환은 GameFlow)
                    };
                if (lobby != null)
                    lobby.PinClicked += action =>
                    {
                        if (action == LobbyView.LabAction && state == GameState.Ready)
                            lab.Show();
                        else if (action == LobbyView.FactoryAction && state == GameState.Ready)
                            factory.Show();
                    };
            }

            // 툴팁·확인 팝업은 HUD 위에 그린다 (나중에 만든 것이 위)
            tooltip = BasicUi.Rect("BlockedTooltip", canvas.transform, Vector2.zero, new Vector2(0.5f, 0f), Vector2.zero, new Vector2(300, 46));
            BasicUi.OwnCanvas(tooltip);
            BasicUi.Panel(tooltip, new Color(0.1f, 0.05f, 0.05f, 0.85f));
            BasicUi.Label(tooltip, BlockedTooltipText, 24, TextAnchor.MiddleCenter, new Color(1f, 0.75f, 0.75f));
            tooltip.gameObject.SetActive(false);
            popup.Init(canvas.transform, build.ConfirmPopup, build.CancelPopup);

            // 지금 설치하는 타워 이름: 포인터 오른쪽 위에 따라다닌다 (건축·재건축 모드, 끌어서 놓는 중)
            canvasRect = (RectTransform)canvas.transform;
            placingLabel = BasicUi.Rect("PlacingName", canvas.transform, new Vector2(0.5f, 0.5f), new Vector2(0f, 0f), Vector2.zero, new Vector2(360f, 44f));
            BasicUi.OwnCanvas(placingLabel);
            BasicUi.Panel(placingLabel, new Color(0.04f, 0.05f, 0.07f, 0.82f)).raycastTarget = false;
            placingText = BasicUi.Label(placingLabel, "", 24, TextAnchor.MiddleCenter, new Color(1f, 0.88f, 0.55f));
            placingLabel.gameObject.SetActive(false);
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
            tree.Show(default);
            tree.SetCash(cash);
            tree.SetFloor(run.Floor);
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
            if (lobby != null)
                lobby.Tick(dt);
            dragView.Tick(dt, build.HasDragPoint, build.DragPoint); // 끌고 있는 타워가 포인터를 따라 공중에 떠 있다
            if (placingLabel != null && placingLabel.gameObject.activeSelf)
            {
                var pointer = UnityEngine.InputSystem.Pointer.current;
                if (pointer != null && RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, pointer.position.ReadValue(), null, out var local))
                    placingLabel.anchoredPosition = local + new Vector2(28f, 30f);
            }
        }

        // --- 이벤트 구독 (GDD 8-4) ---

        public void HandleStateChanged(StateChange e)
        {
            state = e.State;
            int waves = data.Config != null ? LevelRules.GetWaveCount(data.Config, e.Floor) : 0;
            Hud.SetFloor(e.Floor);
            Hud.SetWave(e.Wave, waves);
            tree.SetFloor(e.Floor); // 진화 해금 층

            bool playing = e.State == GameState.Break || e.State == GameState.Wave || e.State == GameState.FloorTransition;
            Hud.SetHudVisible(e.State != GameState.Ready);
            Hud.SetStartPromptVisible(e.State == GameState.Ready && lobby == null); // 로비가 있으면 [방어 시작] 버튼으로 시작
            Hud.SetBottomBarVisible(playing);
            Hud.SetButtonInteractable("build", e.State == GameState.Break || e.State == GameState.Wave);
            Map.SetPathPreviewWanted(e.State == GameState.Ready || e.State == GameState.Break); // 라운드 시작 전 몬스터 경로 미리보기
            Hud.SetButtonInteractable("upgrade", data.Config != null && data.Config.upgradeEnabled); // GDD 5-5: 지금은 비활성
            Hud.SetStartNowVisible(e.State == GameState.Break);
            SetLobbyVisible(e.State == GameState.Ready);

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
                    resultText = $"게임 오버\n도달 층: {e.Floor}층";
                    Hud.ShowResult(resultText);
                    break;
                case GameState.GameClear:
                    Hud.SetTimerText("");
                    resultText = $"게임 클리어!\n도달 층: {e.Floor}층, 남은 체력: {hp}";
                    Hud.ShowResult(resultText);
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
            tree.SetCash(cash);
            if (popupKind == PopupKind.Build)
                RefreshAfford(); // 웨이브 중 처치 캐시로 [설치]가 다시 켜질 수 있다
        }

        public void HandleHpChanged(HpChange e)
        {
            hp = e.Hp;
            Hud.SetNexusHp(hp);
        }

        // 타워 설치·판매로 칸 상태가 바뀌면 그 칸만 다시 칠한다.
        public void HandleTowerChanged(TowerChange e)
        {
            Map.RefreshCell(e.Cell);
            Map.Ranges.HandleTowerChanged(e);
        }

        // 놓을 타워의 칸·방향 (R키로 회전): 사거리 미리보기 + 끌고 있는 타워 모델 방향.
        public void HandlePreviewChanged(PlacementPreview e)
        {
            Map.Ranges.SetPreview(e);
            if (e.Has)
                dragView.SetFacing(e.Facing);
        }

        // G키: 설치된 모든 타워의 사거리 그리드.
        public void HandleRangeDisplayChanged(RangeDisplayChange e) => Map.Ranges.SetShowPlaced(e.Visible);

        // 좌측 타워 회수 목록: 바뀐 종류의 줄만 갱신 ("기본 타워 ×3"). 줄 클릭 → [재건축]/[철거] 팝업 요청.
        public void HandleRecoveryChanged(RecoveryChange e)
        {
            var def = e.Def;
            Hud.SetRecoveryCount(def, e.Count, () => build.OpenRecoveryPopup(def), palette.GetIcon(def), build.BeginRecoveryDrag);
            RefreshPlacingLabel(); // 재건축·이어서 설치: 남은 개수
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

        // 지금 놓는 타워 이름 (상태가 바뀔 때만 글자를 다시 쓴다)
        void RefreshPlacingLabel()
        {
            if (placingLabel == null)
                return;
            TowerDef def = null;
            string text = null;
            switch (build.Drag)
            {
                case DragKind.NewTower:
                    def = dragDef; text = $"{dragDef?.displayName} 설치"; break;
                case DragKind.Recovery:
                    def = dragDef;
                    int left = build.RecoveryCountOf(dragDef);
                    text = build.IsCarrying ? $"{dragDef?.displayName} 이어서 설치 (남은 {left}개) · 클릭해 놓기 · ESC 그만" : $"{dragDef?.displayName} 재건축 (남은 {left}개)";
                    break;
                case DragKind.MoveTower:
                    def = dragDef; text = $"{dragDef?.displayName} 이동"; break;
                default:
                    if (build.Mode == BuildMode.Build && build.ModeDef != null) { def = build.ModeDef; text = $"{def.displayName} 설치"; }
                    else if (build.Mode == BuildMode.Rebuild && build.ModeDef != null) { def = build.ModeDef; text = $"{def.displayName} 재건축 (남은 {build.RecoveryCountOf(def)}개)"; }
                    break;
            }
            bool show = def != null;
            if (show)
            {
                placingText.text = text;
                placingLabel.sizeDelta = new Vector2(Mathf.Max(220f, placingText.preferredWidth + 36f), 44f);
            }
            if (placingLabel.gameObject.activeSelf != show)
                placingLabel.gameObject.SetActive(show);
        }

        TowerDef dragDef;

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
            RefreshPlacingLabel();
        }

        // 드래그 시작·끝: 들고 있는 타워 표시. 일반 상태에서 타워를 옮기는 동안에도 설치 가능 칸 오버레이를 보여 준다
        // (놓일 칸 = 떠 있는 타워 바로 아래 타일이 하이라이트된다).
        public void HandleDragChanged(TowerDrag e)
        {
            dragView.Handle(e);
            if (e.Def != dragDef || e.Kind == DragKind.None)
            {
                dragDef = e.Kind == DragKind.None ? null : e.Def;
                RefreshPlacingLabel();
            }
            else if (e.Kind == DragKind.Recovery)
                RefreshPlacingLabel(); // 이어서 설치: 남은 개수
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
            RefreshPlacingLabel();
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

        // 테크트리 창 열기·닫기·고른 타워 (BuildController.TechTreeChanged).
        public void HandleTechTreeChanged(TechTreeChange e)
        {
            tree.Show(e);
            if (e.Open)
                HideInfo();
        }

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
                    popupDef = e.Def;
                    popupUnlockFloor = e.Def.unlockFloor;
                    popup.Show($"{e.From.displayName} → {e.Def.displayName}\n{(e.Amount == 0 ? "진화 무료" : $"진화 비용 {e.Amount} 캐시")} · 초당 피해 {Dps(e.From):0.#} → {Dps(e.Def):0.#}",
                        "진화", "취소");
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
                    // 테크트리 창 열기/닫기 (게임은 그대로 진행)
                    if (build.IsTechTreeOpen)
                        build.CloseTechTree();
                    else
                        build.OpenTechTree(null);
                    break;
            }
        }

        // 캐시 부족: [설치]·[진화] 비활성 + "캐시가 부족합니다". 진화는 해금 층 전이면 "N층부터 진화 가능".
        void RefreshAfford()
        {
            if (popupKind == PopupKind.Upgrade && player != null && !player.IsTowerUnlocked(popupDef))
            {
                popup.SetLeftEnabled(false, "공학연구소에서 해금해야 진화 가능");
                return;
            }
            if (popupKind == PopupKind.Upgrade && flow.Floor < popupUnlockFloor)
            {
                popup.SetLeftEnabled(false, $"{popupUnlockFloor}층부터 진화 가능");
                return;
            }
            bool afford = cash >= popupCost;
            popup.SetLeftEnabled(afford, afford ? "" : NotEnoughCashText);
        }

        // 공학연구소 진행이 바뀜 (해금, 판 끝 M1반도체 지급).
        public void HandleMetaChanged(MetaChange e)
        {
            lab.SetChips(e.M1Chips);
            factory.Refresh();
            palette.RefreshCosts();  // 설치 비용 할인 단계가 바뀌었을 수 있다
            tree.SetCash(cash);      // 해금 상태·진화 비용이 바뀌었을 수 있다 (열려 있으면 다시 칠함)
            if (state == GameState.GameOver || state == GameState.GameClear)
                Hud.ShowResult($"{resultText}\nM1반도체 +{e.Granted}개 (보유 {e.M1Chips}개) · 작은 반도체 +{e.SmallGranted}개 (보유 {e.SmallChips}개)\n로비의 공학연구소·반도체 공장에서 사용");
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

        // 로비: Ready에서만 도시 화면. 게임이 시작되면 도시를 끄고, 맵 타일이 솟아오르는 연출을 그때 다시 보여 준다
        // (Ready 동안 맵은 로비 카메라 뒤에서 이미 올라와 있으므로).
        void SetLobbyVisible(bool visible)
        {
            if (!visible)
            {
                lab.Hide();
                factory.Hide();
            }
            if (lobby == null || lobby.IsShown == visible)
                return;
            if (visible)
            {
                lobby.Show();
                return;
            }
            lobby.Hide();
            if (board.Map != null)
                Map.Rebuild(board.Map);
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
