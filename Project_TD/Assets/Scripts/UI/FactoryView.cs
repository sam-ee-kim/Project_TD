using System;
using UnityEngine;
using UnityEngine.UI;

namespace TowerDefence
{
    // 반도체 공장 창 (로비에서 반도체 공장 핀을 누르면 열림, GameUI 소속).
    // 노드 5종(공격력·공격 속도·설치 비용 할인·시작 캐시·넥서스 체력)을 카드로 늘어놓고, 단계(최대 5)·지금 효과·다음 가격·[강화]를 보여 준다.
    // [초기화]는 무료: 쓴 작은 반도체를 모두 돌려받는다. 강화·초기화는 PlayerState에 요청만 한다.
    // 창 뒤 어두운 판이 화면 전체를 덮어 로비의 다른 버튼이 눌리지 않는다. 어두운 곳을 누르면 닫힌다.
    public class FactoryView
    {
        const float CardW = 300f, CardH = 380f, Gap = 16f;
        static readonly Color Dim = new Color(0f, 0f, 0f, 0.6f);
        static readonly Color PanelColor = new Color(0.05f, 0.06f, 0.08f, 0.97f);
        static readonly Color CardColor = new Color(0.13f, 0.15f, 0.19f, 1f);
        static readonly Color CardMax = new Color(0.12f, 0.22f, 0.17f, 1f);
        static readonly Color Chip = new Color(0.62f, 0.85f, 1f);
        static readonly Color PipOn = new Color(1f, 0.72f, 0.3f);
        static readonly Color PipOff = new Color(0.26f, 0.28f, 0.33f);
        static readonly Color Good = new Color(0.55f, 0.9f, 0.6f);
        static readonly Color Bad = new Color(1f, 0.55f, 0.5f);
        static readonly Color Muted = new Color(0.68f, 0.71f, 0.76f);

        static readonly string[] Names = { "공격력", "공격 속도", "설치 비용 할인", "시작 캐시", "넥서스 체력" };
        static readonly string[] Descs =
        {
            "모든 타워의 공격력이 오른다.",
            "모든 타워가 더 빨리 쏜다 (공격 간격 단축).",
            "타워 설치·진화 비용이 줄어든다. 판매·회수 환급은 원래 비용 기준.",
            "판을 시작할 때 받는 캐시가 늘어난다.",
            "넥서스 최대 체력이 늘어난다.",
        };

        sealed class Card
        {
            public FactoryNode Node;
            public Image Background;
            public Image[] Pips;
            public Text Level, Effect, Price, ButtonLabel;
            public Button Button;
        }

        GameObject root;
        Text chipsText;
        Button resetButton;
        readonly Card[] cards = new Card[FactoryNodes.Count];
        PlayerState player;
        GameConfig config;

        public bool Visible => root != null && root.activeSelf;

