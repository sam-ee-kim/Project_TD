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
        Hitscan, // 나중
    }

    // TowerManager.TowerChanged 인자 (GDD 8-4).
    public readonly struct TowerChange
    {
        public readonly Vector2Int Cell;
        public readonly TowerDef Def;
        public readonly bool Built; // true = 건축, false = 판매·철거·회수

        public TowerChange(Vector2Int cell, TowerDef def, bool built)
        {
            Cell = cell;
            Def = def;
            Built = built;
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
