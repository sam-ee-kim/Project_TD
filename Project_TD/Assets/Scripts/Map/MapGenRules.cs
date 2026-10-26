using System;
using UnityEngine;

namespace TowerDefence
{
    // 맵 생성 규칙 파라미터 (GDD 9장 MapGenRules, 7장 규칙 ID 대응).
    // 복잡성(1~5)별 꺾임 수·스폰 수는 complexity 표가 정한다 (LevelRules.GetComplexity → MapSpec.Complexity).
    [CreateAssetMenu(menuName = "TowerDefence/MapGenRules", fileName = "MapGenRules")]
    public class MapGenRules : ScriptableObject
    {
        [Serializable]
        public class ComplexityLevel
        {
            public int mainTurnsMin = 2;   // 본선(첫 스폰 → 출구) 꺾임 수 범위. 맵이 작아 못 넣으면 들어가는 만큼
            public int mainTurnsMax = 2;
            public int spawns = 1;         // 스타트 지점 수 (2번째부터는 본선에 합류하는 지선)
            public int branchMaxTurns = 1; // 지선 꺾임 최대 (합류 지점에서 꺾는 것은 제외)

            public ComplexityLevel() { }

            public ComplexityLevel(int turnsMin, int turnsMax, int spawns, int branchMaxTurns)
            {
                mainTurnsMin = turnsMin;
                mainTurnsMax = turnsMax;
                this.spawns = spawns;
                this.branchMaxTurns = branchMaxTurns;
            }
        }

        // 복잡성 1~5: 꺾임이 늘고, 복잡성 2·4에서 스타트 지점이 하나씩 생긴다 (최대 3개).
        public ComplexityLevel[] complexity =
        {
            new ComplexityLevel(2, 2, 1, 0),
            new ComplexityLevel(2, 4, 2, 1),
            new ComplexityLevel(4, 4, 2, 1),
            new ComplexityLevel(4, 6, 3, 2),
            new ComplexityLevel(6, 8, 3, 2),
        };

        public int spawnMinManhattan = 3;       // A4: 스타트 지점끼리 최소 거리
        public bool forbidCornerAnchors = true; // A5
        public bool forbidContact = true;       // B1
        public int minRunLength = 2;            // B2: 꺾은 뒤 최소 직진 칸 수
        public bool forbidBacktrack = true;     // B5
        public bool forbidCornerTurns = true;   // B6
        public int minAdjacentBuildSites = 1;   // D1
        public int buildSiteMaxDistance = 1;    // D2: 경로와 맞닿은 1칸만 부지
        public int maxRetries = 200;            // 맵 전체 재시도 횟수

        // 복잡성 단계 (범위 밖이면 가장 가까운 단계).
        public ComplexityLevel GetLevel(int complexityLevel)
        {
            if (complexity == null || complexity.Length == 0)
                return new ComplexityLevel();
            int i = Mathf.Clamp(complexityLevel - 1, 0, complexity.Length - 1);
            return complexity[i] ?? new ComplexityLevel();
        }
    }
}
