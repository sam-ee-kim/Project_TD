using UnityEngine;

namespace TowerDefence
{
    // 타워 상태머신 (TowerManager.TickCombat이 구동).
    public enum TowerState
    {
        Idle,      // 대상 없음
        Aiming,    // 대상을 향해 포탑을 돌리는 중 (조준이 맞으면 발사)
        Reloading, // 발사 후 재장전
        Charging,  // 쏘기 전 차지 (TowerDef.chargeTime). 끝나면 발사 → Reloading
        Building,  // 건축 연출 중 (끝나야 대상을 찾고 공격한다)
    }

    // 설치된 타워 한 개 (TowerDef 참조 + 칸 좌표). 풀에서 재사용, 개별 Update 없음.
    public class TowerInstance
    {
        public TowerDef Def;
        public Vector2Int Cell;
        public float Cooldown;
        public int BurstLeft;     // 이번 공격에서 아직 안 쏜 연발 수 (이점사: 첫 발 뒤 1)
        public float BurstTimer;  // 다음 연발까지 남은 시간
        public TowerState State;
        public MonsterHandle Locked = MonsterHandle.None; // 고정 대상 (lockMode)

        // 설치 때 1번 계산 (DamageDesign 3-0)
        public int[] Covered;     // 사거리 안 경로 칸의 칸 번호 (모든 스타트 지점 경로, 중복 없음)
        public bool[] CoversCell; // CoversCell[칸 번호] = 사거리 안 경로 칸이면 true
        public int[] TypeRank;    // 몬스터 Def.Index → 종류 우선도 순위 (작을수록 먼저, 목록에 없으면 맨 뒤)

        public Transform View;    // 풀과 함께 재사용하는 모델 (없으면 표시 없음)
        public TowerAim Aim;      // 모델 생성 때 1번 찾아 둔다 (Tick 안 GetComponent 금지)
        public TowerViewMerger.Parts Parts;  // 합친 메시 / 원래 부품 렌더러 전환 (건축 연출 동안은 부품)
        public TowerBuildAnim BuildAnim;     // 건축·철거 연출 (TowerDef.buildClip이 있을 때)
        public float BuildTime;              // 연출 진행 시간 (건축: 0 → 길이, 철거: 길이 → 0)
        public float ChargeTime;             // 차지 진행 시간 (0 → TowerDef.chargeTime)
        public TowerBuildAnim ChargeAnim;    // 차지 연출 (TowerDef.chargeClip이 있을 때)
        public bool ChargePosed;             // 차지 자세를 찍은 상태 (끝나면 기본 자세로 되돌린다)
        public TowerBuildAnim FireAnim;      // 발사 연출 (TowerDef.fireClip이 있을 때)
        public float FireAnimTime = -1f;     // 발사 연출 진행 시간 (−1 = 재생 안 함)
    }
}
