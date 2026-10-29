using System;
using UnityEngine;

namespace TowerDefence
{
    // 전역 규칙 (GDD 9장 GameConfig). 런타임에 수정하지 않는다. 값은 모두 임시.
    [CreateAssetMenu(menuName = "TowerDefence/GameConfig", fileName = "GameConfig")]
    public class GameConfig : ScriptableObject
    {
        public float breakTimeSec = 120f;
        public float floorTransitionSec = 2f; // 층 전환 연출 시간 (임시, GDD 9장에 없는 값)
        public int startCash = 100;
        public int nexusHp = 20;
        public int finalFloor = 10;
        public bool upgradeEnabled = false; // 테크트리: 설치된 타워 클릭 → 다음 단계로 진화 (TowerDef.upgradeTo)
        public float sellRefundRate = 0.7f;
        public float recoveryRefundRate = 0.7f;

        public LevelRuleParams rules = new LevelRuleParams();
        public MapPoolSettings mapPool = new MapPoolSettings();
        public PerformanceSettings performance = new PerformanceSettings();
        public UiSettings ui = new UiSettings();
        public CashLogSettings cashLog = new CashLogSettings();
        public FloorItemSettings floorItems = new FloorItemSettings(); // 층 클리어 아이템 (영구 스탯)

        // 레벨디자인 웨이브 테이블 (GDD 9장 "Waves"). 층·웨이브 항목이 있으면 공식 규칙 대신 이 구성을 쓴다.
        // 특수 몬스터를 섞는 것도 여기 데이터만으로 한다 (코드 수정 없음). 비어 있으면 공식 규칙.
        public DesignedWave[] waves = new DesignedWave[0];

        [Serializable]
        public class DesignedWave
        {
            public int floor = 1;
            public int wave = 1;            // 층 안 웨이브 번호 (1부터)
            public SpawnLine[] lines = new SpawnLine[0];
        }

        [Serializable]
        public class SpawnLine
        {
            public string monsterId = "normal";
            public int count = 10;
            public float intervalSec = 1f;
            public float delaySec = 0f;     // 이 줄을 시작하기 전 대기
        }

        // 층 클리어 보상 아이템 (Docs/LevelDesign_Balance.md 4-2). 마지막 층 전까지 층마다 1개:
        // 홀수 층 = 화력(공격력 +damagePerItem), 짝수 층 = 연사(공격 속도 +speedPerItem). 각각 합산, 둘은 곱한다.
        [Serializable]
        public class FloorItemSettings
        {
            public bool enabled = false;
            public float damagePerItem = 0.10f;
            public float speedPerItem = 0.08f;
        }

        [Serializable]
        public class LevelRuleParams
        {
            public int mapTierEveryFloors = 2;  // 2층마다 맵 단계(Tier)가 오른다: 복잡성 +1, 맵 +2칸
            public int mapBaseSize = 7;
            public int mapGrowPerTier = 2;
            public int mapMaxSize = 15;         // 맵 최대 15×15
            public int maxComplexity = 5;       // 복잡성 = Tier (최대 5). 복잡성별 꺾임·스폰 수는 MapGenRules.complexity
            public int baseWaves = 1;
            public int waveIncreaseEveryFloors = 2;
            public int maxWaves = 5;            // 층당 웨이브 최대 5
            public int baseMonsterCount = 10;
            public int monsterIncreaseEveryWaves = 2;
            public int monsterIncreaseAmount = 2;
            public float spawnIntervalSec = 1f;
            public string defaultMonsterId = "normal"; // 웨이브 테이블이 없을 때 공식 웨이브에 나오는 몬스터 (GDD 4장)

