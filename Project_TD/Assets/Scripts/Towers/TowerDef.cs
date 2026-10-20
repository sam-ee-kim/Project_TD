using UnityEngine;

namespace TowerDefence
{
    // 타워 정의 데이터 (GDD 9장 TowerDefs, DamageDesign 7절). 런타임에 수정하지 않는다.
    [CreateAssetMenu(menuName = "TowerDefence/TowerDef", fileName = "TowerDef")]
    public class TowerDef : ScriptableObject
    {
        public string id = "basic";
        public string displayName = "기본 타워";
        public int buildCost = 50;
        public int damage = 20;
        public float interval = 1f; // 공격 간격(초)

        // 고유 스탯
        public int range = 2;                               // 정사각형 반경(칸): 2 = 5×5칸, max(|dx|,|dy|) ≤ range
        public TargetLock lockMode = TargetLock.Lock;       // 대상 유지
        public TargetRule targetRule = TargetRule.First;    // 사거리 우선도
        public string[] typePriority = new string[0];       // 종류 우선도: 몬스터 id 또는 분류, 예: ["Special"]
        public AttackType attackType = AttackType.Projectile;
        public float projectileSpeed = 8f;                  // 칸/초

        public string[] upgradeTo = new string[0];          // 형태 업그레이드 대상 (보류: 자리만)

        [Header("표시 (아트)")]
        public GameObject viewPrefab;          // 타워 모델 (TowerAim이 붙은 프리팹). 없으면 표시 없음
        public float viewScale = 0.3f;         // 모델 크기 → 1칸에 맞추는 배율
        public GameObject projectilePrefab;    // 투사체 모델 (Projectile 공격일 때)
        public float projectileScale = 0.6f;
        public Vector3 projectileModelForward = Vector3.up; // 투사체 모델에서 탄두가 향한 로컬 축 (트윈캐논 총알: +Y)
        public float projectileTurnSpeed = 720f;            // 유도 회전 속도(도/초). 총구 방향으로 나가 대상 쪽으로 휜다
        public GameObject muzzleFxPrefab;                   // 발사 이펙트 (섬광·화염·불티·연기). 원점 = 총구 끝, +Z = 포신 방향

        // GameData가 로드 때 매기는 연속 번호. 에셋에 저장되지 않는 실행 중 값이라 정의 데이터를 바꾸지 않는다.
        [System.NonSerialized] int index = -1;
        public int Index => index;
        internal void AssignIndex(int value) => index = value;
    }
}
