using UnityEngine;

namespace TowerDefence
{
    // 기존 맵 생성기의 검사 함수(MapLayoutGenerator.Validate, 정본)를 IMapValidator로 감싸는 어댑터.
    // 정본 검사는 경로 규칙(A1~A3, A5, B1~B3, B5, B6)을 한꺼번에 보고 첫 위반을 문장으로만 돌려주므로
    // RuleId는 "PATH"로 묶는다. D1(사각지대 0)은 정본에서 private이라 같은 규칙을 여기서 다시 검사한다.
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

            foreach (var path in map.MonsterPaths)
            {
                if (!MapLayoutGenerator.Validate(path, map.N, out string reason))
                    report.Violations.Add(new RuleViolation("PATH", reason));

                // D1: 모든 경로 칸은 상하좌우 중 최소 1칸이 타워 부지와 맞닿아야 한다.
                int need = rules != null ? rules.minAdjacentBuildSites : 1;
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
    }
}
