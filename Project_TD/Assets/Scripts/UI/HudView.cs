using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TowerDefence
{
    // 화면 고정 UI 배치 (GDD 5-1):
    //   상단 HUD(층, 웨이브, 넥서스 체력, [즉시 시작])          → BuildMode 때 위로 퇴장
    //   캐시·타이머                                              → 화면에 남아 안내 문구 아래로 모임 (exitDirection None)
    //   좌측 타워 회수 목록 패널(작업 9에서 채움)                → 왼쪽으로 퇴장
    //   우측 캐시 로그 패널(자리만, cashLog.enabled = false)    → 오른쪽으로 퇴장
    //   하단 바 (GameConfig.ui.bottomButtons 목록으로 버튼 생성) → 아래로 퇴장
    //   시작 문구, BuildMode 안내 문구, 층 클리어 배너, 결과 팝업(게임 오버/클리어)
    // 값은 바뀔 때만 GameUI가 호출한다 (같은 값이면 문자열을 다시 만들지 않는다). 개별 Update 없음.
    // Canvas 분리 (OptimizationReview 권고 6): 패널마다 자기 Canvas를 둬서, 자주 바뀌는 캐시·타이머 글자가
    // 바뀌어도 그 Canvas만 다시 그린다. 슬라이드 패널도 각자 Canvas라 움직여도 다른 UI를 다시 만들지 않는다.
    // 기본 모양 UI이며 색·글꼴 등 아트는 이후 교체한다.
    public class HudView
    {
        public const string BuildGuideText = "타워를 끌어다 원하는 위치에 놓으세요 (ESC: 나가기)";
        public const string RebuildGuideText = "회수한 타워를 놓을 위치를 선택하세요 (ESC: 나가기)";

        Text guideText;

        // 회수 목록 줄: 종류별 1줄 ("기본 타워 ×3"). 줄은 처음 등장할 때 1번 만들고 이후 글자만 바꾼다.
        sealed class RecoveryRow
        {
            public GameObject Root;
            public Text Label;
            public int Count = -1;
        }

        readonly System.Collections.Generic.Dictionary<TowerDef, RecoveryRow> recoveryRows = new System.Collections.Generic.Dictionary<TowerDef, RecoveryRow>();

        static readonly Color PanelColor = new Color(0.07f, 0.08f, 0.10f, 0.82f);
        static readonly Color Accent = new Color(1f, 0.85f, 0.45f);
        const float BottomButtonWidth = 270f;
        const float BottomButtonGap = 30f;

        Canvas canvas;
        int finalFloor;

        Text floorText, waveText, hpText, cashText, timerText, bannerText, resultText;
        NumberText hpNumber, cashNumber, timerNumber; // 웨이브 중에도 바뀌는 숫자: 할당 없이 갱신 (GDD 8-5)
        Button startNowButton;
        GameObject startPrompt, banner, result;
        readonly System.Collections.Generic.Dictionary<string, Button> bottomButtons = new System.Collections.Generic.Dictionary<string, Button>();
        readonly System.Collections.Generic.HashSet<string> dataDisabled = new System.Collections.Generic.HashSet<string>();

        int shownFloor = -1, shownWave = -1, shownWaves = -1, shownHp = -1, shownCash = -1, shownSeconds = -1;
        bool timerShowsWave;

        public RectTransform TopBar { get; private set; }
        public RectTransform CashTimer { get; private set; }
        public RectTransform BottomBar { get; private set; }
        public RectTransform RecoveryPanel { get; private set; }
        public RectTransform CashLogPanel { get; private set; }
        public CanvasGroup Guide { get; private set; }

        // HUD 글자를 실제로 바꾼 횟수 (값이 바뀔 때만 늘어야 한다, 권고 6 확인용).
        public int TextWrites { get; private set; }

        // BuildMode 때 캐시·타이머가 모이는 자리 (안내 문구 바로 아래)
        public static readonly Vector2 CashTimerBuildPosition = new Vector2(0f, -112f);

        public void Init(Canvas canvas, int finalFloor, Action onStartNow, Action onRestart,
            GameConfig.BottomButton[] buttons, Action<string> onBottomButton, bool cashLogEnabled)
        {
            this.canvas = canvas;
            this.finalFloor = finalFloor;
            if (canvas == null)
                return;
            var root = canvas.transform;

            // --- 상단 HUD ---
            TopBar = BasicUi.Rect("TopBar", root, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(-250f, -14f), new Vector2(1060f, 72f));
            BasicUi.OwnCanvas(TopBar);
            BasicUi.Panel(TopBar, PanelColor);
            floorText = Field(TopBar, "Floor", -420f, 200f, Accent);
            waveText = Field(TopBar, "Wave", -190f, 220f, Color.white);
            hpText = Field(TopBar, "NexusHp", 60f, 240f, new Color(1f, 0.55f, 0.55f));
            hpNumber = new NumberText(hpText, "넥서스 체력 ");
            startNowButton = BasicUi.Button(TopBar, "StartNow", "즉시 시작", new Vector2(0.5f, 0.5f), new Vector2(390f, 0f), new Vector2(200f, 54f), onStartNow, out _);

            // --- 캐시·타이머 (BuildMode에도 남음) ---
            CashTimer = BasicUi.Rect("CashTimer", root, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(530f, -14f), new Vector2(440f, 72f));
            BasicUi.OwnCanvas(CashTimer); // 매초 바뀌는 글자 → 이 Canvas만 다시 그린다
            BasicUi.Panel(CashTimer, PanelColor);
            cashText = Field(CashTimer, "Cash", -105f, 210f, Accent);
            timerText = Field(CashTimer, "Timer", 110f, 210f, Color.white);
            cashNumber = new NumberText(cashText, "캐시 ");
            timerNumber = new NumberText(timerText, "휴식 ", "초");

            // --- 좌측 타워 회수 목록 (작업 9에서 항목을 채운다, 지금은 숨김) ---
            RecoveryPanel = SidePanel(root, "RecoveryPanel", "타워 회수 목록", 0f, 16f);
            RecoveryPanel.gameObject.SetActive(false);

            // --- 우측 캐시 로그 (보류: 자리만, cashLog.enabled = false면 숨김) ---
            CashLogPanel = SidePanel(root, "CashLogPanel", "캐시 로그", 1f, -16f);
            CashLogPanel.gameObject.SetActive(cashLogEnabled);

            // --- 하단 바: 목록 데이터로 버튼 생성 ---
            int count = buttons != null ? buttons.Length : 0;
            float barWidth = Mathf.Max(1, count) * (BottomButtonWidth + BottomButtonGap) + BottomButtonGap;
            BottomBar = BasicUi.Rect("BottomBar", root, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 16f), new Vector2(barWidth, 104f));
            BasicUi.OwnCanvas(BottomBar);
            BasicUi.Panel(BottomBar, PanelColor);
            for (int i = 0; i < count; i++)
            {
                var entry = buttons[i];
                string id = entry.id;
                float x = (i - (count - 1) * 0.5f) * (BottomButtonWidth + BottomButtonGap);
                var button = BasicUi.Button(BottomBar, "Button_" + id, entry.label, new Vector2(0.5f, 0.5f), new Vector2(x, 0f),
                    new Vector2(BottomButtonWidth, 76f), () => onBottomButton?.Invoke(id), out _);
                button.interactable = entry.enabled;
                bottomButtons[id] = button;
                if (!entry.enabled)
                {
                    dataDisabled.Add(id); // 데이터로 비활성 (예: 업그레이드 "준비 중")
                    if (!string.IsNullOrEmpty(entry.disabledTooltip))
                    {
                        var tipRt = BasicUi.Rect("Tooltip_" + id, BottomBar, new Vector2(0.5f, 1f), new Vector2(0.5f, 0f), new Vector2(x, 8f), new Vector2(180f, 40f));
                        BasicUi.Panel(tipRt, new Color(0f, 0f, 0f, 0.85f));
                        BasicUi.Label(tipRt, entry.disabledTooltip, 22, TextAnchor.MiddleCenter, Color.white);
                        var tip = tipRt.gameObject;
                        tip.SetActive(false);
                        AddHover(button.gameObject, () => tip.SetActive(true), () => tip.SetActive(false));
                    }
                }
            }

            // --- 시작 문구 (Ready에서만) ---
            var promptRt = BasicUi.Rect("StartPrompt", root, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 70f), new Vector2(900f, 70f));
            BasicUi.OwnCanvas(promptRt);
            BasicUi.Panel(promptRt, new Color(0f, 0f, 0f, 0.6f));
            BasicUi.Label(promptRt, "아무 곳이나 클릭 시 게임 시작", 34, TextAnchor.MiddleCenter, Color.white);
            startPrompt = promptRt.gameObject;

            // --- BuildMode 안내 문구 (패널 퇴장과 함께 페이드 인) ---
            var guideRt = BasicUi.Rect("BuildGuide", root, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -30f), new Vector2(900f, 62f));
            BasicUi.OwnCanvas(guideRt);
            BasicUi.Panel(guideRt, new Color(0f, 0f, 0f, 0.6f));
            guideText = BasicUi.Label(guideRt, BuildGuideText, 30, TextAnchor.MiddleCenter, Color.white);
            Guide = guideRt.gameObject.AddComponent<CanvasGroup>();
            Guide.blocksRaycasts = false;
            Guide.gameObject.SetActive(false);

            // --- 층 클리어 배너 ---
            var bannerRt = BasicUi.Rect("FloorBanner", root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 180f), new Vector2(700f, 100f));
            BasicUi.OwnCanvas(bannerRt);
            BasicUi.Panel(bannerRt, new Color(0f, 0f, 0f, 0.65f));
            bannerText = BasicUi.Label(bannerRt, "", 48, TextAnchor.MiddleCenter, Accent);
            banner = bannerRt.gameObject;
            banner.SetActive(false);

            // --- 결과 팝업 (게임 오버 / 게임 클리어) ---
            var resultRt = BasicUi.Rect("ResultPopup", root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(640f, 280f));
            BasicUi.OwnCanvas(resultRt);
            BasicUi.Panel(resultRt, new Color(0.06f, 0.06f, 0.08f, 0.94f));
            var resultMsg = BasicUi.Rect("Message", resultRt, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -40f), new Vector2(600f, 110f));
            resultText = BasicUi.Label(resultMsg, "", 40, TextAnchor.MiddleCenter, Color.white);
            BasicUi.Button(resultRt, "Restart", "다시 시작", new Vector2(0.5f, 0f), new Vector2(0f, 64f), new Vector2(260f, 72f), onRestart, out _);
            result = resultRt.gameObject;
            result.SetActive(false);
        }

        public void SetFloor(int floor)
        {
            if (floorText == null || floor == shownFloor)
                return;
            shownFloor = floor;
            floorText.text = $"{floor}층 / {finalFloor}층";
            TextWrites++;
        }

        public void SetWave(int wave, int waveCount)
        {
            if (waveText == null || (wave == shownWave && waveCount == shownWaves))
                return;
            shownWave = wave;
            shownWaves = waveCount;
            waveText.text = $"웨이브 {wave} / {waveCount}";
            TextWrites++;
        }

        public void SetNexusHp(int hp)
        {
            if (hpText == null || hp == shownHp)
                return;
            shownHp = hp;
            hpNumber.Set(hp);
            TextWrites++;
        }

        public void SetCash(int cash)
        {
            if (cashText == null || cash == shownCash)
                return;
            shownCash = cash;
            cashNumber.Set(cash);
            TextWrites++;
        }

        // 휴식 카운트다운 (초가 바뀔 때만 불린다).
        public void SetTimerSeconds(int seconds)
        {
            if (timerText == null || (seconds == shownSeconds && !timerShowsWave))
                return;
            shownSeconds = seconds;
            timerShowsWave = false;
            timerNumber.Set(seconds);
            TextWrites++;
        }

        public void SetTimerWave()
        {
            if (timerText == null || timerShowsWave)
                return;
            timerShowsWave = true;
            shownSeconds = -1;
            timerText.text = "웨이브 진행 중";
            TextWrites++;
        }

        public void SetTimerText(string text)
        {
            if (timerText == null)
                return;
            timerShowsWave = false;
            shownSeconds = -1;
            timerText.text = text;
            TextWrites++;
        }

        public void SetStartPromptVisible(bool visible) => SetActive(startPrompt, visible);
        public void SetStartNowVisible(bool visible) => SetActive(startNowButton != null ? startNowButton.gameObject : null, visible);
        public void SetBottomBarVisible(bool visible) => SetActive(BottomBar != null ? BottomBar.gameObject : null, visible);
        public void SetHudVisible(bool visible)
        {
            SetActive(TopBar != null ? TopBar.gameObject : null, visible);
            SetActive(CashTimer != null ? CashTimer.gameObject : null, visible);
        }

        // 하단 버튼 활성/비활성 (상태에 따라). 데이터로 비활성인 버튼은 계속 비활성.
        public void SetButtonInteractable(string id, bool interactable)
        {
            if (bottomButtons.TryGetValue(id, out var button))
                button.interactable = interactable && !dataDisabled.Contains(id);
        }

        public bool IsButtonInteractable(string id) => bottomButtons.TryGetValue(id, out var button) && button.interactable;

        // BuildMode 안내 문구 (건축 / 재건축).
        public void SetGuideText(string text)
        {
            if (guideText != null && guideText.text != text)
                guideText.text = text;
        }

        // 회수 목록 한 종류의 남은 수. 0이면 그 줄을 숨기고, 모든 줄이 숨으면 패널도 숨긴다.
        public void SetRecoveryCount(TowerDef def, int count, Action onClick)
        {
            if (RecoveryPanel == null || def == null)
                return;
            if (!recoveryRows.TryGetValue(def, out var row))
            {
                if (count <= 0)
                    return;
                float y = -70f - recoveryRows.Count * 70f;
                var button = BasicUi.Button(RecoveryPanel, "Recovery_" + def.id, "", new Vector2(0.5f, 1f), new Vector2(0f, y), new Vector2(270f, 60f), onClick, out var label);
                row = new RecoveryRow { Root = button.gameObject, Label = label };
                recoveryRows.Add(def, row);
            }
            if (row.Count != count)
            {
                row.Count = count;
                if (count > 0)
                    row.Label.text = $"{def.displayName} ×{count}";
            }
            SetActive(row.Root, count > 0);

            bool any = false;
            foreach (var r in recoveryRows.Values)
                any |= r.Count > 0;
            SetActive(RecoveryPanel.gameObject, any);
        }

        // 회수 목록 만료·새 판: 줄과 패널을 모두 숨긴다.
        public void ClearRecovery()
        {
            foreach (var r in recoveryRows.Values)
            {
                r.Count = 0;
                SetActive(r.Root, false);
            }
            if (RecoveryPanel != null)
                SetActive(RecoveryPanel.gameObject, false);
        }

        public void ShowBanner(string text)
        {
            if (banner == null)
                return;
            bannerText.text = text;
            banner.SetActive(true);
        }

        public void HideBanner() => SetActive(banner, false);

        public void ShowResult(string text)
        {
            if (result == null)
                return;
            resultText.text = text;
            result.SetActive(true);
        }

        public void HideResult() => SetActive(result, false);

        static void SetActive(GameObject go, bool active)
        {
            if (go != null && go.activeSelf != active)
                go.SetActive(active);
        }

        // 좌·우 세로 패널 (회수 목록, 캐시 로그). side 0 = 왼쪽, 1 = 오른쪽.
        static RectTransform SidePanel(Transform root, string name, string title, float side, float x)
        {
            var rt = BasicUi.Rect(name, root, new Vector2(side, 0.5f), new Vector2(side, 0.5f), new Vector2(x, 0f), new Vector2(300f, 420f));
            BasicUi.OwnCanvas(rt);
            BasicUi.Panel(rt, PanelColor);
            var titleRt = BasicUi.Rect("Title", rt, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -10f), new Vector2(280f, 44f));
            BasicUi.Label(titleRt, title, 26, TextAnchor.MiddleCenter, Accent);
            return rt;
        }

        static Text Field(RectTransform parent, string name, float x, float width, Color color)
        {
            var rt = BasicUi.Rect(name, parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(x, 0f), new Vector2(width, 60f));
            return BasicUi.Label(rt, "", 30, TextAnchor.MiddleCenter, color);
        }

        // 마우스 올림/내림 (비활성 버튼 툴팁용).
        static void AddHover(GameObject target, Action onEnter, Action onExit)
        {
            var trigger = target.AddComponent<EventTrigger>();
            var enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
            enter.callback.AddListener(_ => onEnter());
            var exit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
            exit.callback.AddListener(_ => onExit());
            trigger.triggers.Add(enter);
            trigger.triggers.Add(exit);
        }
    }
}
