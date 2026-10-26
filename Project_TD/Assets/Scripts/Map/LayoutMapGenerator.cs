using System.Collections.Generic;
using UnityEngine;

namespace TowerDefence
{
    // 맵 생성기(MapLayoutGenerator)를 IMapGenerator로 감싸는 어댑터.
    // MapSpec(크기·복잡성·시드)과 MapGenRules(복잡성 표·공통 규칙)를 생성기 입력으로 바꾸고, 결과 MapLayout을 MapData로 옮긴다.
    // 생성기는 System.Random만 쓰고 Unity 객체를 만들지 않으므로 백그라운드 스레드에서 호출해도 된다.
    public sealed class LayoutMapGenerator : IMapGenerator
    {
        const int DefaultMaxAttempts = 200;

        // 실패 시: 생성기 안에서 재시도하고, 끝내 실패하면 null.
        public MapData Generate(MapSpec spec)
        {
            int maxAttempts = spec.Rules != null && spec.Rules.maxRetries > 0 ? spec.Rules.maxRetries : DefaultMaxAttempts;
            var layout = MapLayoutGenerator.Generate(spec.N, ToParams(spec), new System.Random(spec.Seed), maxAttempts);
            return layout == null ? null : Convert(layout, spec.Seed);
        }

        // 복잡성 단계 → 생성기 입력 (검사기도 같은 값을 쓴다).
        public static MapLayoutParams ToParams(MapSpec spec) => ToParams(spec.Rules, spec.Complexity);

        public static MapLayoutParams ToParams(MapGenRules rules, int complexity)
        {
            if (rules == null)
                return MapLayoutParams.Default;
            var level = rules.GetLevel(complexity);
            return new MapLayoutParams
            {
                MainTurnsMin = level.mainTurnsMin,
                MainTurnsMax = level.mainTurnsMax,
                Spawns = Mathf.Max(1, level.spawns),
                BranchMaxTurns = level.branchMaxTurns,
                MinRun = rules.minRunLength > 0 ? rules.minRunLength : MapLayoutGenerator.MinRunLength,
                SpawnMinDistance = rules.spawnMinManhattan,
            };
        }

        static MapData Convert(MapLayout layout, int seed)
        {
            int n = layout.Size;
            var spawns = new Vector2Int[layout.SpawnCount];
            var paths = new List<List<Vector2Int>>();
            for (int k = 0; k < layout.SpawnCount; k++)
            {
                var path = layout.MonsterPath(k);
                paths.Add(path);
                spawns[k] = path[0];
            }
            var junctions = new List<Vector2Int>();
            foreach (int j in layout.JunctionIndex)
                junctions.Add(layout.Path[j]);

            var map = new MapData
            {
                N = n,
                Tiles = new TileType[n, n],
                Sites = new SiteGrade[n, n],
                Spawns = spawns,
                Exit = layout.Exit,
                Junctions = junctions,
                MonsterPaths = paths,
                Seed = seed,
            };

            for (int x = 0; x < n; x++)
            {
                for (int y = 0; y < n; y++)
                {
                    // 생성기의 칸 종류는 전역 TileType (이 namespace의 TileType과 다른 타입).
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
            // 부지 = 경로와 맞닿은 1칸 (T2 없음).
            return map;
        }
    }
}
