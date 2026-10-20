using UnityEngine;

namespace TowerDefence
{
    // 맵 생성 규칙 파라미터 (GDD 9장 MapGenRules, 7장 규칙 ID 대응).
    // 기본값은 문서 예시가 아니라 정본인 기존 맵 생성기(MapLayoutGenerator) 상수를 따른다.
    [CreateAssetMenu(menuName = "TowerDefence/MapGenRules", fileName = "MapGenRules")]
    public class MapGenRules : ScriptableObject
    {
        public int maxSpawns = 1;               // A2~A4. 정본 생성기는 스폰 1개만 지원 (다중 스폰 보류)
        public int spawnMinManhattan = 3;       // A4 (다중 스폰 보류: 자리만)
        public bool forbidCornerAnchors = true; // A5
        public bool forbidContact = true;       // B1
        public int minRunLength = 2;            // B2 (정본: MapLayoutGenerator.MinRunLength)
        public int mainMaxTurns = 4;            // B3 (정본: MapLayoutGenerator.MaxTurns)
        public int branchMaxTurns = 1;          // B4 (다중 스폰 보류: 자리만)
        public bool forbidBacktrack = true;     // B5
        public bool forbidCornerTurns = true;   // B6
        public ConvergeStrategy[] convergeStrategies = { ConvergeStrategy.Shortest, ConvergeStrategy.Longest }; // C2 (보류: 자리만)
        public Vector2Int longestConvergeRange = new Vector2Int(1, 3); // C2-b (보류: 자리만)
        public int minAdjacentBuildSites = 1;   // D1
        public int buildSiteMaxDistance = 1;    // D2. 정본: 경로와 맞닿은 1칸만 부지 (문서 예시는 2)
        public int maxRetries = 200;            // 정본: MapLayoutGenerator.Generate maxAttempts 기본값
    }

    // 지선 합류 전략 (GDD 7-5 C2). 다중 스폰 보류로 현재 미사용.
    public enum ConvergeStrategy
    {
        Shortest,
        Longest,
    }
}
