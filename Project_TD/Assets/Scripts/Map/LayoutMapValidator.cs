using UnityEngine;

namespace TowerDefence
{
    // 맵 검사기(MapLayoutGenerator.ValidatePaths)를 IMapValidator로 감싸는 어댑터.
    // 경로 규칙(A·B·B1, 다중 스폰·합류 포함)은 첫 위반을 문장으로만 돌려주므로 RuleId는 "PATH"로 묶는다.
    // D1(사각지대 0)은 여기서 모든 경로 칸에 대해 따로 검사한다.
    // 꺾임 상한은 복잡성 표의 최대값으로 넉넉하게 본다 (검사기는 맵의 복잡성 단계를 모른다. 단계별 범위는 생성기가 지킨다).
    public sealed class LayoutMapValidator : IMapValidator
    {
        static readonly Vector2Int[] Dirs = { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };

        public ValidationReport Validate(MapData map, MapGenRules rules)
        {
            var report = new ValidationReport();
            if (map == null || map.MonsterPaths == null || map.MonsterPaths.Count == 0)
            {
                report.Violations.Add(new RuleViolation("MAP", "맵 또는 경로가 비어 있음"));
                return report;
            }

            if (!MapLayoutGenerator.ValidatePaths(map.MonsterPaths, map.N, LoosestParams(rules), out string reason))
                report.Violations.Add(new RuleViolation("PATH", reason));

            // D1: 모든 경로 칸은 상하좌우 중 최소 1칸이 타워 부지와 맞닿아야 한다.
            int need = rules != null ? rules.minAdjacentBuildSites : 1;
            foreach (var path in map.MonsterPaths)
            {
                foreach (var cell in path)
                {
                    int sites = 0;
                    foreach (var d in Dirs)
                    {
                        var c = cell + d;
                        if (c.x >= 0 && c.y >= 0 && c.x < map.N && c.y < map.N && map.Tiles[c.x, c.y] == TileType.BuildSite)
                            sites++;
                    }
                    if (sites < need)
                        report.Violations.Add(new RuleViolation("D1", $"사각지대 {cell}: 맞닿은 부지 {sites}칸"));
                }
            }
            return report;
        }

        // 복잡성 표 전체에서 가장 넉넉한 꺾임 상한 (검사기는 맵의 복잡성 단계를 모른다).
        static MapLayoutParams LoosestParams(MapGenRules rules)
        {
            var p = LayoutMapGenerator.ToParams(rules, 1);
            if (rules == null || rules.complexity == null)
                return p;
            foreach (var level in rules.complexity)
            {
                if (level == null)
                    continue;
                p.MainTurnsMax = Mathf.Max(p.MainTurnsMax, level.mainTurnsMax);
                p.BranchMaxTurns = Mathf.Max(p.BranchMaxTurns, level.branchMaxTurns);
            }
            return p;
        }
    }
}
