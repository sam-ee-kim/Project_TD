using UnityEngine;

namespace TowerDefence
{
    // 타워 상태머신 (TowerManager.TickCombat이 구동).
    public enum TowerState
    {
        Idle,      // 대상 없음
        Aiming,    // 대상을 향해 포탑을 돌리는 중 (조준이 맞으면 발사)
        Reloading, // 발사 후 재장전
    }

    // 설치된 타워 한 개 (TowerDef 참조 + 칸 좌표). 풀에서 재사용, 개별 Update 없음.
    public class TowerInstance
    {
        public TowerDef Def;
        public Vector2Int Cell;
        public float Cooldown;
        public TowerState State;
        public MonsterHandle Locked = MonsterHandle.None; // 고정 대상 (lockMode)

        // 설치 때 1번 계산 (DamageDesign 3-0)
        public int[] Covered;     // 정사각형 사거리 안 경로 칸 번호 (큰 번호 = 출구에 가까운 칸부터)
        public bool[] CoversPath; // CoversPath[경로 칸 번호] = 사거리 안이면 true
        public int[] TypeRank;    // 몬스터 Def.Index → 종류 우선도 순위 (작을수록 먼저, 목록에 없으면 맨 뒤)

        public Transform View;    // 풀과 함께 재사용하는 모델 (없으면 표시 없음)
        public TowerAim Aim;      // 모델 생성 때 1번 찾아 둔다 (Tick 안 GetComponent 금지)
    }
}
