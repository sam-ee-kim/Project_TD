using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace TowerDefence.Tests
{
    // 작업 7: BuildController + MapView 오버레이 + 확인 팝업 (마우스 대신 PointAt/Click을 직접 호출).
    // 고정 7×7 맵: 부지 (2,0) (2,2) (6,4) …, 경로 (3,1), 부지 아님 (0,6)
    public class BuildControllerTests
    {
        GameConfig config;
        GameData data;
        MapProvider maps;
        Board board;
        MonsterManager monsters;
        TowerManager towers;
        PlayerState player;
        GameFlow flow;
        BuildController build;
        GameUI ui;
        GameObject canvasGo;
        TowerDef basic;
        int modeEvents;

        static readonly Vector2Int SiteA = new Vector2Int(2, 0);
        static readonly Vector2Int SiteB = new Vector2Int(2, 2);
        static readonly Vector2Int PathCell = new Vector2Int(3, 1);
        static readonly Vector2Int EmptyCell = new Vector2Int(0, 6);

        [SetUp]
        public void SetUp()
        {
            config = AssetDatabase.LoadAssetAtPath<GameConfig>("Assets/Data/GameConfig.asset");
            basic = AssetDatabase.LoadAssetAtPath<TowerDef>("Assets/Data/Towers/basic.asset");
            data = new GameData();
            data.Init(config, AssetDatabase.LoadAssetAtPath<MapGenRules>("Assets/Data/MapGenRules.asset"),
                new[] { AssetDatabase.LoadAssetAtPath<MonsterDef>("Assets/Data/Monsters/normal.asset") },
                new[] { basic });

            maps = new MapProvider();
            board = new Board();
            monsters = new MonsterManager();
            towers = new TowerManager();
            player = new PlayerState();
            flow = new GameFlow { LogStates = false };
            build = new BuildController();
            ui = new GameUI();

            canvasGo = new GameObject("TestCanvas", typeof(Canvas), typeof(CanvasScaler));
            maps.Init(data, null, null);
            board.Init(data);
            player.Init(data);
            monsters.Init(data, board);
            towers.Init(data, board, monsters, player);
            flow.Init(data, maps, board, monsters, towers);
            build.Init(data, flow, board, towers, player, null);
            ui.Init(data, canvasGo.GetComponent<Canvas>(), board, build, flow, null, null, null);

            modeEvents = 0;
            flow.StateChanged += ui.HandleStateChanged;
            flow.StateChanged += build.HandleStateChanged;
            player.CashChanged += ui.HandleCashChanged;
            towers.TowerChanged += board.HandleTowerChanged;
            towers.TowerChanged += ui.HandleTowerChanged;
            build.ModeChanged += ui.HandleModeChanged;
            build.ModeChanged += e => modeEvents++;
            build.HoverChanged += ui.HandleHoverChanged;
            build.SelectionChanged += ui.HandleSelectionChanged;
            build.PopupRequested += ui.HandlePopupRequested;

            var run = RunState.CreateNew(config);
            maps.ResetForNewRun(run);
            board.ResetForNewRun(run);
            player.ResetForNewRun(run);
            monsters.ResetForNewRun(run);
            towers.ResetForNewRun(run);
            build.ResetForNewRun(run);
            ui.ResetForNewRun(run);
            flow.ResetForNewRun(run);
            board.Load(new FixedTestMapGenerator().Generate(default));
            flow.RequestStartGame(); // Break: 건축 가능
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(canvasGo);
            var overlay = GameObject.Find("BuildOverlay");
            while (overlay != null)
            {
                Object.DestroyImmediate(overlay);
                overlay = GameObject.Find("BuildOverlay");
            }
        }

        GameObject PopupRoot => canvasGo.transform.Find("ConfirmPopup").gameObject;
        Button PopupLeft => canvasGo.transform.Find("ConfirmPopup/Left").GetComponent<Button>();
        string PopupMessage => canvasGo.transform.Find("ConfirmPopup/Message/Label").GetComponent<Text>().text;
        string PopupWarning => canvasGo.transform.Find("ConfirmPopup/Warning/Label").GetComponent<Text>().text;

        [Test]
        public void Gdd54_BuildProcedure_StaysInModeAfterBuild()
        {
            // 1. [타워 건축] → BuildMode
            build.EnterBuildMode(basic);
            Assert.AreEqual(BuildMode.Build, build.Mode);
            Assert.AreEqual(1, modeEvents);
            Assert.IsTrue(ui.Map.OverlayVisible, "2. 오버레이 표시");

            // 3. 설치 가능 칸 Hover, 4. 클릭 → 파랑 + 팝업
            build.PointAt(true, SiteA);
            build.Click(SiteA);
            Assert.IsTrue(build.HasSelection);
            Assert.AreEqual(SiteA, build.SelectedCell);
            Assert.AreEqual(PopupKind.Build, build.Popup);
            Assert.IsTrue(PopupRoot.activeSelf);
            Assert.AreEqual("설치하시겠습니까?", PopupMessage);
            Assert.IsTrue(PopupLeft.interactable);

            // 4-1. [설치] → 캐시 차감, 타워, 파랑 제거, 모드 유지
            build.ConfirmPopup();
            Assert.IsNotNull(towers.GetTowerAt(SiteA));
            Assert.AreEqual(50, player.Cash);
            Assert.IsFalse(build.HasSelection);
            Assert.AreEqual(PopupKind.None, build.Popup);
            Assert.IsFalse(PopupRoot.activeSelf);
            Assert.AreEqual(BuildMode.Build, build.Mode, "설치 후에도 BuildMode 유지");

            // 4-2. 다른 칸 클릭 → [취소] → 파랑 제거, 계속 선택
            build.Click(SiteB);
            Assert.AreEqual(PopupKind.Build, build.Popup);
            build.CancelPopup();
            Assert.IsNull(towers.GetTowerAt(SiteB));
            Assert.IsFalse(build.HasSelection);
            Assert.AreEqual(BuildMode.Build, build.Mode);
            Assert.AreEqual(50, player.Cash, "취소는 캐시 그대로");

            // 5. ESC(ExitMode) → 종료, 오버레이 제거
            build.ExitMode();
            Assert.AreEqual(BuildMode.None, build.Mode);
            Assert.IsFalse(ui.Map.OverlayVisible);
            Assert.AreEqual(2, modeEvents);
        }

        [Test]
        public void BlockedCells_DoNotReact()
        {
            build.EnterBuildMode(basic);
            build.Click(PathCell);
            build.Click(EmptyCell);
            Assert.AreEqual(PopupKind.None, build.Popup, "경로·부지 아닌 칸은 반응 없음");
            Assert.IsFalse(build.HasSelection);

            towers.Build(SiteA, basic);
            build.Click(SiteA);
            Assert.AreEqual(PopupKind.None, build.Popup, "타워가 있는 칸은 반응 없음");
        }

        [Test]
        public void NotEnoughCash_InstallDisabled_WithMessage()
        {
            player.Spend(70, CashReason.Build); // 30 남음 < 50
            build.EnterBuildMode(basic);
            build.Click(SiteA);
            Assert.AreEqual(PopupKind.Build, build.Popup);
            Assert.IsFalse(PopupLeft.interactable, "[설치] 비활성");
            Assert.AreEqual("캐시가 부족합니다", PopupWarning);

            // 실시간: 팝업이 열린 채 캐시가 늘면 다시 활성
            player.Earn(25, CashReason.Kill);
            Assert.IsTrue(PopupLeft.interactable);
            Assert.AreEqual("", PopupWarning);
        }

        [Test]
        public void Hover_RepaintsAtMostTwoCells()
        {
            build.EnterBuildMode(basic);
            int afterEnter = ui.Map.OverlayCellWrites;
            Assert.AreEqual(49, afterEnter, "진입 때 1번 전체(7×7)");

            var path = new[]
            {
                new Vector2Int(0, 0), new Vector2Int(1, 0), new Vector2Int(2, 0), new Vector2Int(2, 1), new Vector2Int(3, 1),
                new Vector2Int(4, 1), new Vector2Int(4, 2), new Vector2Int(6, 6), new Vector2Int(0, 6),
            };
            int last = ui.Map.OverlayCellWrites;
            foreach (var cell in path)
            {
                build.PointAt(true, cell);
                int delta = ui.Map.OverlayCellWrites - last;
                Assert.LessOrEqual(delta, 2, $"{cell}로 이동 시 {delta}칸 갱신");
                last = ui.Map.OverlayCellWrites;

                build.PointAt(true, cell); // 같은 칸이면 갱신 없음
                Assert.AreEqual(last, ui.Map.OverlayCellWrites, "칸이 안 바뀌면 Hover 처리 없음");
            }

            build.PointAt(false, default); // 맵 밖·UI 위로
            Assert.LessOrEqual(ui.Map.OverlayCellWrites - last, 1);
        }

        [Test]
        public void Sell_PopupShowsRefund_ConfirmReturns35()
        {
            Assert.IsTrue(towers.Build(SiteA, basic));
            Assert.AreEqual(50, player.Cash);

            build.Click(SiteA); // 일반 상태에서 타워 클릭
            Assert.AreEqual(PopupKind.Sell, build.Popup);
            Assert.AreEqual("판매하시겠습니까? (+35 캐시)", PopupMessage);

            build.ConfirmPopup();
            Assert.AreEqual(85, player.Cash, "+35");
            Assert.IsNull(towers.GetTowerAt(SiteA));
            Assert.IsTrue(board.IsBuildable(SiteA));
            Assert.AreEqual(PopupKind.None, build.Popup);
        }

        [Test]
        public void Sell_Cancel_KeepsTower()
        {
            towers.Build(SiteA, basic);
            build.Click(SiteA);
            build.CancelPopup();
            Assert.IsNotNull(towers.GetTowerAt(SiteA));
            Assert.AreEqual(50, player.Cash);
        }

        [Test]
        public void ClickOnEmptyCell_InNoneMode_DoesNothing()
        {
            build.Click(SiteA);
            build.Click(PathCell);
            Assert.AreEqual(PopupKind.None, build.Popup);
        }

        [Test]
        public void GameOver_ClosesModeAndPopup()
        {
            build.EnterBuildMode(basic);
            build.Click(SiteA);
            flow.StartNow();
            Assert.AreEqual(BuildMode.Build, build.Mode, "웨이브가 시작돼도 모드 유지 (실시간)");
            Assert.AreEqual(PopupKind.Build, build.Popup);

            player.TakeNexusDamage(100);
            flow.HandleDefeated();
            Assert.AreEqual(GameState.GameOver, flow.State);
            Assert.AreEqual(BuildMode.None, build.Mode);
            Assert.AreEqual(PopupKind.None, build.Popup);
        }

        // ---------- 타워 목록 · 드래그 설치 · 이동 ----------

        [Test]
        public void DragNewTower_DropOnSite_BuildsWithoutPopup()
        {
            build.EnterBuildMode(basic);
            int drags = 0;
            build.DragChanged += e => drags++;

            build.BeginNewTowerDrag(basic);
            Assert.AreEqual(DragKind.NewTower, build.Drag);
            build.PointAt(true, SiteA);
            build.Drop();

            Assert.AreEqual(DragKind.None, build.Drag);
            Assert.IsNotNull(towers.GetTowerAt(SiteA), "놓은 칸에 설치");
            Assert.AreEqual(config.startCash - basic.buildCost, player.Cash);
            Assert.AreEqual(PopupKind.None, build.Popup, "드래그 설치는 확인 팝업 없음");
            Assert.AreEqual(BuildMode.Build, build.Mode, "연속 건축 가능");
            Assert.GreaterOrEqual(drags, 3, "시작·칸 변경·끝");
        }

        [Test]
        public void DragNewTower_DropOnPathOrOffMap_Cancels()
        {
            build.EnterBuildMode(basic);
            build.BeginNewTowerDrag(basic);
            build.PointAt(true, PathCell);
            build.Drop();
            Assert.IsNull(towers.GetTowerAt(PathCell));

            build.BeginNewTowerDrag(basic);
            build.PointAt(false, default); // UI 위·맵 밖에서 놓음
            build.Drop();

            Assert.AreEqual(0, towers.Towers.Count);
            Assert.AreEqual(config.startCash, player.Cash, "캐시 그대로");
        }

        [Test]
        public void DragNewTower_NotEnoughCash_ShowsInvalid()
        {
            player.Spend(player.Cash - (basic.buildCost - 1), CashReason.Build);
            build.EnterBuildMode(basic);
            TowerDrag last = default;
            build.DragChanged += e => last = e;

            build.BeginNewTowerDrag(basic);
            build.PointAt(true, SiteA);
            Assert.IsFalse(last.Valid, "캐시 부족 → 빨강");
            build.Drop();
            Assert.IsNull(towers.GetTowerAt(SiteA));
        }

        [Test]
        public void DragNewTower_OnlyInBuildMode()
        {
            build.BeginNewTowerDrag(basic); // 일반 상태 (타워 목록이 없음)
            Assert.AreEqual(DragKind.None, build.Drag);
        }

        [Test]
        public void MoveDrag_ToEmptySite_MovesTowerForFree()
        {
            towers.Build(SiteA, basic);
            var tower = towers.GetTowerAt(SiteA);
            int cash = player.Cash;

            build.BeginMoveDrag(tower);
            Assert.AreEqual(DragKind.MoveTower, build.Drag);
            build.PointAt(true, SiteB);
            build.Drop();

            Assert.AreEqual(SiteB, tower.Cell);
            Assert.AreSame(tower, towers.GetTowerAt(SiteB));
            Assert.IsNull(towers.GetTowerAt(SiteA));
            Assert.IsTrue(board.IsOccupied(SiteB));
            Assert.IsFalse(board.IsOccupied(SiteA), "옛 칸 비움");
            Assert.AreEqual(cash, player.Cash, "이동은 무료");

            // 사거리 칸을 새 위치 기준으로 다시 계산
            for (int i = 0; i < board.GetPathLength(0); i++)
            {
                var c = board.GetPathCell(0, i);
                bool inRange = Mathf.Max(Mathf.Abs(c.x - SiteB.x), Mathf.Abs(c.y - SiteB.y)) <= basic.range;
                Assert.AreEqual(inRange, tower.CoversCell[board.CellId(c)], $"경로 칸 {i}");
            }
        }

        [Test]
        public void MoveDrag_ToBlockedCellOrSameCell_KeepsPosition()
        {
            towers.Build(SiteA, basic);
            towers.Build(SiteB, basic);
            var tower = towers.GetTowerAt(SiteA);
            TowerDrag last = default;
            build.DragChanged += e => last = e;

            build.BeginMoveDrag(tower);
            build.PointAt(true, SiteB);  // 다른 타워가 있는 칸
            Assert.IsFalse(last.Valid);
            build.PointAt(true, PathCell);
            Assert.IsFalse(last.Valid);
            build.PointAt(true, SiteA);  // 제자리
            Assert.IsTrue(last.Valid, "제자리는 그대로 두기");
            build.PointAt(true, PathCell);
            build.Drop();

            Assert.AreEqual(SiteA, tower.Cell);
            Assert.IsTrue(board.IsOccupied(SiteA));
        }

        [Test]
        public void MoveDrag_CancelledWhenLeavingBreakOrWave()
        {
            towers.Build(SiteA, basic);
            build.BeginMoveDrag(towers.GetTowerAt(SiteA));
            build.HandleStateChanged(new StateChange(GameState.FloorTransition, 1, 1));
            Assert.AreEqual(DragKind.None, build.Drag);
        }

        [Test]
        public void Palette_SlidesInOnlyInBuildMode_InfoShowsDef()
        {
            var palette = ui.Palette.Root;
            var hidden = palette.anchoredPosition;
            Assert.AreEqual(1, ui.Palette.SlotCount);
            Assert.IsNotNull(palette.Find("Slot_basic/Name"), "아이콘 아래 이름");
            Assert.AreEqual($"{basic.buildCost} 캐시", palette.Find("Slot_basic/Cost").GetComponentInChildren<Text>().text);

            build.EnterBuildMode(basic);
            ui.Tick(1f);
            Assert.Greater(palette.anchoredPosition.y, hidden.y, "건축 모드: 타워 목록이 올라옴");

            palette.Find("Slot_basic").GetComponent<TowerSlot>().Clicked(basic);
            Assert.IsTrue(ui.Info.Visible);
            Assert.AreSame(basic, ui.Info.Shown);
            string title = canvasGo.transform.Find("TowerInfo/Title").GetComponentInChildren<Text>().text;
            Assert.AreEqual(basic.displayName, title);

            build.ExitMode();
            ui.Tick(1f);
            Assert.AreEqual(hidden, palette.anchoredPosition, "나가면 다시 숨음");
            Assert.IsFalse(ui.Info.Visible, "정보 패널도 닫힘");
        }
    }
}
