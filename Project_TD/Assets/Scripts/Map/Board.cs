using System.Collections.Generic;
using UnityEngine;

namespace TowerDefence
{
    // 현재 맵(MapData)과 칸 상태, 좌표 변환, 설치 가능 여부, 경로 칸 번호별 몬스터 목록 (GDD 8-1).
    // 2차원 배열, 할당 없는 조회. 그리기는 하지 않는다(GameUI.MapView 몫).
    //
    // 좌표: 3D 바닥(X, Z). 칸 한 변 1, 맵 중앙이 월드 원점, 타일 윗면이 y = 0.
    //       칸 (x, y)의 중심 = 월드 (x − (N−1)/2, 0, y − (N−1)/2). 3D 맵 표시(MapGenerator.GridToWorld)와 같다.
    // 경로 칸 번호: MonsterPaths[0](스폰 → 출구)의 순번. 다중 스폰은 보류라 경로 0만 칸 번호를 매긴다.
    public class Board
    {
        public event System.Action<MapData> MapLoaded; // → GameUI (MapView 재구성)

        static readonly List<MonsterHandle> EmptyCell = new List<MonsterHandle>(0);
        const int MainPath = 0;

        GameData data;
        RunState run;
        MapData map;
        bool[,] occupied;
        int[,] pathIndexAt;                                         // 칸 → 경로 칸 번호, 경로가 아니면 -1
        List<MonsterHandle>[] buckets = new List<MonsterHandle>[0]; // 경로 칸 번호 → 그 칸의 몬스터
        int bucketCapacity;

        public MapData Map => map;
        public int N => map != null ? map.N : 0;

        public void Init(GameData data)
        {
            this.data = data;
            // 한 칸에 몰릴 수 있는 최대 몬스터 수만큼 미리 잡아 추가·제거 때 할당이 없게 한다.
            bucketCapacity = data.Config != null ? data.Config.performance.monsterPoolPrewarm : 300;
        }

        // 새 판: 맵·칸 점유·칸별 몬스터 목록 비우기.
        public void ResetForNewRun(RunState run)
        {
            this.run = run;
            map = null;
            occupied = null;
            pathIndexAt = null;
            foreach (var bucket in buckets)
                bucket.Clear();
        }

        // Tier 교체 시 새 맵 적용. 경로 칸 번호별 몬스터 목록을 경로 길이만큼 준비한다 (버킷은 재사용).
        public void Load(MapData newMap)
        {
            map = newMap;
            if (run != null)
                run.Map = newMap;

            int n = newMap.N;
            occupied = new bool[n, n];
            pathIndexAt = new int[n, n];
            for (int x = 0; x < n; x++)
                for (int y = 0; y < n; y++)
                    pathIndexAt[x, y] = -1;

            var path = newMap.MonsterPaths[MainPath];
            for (int i = 0; i < path.Count; i++)
                pathIndexAt[path[i].x, path[i].y] = i;

            if (buckets.Length < path.Count)
            {
                int old = buckets.Length;
                System.Array.Resize(ref buckets, path.Count);
                for (int i = old; i < buckets.Length; i++)
                    buckets[i] = new List<MonsterHandle>(bucketCapacity);
            }
            foreach (var bucket in buckets)
                bucket.Clear();
            MapLoaded?.Invoke(newMap);
        }

        public bool InBounds(Vector2Int cell) =>
            map != null && cell.x >= 0 && cell.y >= 0 && cell.x < map.N && cell.y < map.N;

        public TileType GetTile(Vector2Int cell) => InBounds(cell) ? map.Tiles[cell.x, cell.y] : TileType.Empty;

        public SiteGrade GetSite(Vector2Int cell) => InBounds(cell) ? map.Sites[cell.x, cell.y] : SiteGrade.None;

        // 타워 부지이고 비어 있는지 (캐시 판정은 TowerManager/PlayerState 몫).
        public bool IsBuildable(Vector2Int cell) =>
            InBounds(cell) && map.Tiles[cell.x, cell.y] == TileType.BuildSite && !occupied[cell.x, cell.y];

        public bool IsOccupied(Vector2Int cell) => InBounds(cell) && occupied[cell.x, cell.y];

        // 타워 점유 표시. TowerChanged 이벤트로 불린다.
        public void Occupy(Vector2Int cell)
        {
            if (InBounds(cell))
                occupied[cell.x, cell.y] = true;
        }

        public void Release(Vector2Int cell)
        {
            if (InBounds(cell))
                occupied[cell.x, cell.y] = false;
        }

        // 칸 윗면 중심의 월드 좌표.
        public Vector3 CellToWorld(Vector2Int cell)
        {
            float half = (N - 1) * 0.5f;
            return new Vector3(cell.x - half, 0f, cell.y - half);
        }

        // 월드 좌표(바닥 X, Z)가 가리키는 칸. 맵 밖이면 false.
        public bool TryWorldToCell(Vector3 world, out Vector2Int cell)
        {
            float half = (N - 1) * 0.5f;
            cell = new Vector2Int(Mathf.RoundToInt(world.x + half), Mathf.RoundToInt(world.z + half));
            return InBounds(cell);
        }

        // 경로(스폰별) 칸 수.
        public int GetPathLength(int pathId) =>
            map != null && pathId >= 0 && pathId < map.MonsterPaths.Count ? map.MonsterPaths[pathId].Count : 0;

        // 경로 칸 번호 → 칸 좌표.
        public Vector2Int GetPathCell(int pathId, int pathIndex) => map.MonsterPaths[pathId][pathIndex];

        // 칸 → 본선 경로 칸 번호. 경로가 아니면 -1.
        public int GetPathIndex(Vector2Int cell) => InBounds(cell) ? pathIndexAt[cell.x, cell.y] : -1;

        // 몬스터가 경로 칸 번호에 들어올 때 (칸이 바뀔 때만 호출).
        public void AddToCell(int pathIndex, MonsterHandle handle)
        {
            buckets[pathIndex].Add(handle); // 용량을 미리 잡아 두어 할당 없음
        }

        // 몬스터가 칸을 떠나거나 사망·출구 도착할 때. 순서는 의미 없으므로 마지막 항목과 바꿔 지운다.
        public void RemoveFromCell(int pathIndex, MonsterHandle handle)
        {
            var bucket = buckets[pathIndex];
            int last = bucket.Count - 1;
            for (int i = 0; i <= last; i++)
            {
                if (bucket[i] == handle)
                {
                    bucket[i] = bucket[last];
                    bucket.RemoveAt(last);
                    return;
                }
            }
        }

        // 경로 칸 번호의 몬스터 목록. List 그대로 반환해 foreach가 할당하지 않게 한다. 읽기 전용으로 쓴다.
        public List<MonsterHandle> MonstersAt(int pathIndex) =>
            pathIndex >= 0 && pathIndex < buckets.Length ? buckets[pathIndex] : EmptyCell;

        // TowerManager.TowerChanged 구독: 칸 점유 갱신.
        public void HandleTowerChanged(TowerChange change)
        {
            if (change.Built)
                Occupy(change.Cell);
            else
                Release(change.Cell);
        }
    }
}
