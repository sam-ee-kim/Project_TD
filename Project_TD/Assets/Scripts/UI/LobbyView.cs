using System;
using UnityEngine;
using UnityEngine.UI;

namespace TowerDefence
{
    // 로비 화면: 게임 시작 전(Ready) 도시를 위에서 내려다보고, 주요 건물 위에 핀 아이콘을 띄운다.
    // 도시 모델(LobbyCity 프리팹)은 메뉴 TowerDefence/Setup/Lobby City가 SF_City에서 만든다 (메시 합치기·바닥 굽기로 가볍게).
    // Update가 없다: GameUI가 Show/Hide/Tick을 부른다. 게임 중에는 도시 오브젝트를 통째로 꺼서 그리지 않는다.
    //
    // 카메라: 로비 전용 카메라를 켜고 게임 카메라는 끈다 (Camera 컴포넌트만 끄므로 AudioListener는 그대로).
    // 핀: 화면 UI. 건물 위 기준점(anchor)을 로비 카메라로 화면 좌표로 바꿔 매 프레임 옮긴다 (할당 없음).
    public class LobbyView : MonoBehaviour
    {
        [Serializable]
        public class Pin
        {
            public string label;
            public Transform anchor;            // 건물 꼭대기 위 기준점 (핀 끝이 이 점을 가리킨다)
            public Color color = Color.white;
            public string action = "";          // 비어 있지 않으면 누를 수 있는 핀 (예: "lab" = 공학연구소 창). 누르면 PinClicked
        }

        public const string LabAction = "lab";
        public const string FactoryAction = "factory";
        public event Action<string> PinClicked; // → GameUI (action 값)
        public event Action StartClicked;       // 우측 하단 [방어 시작] → GameUI → GameFlow.RequestStartGame

        [Header("방어 시작 버튼 (우측 하단 큰 동그라미)")]
        [SerializeField] float startButtonSize = 250f;
        [SerializeField] Vector2 startButtonOffset = new Vector2(-200f, 200f); // 화면 우측 하단 모서리에서
        [SerializeField] Color startButtonColor = new Color(0.93f, 0.36f, 0.16f);

        RectTransform startButton;
        RectTransform startRing;
        Image startRingImage;

        [SerializeField] Camera lobbyCamera;
        [SerializeField] GameObject city;       // 도시 모델 (게임 중에는 꺼 둔다)
        [SerializeField] Pin[] pins = new Pin[0];
        [SerializeField] Sprite pinSprite;
        [SerializeField] Sprite pinDotSprite;
        [SerializeField] Sprite pinShadowSprite;
        [SerializeField] Vector2 pinSize = new Vector2(48f, 60f);
        [SerializeField] float bobHeight = 6f;  // 핀이 위아래로 떠 있는 폭 (픽셀)
        [SerializeField] float bobSpeed = 2.2f;

        Camera gameCamera;
        RectTransform canvasRect;
        RectTransform pinRoot;
        RectTransform[] pinRects = new RectTransform[0];
        RectTransform[] pinBodies = new RectTransform[0];
        float time;

        public bool IsShown { get; private set; }
        public Camera LobbyCamera => lobbyCamera;
        public int PinCount => pinRects.Length;
        public RectTransform GetPinRect(int i) => pinRects[i];

