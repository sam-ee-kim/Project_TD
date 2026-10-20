using System;
using UnityEngine;
using UnityEngine.UI;

namespace TowerDefence
{
    // 기능 확인용 기본 모양 UI를 코드로 만드는 도우미 (정식 UI 디자인은 작업 8).
    // 한글은 OS 글꼴(맑은 고딕 등)로 표시한다.
    public static class BasicUi
    {
        static Font font;

        public static Font Font
        {
            get
            {
                if (font == null)
                    font = Font.CreateDynamicFontFromOSFont(new[] { "Malgun Gothic", "Apple SD Gothic Neo", "Noto Sans CJK KR", "Arial" }, 28);
                return font;
            }
        }

        public static RectTransform Rect(string name, Transform parent, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = parent.gameObject.layer;
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            rt.pivot = pivot;
            rt.anchoredPosition = position;
            rt.sizeDelta = size;
            return rt;
        }

        // 이 패널을 자기 Canvas로 분리한다 (안의 글자·위치가 바뀌어도 이 Canvas만 다시 그린다). 버튼 클릭용 Raycaster 포함.
        public static Canvas OwnCanvas(RectTransform rt)
        {
            var canvas = rt.gameObject.AddComponent<Canvas>();
            rt.gameObject.AddComponent<GraphicRaycaster>();
            return canvas;
        }

        public static Image Panel(RectTransform rt, Color color)
        {
            var image = rt.gameObject.AddComponent<Image>();
            image.color = color;
            return image;
        }

        public static Text Label(Transform parent, string text, int size, TextAnchor alignment, Color color)
        {
            var rt = Rect("Label", parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.sizeDelta = Vector2.zero;
            var label = rt.gameObject.AddComponent<Text>();
            label.font = Font;
            label.fontSize = size;
            label.alignment = alignment;
            label.color = color;
            label.text = text;
            label.raycastTarget = false;
            return label;
        }

        public static Button Button(Transform parent, string name, string text, Vector2 anchor, Vector2 position, Vector2 size, Action onClick, out Text label)
        {
            var rt = Rect(name, parent, anchor, new Vector2(0.5f, 0.5f), position, size);
            var image = Panel(rt, new Color(0.22f, 0.24f, 0.28f, 0.95f));
            var button = rt.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            var colors = button.colors;
            colors.disabledColor = new Color(0.5f, 0.5f, 0.5f, 0.5f);
            button.colors = colors;
            label = Label(rt, text, 26, TextAnchor.MiddleCenter, Color.white);
            if (onClick != null)
                button.onClick.AddListener(() => onClick());
            return button;
        }
    }
}
