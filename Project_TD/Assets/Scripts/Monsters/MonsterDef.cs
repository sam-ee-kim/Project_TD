using UnityEngine;

namespace TowerDefence
{
    // 몬스터 정의 데이터 (GDD 9장 MonsterDefs, DamageDesign 7절). 런타임에 수정하지 않는다.
    [CreateAssetMenu(menuName = "TowerDefence/MonsterDef", fileName = "MonsterDef")]
    public class MonsterDef : ScriptableObject
    {
        public string id = "normal";
        public string displayName = "일반 몬스터";
        public MonsterCategory category = MonsterCategory.Normal;
        public float hp = 4f;          // 기본 체력 (레벨디자인: Docs/HpLevelDesign.md)
        public float speed = 1f;       // 칸/초
        public int reward = 5;         // 처치 캐시
        public int nexusDamage = 1;
        public string[] traits = new string[0]; // 예: "flying". 종류별 클래스를 따로 만들지 않는다.

        [Header("행동")]
        public float spawnIdleSec = 0f;  // 스폰 직후 제자리 대기(Idle) 시간. 이 동안 움직이지 않는다

        [Header("표시 (아트)")]
        public GameObject viewPrefab;     // Animator(Idle/Walk/Die 상태)가 붙은 모델 프리팹. 없으면 기본 캡슐
        public float viewScale = 0.25f;   // 모델 크기 → 1칸에 맞추는 배율
        public float aimHeight = 0.4f;    // 타워가 조준·명중하는 높이 (몸통 중앙, 바닥 기준)
        [Tooltip("Walk 애니메이션 재생 배속. 이동 속도와 발걸음이 맞지 않으면 이 값으로 직접 맞춘다.")]
        public float walkAnimSpeed = 1f;
        public float animCrossFadeSec = 0.12f; // 상태 전환 시 애니메이션 섞는 시간

        // GameData가 로드 때 매기는 연속 번호 (타워 종류 우선도 순위표 등 배열 인덱스용).
        // 에셋에 저장되지 않는 실행 중 값이라 정의 데이터를 바꾸지 않는다.
        [System.NonSerialized] int index = -1;
        public int Index => index;
        internal void AssignIndex(int value) => index = value;
    }
}