        // 앱 시작 때 1번 (GameUI.Init): 핀 UI를 만들고 로비를 숨긴 상태로 둔다.
        public void Init(Canvas canvas, Camera gameCamera)
        {
            this.gameCamera = gameCamera;
            if (canvas != null)
            {
                canvasRect = (RectTransform)canvas.transform;
                // HUD보다 먼저 만들어 HUD(시작 안내 문구) 아래에 그린다
                pinRoot = BasicUi.Rect("LobbyPins", canvas.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
                BasicUi.OwnCanvas(pinRoot); // 핀은 매 프레임 움직이므로 자기 Canvas만 다시 그린다
                pinRects = new RectTransform[pins.Length];
                pinBodies = new RectTransform[pins.Length];
                for (int i = 0; i < pins.Length; i++)
                    CreatePin(i);
                CreateStartButton(canvas.transform);
            }
            Hide();
        }

        void CreatePin(int i)
        {
            var pin = pins[i];
            // 핀 한 개: 바닥 그림자 → 몸통(물방울, 건물 색) → 가운데 흰 점 → 위에 이름표. 피벗 = 핀 끝
            var root = BasicUi.Rect("Pin_" + pin.label, pinRoot, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0f), Vector2.zero, pinSize);
            if (pinShadowSprite != null)
            {
                var shadow = BasicUi.Rect("Shadow", root, new Vector2(0.5f, 0f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(pinSize.x * 0.9f, pinSize.x * 0.32f));
                Image(shadow, pinShadowSprite, Color.white);
            }
            var body = BasicUi.Rect("Body", root, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), Vector2.zero, pinSize);
            var bodyImage = Image(body, pinSprite, pin.color);
            if (pinDotSprite != null)
            {
                // 핀 PNG: 원 중심이 위에서 36%, 지름 = 가로 0.68 × 1.25 (가로세로 비 4:5) → 구멍 지름은 가로의 약 0.35
                float d = pinSize.x * 0.36f;
                var dot = BasicUi.Rect("Dot", body, new Vector2(0.5f, 0.64f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(d, d));
                Image(dot, pinDotSprite, Color.white);
            }
            var plate = BasicUi.Rect("Label", body, new Vector2(0.5f, 1f), new Vector2(0.5f, 0f), new Vector2(0f, 8f), new Vector2(170f, 38f));
            BasicUi.Panel(plate, new Color(0.04f, 0.06f, 0.1f, 0.82f));
            var line = BasicUi.Rect("Accent", plate, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), Vector2.zero, new Vector2(170f, 3f));
            BasicUi.Panel(line, pin.color);
            var plateImage = plate.GetComponent<Image>();
            if (plateImage != null)
                plateImage.raycastTarget = false;
            BasicUi.Label(plate, pin.label, 22, TextAnchor.MiddleCenter, Color.white);
            if (!string.IsNullOrEmpty(pin.action))
            {
                // 누를 수 있는 핀: 몸통과 이름표를 누르면 PinClicked (나머지 핀은 클릭을 막지 않아 화면 클릭 = 시작)
                bodyImage.raycastTarget = true;
                if (plateImage != null)
                    plateImage.raycastTarget = true;
                var button = body.gameObject.AddComponent<Button>();
                button.targetGraphic = bodyImage;
                var colors = button.colors;
                colors.highlightedColor = new Color(1.15f, 1.15f, 1.15f, 1f);
                colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
                button.colors = colors;
                string action = pin.action;
                button.onClick.AddListener(() => PinClicked?.Invoke(action));
                BasicUi.Label(BasicUi.Rect("Hint", plate, new Vector2(0.5f, 1f), new Vector2(0.5f, 0f), new Vector2(0f, 2f), new Vector2(170f, 22f)),
                    "눌러서 열기", 16, TextAnchor.MiddleCenter, new Color(1f, 1f, 1f, 0.75f));
            }
            pinRects[i] = root;
            pinBodies[i] = body;
        }

        // 큰 동그라미 버튼: 바깥 링(천천히 퍼지며 사라짐) → 밝은 테두리 → 본체 → "방어 시작"
        void CreateStartButton(Transform canvas)
        {
            var circle = CircleSprite();
            var root = BasicUi.Rect("StartDefense", canvas, new Vector2(1f, 0f), new Vector2(0.5f, 0.5f), startButtonOffset, new Vector2(startButtonSize, startButtonSize));
            BasicUi.OwnCanvas(root);

            startRing = BasicUi.Rect("Ring", root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(startButtonSize, startButtonSize));
            startRingImage = Image(startRing, circle, startButtonColor);
            var rim = BasicUi.Rect("Rim", root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(startButtonSize, startButtonSize));
            Image(rim, circle, new Color(1f, 0.92f, 0.78f));
            var body = BasicUi.Rect("Body", root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(startButtonSize - 16f, startButtonSize - 16f));
            var bodyImage = Image(body, circle, startButtonColor);
            bodyImage.raycastTarget = true; // 이 동그라미만 클릭을 받는다
            var shine = BasicUi.Rect("Shine", body, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 18f), new Vector2(startButtonSize - 60f, startButtonSize - 90f));
            Image(shine, circle, new Color(1f, 1f, 1f, 0.12f));
            var label = BasicUi.Label(body, "방어 시작", 46, TextAnchor.MiddleCenter, Color.white);
            label.fontStyle = FontStyle.Bold;
            var outline = label.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0.35f, 0.08f, 0.02f, 0.9f);
            outline.effectDistance = new Vector2(2f, -2f);

