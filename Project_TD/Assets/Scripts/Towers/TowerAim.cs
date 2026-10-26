using UnityEngine;

namespace TowerDefence
{
    // 타워 조준: 좌우는 회전판(XX_Rotation_Plate), 상하는 목 관절(XX_Neck_Joint).
    // FBX 축 변환 결과(로컬 축 방향)에 의존하지 않도록, 시작 자세를 기준으로 월드 축에서 회전시킨다.
    // (TowerList_FBX/TowerAim.cs의 계산 그대로. 최적화 규칙상 LateUpdate 대신 TowerManager.TickCombat이 Aim(dt)을 부른다.)
    // 발사: 총구 끝에서 포신 방향으로 내보내고, 쏜 포신은 뒤로 밀렸다가 돌아온다(반동).
    public class TowerAim : MonoBehaviour
    {
        [Header("Pivots (FBX 안의 오브젝트)")]
        public Transform yawPivot;      // TT_Rotation_Plate / FT_Rotation_Plate
        public Transform pitchPivot;    // TT_Neck_Joint / FT_Neck_Joint
        public Transform muzzle;        // 정면 방향 기준점(가운데에 있는 것): 트윈캐논은 TT_Head_Eye, 화염방사는 FT_Flame_Emitter

        [Header("Fire")]
        public Transform[] firePoints;     // 총구 (트윈캐논: 좌·우를 번갈아)
        public Vector3 barrelAxis = Vector3.up; // 총구 오브젝트에서 포신이 바깥으로 향한 로컬 축 (트윈캐논: +Y)
        public float muzzleTipOffset = 0.13f;   // 총구 피벗에서 끝까지 거리 (총구 로컬 단위)
        public Transform[] recoilPivots;   // firePoints와 같은 순서로 반동할 부품 (트윈캐논: TT_Gun_Recoil_L/R). 없으면 반동 없음
        public float recoilDistance = 0.18f;    // 반동 거리 (부품 부모 로컬 단위)
        public float recoilReturnTime = 0.15f;  // 제자리로 돌아오는 시간
        public float fireAlignAngle = 8f;       // 수평 조준 오차가 이 각도 안이면 발사
        public Transform[] animatedParts;       // 차지 연출로 움직이는 부품 (번개 타워: 코일 링·구슬). 메시 합치기에서 따로 둔다

        [Header("Kick (발마다 포탑이 튀는 반동)")]
        public float kickPitch = 6f;        // 위로 튀는 각도(도)
        public float kickYaw = 2.5f;        // 쏜 포신 쪽으로 비틀리는 각도(도), 좌·우 번갈아
        public float kickReturnTime = 0.14f; // 제자리로 돌아오는 시간

        [Header("Limits / Speed")]
        public float minPitch = -25f; // 화염방사는 -30
        public float maxPitch = 15f;
        public float yawSpeed = 240f;
        public float pitchSpeed = 120f;

        Quaternion yawRestLocal, pitchRestLocal;
        Vector3 restForwardInYawParent;
        float yaw, pitch;
        int nextFirePoint;
        float kick;      // 1 = 막 쏨, 0 = 제자리
        float kickSide;  // +1 / −1 (쏜 포신 쪽)

        Vector3[] recoilRest;      // 반동 부품의 원래 localPosition
        Vector3[] recoilBackLocal; // 포신 반대 방향 (부품 부모 로컬)
        float[] recoilAmount;      // 1 = 최대로 밀림, 0 = 제자리

        void Awake()
        {
            yawRestLocal = yawPivot.localRotation;
            pitchRestLocal = pitchPivot.localRotation;
            // 시작 자세에서 타워가 바라보는 수평 방향
            Vector3 fwd = muzzle.position - yawPivot.position;
            fwd.y = 0f;
            restForwardInYawParent = yawPivot.parent.InverseTransformDirection(fwd.normalized);

            int n = recoilPivots != null ? recoilPivots.Length : 0;
            recoilRest = new Vector3[n];
            recoilBackLocal = new Vector3[n];
            recoilAmount = new float[n];
            for (int i = 0; i < n; i++)
            {
                recoilRest[i] = recoilPivots[i].localPosition;
                var barrelWorld = firePoints[i].TransformDirection(barrelAxis);
                recoilBackLocal[i] = -recoilPivots[i].parent.InverseTransformDirection(barrelWorld).normalized;
            }
        }

        // 풀에서 다시 꺼낼 때 시작 자세로.
        public void ResetPose()
        {
            yaw = 0f;
            pitch = 0f;
            nextFirePoint = 0;
            kick = 0f;
            yawPivot.localRotation = yawRestLocal;
            pitchPivot.localRotation = pitchRestLocal;
            for (int i = 0; i < recoilAmount.Length; i++)
            {
                recoilAmount[i] = 0f;
                recoilPivots[i].localPosition = recoilRest[i];
            }
        }

