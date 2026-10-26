using System.Collections.Generic;
using UnityEngine;

namespace TowerDefence
{
    // 드래그 미리보기: 끌고 있는 타워가 마우스 포인터에 잡혀 공중에 떠서 따라다닌다.
    //   - 목록에서 끌어내면 포인터 위치에서 톡 튀어나오고, 설치된 타워를 집으면 제자리에서 들어 올려진다.
    //   - 포인터를 부드럽게 쫓아가며 움직이는 쪽으로 살짝 기울고, 떠 있는 동안 가볍게 흔들린다.
    //   - 놓일 칸(바로 아래 타일)은 오버레이 하이라이트가 알려 주고, 바닥에 드리운 그림자가 위치를 보여 준다.
    // 모델은 종류별로 처음 끌 때 1번 만들어 재사용한다. 설치된 타워를 옮길 때는 원래 모델을 잠시 숨긴다.
    public class TowerDragView
    {
        const float FollowSharpness = 18f;  // 포인터를 쫓아가는 빠르기 (클수록 딱 붙음)
        const float TiltPerSpeed = 3.5f;    // 움직이는 속도(칸/초)당 기울기(도)
        const float MaxTilt = 16f;
        const float TiltSharpness = 10f;
        const float BobHeight = 0.04f;      // 떠 있는 동안 위아래 흔들림
        const float BobSpeed = 5f;
        const float HeldScale = 1.08f;      // 들고 있을 때 살짝 크게
        const float PopSec = 0.16f;         // 목록에서 끌어낼 때 커지며 나타나는 시간
        const float BlobSize = 0.9f;        // 바로 아래 바닥의 동그란 그림자 지름 (칸)
        const float BlobHeight = 0.03f;     // 타일 윗면 바로 위

        sealed class Ghost
        {
            public Transform Root;
            public Quaternion BaseRotation; // 프리팹 루트 회전 (FBX 축 변환)
            public float CenterHeight;      // 루트(바닥)에서 모델 중심까지 높이: 포인터가 타워 몸통을 잡은 것처럼
            public float Scale;
        }

        readonly Dictionary<TowerDef, Ghost> ghosts = new Dictionary<TowerDef, Ghost>();
        Ghost shown;
        Transform blob;        // 떠 있는 타워 바로 아래 바닥의 동그란 그림자 (공중에 들려 있다는 표시)
        Transform hiddenTower; // 이동 중이라 숨긴 원래 모델
        Vector3 position;
        Quaternion tilt = Quaternion.identity;
        float heldTime;
        bool popIn;

        public bool Visible => shown != null;
        public Vector3 Position => position;

        // 드래그 시작·끝 (칸 변경은 오버레이가 표시하므로 여기서는 무시).
        public void Handle(TowerDrag e)
        {
            if (e.Kind == DragKind.None)
            {
                SetShown(null);
                RestoreHiddenTower();
                return;
            }

            var ghost = GetGhost(e.Def);
            if (ghost == null || ghost == shown)
                return;

            SetShown(ghost);
            heldTime = 0f;
            tilt = Quaternion.identity;
            if (e.Kind == DragKind.MoveTower && e.Tower != null && e.Tower.View != null)
            {
                // 설치된 타워를 집음: 그 자리에서 들어 올린다
                position = e.Tower.View.position;
                popIn = false;
                hiddenTower = e.Tower.View;
                hiddenTower.gameObject.SetActive(false);
            }
            else
            {
                popIn = true; // 목록에서 끌어냄: 포인터 자리에서 톡 튀어나온다 (첫 Tick에서 위치를 맞춘다)
                position = new Vector3(float.NaN, 0f, 0f);
            }
            Apply();
        }

        // 매 프레임 (GameUI.Tick). heldPoint = 포인터가 잡고 있는 공중 위치.
        public void Tick(float dt, bool hasPoint, Vector3 heldPoint)
        {
            if (shown == null || dt <= 0f)
                return;
            heldTime += dt;

            if (hasPoint)
            {
                var target = heldPoint - Vector3.up * shown.CenterHeight + Vector3.up * (Mathf.Sin(heldTime * BobSpeed) * BobHeight);
                if (float.IsNaN(position.x))
                    position = target;
                var previous = position;
                position = Vector3.Lerp(position, target, 1f - Mathf.Exp(-FollowSharpness * dt));

                // 움직이는 쪽으로 살짝 기울기 (끌려가는 느낌)
                var velocity = (position - previous) / dt;
                var targetTilt = Quaternion.Euler(
                    Mathf.Clamp(velocity.z * TiltPerSpeed, -MaxTilt, MaxTilt), 0f,
                    Mathf.Clamp(-velocity.x * TiltPerSpeed, -MaxTilt, MaxTilt));
                tilt = Quaternion.Slerp(tilt, targetTilt, 1f - Mathf.Exp(-TiltSharpness * dt));
            }
            Apply();
        }