            var button = body.gameObject.AddComponent<Button>();
            button.targetGraphic = bodyImage;
            var colors = button.colors;
            colors.highlightedColor = new Color(1.12f, 1.12f, 1.12f, 1f);
            colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
            button.colors = colors;
            button.onClick.AddListener(() => StartClicked?.Invoke());
            startButton = root;
        }

        // 매끈한 원 스프라이트 (가장자리 1픽셀 부드럽게). 앱 시작 때 1번
        static Sprite CircleSprite()
        {
            const int N = 256;
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, false) { name = "LobbyCircle", wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[N * N];
            float r = N * 0.5f - 1f;
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float dx = x + 0.5f - N * 0.5f, dy = y + 0.5f - N * 0.5f;
                    float a = Mathf.Clamp01(r - Mathf.Sqrt(dx * dx + dy * dy) + 0.5f);
                    px[y * N + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return Sprite.Create(tex, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), 100f);
        }

        static Image Image(RectTransform rt, Sprite sprite, Color color)
        {
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = sprite;
            img.color = color;
            img.raycastTarget = false; // 핀은 클릭을 막지 않는다 (Ready: 아무 곳이나 클릭 → 시작). 누를 수 있는 핀만 켠다
            img.preserveAspect = true;
            return img;
        }

        // Ready에 들어갈 때: 도시를 켜고 로비 카메라로 바꾼다.
        public void Show()
        {
            IsShown = true;
            if (city != null)
                city.SetActive(true);
            if (lobbyCamera != null)
                lobbyCamera.enabled = true;
            if (gameCamera != null && lobbyCamera != null)
                gameCamera.enabled = false;
            if (pinRoot != null)
                pinRoot.gameObject.SetActive(true);
            if (startButton != null)
                startButton.gameObject.SetActive(true);
            time = 0f;
            Tick(0f);
        }

        // 게임 시작(Ready를 벗어날 때): 도시를 끄고 게임 카메라로 돌아간다.
        public void Hide()
        {
            IsShown = false;
            if (city != null)
                city.SetActive(false);
            if (lobbyCamera != null)
                lobbyCamera.enabled = false;
            if (gameCamera != null)
                gameCamera.enabled = true;
            if (pinRoot != null)
                pinRoot.gameObject.SetActive(false);
            if (startButton != null)
                startButton.gameObject.SetActive(false);
        }

        // GameUI.Tick (로비가 보일 때만): 핀을 건물 위 화면 위치로 옮기고 살짝 떠 있게 흔든다.
        public void Tick(float dt)
        {
            if (!IsShown || lobbyCamera == null || canvasRect == null)
                return;
            time += dt;
            if (startRing != null)
            {
                // 바깥 링이 1.6초마다 퍼지며 옅어진다 (누르라는 신호)
                float k = (time % 1.6f) / 1.6f;
                float sc = 1f + k * 0.35f;
                startRing.localScale = new Vector3(sc, sc, 1f);
                var c = startButtonColor;
                c.a = (1f - k) * 0.55f;
                startRingImage.color = c;
            }
            for (int i = 0; i < pinRects.Length; i++)
            {
                var anchor = pins[i].anchor;
                var rt = pinRects[i];
                if (anchor == null)
                    continue;
                var screen = lobbyCamera.WorldToScreenPoint(anchor.position);
                bool visible = screen.z > 0f;
                if (rt.gameObject.activeSelf != visible)
                    rt.gameObject.SetActive(visible);
                if (!visible)
                    continue;
                RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screen, null, out var local);
                rt.anchoredPosition = local;
                // 핀마다 박자를 조금씩 어긋나게
                pinBodies[i].anchoredPosition = new Vector2(0f, (Mathf.Sin(time * bobSpeed + i * 1.7f) * 0.5f + 0.5f) * bobHeight);
            }
        }
    }
}
