using UnityEngine;

namespace TowerDefence
{
    // 최적화 검증용 스트레스 테스트 설정 (작업 10, GDD 8-5). StressTest 씬의 GameLoop에만 연결한다.
    // 게임 코드는 그대로 두고, 데이터(전용 GameConfig 복사본의 웨이브 테이블, PreferType 타워 데이터)로만 조건을 만든다.
    [CreateAssetMenu(menuName = "TowerDefence/Stress Test Settings", fileName = "StressTestSettings")]
    public class StressTestSettings : ScriptableObject
    {
        [Header("데이터 (씬의 GameLoop 설정 대신 사용)")]
        public GameConfig config;          // 스트레스 전용 GameConfig (마지막 층 웨이브 테이블, 큰 시작 캐시·넥서스 체력)
        public MonsterDef[] monsterDefs;
        public TowerDef[] towerDefs;       // 풀에 등록할 타워 종류 전체
        public TowerDef[] towerPattern;    // 자동 배치 순서 (반복). 예: basic, basic, basic, basic_prefer

        [Header("진행")]
        [Tooltip("이 층의 마지막 웨이브를 즉시 끝내고 다음 층으로 넘어가며 Tier 전환을 측정한다 (9 → 10층, 13×13 → 15×15).")]
        public int startFloor = 9;
        public int towerCount = 100;
        [Tooltip("측정할 웨이브 수 (다음 층 1번 웨이브부터). 구성은 config.waves 테이블이 정한다.")]
        public int wavesToMeasure = 3;
        [Tooltip("타워 배치 후, 웨이브 사이에 기다리는 시간 (타일 등장 연출·준비 할당이 측정에 섞이지 않게).")]
        public float settleSec = 3f;
        [Tooltip("측정 중 수직동기·프레임 제한을 끈다 (여유 성능까지 보기 위해).")]
        public bool uncapFrameRate = true;
        [Tooltip("보고서를 쓰고 나면 종료 (빌드) / 플레이 모드 종료 (에디터).")]
        public bool quitWhenDone = true;
        [Tooltip("0보다 크면 층 전환 시작부터 이 시간(초) 동안 프로파일러 기록을 보고서 옆에 .raw로 남긴다 (원인 분석용, Profiler 창에서 열기).")]
        public float profileCaptureSec = 0f;

        // GameLoop.Awake: 씬 설정 대신 스트레스 데이터를 쓰고, 테스트용 임시 값은 끈다.
        public void ApplyTo(ref GameConfig gameConfig, ref MonsterDef[] monsters, ref TowerDef[] towers,
            ref int debugStartCash, ref int debugMonsterHp, ref bool useFixedTestMap)
        {
            if (config != null) gameConfig = config;
            if (monsterDefs != null && monsterDefs.Length > 0) monsters = monsterDefs;
            if (towerDefs != null && towerDefs.Length > 0) towers = towerDefs;
            debugStartCash = 0;
            debugMonsterHp = 0;
            useFixedTestMap = false;
        }
    }
}
