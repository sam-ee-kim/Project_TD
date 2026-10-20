using System.Collections.Generic;
using UnityEngine;

namespace TowerDefence
{
    // 패널(또는 위젯)이 BuildMode 진입 때 밀려날 방향 (GDD 5-2). None = 화면에 남아 지정 위치로 모인다.
    public enum ExitDirection
    {
        None,
        Up,
        Down,
        Left,
        Right,
    }

    // BuildMode 패널 슬라이드 연출 (GDD 5-2). GameUI.Tick에서만 움직인다.
    // - 퇴장: 자기가 붙은 쪽으로 화면 밖까지, 모든 패널 동시, slideSec, Ease-In
    // - 복귀: 같은 경로를 거꾸로, slideSec, Ease-Out
    // - 중간 취소: 애니메이션 도중 모드가 바뀌면 현재 위치에서 바로 반대로 이어서 움직인다
    // - 안내 문구 등 페이드 요소: 퇴장과 동시에 페이드 인, 복귀와 동시에 페이드 아웃
    public class PanelSlider
    {
        sealed class Entry
        {
            public RectTransform Panel;
            public Vector2 Home;   // 평소 위치
            public Vector2 Away;   // BuildMode 위치 (화면 밖, 또는 None이면 모이는 자리)
            public Vector2 From;
            public Vector2 To;
        }

        sealed class Fade
        {
            public CanvasGroup Group;
            public float From;
            public float To;
        }

        readonly List<Entry> entries = new List<Entry>();
        readonly List<Fade> fades = new List<Fade>();
        float slideSec;
        float elapsed;
        bool easeIn;    // 퇴장 = Ease-In, 복귀 = Ease-Out
        bool animating;

        public bool IsAnimating => animating;
        public bool IsOut { get; private set; }

        public void Init(float slideSec)
        {
            this.slideSec = Mathf.Max(0.01f, slideSec);
        }

        // 패널 등록. 패널마다 exitDirection만 정하면 같은 연출을 따른다.
        // direction = None이면 noneTarget(anchoredPosition)으로 옮겨 화면에 남긴다.
        public void Register(RectTransform panel, ExitDirection direction, Vector2 noneTarget = default)
        {
            var home = panel.anchoredPosition;
            entries.Add(new Entry
            {
                Panel = panel,
                Home = home,
                Away = direction == ExitDirection.None ? noneTarget : home + Offscreen(panel, direction),
                From = home,
                To = home,
            });
        }

        // 퇴장과 함께 나타나는 요소 (안내 문구).
        public void RegisterFadeIn(CanvasGroup group)
        {
            group.alpha = 0f;
            fades.Add(new Fade { Group = group, From = 0f, To = 0f });
        }

        public void SlideOut() => Begin(true);
        public void SlideIn() => Begin(false);

        // 연출 없이 바로 제자리로 (새 판).
        public void SnapIn()
        {
            foreach (var e in entries)
                e.Panel.anchoredPosition = e.Home;
            foreach (var f in fades)
            {
                f.Group.alpha = 0f;
                f.Group.gameObject.SetActive(false);
            }
            animating = false;
            IsOut = false;
        }

        public void Tick(float dt)
        {
            if (!animating)
                return;
            elapsed += dt;
            float t = Mathf.Clamp01(elapsed / slideSec);
            float k = easeIn ? t * t : 1f - (1f - t) * (1f - t);
            foreach (var e in entries)
                e.Panel.anchoredPosition = Vector2.LerpUnclamped(e.From, e.To, k);
            foreach (var f in fades)
                f.Group.alpha = Mathf.LerpUnclamped(f.From, f.To, k);
            if (t >= 1f)
            {
                animating = false;
                if (!IsOut)
                    foreach (var f in fades)
                        f.Group.gameObject.SetActive(false);
            }
        }

        void Begin(bool goOut)
        {
            if (IsOut == goOut)
                return; // 이미 그쪽으로 가는 중이거나 도착함
            IsOut = goOut;
            easeIn = goOut;
            elapsed = 0f;
            animating = true;
            // 현재 위치에서 출발하므로 도중에 바뀌어도 끊기지 않고 반대로 이어진다.
            foreach (var e in entries)
            {
                e.From = e.Panel.anchoredPosition;
                e.To = goOut ? e.Away : e.Home;
            }
            foreach (var f in fades)
            {
                f.Group.gameObject.SetActive(true);
                f.From = f.Group.alpha;
                f.To = goOut ? 1f : 0f;
            }
        }

        // 화면 밖으로 나갈 거리: 패널 크기 + 지금 가장자리에서 떨어진 거리 + 여유.
        static Vector2 Offscreen(RectTransform panel, ExitDirection direction)
        {
            var size = panel.rect.size;
            var pos = panel.anchoredPosition;
            const float margin = 40f;
            switch (direction)
            {
                case ExitDirection.Up: return new Vector2(0f, size.y + Mathf.Abs(pos.y) + margin);
                case ExitDirection.Down: return new Vector2(0f, -(size.y + Mathf.Abs(pos.y) + margin));
                case ExitDirection.Left: return new Vector2(-(size.x + Mathf.Abs(pos.x) + margin), 0f);
                case ExitDirection.Right: return new Vector2(size.x + Mathf.Abs(pos.x) + margin, 0f);
                default: return Vector2.zero;
            }
        }
    }
}
