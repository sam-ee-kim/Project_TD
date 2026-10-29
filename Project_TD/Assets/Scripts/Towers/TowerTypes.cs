using UnityEngine;

namespace TowerDefence
{
    // 타워 고유 스탯: 사거리 우선도 (DamageDesign 3-0).
    public enum TargetRule
    {
        First,     // 경로를 가장 많이 진행한 몬스터
        Last,      // 경로를 가장 적게 진행한 몬스터
        LowestHp,
        HighestHp,
    }

    // 타워 고유 스탯: 대상 유지 방식 (DamageDesign 3-0).
    public enum TargetLock
    {
        Lock,       // 기본: 죽거나, 출구에 들어가거나, 사거리 밖으로 나갈 때까지 고정
        PreferType, // 고정하되 더 높은 종류 우선도가 오면 교체
        Retarget,   // 매 공격마다 새로 선택
    }

    public enum AttackType
    {
        Projectile,
        Hitscan, // 즉시 명중 (대상 1마리)
        Cone,    // 부채꼴 범위: 조준 방향 부채꼴(히트박스) 안의 몬스터 모두에게 즉시 피해 (화염방사)
        Pulse,   // 전자파: 타워 중심에서 원형으로 퍼지며, 파면이 지나가는 몬스터마다 1번씩 피해 (EMP)
        Line,    // 일직선: 헤드가 돌지 않고 바라보는 방향으로 range칸. 그 칸들 위의 몬스터 모두에게 즉시 피해 (플라즈마)
    }

    // 타워 방향 (설치 때 R키로 90°씩 돌린다). 공격은 바라보는 쪽 앞 칸만 (바로 옆 줄 제외, 180°).
    // 0 = 칸 +y (월드 +Z), 1 = +x (+X), 2 = −y (−Z), 3 = −x (−X). 시계 방향 순서.
    public static class TowerFacing
    {
        public const int Count = 4;

        public static Vector2Int Dir(int facing)
        {
            switch (facing & 3)
            {
                case 1: return new Vector2Int(1, 0);
                case 2: return new Vector2Int(0, -1);
                case 3: return new Vector2Int(-1, 0);
                default: return new Vector2Int(0, 1);
            }
        }

        public static Vector3 World(int facing)
        {
            var d = Dir(facing);
            return new Vector3(d.x, 0f, d.y);
        }

        public static int Next(int facing) => (facing + 1) & 3;

        // 월드 Y축 회전각 (+Z = 0°, +X = 90°)
        public static float Yaw(int facing) => (facing & 3) * 90f;

        // 모델이 기본 자세에서 바라보는 수평 방향의 Y축 각도 (총구 − 회전판). 조준 부품이 없으면 0.
        public static float RestYaw(TowerAim aim)
        {
            if (aim == null || aim.muzzle == null || aim.yawPivot == null)
                return 0f;
            var forward = aim.muzzle.position - aim.yawPivot.position;
            return forward.x * forward.x + forward.z * forward.z < 1e-8f ? 0f : Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
        }

        // 모델 루트 회전: 기본 자세의 정면(restYaw)이 facing 쪽을 보도록 Y축으로 돌린다.
        public static Quaternion ViewRotation(int facing, float restYaw, Quaternion baseRotation) =>
            Quaternion.AngleAxis(Yaw(facing) - restYaw, Vector3.up) * baseRotation;

        // 사거리 모양 판정 (설치 칸 기준 dx, dy). 범위 표시와 타겟팅 칸 계산이 같은 규칙을 쓴다.
        //   Line          : 바라보는 방향 일직선 1~range칸
        //   Cone / Pulse  : 원 (칸 중심 거리 ≤ 길이 + 반 칸) 중 앞쪽
        //   그 밖         : 정사각형 (체비쇼프 거리 ≤ range) 중 앞쪽
        // 앞쪽 = 바라보는 방향 성분이 0보다 큰 칸 (타워 바로 옆 줄은 제외).
        public static bool InArea(TowerDef def, int facing, int dx, int dy)
        {
            var dir = Dir(facing);
            int ahead = dx * dir.x + dy * dir.y;
            if (ahead <= 0)
                return false;
            switch (def.attackType)
            {
                case AttackType.Line:
                    return dx * dir.y - dy * dir.x == 0 && ahead <= def.range;
                case AttackType.Cone:
                case AttackType.Pulse:
                    float reach = (def.attackType == AttackType.Pulse ? def.pulseRadius : def.coneLength) + 0.5f;
                    return dx * dx + dy * dy <= reach * reach;
                default:
                    return Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) <= def.range;
            }
        }
    }

    // TowerManager.TowerChanged 인자 (GDD 8-4).
    public readonly struct TowerChange
    {
        public readonly Vector2Int Cell;
        public readonly TowerDef Def;
        public readonly bool Built; // true = 건축, false = 판매·철거·회수
        public readonly int Facing; // 건축일 때 타워 방향 (TowerFacing)

        public TowerChange(Vector2Int cell, TowerDef def, bool built, int facing = 0)
        {
            Cell = cell;
            Def = def;
            Built = built;
            Facing = facing;
        }
    }

    // TowerManager.ItemGranted 인자: 층 클리어 아이템 1개를 받았을 때.
    public readonly struct FloorItemGrant
    {
        public readonly int Floor;        // 클리어한 층
        public readonly bool Damage;      // true = 화력(공격력), false = 연사(공격 속도)
        public readonly float Bonus;      // 이번 아이템 증가량 (0.10 = +10%)
        public readonly float DamageMul;  // 받은 뒤 누적 공격력 배율
        public readonly float SpeedMul;   // 받은 뒤 누적 공격 속도 배율

        public FloorItemGrant(int floor, bool damage, float bonus, float damageMul, float speedMul)
        {
            Floor = floor;
            Damage = damage;
            Bonus = bonus;
            DamageMul = damageMul;
            SpeedMul = speedMul;
        }
    }

    // TowerManager.RecoveryChanged 인자 (GDD 8-4). 타워 회수 목록이 바뀔 때.
    public readonly struct RecoveryChange
    {
        public readonly TowerDef Def;   // 바뀐 종류 (null = 목록 전체 갱신)
        public readonly int Count;      // 그 종류 남은 수
        public readonly int TotalCount; // 목록 전체 남은 수

        public RecoveryChange(TowerDef def, int count, int totalCount)
        {
            Def = def;
            Count = count;
            TotalCount = totalCount;
        }
    }

    // TowerManager.RecoveryExpired 인자 (GDD 8-4). 첫 웨이브 종료 시 남은 타워 강제 반환.
    public readonly struct RecoveryExpire
    {
        public readonly int TowerCount;
        public readonly int Refund; // 건축 비용 × recoveryRefundRate 합계

        public RecoveryExpire(int towerCount, int refund)
        {
            TowerCount = towerCount;
            Refund = refund;
        }
    }
}
