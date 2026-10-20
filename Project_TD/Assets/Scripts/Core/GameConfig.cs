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
        public int finalFloor = 46;
        public bool upgradeEnabled = false; // 보류(GDD 5-5): 플래그만
        public float sellRefundRate = 0.7f;
        public float recoveryRefundRate = 0.7f;

        public LevelRuleParams rules = new LevelRuleParams();
        public MapPoolSettings mapPool = new MapPoolSettings();
        public PerformanceSettings performance = new PerformanceSettings();
        public UiSettings ui = new UiSettings();
        public CashLogSettings cashLog = new CashLogSettings();

        [Serializable]
        public class LevelRuleParams
        {
            public int mapTierEveryFloors = 3;
            public int mapBaseSize = 7;
            public int mapGrowPerTier = 2;
            public int baseWaves = 2;
            public int waveIncreaseEveryFloors = 5;
            public int baseMonsterCount = 10;
            public int monsterIncreaseEveryWaves = 2;
            public int monsterIncreaseAmount = 2;
            public float spawnIntervalSec = 1f;
            public int spawnCount = 1; // 다중 스폰은 보류
            public string defaultMonsterId = "normal"; // 웨이브 테이블이 없을 때 공식 웨이브에 나오는 몬스터 (GDD 4장)
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
