using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace TowerDefence
{
    // 테크트리 창 (하단 [타워 업그레이드] / 진화할 수 있는 타워 클릭). 게임은 멈추지 않는다 (창은 화면 가운데 위쪽, 맵은 그대로 움직인다).
    // 1단계 타워마다 한 갈래: 위에 1단계 타워, 아래에 진화 갈래(TowerDef.upgradeTo)를 가지처럼 잇는다.
    //   듀얼샷 → 냉기 · 전기 · 미니건 / 화염방사 → 플라즈마 · 혼합분진 · 혼합백린
    // 갈래 칸을 누르면 BuildController.ChooseUpgrade: 고른 타워가 있으면 확인 팝업, 없으면 맵에서 그 종류 타워를 클릭하게 안내.
    // 상태(캐시·층·고른 타워)를 바꾸지 않고 표시만 한다. 창을 열 때와 캐시·층이 바뀔 때만 다시 칠한다.
    public class TechTreeView
    {
        const float NodeW = 190f, NodeH = 250f, RootW = 190f, RootH = 150f, Gap = 22f, LineW = 4f;
        static readonly Color PanelColor = new Color(0.05f, 0.06f, 0.08f, 0.95f);
        static readonly Color NodeColor = new Color(0.13f, 0.15f, 0.19f, 1f);
        static readonly Color NodeHot = new Color(0.20f, 0.30f, 0.42f, 1f);      // 고른 타워로 진화 가능 / 고르는 중
        static readonly Color NodeDim = new Color(0.10f, 0.11f, 0.13f, 0.85f);   // 다른 갈래 (고른 타워와 상관없음)
        static readonly Color LineColor = new Color(0.45f, 0.52f, 0.62f, 1f);
        static readonly Color Accent = new Color(1f, 0.85f, 0.45f);
        static readonly Color Good = new Color(0.55f, 0.9f, 0.6f);
        static readonly Color Bad = new Color(1f, 0.55f, 0.5f);
        static readonly Color Muted = new Color(0.65f, 0.68f, 0.72f);
        static readonly Color LockShade = new Color(0.01f, 0.01f, 0.02f, 0.88f); // 잠긴 칸 덮개
        static readonly Color LockColor = new Color(0.78f, 0.74f, 0.62f);

        sealed class Branch
        {
            public TowerDef Root;
            public Image RootBg;
            public readonly List<Node> Nodes = new List<Node>();
        }

        sealed class Node
        {
            public TowerDef From, To;
            public Image Background;
            public Button Button;
            public Text Cost, State;
            public GameObject Lock; // 잠김 덮개 (검은 막 + 자물쇠)
        }

        GameObject root;
        Text guide;
        Button sellButton;
        Text sellLabel;
        readonly List<Branch> branches = new List<Branch>();
        GameData data;
        TowerInstance tower;
        TowerDef picking;
        int cash, floor = 1;

        public bool Visible => root != null && root.activeSelf;
        public Func<TowerDef, bool> LabUnlocked; // 공학연구소에서 해금했는지 (null이면 모두 해금)
        public Func<int, int> CostMod;            // 반도체 공장 할인 (null이면 그대로)

        public void Init(Transform canvas, GameData data, Func<TowerDef, Texture> icon, Action<TowerDef> onChoose, Action onSell, Action onClose)
        {
            this.data = data;
            // 진화 갈래가 있는 1단계 타워만 (data.Towers 순서)
            foreach (var def in data.Towers)
            {
                if (def.tier > 1 || def.upgradeTo == null || def.upgradeTo.Length == 0)
                    continue;
                var b = new Branch { Root = def };
                foreach (var id in def.upgradeTo)
                    if (data.TryGetTower(id, out var next))
                        b.Nodes.Add(new Node { From = def, To = next });
                if (b.Nodes.Count > 0)
                    branches.Add(b);
            }

            float branchW = 0f;
            foreach (var b in branches)
                branchW = Mathf.Max(branchW, b.Nodes.Count * NodeW + (b.Nodes.Count - 1) * Gap);
            float width = Mathf.Max(760f, branches.Count * branchW + (branches.Count + 1) * 40f);
            float height = 110f + RootH + 60f + NodeH + 40f;

            var rt = BasicUi.Rect("TechTree", canvas, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -110f), new Vector2(width, height));
            BasicUi.OwnCanvas(rt);
            BasicUi.Panel(rt, PanelColor);
            root = rt.gameObject;

            var titleRt = BasicUi.Rect("Title", rt, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(28f, -14f), new Vector2(500f, 48f));
            BasicUi.Label(titleRt, "타워 업그레이드 · 테크트리", 32, TextAnchor.MiddleLeft, Accent);
            var guideRt = BasicUi.Rect("Guide", rt, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(28f, -60f), new Vector2(width - 300f, 34f));
            guide = BasicUi.Label(guideRt, "", 22, TextAnchor.MiddleLeft, Color.white);

            BasicUi.Button(rt, "Close", "닫기", new Vector2(1f, 1f), new Vector2(-70f, -38f), new Vector2(110f, 46f), onClose, out _);
            sellButton = BasicUi.Button(rt, "Sell", "판매", new Vector2(1f, 1f), new Vector2(-210f, -38f), new Vector2(150f, 46f), onSell, out sellLabel);

            float x = 40f + branchW * 0.5f;
            foreach (var b in branches)
            {
                BuildBranch(rt, b, x, icon, onChoose);
                x += branchW + 40f;
            }
            root.SetActive(false);
        }

        void BuildBranch(RectTransform panel, Branch b, float centerX, Func<TowerDef, Texture> icon, Action<TowerDef> onChoose)
        {
            float top = -110f;
            // 1단계 타워 (갈래의 뿌리)
            var rootRt = BasicUi.Rect("Root_" + b.Root.id, panel, new Vector2(0f, 1f), new Vector2(0.5f, 1f), new Vector2(centerX, top), new Vector2(RootW, RootH));
            b.RootBg = BasicUi.Panel(rootRt, NodeColor);
            Icon(rootRt, icon(b.Root), 84f, -8f);
            var nameRt = BasicUi.Rect("Name", rootRt, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 26f), new Vector2(RootW - 10f, 30f));
            BasicUi.Label(nameRt, b.Root.displayName, 22, TextAnchor.MiddleCenter, Color.white);
            var tierRt = BasicUi.Rect("Tier", rootRt, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 4f), new Vector2(RootW - 10f, 24f));
            BasicUi.Label(tierRt, "1단계", 18, TextAnchor.MiddleCenter, Muted);

            // 가지: 뿌리 아래로 내려와 가로로 펼친 뒤 각 갈래로 내려간다
            float rowTop = top - RootH - 60f;
            float midY = top - RootH - 30f;
            int n = b.Nodes.Count;
            float span = n * NodeW + (n - 1) * Gap;
            float left = centerX - span * 0.5f + NodeW * 0.5f;
            Line(panel, centerX, top - RootH, centerX, midY);
            if (n > 1)
                Line(panel, left, midY, left + (n - 1) * (NodeW + Gap), midY);
            for (int i = 0; i < n; i++)
            {
                float cx = left + i * (NodeW + Gap);
                Line(panel, cx, midY, cx, rowTop);
                BuildNode(panel, b.Nodes[i], cx, rowTop, icon, onChoose);
            }
        }

        void BuildNode(RectTransform panel, Node node, float cx, float top, Func<TowerDef, Texture> icon, Action<TowerDef> onChoose)
        {
            var rt = BasicUi.Rect("Node_" + node.To.id, panel, new Vector2(0f, 1f), new Vector2(0.5f, 1f), new Vector2(cx, top), new Vector2(NodeW, NodeH));
            node.Background = BasicUi.Panel(rt, NodeColor);
            node.Button = rt.gameObject.AddComponent<Button>();
            node.Button.targetGraphic = node.Background;
            var to = node.To;
            node.Button.onClick.AddListener(() => onChoose(to));

            Icon(rt, icon(to), 96f, -6f);
            var nameRt = BasicUi.Rect("Name", rt, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -104f), new Vector2(NodeW - 10f, 30f));
            BasicUi.Label(nameRt, to.displayName, 22, TextAnchor.MiddleCenter, Color.white);
            var statRt = BasicUi.Rect("Stat", rt, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -136f), new Vector2(NodeW - 14f, 52f));
            var stat = BasicUi.Label(statRt, Summary(to), 17, TextAnchor.UpperCenter, Muted);
            stat.lineSpacing = 1.1f;
            var costRt = BasicUi.Rect("Cost", rt, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 34f), new Vector2(NodeW - 10f, 28f));
            node.Cost = BasicUi.Label(costRt, "", 21, TextAnchor.MiddleCenter, Accent);
            var stateRt = BasicUi.Rect("State", rt, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 8f), new Vector2(NodeW - 10f, 24f));
            node.State = BasicUi.Label(stateRt, "", 17, TextAnchor.MiddleCenter, Muted);

            // 잠김 덮개: 칸 전체를 검게 덮고 가운데 자물쇠. 잠긴 이유(State)는 덮개 위에 보인다
            var lockRt = BasicUi.Rect("Lock", rt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            lockRt.anchorMin = Vector2.zero;
            lockRt.anchorMax = Vector2.one;
            BasicUi.Panel(lockRt, LockShade).raycastTarget = false;
            Padlock(lockRt, new Vector2(0f, 62f), 58f, LockColor); // 아이콘 자리 (이름은 가리지 않게)
            node.Lock = lockRt.gameObject;
            node.State.transform.parent.SetAsLastSibling();
            node.Lock.SetActive(false);
        }

        // 자물쇠 (사각 몸통 + ㄷ자 고리 + 열쇠 구멍). 그림 파일 없이 사각형으로 그린다
        static void Padlock(RectTransform parent, Vector2 center, float s, Color color)
        {
            var root = BasicUi.Rect("Padlock", parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), center, new Vector2(s, s * 1.2f));
            void Box(string name, Vector2 pos, Vector2 size, Color c)
            {
                var r = BasicUi.Rect(name, root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), pos, size);
                BasicUi.Panel(r, c).raycastTarget = false;
            }
            float bar = s * 0.16f;
            Box("ShackleL", new Vector2(-s * 0.3f, s * 0.28f), new Vector2(bar, s * 0.5f), color);
            Box("ShackleR", new Vector2(s * 0.3f, s * 0.28f), new Vector2(bar, s * 0.5f), color);
            Box("ShackleTop", new Vector2(0f, s * 0.5f), new Vector2(s * 0.76f, bar), color);
            Box("Body", new Vector2(0f, -s * 0.18f), new Vector2(s, s * 0.72f), color);
            Box("Keyhole", new Vector2(0f, -s * 0.12f), new Vector2(s * 0.14f, s * 0.26f), new Color(0.08f, 0.08f, 0.1f, 1f));
        }

        static void Icon(RectTransform parent, Texture texture, float size, float y)
        {
            var rt = BasicUi.Rect("Icon", parent, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, y), new Vector2(size, size));
            var image = rt.gameObject.AddComponent<RawImage>();
            image.texture = texture;
            image.enabled = texture != null;
            image.raycastTarget = false;
        }

        static void Line(RectTransform panel, float x0, float y0, float x1, float y1)
        {
            bool vertical = Mathf.Approximately(x0, x1);
            var center = new Vector2((x0 + x1) * 0.5f, (y0 + y1) * 0.5f);
            var size = vertical ? new Vector2(LineW, Mathf.Abs(y1 - y0) + LineW) : new Vector2(Mathf.Abs(x1 - x0) + LineW, LineW);
            var rt = BasicUi.Rect("Line", panel, new Vector2(0f, 1f), new Vector2(0.5f, 0.5f), center, size);
            BasicUi.Panel(rt, LineColor).raycastTarget = false;
        }

        // 갈래 칸 한 줄 요약: 초당 피해와 공격 방식
        static string Summary(TowerDef d)
        {
            float dps = d.damage * Mathf.Max(1, d.burstCount) / Mathf.Max(0.01f, d.interval + d.chargeTime);
            string kind;
            switch (d.attackType)
            {
                case AttackType.Cone: kind = $"부채꼴 {d.coneAngle:0}° {d.coneLength:0.#}칸"; break;
                case AttackType.Line: kind = $"정면 일직선 {d.range}칸"; break;
                case AttackType.Pulse: kind = $"원형 {d.pulseRadius:0.#}칸"; break;
                default: kind = $"사거리 {d.range}칸"; break;
            }
            string extra = d.spinUpTime > 0f ? " · 예열" : TowerInfoView.StatusShortName(d.status);
            return d.spinUpTime > 0f ? $"초당 피해 최대 {dps:0.#}\n{kind}{extra}" : $"초당 피해 {dps:0.#}\n{kind}{extra}";
        }

        // 창 열기·닫기·고른 타워 변경 (BuildController.TechTreeChanged).
        public void Show(TechTreeChange e)
        {
            if (root == null)
                return;
            if (!e.Open)
            {
                root.SetActive(false);
                return;
            }
            tower = e.Tower;
            picking = e.Picking;
            root.SetActive(true);
            Refresh();
        }

        public void SetCash(int value)
        {
            cash = value;
            if (Visible)
                Refresh();
        }

        public void SetFloor(int value)
        {
            floor = value;
            if (Visible)
                Refresh();
        }

        void Refresh()
        {
            var from = tower != null ? tower.Def : null;
            if (picking != null)
                guide.text = $"맵에서 진화할 타워를 클릭하세요 → {picking.displayName}   (ESC 취소)";
            else if (from != null)
                guide.text = $"고른 타워: {from.displayName}   진화할 갈래를 누르세요";
            else
                guide.text = "갈래를 누른 뒤 맵에서 진화할 타워를 클릭하세요. 설치된 타워를 눌러도 이 창이 열립니다.";
            sellButton.gameObject.SetActive(from != null && data.Config != null);
            if (from != null && data.Config != null)
                sellLabel.text = $"판매 (+{Mathf.RoundToInt(from.buildCost * data.Config.sellRefundRate)})";

            foreach (var b in branches)
            {
                bool mine = from == null || from == b.Root;
                b.RootBg.color = from == b.Root ? NodeHot : (mine ? NodeColor : NodeDim);
                foreach (var node in b.Nodes)
                {
                    int cost = TowerManager.UpgradeCost(data.Config, node.From, node.To);
                    if (CostMod != null)
                        cost = CostMod(cost);
                    bool lab = LabUnlocked == null || LabUnlocked(node.To);
                    bool unlocked = floor >= node.To.unlockFloor && lab;
                    bool affordable = cash >= cost;
                    node.Cost.text = cost == 0 ? "진화 무료" : $"진화 {cost} 캐시";
                    node.Cost.color = affordable ? Accent : Bad;
                    if (!lab)
                    {
                        node.State.text = "공학연구소에서 해금";
                        node.State.color = Bad;
                    }
                    else if (!unlocked)
                    {
                        node.State.text = $"{node.To.unlockFloor}층부터";
                        node.State.color = Bad;
                    }
                    else if (!affordable)
                    {
                        node.State.text = "캐시 부족";
                        node.State.color = Bad;
                    }
                    else
                    {
                        node.State.text = from == b.Root ? "누르면 진화" : (from == null ? "누른 뒤 타워 선택" : "");
                        node.State.color = Good;
                    }
                    node.Button.interactable = mine && unlocked;
                    if (node.Lock.activeSelf == unlocked)
                        node.Lock.SetActive(!unlocked); // 잠김(공학연구소 미해금·해금 층 전): 검게 + 자물쇠
                    node.Background.color = picking == node.To || (from == b.Root && unlocked) ? NodeHot : (mine ? NodeColor : NodeDim);
                }
            }
        }
    }
}
