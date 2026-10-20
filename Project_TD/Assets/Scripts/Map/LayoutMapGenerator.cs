using System.Collections.Generic;
using UnityEngine;

namespace TowerDefence
{
    // 기존 맵 생성기(MapLayoutGenerator, 정본)를 IMapGenerator로 감싸는 어댑터.
    // 생성 규칙·알고리즘은 건드리지 않고, 결과 MapLayout을 MapData로 옮기기만 한다.
    // 생성기는 System.Random만 쓰고 Unity 객체를 만들지 않으므로 백그라운드 스레드에서 호출해도 된다
    // (UnityEngine 쪽은 값 타입 Vector2Int, Mathf.Abs, 스레드 안전한 Debug.Log만 사용).
    public sealed class LayoutMapGenerator : IMapGenerator
    {
        // 정본 생성기의 재시도 기본값 (MapLayoutGenerator.Generate maxAttempts).
        const int DefaultMaxAttempts = 200;

        // 실패 시 정본 방식 그대로: 생성기 안에서 재시도하고, 끝내 실패하면 null.
        public MapData Generate(MapSpec spec)
        {
            // 정본 생성기는 스폰 1개만 만든다. SpawnCount > 1은 다중 스폰 보류라 무시.
            int maxAttempts = spec.Rules != null && spec.Rules.maxRetries > 0 ? spec.Rules.maxRetries : DefaultMaxAttempts;
            var layout = MapLayoutGenerator.Generate(spec.N, new System.Random(spec.Seed), maxAttempts);
            return layout == null ? null : Convert(layout, spec.Seed);
        }

        static MapData Convert(MapLayout layout, int seed)
        {
            int n = layout.Size;
            var map = new MapData
            {
                N = n,
                Tiles = new TileType[n, n],
                Sites = new SiteGrade[n, n],
                Spawns = new[] { layout.Spawn },
                Exit = layout.Exit,
                Junctions = new List<Vector2Int>(),
                MonsterPaths = new List<List<Vector2Int>> { new List<Vector2Int>(layout.Path) },
                Seed = seed,
            };

            for (int x = 0; x < n; x++)
            {
                for (int y = 0; y < n; y++)
                {
                    // 기존 생성기의 칸 종류는 전역 TileType (이 namespace의 TileType과 다른 타입).
                    switch (layout.Tiles[x, y])
                    {
                        case global::TileType.Path:      map.Tiles[x, y] = TileType.Path; break;
                        case global::TileType.Spawn:     map.Tiles[x, y] = TileType.Spawn; break;
                        case global::TileType.Exit:      map.Tiles[x, y] = TileType.Exit; break;
                        case global::TileType.TowerT1:   map.Tiles[x, y] = TileType.BuildSite; map.Sites[x, y] = SiteGrade.T1; break;
                        case global::TileType.TowerStar: map.Tiles[x, y] = TileType.BuildSite; map.Sites[x, y] = SiteGrade.TStar; break;
                        default:                         map.Tiles[x, y] = TileType.Empty; break;
                    }
                }
            }
            // 정본 생성기는 T2(경로에서 2칸) 부지를 만들지 않는다 (부지 = 경로와 맞닿은 1칸).
            return map;
        }
    }
}
