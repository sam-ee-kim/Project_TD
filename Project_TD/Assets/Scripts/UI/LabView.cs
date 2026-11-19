using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace TowerDefence
{
    // 공학연구소 창 (로비에서 공학연구소 핀을 누르면 열림, GameUI 소속).
    // 2단계 이상 타워마다 카드 한 장: 모델 아이콘 · 이름 · 단계 · 공격 형식 · 진화 원본 · 주요 수치 · 해금 가격(M1반도체) · [해금].
    // 해금은 PlayerState.TryUnlockTower에 요청만 한다. 표시는 창을 열 때와 M1반도체·해금 상태가 바뀔 때만 다시 칠한다.
    // 창 뒤 어두운 판이 화면 전체를 덮어, 창이 열린 동안 화면 클릭으로 게임이 시작되지 않는다. 어두운 곳을 누르면 닫힌다.
    public class LabView
    {
        const float CardW = 340f, CardH = 370f, Gap = 16f, IconSize = 120f;
        const int Columns = 3;
        static readonly Color Dim = new Color(0f, 0f, 0f, 0.6f);
        static readonly Color PanelColor = new Color(0.05f, 0.06f, 0.08f, 0.97f);
        static readonly Color CardColor = new Color(0.13f, 0.15f, 0.19f, 1f);
        static readonly Color CardDone = new Color(0.12f, 0.22f, 0.17f, 1f);
        static readonly Color Chip = new Color(0.45f, 0.85f, 1f);
        static readonly Color Good = new Color(0.55f, 0.9f, 0.6f);
        static readonly Color Bad = new Color(1f, 0.55f, 0.5f);
        static readonly Color Muted = new Color(0.68f, 0.71f, 0.76f);

        sealed class Card
        {
            public TowerDef Def;
            public Image Background;
            public Button Button;
            public Text ButtonLabel, Price;
        }

        GameObject root;
        Text chipsText;
        readonly List<Card> cards = new List<Card>();
        Func<TowerDef, bool> isUnlocked;
        int chips;

        public bool Visible => root != null && root.activeSelf;

        public void Init(Transform canvas, GameData data, Func<TowerDef, Texture> icon, Func<TowerDef, bool> isUnlocked, Action<TowerDef> onUnlock)
        {
            this.isUnlocked = isUnlocked;
            var targets = Targets(data, out var origin);

            var dim = BasicUi.Rect("Lab", canvas, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            dim.anchorMin = Vector2.zero;
            dim.anchorMax = Vector2.one;
            dim.sizeDelta = Vector2.zero;
            BasicUi.OwnCanvas(dim);
            var dimImage = BasicUi.Panel(dim, Dim);
            var dimButton = dim.gameObject.AddComponent<Button>();
            dimButton.targetGraphic = dimImage;
            dimButton.transition = Selectable.Transition.None;
            dimButton.onClick.AddListener(Hide);
            root = dim.gameObject;

            int rows = Mathf.Max(1, (targets.Count + Columns - 1) / Columns);
            float w = Columns * CardW + (Columns - 1) * Gap + 60f;
            float h = rows * CardH + (rows - 1) * Gap + 180f;
            var panel = BasicUi.Rect("Panel", dim, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(w, h));
            BasicUi.Panel(panel, PanelColor); // 창 안쪽 클릭은 여기서 막혀 닫히지 않는다

            var title = BasicUi.Rect("Title", panel, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(30f, -18f), new Vector2(400f, 48f));
            BasicUi.Label(title, "공학연구소", 36, TextAnchor.MiddleLeft, Color.white);
            var chipsRt = BasicUi.Rect("Chips", panel, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-150f, -22f), new Vector2(380f, 40f));
            chipsText = BasicUi.Label(chipsRt, "", 26, TextAnchor.MiddleRight, Chip);
            BasicUi.Button(panel, "Close", "닫기", new Vector2(1f, 1f), new Vector2(-70f, -42f), new Vector2(100f, 46f), Hide, out _);
            var sub = BasicUi.Rect("Sub", panel, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(30f, -70f), new Vector2(w - 60f, 60f));
            BasicUi.Label(sub, "2단계 이상 타워를 해금합니다. 해금한 타워만 게임 중 진화 목록에서 진화할 수 있습니다.\nM1반도체는 판이 끝날 때 클리어한 층 수만큼 받습니다.",
                18, TextAnchor.UpperLeft, Muted);

            for (int i = 0; i < targets.Count; i++)
            {
                var def = targets[i];
                int col = i % Columns, row = i / Columns;
                var rt = BasicUi.Rect("Card_" + def.id, panel, new Vector2(0f, 1f), new Vector2(0f, 1f),
                    new Vector2(30f + col * (CardW + Gap), -150f - row * (CardH + Gap)), new Vector2(CardW, CardH));
                var card = new Card { Def = def, Background = BasicUi.Panel(rt, CardColor) };

                var iconRt = BasicUi.Rect("Icon", rt, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(12f, -12f), new Vector2(IconSize, IconSize));
                var raw = iconRt.gameObject.AddComponent<RawImage>();
                raw.raycastTarget = false;
                raw.texture = icon != null ? icon(def) : null;
                raw.enabled = raw.texture != null;

                var nameRt = BasicUi.Rect("Name", rt, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(142f, -14f), new Vector2(CardW - 152f, 36f));
                BasicUi.Label(nameRt, def.displayName, 24, TextAnchor.MiddleLeft, Color.white);
                var tierRt = BasicUi.Rect("Tier", rt, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(142f, -50f), new Vector2(CardW - 152f, 26f));
                BasicUi.Label(tierRt, $"{def.tier}단계 타워", 16, TextAnchor.MiddleLeft, Muted);
                var typeRt = BasicUi.Rect("Type", rt, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(142f, -78f), new Vector2(CardW - 152f, 50f));
                BasicUi.Label(typeRt, TowerInfoView.AttackName(def), 16, TextAnchor.UpperLeft, new Color(1f, 0.85f, 0.45f));

                var statRt = BasicUi.Rect("Stats", rt, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(14f, -140f), new Vector2(CardW - 28f, 150f));
                BasicUi.Label(statRt, (origin.TryGetValue(def, out var from) ? $"진화: {from.displayName} → {def.displayName}\n" : "") + Stats(def), 16, TextAnchor.UpperLeft, new Color(0.88f, 0.9f, 0.93f));

                var priceRt = BasicUi.Rect("Price", rt, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(14f, 16f), new Vector2(160f, 50f));
                card.Price = BasicUi.Label(priceRt, $"M1반도체 {def.unlockPrice}개", 22, TextAnchor.MiddleLeft, Chip);
                card.Button = BasicUi.Button(rt, "Unlock", "해금", new Vector2(1f, 0f), new Vector2(-82f, 41f), new Vector2(140f, 50f),
                    () => onUnlock?.Invoke(def), out card.ButtonLabel);
                cards.Add(card);
            }
            root.SetActive(false);
        }

        // 해금 대상: 1단계 타워의 진화 갈래(upgradeTo)에 있는 2단계 이상 타워 (data.Towers 순서, 원본 갈래 순서).
        static List<TowerDef> Targets(GameData data, out Dictionary<TowerDef, TowerDef> origin)
        {
            origin = new Dictionary<TowerDef, TowerDef>();
            var list = new List<TowerDef>();
            var byId = new Dictionary<string, TowerDef>();
            foreach (var d in data.Towers)
                byId[d.id] = d;
            foreach (var root in data.Towers)
            {
                if (root.tier > 1 || root.upgradeTo == null)
                    continue;
                foreach (var id in root.upgradeTo)
                    if (byId.TryGetValue(id, out var to) && to.tier >= 2 && !origin.ContainsKey(to))
                    {
                        origin[to] = root;
                        list.Add(to);
                    }
            }
            return list;
        }

        static string Stats(TowerDef d)
        {
            int shots = Mathf.Max(1, d.burstCount);
            string dmg = $"공격력 {d.damage:0.##}" + (shots > 1 ? $" × {shots}연발" : "");
            return $"{dmg} · 공격 간격 {d.interval:0.##}초\n사거리 {d.range}칸 · 설치 비용 {d.buildCost} 캐시\n" +
                   (string.IsNullOrEmpty(d.description) ? "" : d.description);
        }

        public void Show()
        {
            if (root == null)
                return;
            root.SetActive(true);
            root.transform.SetAsLastSibling();
            Refresh();
        }

        public void Hide()
        {
            if (root != null)
                root.SetActive(false);
        }

        public void SetChips(int value)
        {
            chips = value;
            if (Visible)
                Refresh();
        }

        void Refresh()
        {
            chipsText.text = $"보유 M1반도체 {chips}개";
            foreach (var c in cards)
            {
                bool done = isUnlocked != null && isUnlocked(c.Def);
                bool afford = chips >= c.Def.unlockPrice;
                c.Background.color = done ? CardDone : CardColor;
                c.Button.interactable = !done && afford;
                c.ButtonLabel.text = done ? "해금됨" : afford ? "해금" : "부족";
                c.ButtonLabel.color = done ? Good : afford ? Color.white : Bad;
                c.Price.text = done ? "해금 완료" : $"M1반도체 {c.Def.unlockPrice}개";
                c.Price.color = done ? Good : afford ? Chip : Bad;
            }
        }
    }
}