            // 돌격조 (해머헤드): runnerFromFloor층부터 모든 웨이브에서 일반 몬스터 뒤에 몰려 나온다 (0이면 안 나옴).
            // 마리 수 = runnerBaseCount + (맵 단계(Tier) − 처음 나온 층의 Tier) → 2층 2, 3·4층 3, 5·6층 4, 7·8층 5, 9·10층 6.
            // 일반 몬스터를 다 내보낸 뒤 runnerDelaySec 쉬고 runnerIntervalSec 간격으로 빠르게.
            public string runnerMonsterId = "fast";
            public int runnerFromFloor = 2;
            public int runnerBaseCount = 2;
            public float runnerDelaySec = 1f;
            public float runnerIntervalSec = 0.5f;

            // 탱커 (바스티온): tankerFromFloor층부터 층의 마지막 웨이브 끝에 따로 나온다 (0이면 안 나옴).
            // 마리 수 = 맵 단계(Tier) − 처음 나온 층의 Tier + 1 → 3·4층 1, 5·6층 2, 7·8층 3, 9·10층 4.
            // 일반 몬스터를 다 내보낸 뒤 tankerDelaySec 쉬고 tankerIntervalSec 간격으로.
            public string tankerMonsterId = "tank";
            public int tankerFromFloor = 3;
            public float tankerDelaySec = 2f;
            public float tankerIntervalSec = 3f;

            // 몬스터 체력 배율 (Docs/HpLevelDesign.md): 체력 = 기본 체력 × M × C × F × W, hpRoundStep 단위 반올림
            public float[] hpMapScale = { 1f, 1.6f, 2.6f, 4f, 6f };               // M: 맵 단계(Tier) 1~5 (맵 확장)
            public float[] hpComplexityScale = { 1f, 0.89f, 0.94f, 0.86f, 0.94f }; // C: 복잡성 1~5 (꺾임 보너스 × 스타트 지점 분산)
            public float hpSecondFloorScale = 1.25f; // F: 같은 맵 단계의 두 번째 층
            public float hpPerWave = 0.08f;          // W: 층 안 웨이브마다 +8% (1웨이브 1.0)
            public float hpRoundStep = 0.5f;         // 반올림 단위 (화염방사 1틱 = 0.5)
        }

        [Serializable]
        public class MapPoolSettings
        {
            public int mapsPerTier = 1;
            public bool prefetchNextTier = true;
        }

        [Serializable]
        public class PerformanceSettings
        {
            public int monsterPoolPrewarm = 300;
            public int projectilePoolPrewarm = 200;
            public int towerPoolPrewarm = 100;
            public int effectPoolPrewarm = 48; // 타워 종류별 발사 이펙트 수 (동시에 보이는 최대 수, 넘으면 가장 오래된 것을 재사용)
            public bool mapGenUseThread = true;
            public float mapGenFrameBudgetMs = 2f;
        }

        [Serializable]
        public class UiSettings
        {
            public float panelSlideSec = 0.25f;
            public Color blockedTint = new Color(0xE5 / 255f, 0x73 / 255f, 0x73 / 255f); // #E57373
            public float blockedAlpha = 0.28f;
            public float blockedHoverAlpha = 0.4f;
            public string startPromptText = "아무 곳이나 클릭 시 게임 시작";

            // 하단 바 버튼 목록 (GDD 5-1). 버튼 추가 = 항목 추가 + GameUI에 id별 동작 연결.
            public BottomButton[] bottomButtons =
            {
                new BottomButton { id = "build", label = "타워 건축", enabled = true },
                new BottomButton { id = "upgrade", label = "타워 업그레이드", enabled = false, disabledTooltip = "준비 중" }, // GDD 5-5
            };
        }

        [Serializable]
        public class BottomButton
        {
            public string id;
            public string label;
            public bool enabled = true;          // false면 회색 비활성 (눌러도 반응 없음)
            public string disabledTooltip = "";  // 비활성일 때 마우스 올림 툴팁
        }

        [Serializable]
        public class CashLogSettings // 보류(GDD 5-6): 자리만
        {
            public bool enabled = false;
            public int maxLines = 5;
            public float lifetimeSec = 3f;
        }
    }
}