        public void Init(Transform canvas, PlayerState player, GameConfig config)
        {
            this.player = player;
            this.config = config;

            var dim = BasicUi.Rect("Factory", canvas, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            dim.anchorMin = Vector2.zero;
            dim.anchorMax = Vector2.one;
            BasicUi.OwnCanvas(dim);
            var dimImage = BasicUi.Panel(dim, Dim);
            var dimButton = dim.gameObject.AddComponent<Button>();
            dimButton.targetGraphic = dimImage;
            dimButton.transition = Selectable.Transition.None;
            dimButton.onClick.AddListener(Hide);
            root = dim.gameObject;

            float w = FactoryNodes.Count * CardW + (FactoryNodes.Count - 1) * Gap + 60f;
            float h = CardH + 200f;
            var panel = BasicUi.Rect("Panel", dim, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(w, h));
            BasicUi.Panel(panel, PanelColor);

            var title = BasicUi.Rect("Title", panel, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(30f, -18f), new Vector2(400f, 48f));
            BasicUi.Label(title, "반도체 공장", 36, TextAnchor.MiddleLeft, Color.white);
            var chipsRt = BasicUi.Rect("Chips", panel, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-330f, -22f), new Vector2(380f, 40f));
            chipsText = BasicUi.Label(chipsRt, "", 26, TextAnchor.MiddleRight, Chip);
            resetButton = BasicUi.Button(panel, "Reset", "초기화 (무료)", new Vector2(1f, 1f), new Vector2(-220f, -42f), new Vector2(170f, 46f), () => player.ResetFactory(), out var resetLabel);
            resetLabel.fontSize = 22;
            BasicUi.Button(panel, "Close", "닫기", new Vector2(1f, 1f), new Vector2(-70f, -42f), new Vector2(100f, 46f), Hide, out _);
            var sub = BasicUi.Rect("Sub", panel, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(30f, -70f), new Vector2(w - 60f, 60f));
            int perFloor = config != null ? config.factory.chipsPerFloor : 2;
            BasicUi.Label(sub, $"모든 타워의 기본 스탯을 올립니다. 노드마다 최대 {MaxLevel}단계, 초기화는 무료(쓴 재화를 모두 돌려받음).\n작은 반도체는 판이 끝날 때 클리어한 층마다 {perFloor}개씩 받습니다.",
                18, TextAnchor.UpperLeft, Muted);

            for (int i = 0; i < FactoryNodes.Count; i++)
            {
                var node = (FactoryNode)i;
                var rt = BasicUi.Rect("Node_" + node, panel, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(30f + i * (CardW + Gap), -150f), new Vector2(CardW, CardH));
                var card = new Card { Node = node, Background = BasicUi.Panel(rt, CardColor), Pips = new Image[MaxLevel] };

                var nameRt = BasicUi.Rect("Name", rt, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(16f, -14f), new Vector2(CardW - 32f, 40f));
                BasicUi.Label(nameRt, Names[i], 28, TextAnchor.MiddleLeft, Color.white);
                var descRt = BasicUi.Rect("Desc", rt, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(16f, -58f), new Vector2(CardW - 32f, 70f));
                BasicUi.Label(descRt, Descs[i], 17, TextAnchor.UpperLeft, Muted);

                // 단계 칸 (최대 5)
                float pipW = (CardW - 32f - (MaxLevel - 1) * 8f) / MaxLevel;
                for (int p = 0; p < MaxLevel; p++)
                {
                    var pip = BasicUi.Rect("Pip" + p, rt, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(16f + p * (pipW + 8f), -140f), new Vector2(pipW, 16f));
                    card.Pips[p] = BasicUi.Panel(pip, PipOff);
                    card.Pips[p].raycastTarget = false;
                }
                var levelRt = BasicUi.Rect("Level", rt, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(16f, -166f), new Vector2(CardW - 32f, 30f));
                card.Level = BasicUi.Label(levelRt, "", 20, TextAnchor.MiddleLeft, Color.white);
                var effectRt = BasicUi.Rect("Effect", rt, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(16f, -200f), new Vector2(CardW - 32f, 70f));
                card.Effect = BasicUi.Label(effectRt, "", 22, TextAnchor.UpperLeft, PipOn);

                var priceRt = BasicUi.Rect("Price", rt, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(16f, 76f), new Vector2(CardW - 32f, 30f));
                card.Price = BasicUi.Label(priceRt, "", 20, TextAnchor.MiddleLeft, Chip);
                card.Button = BasicUi.Button(rt, "Upgrade", "강화", new Vector2(0.5f, 0f), new Vector2(0f, 38f), new Vector2(CardW - 32f, 52f),
                    () => player.TryUpgradeFactory(node), out card.ButtonLabel);
                cards[i] = card;
            }
            root.SetActive(false);
        }

        int MaxLevel => config != null ? Mathf.Max(1, config.factory.maxLevel) : 5;
        float PerLevel => config != null ? config.factory.perLevel : 0.05f;

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

        // PlayerState.MetaChanged마다 (창이 열려 있을 때만 다시 칠한다)
        public void Refresh()
        {
            if (!Visible || player == null)
                return;
            int chips = player.SmallChips;
            chipsText.text = $"보유 작은 반도체 {chips}개";
            bool any = false;
            foreach (var c in cards)
            {
                int level = player.FactoryLevel(c.Node);
                any |= level > 0;
                bool max = level >= MaxLevel;
                int cost = player.FactoryNextCost(c.Node);
                bool afford = chips >= cost;
                for (int p = 0; p < c.Pips.Length; p++)
                    c.Pips[p].color = p < level ? PipOn : PipOff;
                c.Level.text = $"{level} / {MaxLevel}단계";
                c.Effect.text = level == 0 ? "효과 없음" : EffectText(c.Node, level);
                c.Background.color = max ? CardMax : CardColor;
                c.Price.text = max ? "최대 단계" : $"다음 단계: 작은 반도체 {cost}개";
                c.Price.color = max ? Good : afford ? Chip : Bad;
                c.Button.interactable = !max && afford;
                c.ButtonLabel.text = max ? "MAX" : afford ? "강화" : "부족";
            }
            resetButton.interactable = any;
        }

        string EffectText(FactoryNode node, int level)
        {
            float pct = level * PerLevel * 100f;
            switch (node)
            {
                case FactoryNode.Damage: return $"공격력 +{pct:0}%";
                case FactoryNode.Speed: return $"공격 속도 +{pct:0}%";
                case FactoryNode.Cost: return $"설치·진화 비용 −{pct:0}%";
                case FactoryNode.StartCash:
                    return config != null ? $"시작 캐시 +{pct:0}%\n({config.startCash} → {Mathf.RoundToInt(config.startCash * (1f + level * PerLevel))})" : $"시작 캐시 +{pct:0}%";
                default:
                    return config != null ? $"넥서스 체력 +{pct:0}%\n({config.nexusHp} → {config.nexusHp + Mathf.RoundToInt(config.nexusHp * level * PerLevel)})" : $"넥서스 체력 +{pct:0}%";
            }
        }
    }
}
