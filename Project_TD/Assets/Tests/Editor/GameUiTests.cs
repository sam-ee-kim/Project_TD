using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace TowerDefence.Tests
{
    // 작업 8: GameUI 배치(GDD 5-1), BuildMode 슬라이드(5-2), 값 변경 때만 글자 갱신(권고 6), 결과 팝업(6-4).
    public class GameUiTests
    {
        GameConfig config;
        GameData data;
        Board board;
        GameFlow flow;
        BuildController build;
        GameUI ui;
        GameObject canvasGo;

        [SetUp]
        public void SetUp()
        {
            config = AssetDatabase.LoadAssetAtPath<GameConfig>("Assets/Data/GameConfig.asset");
            data = new GameData();
            data.Init(config, AssetDatabase.LoadAssetAtPath<MapGenRules>("Assets/Data/MapGenRules.asset"),
                new[] { AssetDatabase.LoadAssetAtPath<MonsterDef>("Assets/Data/Monsters/normal.asset") },
                new[] { AssetDatabase.LoadAssetAtPath<TowerDef>("Assets/Data/Towers/basic.asset") });

            var maps = new MapProvider();
            board = new Board();
            var monsters = new MonsterManager();
            var towers = new TowerManager();
            var player = new PlayerState();
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

            var run = RunState.CreateNew(config);
            board.ResetForNewRun(run);
            ui.ResetForNewRun(run);
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

        Transform Find(string path) => canvasGo.transform.Find(path);
        RectTransform Rt(string path) => (RectTransform)Find(path);

        [Test]
        public void AllGdd51Areas_Exist_EachPanelOwnCanvas()
        {
            string[] areas = { "TopBar", "CashTimer", "RecoveryPanel", "CashLogPanel", "BottomBar", "StartPrompt", "BuildGuide", "ConfirmPopup", "ResultPopup", "FloorBanner" };
            foreach (var area in areas)
            {
                Assert.IsNotNull(Find(area), area + " 없음");
                Assert.IsNotNull(Find(area).GetComponent<Canvas>(), area + "는 자기 Canvas");
            }
            Assert.IsNotNull(Find("TopBar/Floor"));
            Assert.IsNotNull(Find("TopBar/Wave"));
            Assert.IsNotNull(Find("TopBar/NexusHp"));
            Assert.IsNotNull(Find("TopBar/StartNow"), "[즉시 시작]");
            Assert.IsNotNull(Find("CashTimer/Cash"));
            Assert.IsNotNull(Find("CashTimer/Timer"));
            Assert.IsFalse(Find("CashLogPanel").gameObject.activeSelf, "캐시 로그는 자리만 (cashLog.enabled = false)");
        }

        [Test]
        public void BottomButtons_FromListData_UpgradeDisabledWithTooltip()
        {
            Assert.AreEqual(config.ui.bottomButtons.Length, Find("BottomBar").GetComponentsInChildren<Button>(true).Length);
            foreach (var entry in config.ui.bottomButtons)
            {
                var button = Find("BottomBar/Button_" + entry.id).GetComponent<Button>();
                Assert.AreEqual(entry.label, button.GetComponentInChildren<Text>().text);
            }

            ui.HandleStateChanged(new StateChange(GameState.Break, 1, 0));
            Assert.IsTrue(ui.Hud.IsButtonInteractable("build"));
            Assert.IsFalse(ui.Hud.IsButtonInteractable("upgrade"), "업그레이드 회색 비활성 (GDD 5-5)");
            Assert.AreEqual("준비 중", Find("BottomBar/Tooltip_upgrade").GetComponentInChildren<Text>().text);
        }

        [Test]
        public void BuildMode_PanelsSlideOutByDirection_CashTimerStays_ThenBack()
        {
            ui.HandleStateChanged(new StateChange(GameState.Break, 1, 0));
            Vector2 top = Rt("TopBar").anchoredPosition, bottom = Rt("BottomBar").anchoredPosition;
            Vector2 left = Rt("RecoveryPanel").anchoredPosition, right = Rt("CashLogPanel").anchoredPosition;
            Vector2 cash = Rt("CashTimer").anchoredPosition;

            ui.HandleModeChanged(new BuildModeChange(BuildMode.Build, null));
            ui.Tick(0.3f);
            Assert.Greater(Rt("TopBar").anchoredPosition.y, top.y + 72f, "상단 → 위로 화면 밖");
            Assert.Less(Rt("BottomBar").anchoredPosition.y, bottom.y - 104f, "하단 → 아래로 화면 밖");
            Assert.Less(Rt("RecoveryPanel").anchoredPosition.x, left.x - 300f, "좌측 → 왼쪽");
            Assert.Greater(Rt("CashLogPanel").anchoredPosition.x, right.x + 300f, "우측 → 오른쪽");
            Assert.AreEqual(HudView.CashTimerBuildPosition, Rt("CashTimer").anchoredPosition, "캐시·타이머는 안내 문구 아래로 모임");
            Assert.IsTrue(Find("CashTimer").gameObject.activeSelf, "BuildMode 중에도 캐시·타이머 보임");
            Assert.AreEqual(1f, Find("BuildGuide").GetComponent<CanvasGroup>().alpha, 1e-4f, "안내 문구 페이드 인");

            ui.HandleModeChanged(new BuildModeChange(BuildMode.None, null));
            ui.Tick(0.3f);
            Assert.AreEqual(top, Rt("TopBar").anchoredPosition);
            Assert.AreEqual(bottom, Rt("BottomBar").anchoredPosition);
            Assert.AreEqual(cash, Rt("CashTimer").anchoredPosition);
            Assert.IsFalse(Find("BuildGuide").gameObject.activeSelf, "안내 문구 페이드 아웃 후 숨김");
        }

        [Test]
        public void Slide_ReversedMidway_ContinuesFromCurrentPosition()
        {
            var home = Rt("TopBar").anchoredPosition;
            ui.HandleModeChanged(new BuildModeChange(BuildMode.Build, null));
            ui.Tick(0.15f);
            var mid = Rt("TopBar").anchoredPosition;
            Assert.Greater(mid.y, home.y, "나가는 중");

            ui.HandleModeChanged(new BuildModeChange(BuildMode.None, null)); // 도중에 취소
            ui.Tick(0.001f);
            Assert.That(Vector2.Distance(Rt("TopBar").anchoredPosition, mid), Is.LessThan(1f), "현재 위치에서 이어서 (튀지 않음)");
            ui.Tick(0.3f);
            Assert.AreEqual(home, Rt("TopBar").anchoredPosition);
        }

        [Test]
        public void HudText_WrittenOnlyWhenValueChanges()
        {
            ui.HandleStateChanged(new StateChange(GameState.Wave, 1, 1));
            int start = ui.Hud.TextWrites;

            for (int i = 0; i < 300; i++)
                ui.Tick(0.016f); // 웨이브 중 프레임: 값 변경 없음
            Assert.AreEqual(start, ui.Hud.TextWrites, "매 프레임 글자 갱신 없음");

            ui.HandleCashChanged(new CashChange(105, 5, CashReason.Kill));
            ui.HandleCashChanged(new CashChange(105, 0, CashReason.Kill)); // 같은 값
            Assert.AreEqual(start + 1, ui.Hud.TextWrites);

            ui.HandleHpChanged(new HpChange(19, -1));
            Assert.AreEqual(start + 2, ui.Hud.TextWrites);
            Assert.AreEqual("캐시 105", Find("CashTimer/Cash").GetComponentInChildren<Text>().text);
            Assert.AreEqual("넥서스 체력 19", Find("TopBar/NexusHp").GetComponentInChildren<Text>().text);

            ui.HandleStateChanged(new StateChange(GameState.Break, 1, 1));
            int beforeTimer = ui.Hud.TextWrites;
            ui.HandleTimerSecond(new TimerSecond(120));
            ui.HandleTimerSecond(new TimerSecond(120));
            ui.HandleTimerSecond(new TimerSecond(119));
            Assert.AreEqual(beforeTimer + 2, ui.Hud.TextWrites, "초가 바뀔 때만");
            Assert.AreEqual("휴식 119초", Find("CashTimer/Timer").GetComponentInChildren<Text>().text);
        }

        [Test]
        public void ResultPopups_GameOverAndGameClear()
        {
            ui.HandleStateChanged(new StateChange(GameState.GameOver, 7, 2));
            Assert.IsTrue(Find("ResultPopup").gameObject.activeSelf);
            Assert.AreEqual("게임 오버\n도달 층: 7층", Find("ResultPopup/Message").GetComponentInChildren<Text>().text);
            Assert.IsNotNull(Find("ResultPopup/Restart").GetComponent<Button>(), "[다시 시작]");

            ui.HandleHpChanged(new HpChange(13, -1));
            ui.HandleStateChanged(new StateChange(GameState.GameClear, 46, 11));
            Assert.AreEqual("게임 클리어!\n도달 층: 46층, 남은 체력: 13", Find("ResultPopup/Message").GetComponentInChildren<Text>().text);

            ui.HandleStateChanged(new StateChange(GameState.Ready, 1, 0));
            Assert.IsFalse(Find("ResultPopup").gameObject.activeSelf);
            Assert.IsTrue(Find("StartPrompt").gameObject.activeSelf, "Ready에서만 시작 문구");
        }
    }
}
