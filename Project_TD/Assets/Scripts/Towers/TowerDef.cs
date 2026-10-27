using UnityEngine;

namespace TowerDefence
{
    // 타워 정의 데이터 (GDD 9장 TowerDefs, DamageDesign 7절). 런타임에 수정하지 않는다.
    [CreateAssetMenu(menuName = "TowerDefence/TowerDef", fileName = "TowerDef")]
    public class TowerDef : ScriptableObject
    {
        public string id = "basic";
        public string displayName = "기본 타워";
        [TextArea(2, 5)]
        public string description = "";   // 타워 정보 패널 설명 (비어 있으면 "설명 준비 중")
        public int buildCost = 50;
        public float damage = 20f;   // 1발(화염방사는 1틱) 피해, 소수 가능
        public float interval = 1f; // 공격 간격(초)
        public string bonusTrait = "";  // 이 속성(MonsterDef.traits)의 몬스터에게 bonusMultiplier배 피해 (EMP: "mechanical")
        public float bonusMultiplier = 1f;
        public int burstCount = 1;     // 한 번 공격에 쏘는 발 수 (총 타워: 2 = 이점사, 발마다 damage)
        public float burstGap = 0.12f; // 연발 사이 간격(초)
        public float chargeTime = 0f;  // 쏘기 전 차지 시간(초). 0이면 조준이 맞는 즉시 쏜다 (번개 타워: 차지 클립 길이)

        // 고유 스탯
        public int range = 2;                               // 정사각형 반경(칸): 2 = 5×5칸, max(|dx|,|dy|) ≤ range
        public TargetLock lockMode = TargetLock.Lock;       // 대상 유지
        public TargetRule targetRule = TargetRule.First;    // 사거리 우선도
        public string[] typePriority = new string[0];       // 종류 우선도: 몬스터 id 또는 분류, 예: ["Special"]
        public AttackType attackType = AttackType.Projectile;
        public float projectileSpeed = 8f;                  // 칸/초

        [Header("테크트리 (GameConfig.upgradeEnabled일 때)")]
        public string[] upgradeTo = new string[0];          // 진화 대상 타워 id ([0]만 사용). buildCost는 누적 비용, 진화 비용 = 차액
        public int tier = 1;                                // 진화 단계. 타워 목록(건축)에는 1단계만 나온다
        public int unlockFloor = 1;                         // 이 타워로 진화할 수 있는 첫 층

        [Header("나노필드 (첫 발이 명중한 자리에 감속 영역, 피해는 맞은 몬스터만)")]
        public float fieldRadius = 0f;      // 반지름(칸). 0이면 필드 없음
        public float fieldDuration = 3f;    // 유지 시간(초)
        [Range(0f, 1f)] public float fieldSlow = 0.6f; // 이동 속도 감소율 (0.6 = 60% 느려짐 → 원래 속도의 40%)
        public GameObject fieldFxPrefab;    // 필드 표시 (원점 = 바닥 중앙)
        public float fieldFxRadius = 1.6f;  // 표시 모델의 반지름(모델 단위). fieldRadius에 맞춰 크기를 정한다

        [Header("전자파 (attackType = Pulse)")]
        public float pulseRadius = 2f;   // 파동이 퍼지는 최대 반지름(칸). 대상 탐색 범위도 이 원
        public float pulseSpeed = 6f;    // 퍼지는 속도(칸/초). 파면이 몬스터에 닿는 순간 피해

        [Header("부채꼴 범위 공격 (attackType = Cone)")]
        public float coneAngle = 90f;    // 부채꼴 전체 각도(도)
        public float coneLength = 1.6f;  // 타워 중심에서 불길 끝까지 (칸). 대상 탐색 범위도 이 길이(원형)

        [Header("표시 (아트)")]
        public GameObject viewPrefab;          // 타워 모델 (TowerAim이 붙은 프리팹). 없으면 표시 없음
        public float viewScale = 0.3f;         // 모델 크기 → 1칸에 맞추는 배율
        public GameObject projectilePrefab;    // 투사체 모델 (Projectile 공격일 때)
        public float projectileScale = 0.6f;
        public Vector3 projectileModelForward = Vector3.up; // 투사체 모델에서 탄두가 향한 로컬 축 (트윈캐논 총알: +Y)
        public float projectileTurnSpeed = 720f;            // 유도 회전 속도(도/초). 총구 방향으로 나가 대상 쪽으로 휜다
        public GameObject muzzleFxPrefab;                   // 발사 이펙트 (섬광·화염·불티·연기). 원점 = 총구 끝, +Z = 포신 방향
        public float coneFxReach = 2.28f;                   // Cone: 이펙트 모델의 불길 길이(모델 단위). coneLength에 맞춰 이펙트 크기를 정한다
        public AnimationClip buildClip;                     // 건축 연출 (모델@Build의 Build 클립). 철거는 거꾸로 재생. 없으면 바로 완성
        public AnimationClip chargeClip;                    // 차지 연출 (모델@Charge의 Charge 클립). 차지 동안 재생 (TowerAim.animatedParts만 움직임)
        public AnimationClip fireClip;                      // 발사 연출 (모델@Fire의 Fire 클립). 공격(연발 첫 발)마다 1번 재생 (animatedParts만 움직임)
        public GameObject chargeFxPrefab;                   // 차지 동안 발사점(첫 firePoint)에서 재생하는 이펙트
        public GameObject hitFxPrefab;                      // 투사체가 명중한 자리에서 재생하는 이펙트

        // GameData가 로드 때 매기는 연속 번호. 에셋에 저장되지 않는 실행 중 값이라 정의 데이터를 바꾸지 않는다.
        [System.NonSerialized] int index = -1;
        public int Index => index;
        internal void AssignIndex(int value) => index = value;
    }
}