        // 포탑이 대상 쪽을 보고 있는지 (수평 방향 오차 ≤ fireAlignAngle). 아직 돌아가는 중이면 쏘지 않는다.
        public bool IsAimedAt(Vector3 aim)
        {
            Vector3 barrel = firePoints != null && firePoints.Length > 0
                ? firePoints[0].TransformDirection(barrelAxis)
                : muzzle.position - yawPivot.position;
            Vector3 toTarget = aim - yawPivot.position;
            barrel.y = 0f;
            toTarget.y = 0f;
            if (toTarget.sqrMagnitude < 0.0001f)
                return true;
            return Vector3.Angle(barrel, toTarget) <= fireAlignAngle;
        }

        // 발사: 다음 총구의 끝 위치와 포신 방향을 돌려주고, 그 포신에 반동을 준다 (총구를 번갈아 쓴다).
        public void Fire(out Vector3 position, out Vector3 direction)
        {
            if (firePoints == null || firePoints.Length == 0)
            {
                position = muzzle.position;
                direction = (muzzle.position - yawPivot.position).normalized;
                return;
            }

            int i = nextFirePoint;
            nextFirePoint = (nextFirePoint + 1) % firePoints.Length;
            var fp = firePoints[i];
            direction = fp.TransformDirection(barrelAxis).normalized;
            position = fp.position + fp.TransformVector(barrelAxis * muzzleTipOffset);
            if (i < recoilAmount.Length)
                recoilAmount[i] = 1f;
            kick = 1f;
            kickSide = i % 2 == 0 ? 1f : -1f;
            ApplyPose();
        }

        // 반동 복귀만 진행 (대상이 없어도 매 프레임).
        public void TickRecoil(float dt)
        {
            if (kick > 0f)
            {
                kick = Mathf.Max(0f, kick - dt / kickReturnTime);
                ApplyPose();
            }
            for (int i = 0; i < recoilAmount.Length; i++)
            {
                if (recoilAmount[i] <= 0f)
                    continue;
                recoilAmount[i] = Mathf.Max(0f, recoilAmount[i] - dt / recoilReturnTime);
                float k = recoilAmount[i] * recoilAmount[i]; // 처음엔 빠르게, 끝에서 부드럽게
                recoilPivots[i].localPosition = recoilRest[i] + recoilBackLocal[i] * (recoilDistance * k);
            }
        }

        // aim = 몬스터 몸통 중앙. 회전 속도 제한을 두고 dt만큼 따라간다.
        public void Aim(Vector3 aim, float dt)
        {
            Quaternion parentRot = yawPivot.parent.rotation;
            Vector3 restFwd = parentRot * restForwardInYawParent;

            // 1) 좌우: 수평 방향만 사용
            Vector3 flat = aim - yawPivot.position;
            flat.y = 0f;
            if (flat.sqrMagnitude > 0.0001f)
            {
                float wantYaw = Vector3.SignedAngle(restFwd, flat, Vector3.up);
                yaw = Mathf.MoveTowardsAngle(yaw, wantYaw, yawSpeed * dt);
            }
            yawPivot.rotation = Quaternion.AngleAxis(yaw, Vector3.up) * (parentRot * yawRestLocal); // 목 관절 위치를 새 yaw로 (반동은 ApplyPose)

            // 2) 상하: 관절 높이 기준 (높이 차 / 수평 거리), 위가 +
            Vector3 d = aim - pitchPivot.position;
            float horiz = new Vector2(d.x, d.z).magnitude;
            float wantPitch = Mathf.Clamp(Mathf.Atan2(d.y, horiz) * Mathf.Rad2Deg, minPitch, maxPitch);
            pitch = Mathf.MoveTowards(pitch, wantPitch, pitchSpeed * dt);
            ApplyPose();
        }

        // 조준 각도 + 발사 반동(위로 튐·좌우 비틀림)을 회전판·목 관절에 적용.
        void ApplyPose()
        {
            Quaternion parentRot = yawPivot.parent.rotation;
            Vector3 restFwd = parentRot * restForwardInYawParent;
            float k = kick * kick; // 쏜 순간 크게, 끝에서 부드럽게
            float y = yaw + kickSide * kickYaw * k;
            yawPivot.rotation = Quaternion.AngleAxis(y, Vector3.up) * (parentRot * yawRestLocal);

            Vector3 facing = Quaternion.AngleAxis(y, Vector3.up) * restFwd;
            Vector3 right = Vector3.Cross(Vector3.up, facing).normalized;
            pitchPivot.rotation = Quaternion.AngleAxis(-(pitch + kickPitch * k), right) * (yawPivot.rotation * pitchRestLocal);
        }
    }
}
