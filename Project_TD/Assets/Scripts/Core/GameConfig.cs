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
        public bool upgradeFree = false;    // 임시: 진화 비용 0 (켜 두면 차액을 받지 않는다)
        public bool labUnlock = false;      // 공학연구소: 켜면 2단계 이상 타워는 M1반도체로 해금해야 진화할 수 있다 (GameConfig.asset은 켬)
        public float sellRefundRate = 0.7f;
        public float recoveryRefundRate = 0.7f;

        public LevelRuleParams rules = new LevelRuleParams();
        public MapPoolSettings mapPool = new MapPoolSettings();
        public PerformanceSettings performance = new PerformanceSettings();
        public UiSettings ui = new UiSettings();
        public CashLogSettings cashLog = new CashLogSettings();
        public FloorItemSettings floorItems = new FloorItemSettings(); // 층 클리어 아이템 (영구 스탯)
        public FactorySettings factory = new FactorySettings();          // 반도체 공장 (로비): 판을 넘어 남는 기본 스탯 상승

        // 레벨디자인 웨이브 테이블 (GDD 9장 "Waves"). 층·웨이브 항목이 있으면 공식 규칙 대신 이 구성을 쓴다.
        // 특수 몬스터를 섞는 것도 여기 데이터만으로 한다 (코드 수정 없음). 비어 있으면 공식 규칙.
        public DesignedWave[] waves = new DesignedWave[0];

        [Serializable]
        public class DesignedWave
        {
            public int floor = 1;
            public int wave = 1;            // 층 안 웨이브 번호 (1부터)
            public bool parallel;           // true면 줄마다 웨이브 시작부터 delaySec 뒤에 따로 시작 (스타트 지점별 동시 진행)
            public SpawnLine[] lines = new SpawnLine[0];
        }

        [Serializable]
        public class SpawnLine
        {
            public string monsterId = "normal";
            public int count = 10;
            public float intervalSec = 1f;
            public float delaySec = 0f;     // 이 줄을 시작하기 전 대기 (parallel이면 웨이브 시작부터)
            public int spawnIndex = -1;     // 이 줄을 낼 스타트 지점 (−1 = 자동: 맵 가중치 또는 번갈아)
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
            public int mapMaxSize = 11;         // 맵 최대 11×11 (3단계). 4·5단계는 크기는 그대로, 복잡성만 오른다
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
            public float[] hpMapScale = { 1f, 1.8f, 3.0f, 4.4f, 6.0f };           // M: 맵 단계(Tier) 1~5. 맵 크기는 3단계에서 멈춰도 체력은 단계마다 계속 오른다 (2026-11 상향)
            public float[] hpComplexityScale = { 1f, 0.89f, 0.94f, 0.86f, 0.94f }; // C: 복잡성 1~5 (꺾임 보너스 × 스타트 지점 분산)
            public float hpSecondFloorScale = 1.25f; // F: 같은 맵 단계의 두 번째 층
            public float hpPerWave = 0.08f;          // W: 층 안 웨이브마다 +8% (1웨이브 1.0)
            public float hpRoundStep = 0.5f;         // 반올림 단위 (화염방사 1틱 = 0.5)

            // 스타트 지점이 2곳 이상인 맵: 웨이브 몬스터를 스타트마다 나눠, 웨이브마다 순서/동시를 고른다 (LevelRules.ArrangeSpawns)
            //   순서대로(spawnSequentialChance): s1 몫이 다 나오면 spawnSequentialGapSec 뒤 s2 … / 나머지: 두 곳이 동시에 (체력 × simultaneousHpMul)
            //   스타트 3곳: 세 곳이 한꺼번에 나오는 일은 없다 (s1·s2s3 / s1s2·s3 / s1s3·s2 중 하나)
            public float spawnSequentialChance = 0.67f;
            public float spawnSequentialGapSec = 2f;
            public float simultaneousHpMul = 0.9f;
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
                new BottomButton { id = "upgrade", label = "타워 업그레이드", enabled = true, disabledTooltip = "준비 중" }, // 테크트리 창 (TechTreeView)
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

        // 반도체 공장 노드 5종(공격력·공격 속도·설치 비용 할인·시작 캐시·넥서스 체력), 노드마다 최대 maxLevel단계.
        // 재화 '작은 반도체'는 판이 끝날 때 클리어한 층 수 × chipsPerFloor개. 초기화는 무료(쓴 재화를 모두 돌려받음).
        [Serializable]
        public class FactorySettings
        {
            public bool enabled = true;
            public float perLevel = 0.05f;                     // 단계당 +5% (할인은 −5%)
            public int maxLevel = 5;
            public int[] levelCosts = { 1, 2, 3, 4, 5 };       // 1→2→…단계로 올리는 가격 (작은 반도체)
            public int chipsPerFloor = 2;
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
