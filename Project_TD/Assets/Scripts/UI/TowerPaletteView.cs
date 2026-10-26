using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace TowerDefence
{
    // BuildMode 하단 타워 목록 (하단 바가 내려간 자리로 올라온다).
    // 칸마다 타워 모델 아이콘, 그 아래 이름과 건축 비용. 캐시가 모자라면 비용이 빨갛게 바뀐다.
    public class TowerPaletteView
    {
        const float SlotWidth = 180f;
        const float SlotHeight = 220f;
        const float SlotGap = 20f;
        const int IconSize = 256;

        static readonly Color PanelColor = new Color(0.07f, 0.08f, 0.10f, 0.88f);
        static readonly Color SlotColor = new Color(0.16f, 0.18f, 0.22f, 0.95f);
        static readonly Color SlotSelectedColor = new Color(0.30f, 0.45f, 0.75f, 0.95f);
        static readonly Color CostColor = new Color(1f, 0.85f, 0.45f);
        static readonly Color CostShortColor = new Color(1f, 0.45f, 0.45f);

        sealed class Slot
        {
            public TowerDef Def;
            public Image Background;
            public Text Cost;
            public bool Affordable = true;
        }

        readonly List<Slot> slots = new List<Slot>();
        readonly Dictionary<TowerDef, Texture> icons = new Dictionary<TowerDef, Texture>();

        public RectTransform Root { get; private set; }
        public int SlotCount => slots.Count;

        public void Init(Transform canvas, IReadOnlyList<TowerDef> towers, Action<TowerDef> onClick, Action<TowerDef> onBeginDrag)
        {
            int count = towers.Count;
            float width = Mathf.Max(1, count) * (SlotWidth + SlotGap) + SlotGap;
            Root = BasicUi.Rect("TowerPalette", canvas, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 16f), new Vector2(width, SlotHeight + 2f * SlotGap));
            BasicUi.OwnCanvas(Root);
            BasicUi.Panel(Root, PanelColor);

            for (int i = 0; i < count; i++)
            {
                var def = towers[i];
                float x = (i - (count - 1) * 0.5f) * (SlotWidth + SlotGap);
                var rt = BasicUi.Rect("Slot_" + def.id, Root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(x, 0f), new Vector2(SlotWidth, SlotHeight));
                var background = BasicUi.Panel(rt, SlotColor); // 클릭·드래그를 받는 영역

                var iconRt = BasicUi.Rect("Icon", rt, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -8f), new Vector2(150f, 150f));
                var icon = iconRt.gameObject.AddComponent<RawImage>();
                icon.raycastTarget = false;
                icon.texture = GetIcon(def);
                icon.enabled = icon.texture != null;

                var nameRt = BasicUi.Rect("Name", rt, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 30f), new Vector2(SlotWidth - 8f, 32f));
                BasicUi.Label(nameRt, def.displayName, 22, TextAnchor.MiddleCenter, Color.white);
                var costRt = BasicUi.Rect("Cost", rt, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 4f), new Vector2(SlotWidth - 8f, 28f));
                var cost = BasicUi.Label(costRt, $"{def.buildCost} 캐시", 20, TextAnchor.MiddleCenter, CostColor);

                var slot = rt.gameObject.AddComponent<TowerSlot>();
                slot.Def = def;
                slot.Clicked = onClick;
                slot.DragStarted = onBeginDrag;
                slots.Add(new Slot { Def = def, Background = background, Cost = cost });
            }
        }

        // 아이콘 (종류별 1번 찍어 재사용, 정보 패널도 같은 것을 쓴다).
        public Texture GetIcon(TowerDef def)
        {
            if (def == null)
                return null;
            if (!icons.TryGetValue(def, out var texture))
            {
                texture = TowerIconRenderer.Render(def, IconSize);
                icons.Add(def, texture);
            }
            return texture;
        }

        // 지금 캐시로 지을 수 있는지에 따라 비용 색 (바뀐 칸만).
        public void RefreshAfford(int cash)
        {
            foreach (var slot in slots)
            {
                bool afford = cash >= slot.Def.buildCost;
                if (afford == slot.Affordable)
                    continue;
                slot.Affordable = afford;
                slot.Cost.color = afford ? CostColor : CostShortColor;
            }
        }

        // 정보를 보고 있는 칸 강조 (null = 없음).
        public void SetSelected(TowerDef def)
        {
            foreach (var slot in slots)
                slot.Background.color = slot.Def == def ? SlotSelectedColor : SlotColor;
        }
    }
}
