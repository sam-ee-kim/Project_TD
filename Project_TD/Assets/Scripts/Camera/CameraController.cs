using System;
using UnityEngine;
using UnityEngine.InputSystem;

public enum CameraViewMode
{
    TopDown, // 탑다운뷰: 정면에서 비스듬히 내려다봄
    Top,     // 탑뷰: 수직으로 내려다봄
}

// Tab: 시점 전환 (탑다운 ↔ 탑)
// WASD: 월드 축 기준 이동 (W=+Z, S=-Z, A=-X, D=+X). 카메라가 어느 쪽을 보든 맵의 위/아래/좌/우로 움직인다.
// 맵 생성 연출 중에는 탑다운뷰로 고정되고 입력을 받지 않는다.
public class CameraController : MonoBehaviour
{
    [Serializable]
    public struct ViewSettings
    {
        [Range(10f, 90f)] public float pitch; // 내려다보는 각도
        public float yaw;                      // 수평 회전
        public float distanceFactor;           // 맵 크기 대비 거리
    }

    [SerializeField] MapGenerator map;
    [SerializeField] CameraViewMode mode = CameraViewMode.TopDown;

    [SerializeField] ViewSettings topDownView = new ViewSettings { pitch = 55f, yaw = 0f, distanceFactor = 1.2f };
    [SerializeField] ViewSettings topView = new ViewSettings { pitch = 90f, yaw = 0f, distanceFactor = 1.0f };

    [Header("이동")]
    [SerializeField] float moveSpeed = 10f;
    [SerializeField] float transitionSpeed = 8f; // 시점 전환 보간 속도
    [SerializeField] bool clampToMap = true;

    Vector3 focus;  // 카메라가 바라보는 바닥 위 지점
    float pitch, yaw, distance;

    public CameraViewMode Mode => mode;

    void Start()
    {
        if (map != null)
            focus = map.transform.position;

        // 첫 프레임은 보간 없이 바로 목표 시점에 둔다.
        var target = Current;
        pitch = target.pitch;
        yaw = target.yaw;
        distance = TargetDistance(target);
        Apply();
    }

    // 최적화 규칙(GDD 8-5 #1): Update는 GameLoop 하나만. GameUI.MapView가 매 프레임 Tick(dt)을 부른다.
    public void Tick(float dt)
    {
        var keyboard = Keyboard.current;
        if (map != null && map.IsBuilding)
        {
            // 맵이 솟아오르는 동안은 맵 중앙을 탑다운뷰로 고정, 입력 무시.
            mode = CameraViewMode.TopDown;
            focus = map.transform.position;
        }
        else if (keyboard != null)
        {
            if (keyboard.tabKey.wasPressedThisFrame)
            {
                mode = (CameraViewMode)(((int)mode + 1) % Enum.GetValues(typeof(CameraViewMode)).Length);
                Debug.Log($"[CameraController] 시점: {mode}");
            }

            var move = Vector3.zero;
            if (keyboard.wKey.isPressed) move += Vector3.forward;
            if (keyboard.sKey.isPressed) move += Vector3.back;
            if (keyboard.aKey.isPressed) move += Vector3.left;
            if (keyboard.dKey.isPressed) move += Vector3.right;
            if (move != Vector3.zero)
                focus += move.normalized * (moveSpeed * dt);
        }

        if (clampToMap && map != null)
        {
            var center = map.transform.position;
            var half = map.WorldExtent * 0.5f;
            focus.x = Mathf.Clamp(focus.x, center.x - half.x, center.x + half.x);
            focus.z = Mathf.Clamp(focus.z, center.z - half.y, center.z + half.y);
        }

        var target = Current;
        float t = 1f - Mathf.Exp(-transitionSpeed * dt);
        pitch = Mathf.Lerp(pitch, target.pitch, t);
        yaw = Mathf.LerpAngle(yaw, target.yaw, t);
        distance = Mathf.Lerp(distance, TargetDistance(target), t);
        Apply();
    }

    ViewSettings Current
    {
        get
        {
            return mode == CameraViewMode.Top ? topView : topDownView;
        }
    }

    float TargetDistance(ViewSettings view)
    {
        // 맵이 아직 없으면(첫 맵 생성 전) 7×7 맵 기준 거리로 둔다.
        float mapSize = map != null ? Mathf.Max(map.WorldExtent.x, map.WorldExtent.y) : 0f;
        if (mapSize <= 0f)
            mapSize = 7f;
        return mapSize * view.distanceFactor;
    }

    void Apply()
    {
        var rotation = Quaternion.Euler(pitch, yaw, 0f);
        transform.SetPositionAndRotation(focus - rotation * Vector3.forward * distance, rotation);
    }
}
