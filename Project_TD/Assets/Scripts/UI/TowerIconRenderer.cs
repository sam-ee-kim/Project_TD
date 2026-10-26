using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace TowerDefence
{
    // 타워 목록·정보 패널 아이콘: 타워 모델을 한 번 찍어 RenderTexture로 만든다 (앱 시작 때 종류별 1번).
    // 맵에서 멀리 떨어진 곳에 모델을 잠깐 놓고, 그 레이어만 보는 임시 카메라로 찍은 뒤 바로 지운다.
    // 조명은 씬의 방향광을 그대로 받는다. 에디터 모드(단위 테스트)에서는 만들지 않는다 (아이콘 없이 이름만 표시).
    public static class TowerIconRenderer
    {
        const int IconLayer = 31;                                      // 아이콘 촬영 전용 (다른 오브젝트가 쓰지 않는 레이어)
        static readonly Vector3 Stage = new Vector3(0f, -500f, 0f);    // 게임 카메라에 보이지 않는 곳
        static readonly Vector3 ViewDirection = new Vector3(-0.55f, 0.6f, -0.75f); // 왼쪽 위 앞에서 내려다봄

        public static Texture Render(TowerDef def, int size)
        {
            if (!Application.isPlaying || def == null || def.viewPrefab == null)
                return null;

            var model = Object.Instantiate(def.viewPrefab); // 루트 회전(FBX 축 변환)은 프리팹 그대로 둔다
            model.transform.position = Stage;
            var cameraGo = new GameObject("TowerIconCamera");
            try
            {
                SetLayer(model.transform, IconLayer);
                if (!TryGetBounds(model, out var bounds))
                    return null;

                var camera = cameraGo.AddComponent<Camera>();
                camera.enabled = false;
                camera.cullingMask = 1 << IconLayer;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0f, 0f, 0f, 0f); // 배경 투명
                camera.fieldOfView = 25f;
                float radius = bounds.extents.magnitude;
                float distance = radius / Mathf.Sin(camera.fieldOfView * 0.5f * Mathf.Deg2Rad) * 1.05f;
                camera.nearClipPlane = Mathf.Max(0.01f, distance - radius * 2f);
                camera.farClipPlane = distance + radius * 2f;
                camera.transform.position = bounds.center + ViewDirection.normalized * distance;
                camera.transform.LookAt(bounds.center);

                var urp = camera.GetUniversalAdditionalCameraData();
                urp.renderPostProcessing = false; // 블룸·톤매핑 없이 원래 색
                urp.renderShadows = false;

                var texture = new RenderTexture(size, size, 24, RenderTextureFormat.ARGB32)
                {
                    name = def.id + "_Icon",
                    antiAliasing = 4,
                };
                camera.targetTexture = texture;
                camera.Render();
                camera.targetTexture = null;
                return texture;
            }
            finally
            {
                Object.DestroyImmediate(model);
                Object.DestroyImmediate(cameraGo);
            }
        }

        static bool TryGetBounds(GameObject model, out Bounds bounds)
        {
            bounds = default;
            bool any = false;
            foreach (var r in model.GetComponentsInChildren<Renderer>())
            {
                if (!any) { bounds = r.bounds; any = true; }
                else bounds.Encapsulate(r.bounds);
            }
            return any;
        }

        static void SetLayer(Transform t, int layer)
        {
            t.gameObject.layer = layer;
            foreach (Transform child in t)
                SetLayer(child, layer);
        }
    }
}
