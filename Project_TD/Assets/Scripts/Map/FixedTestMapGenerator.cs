using System.Collections.Generic;
using UnityEngine;

namespace TowerDefence
{
    // 테스트용 고정 7×7 맵 (생성기 연결 전후 비교용). GameLoop의 useFixedTestMap 플래그로 전환.
    // spec과 상관없이 항상 같은 맵을 돌려준다 (Tier가 바뀌어도 7×7).
    //
    //   y=6  . . . . # E #      S = 스폰 (3,0), E = 출구 (5,6)
    //   y=5  . . . . # P #      P = 경로, # = 타워 부지
    //   y=4  . . . . # P #      (4,1), (4,3) = T★, 나머지 # = T1
    //   y=3  . . . # # P #
    //   y=2  . . # P P P #
    //   y=1  . . # P # # .
    //   y=0  . . # S # . .
    public sealed class FixedTestMapGenerator : IMapGenerator
    {
        public const int Size = 7;

        static readonly Vector2Int[] Path =
        {
            new Vector2Int(3, 0), new Vector2Int(3, 1), new Vector2Int(3, 2), // 위로 2칸
            new Vector2Int(4, 2), new Vector2Int(5, 2),                       // 오른쪽 2칸
            new Vector2Int(5, 3), new Vector2Int(5, 4), new Vector2Int(5, 5), new Vector2Int(5, 6), // 위로 4칸
        };

        static readonly Vector2Int[] Dirs = { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };

        public MapData Generate(MapSpec spec)
        {
            var map = new MapData
            {
                N = Size,
                Tiles = new TileType[Size, Size],
                Sites = new SiteGrade[Size, Size],
                Spawns = new[] { Path[0] },
                Exit = Path[Path.Length - 1],
                Junctions = new List<Vector2Int>(),
                MonsterPaths = new List<List<Vector2Int>> { new List<Vector2Int>(Path) },
                Seed = 0,
            };

            // 경로 칸과 각 칸이 속한 직선 구간 번호 (꺾인 칸은 들어온 쪽 구간).
            var segmentOf = new Dictionary<Vector2Int, int>();
            int segment = 0;
            for (int i = 0; i < Path.Length; i++)
            {
                if (i >= 2 && Path[i] - Path[i - 1] != Path[i - 1] - Path[i - 2])
                    segment++;
                segmentOf[Path[i]] = segment;
                map.Tiles[Path[i].x, Path[i].y] = TileType.Path;
            }
            map.Tiles[map.Spawns[0].x, map.Spawns[0].y] = TileType.Spawn;
            map.Tiles[map.Exit.x, map.Exit.y] = TileType.Exit;

            // 정본과 같은 부지 규칙: 경로와 맞닿은 칸 = T1, 서로 다른 구간 2개 이상에 닿으면 T★.
            var touched = new HashSet<int>();
            for (int x = 0; x < Size; x++)
            {
                for (int y = 0; y < Size; y++)
                {
                    var cell = new Vector2Int(x, y);
                    if (segmentOf.ContainsKey(cell))
                        continue;
                    touched.Clear();
                    foreach (var d in Dirs)
                        if (segmentOf.TryGetValue(cell + d, out int seg))
                            touched.Add(seg);
                    if (touched.Count > 0)
                    {
                        map.Tiles[x, y] = TileType.BuildSite;
                        map.Sites[x, y] = touched.Count >= 2 ? SiteGrade.TStar : SiteGrade.T1;
                    }
                }
            }
            return map;
        }
    }
}