        void Apply()
        {
            if (shown == null || float.IsNaN(position.x))
            {
                if (shown != null)
                    shown.Root.gameObject.SetActive(false); // 아직 포인터 위치를 모름
                SetBlob(false);
                return;
            }
            if (!shown.Root.gameObject.activeSelf)
                shown.Root.gameObject.SetActive(true);
            SetBlob(true);
            blob.position = new Vector3(position.x, BlobHeight, position.z);

            float k = 1f;
            if (popIn && heldTime < PopSec)
                k = EaseOutBack(heldTime / PopSec);
            shown.Root.SetPositionAndRotation(position, tilt * shown.BaseRotation);
            shown.Root.localScale = Vector3.one * (shown.Scale * HeldScale * k);
        }

        static float EaseOutBack(float t)
        {
            const float c1 = 1.70158f, c3 = c1 + 1f;
            float u = t - 1f;
            return 1f + c3 * u * u * u + c1 * u * u;
        }

        void SetShown(Ghost ghost)
        {
            if (shown == ghost)
                return;
            if (shown != null)
                shown.Root.gameObject.SetActive(false);
            shown = ghost;
            if (shown == null)
                SetBlob(false);
        }

        void SetBlob(bool visible)
        {
            if (visible && blob == null)
                blob = CreateBlob();
            if (blob != null && blob.gameObject.activeSelf != visible)
                blob.gameObject.SetActive(visible);
        }

        // 가장자리가 흐린 검은 원 스프라이트를 바닥에 눕힌다 (처음 1번).
        static Transform CreateBlob()
        {
            const int size = 64;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "DragBlob", wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f) / size * 2f - 1f, dy = (y + 0.5f) / size * 2f - 1f;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Clamp01(1f - d);
                    pixels[y * size + x] = new Color32(0, 0, 0, (byte)(Mathf.SmoothStep(0f, 1f, a) * 140f));
                }
            texture.SetPixels32(pixels);
            texture.Apply();
            var go = new GameObject("DragBlobShadow");
            var sprite = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size / BlobSize);
            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.sortingOrder = 1; // 설치 가능 칸 오버레이(0) 위에
            go.transform.rotation = Quaternion.Euler(90f, 0f, 0f); // 바닥에 눕힘
            go.SetActive(false);
            return go.transform;
        }

        void RestoreHiddenTower()
        {
            // 드래그는 판매·회수(층 전환)보다 먼저 끝나므로, 원래 모델은 항상 설치된 상태로 돌아온다.
            if (hiddenTower == null)
                return;
            hiddenTower.gameObject.SetActive(true);
            hiddenTower = null;
        }

        Ghost GetGhost(TowerDef def)
        {
            if (def == null || def.viewPrefab == null || !Application.isPlaying) // 에디터 모드(테스트)에서는 만들지 않는다
                return null;
            if (ghosts.TryGetValue(def, out var ghost))
                return ghost;
            var go = Object.Instantiate(def.viewPrefab); // 루트 회전(FBX 축 변환)은 프리팹 그대로
            go.name = def.id + "_DragPreview";
            go.transform.localScale = Vector3.one * def.viewScale;

            // 바닥(루트)에서 모델 중심까지 높이 (그림자는 켜 둔다: 바닥에 드리워 놓일 곳을 보여 준다)
            float centerHeight = 0f;
            bool any = false;
            Bounds bounds = default;
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                if (!any) { bounds = r.bounds; any = true; }
                else bounds.Encapsulate(r.bounds);
            }
            if (any)
                centerHeight = bounds.center.y - go.transform.position.y;

            go.SetActive(false);
            ghost = new Ghost
            {
                Root = go.transform,
                BaseRotation = go.transform.rotation,
                CenterHeight = centerHeight,
                Scale = def.viewScale,
            };
            ghosts.Add(def, ghost);
            return ghost;
        }
    }
}
