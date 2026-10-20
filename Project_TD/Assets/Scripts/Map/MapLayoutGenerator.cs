using System;
using System.Collections.Generic;
using UnityEngine;

// 규칙 기반 타워 디펜스 맵 생성기 (순수 로직, 씬 오브젝트 없음).
//
// 생성 방식: 스폰을 아래 변(y=0), 출구를 위 변(y=N-1)에 둔 기준 방향에서
// "세로 직진 → 가로 직진 → 세로 직진 ..." 형태로 경로를 조립한 뒤
// 4방향 중 하나로 변환한다. 경로는 항상 출구 쪽으로만 전진하므로
// 역주행과 자기 접촉이 구조적으로 생기지 않고, 마지막에 Validate로 다시 확인한다.
public static class MapLayoutGenerator
{
    public const int MinSize = 5;
    public const int MinRunLength = 2; // 방향 전환 후 최소 직진 칸 수
    public const int MaxTurns = 4;     // 본선 최대 꺾임 횟수

    static readonly Vector2Int[] Dirs = { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };

    public static MapLayout Generate(int size, System.Random rng, int maxAttempts = 200)
    {
        if (size < MinSize)
            throw new ArgumentException($"그리드 크기는 {MinSize} 이상이어야 합니다: {size}");

        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            var path = BuildCanonicalPath(size, rng, out int turns);
            if (path == null)
                continue;

            int edge = rng.Next(4);
            for (int i = 0; i < path.Count; i++)
                path[i] = Orient(path[i], size, edge);

            if (!Validate(path, size, out string reason))
            {
                Debug.LogWarning($"[MapLayoutGenerator] 경로 규칙 위반, 재시도: {reason}");
                continue;
            }

            var layout = new MapLayout(size, path, turns);
            ClassifyTiles(layout);

            if (!HasNoBlindSpot(layout))
                continue;

            return layout;
        }

        Debug.LogError($"[MapLayoutGenerator] {maxAttempts}회 시도 안에 유효한 맵을 만들지 못했습니다.");
        return null;
    }

    // 기준 방향(스폰 y=0 → 출구 y=N-1)으로 경로를 만든다.
    static List<Vector2Int> BuildCanonicalPath(int size, System.Random rng, out int turns)
    {
        turns = 0;

        // 꼭짓점 배제: x는 1 ~ N-2
        int sx = rng.Next(1, size - 1);
        int ex = rng.Next(1, size - 1);
        int lateral = Mathf.Abs(ex - sx);
        int forward = size - 1;

        // 첫 구간(변에서 안쪽으로)과 마지막 구간(출구로 진입)은 세로 방향이므로
        // 꺾임 횟수는 0, 2, 4 중 하나가 된다.
        var options = new List<int>();
        if (lateral == 0)
        {
            options.Add(0);
        }
        else
        {
            for (int t = 2; t <= MaxTurns; t += 2)
            {
                int lateralRuns = t / 2;
                if (lateral >= lateralRuns * MinRunLength && forward >= (lateralRuns + 1) * MinRunLength)
                    options.Add(t);
            }
        }
        if (options.Count == 0)
            return null;

        turns = options[rng.Next(options.Count)];
        int latRunCount = turns / 2;
        int[] forwardRuns = RandomPartition(forward, latRunCount + 1, MinRunLength, rng);
        int[] lateralRunLens = RandomPartition(lateral, latRunCount, MinRunLength, rng);
        var side = new Vector2Int(Math.Sign(ex - sx), 0);

        var path = new List<Vector2Int>();
        var cur = new Vector2Int(sx, 0);
        path.Add(cur);
        for (int i = 0; i < forwardRuns.Length; i++)
        {
            for (int s = 0; s < forwardRuns[i]; s++) { cur += Vector2Int.up; path.Add(cur); }
            if (i < lateralRunLens.Length)
                for (int s = 0; s < lateralRunLens[i]; s++) { cur += side; path.Add(cur); }
        }
        return path;
    }

    // total을 count개 조각으로 나눈다. 각 조각은 min 이상.
    static int[] RandomPartition(int total, int count, int min, System.Random rng)
    {
        var parts = new int[count];
        if (count == 0)
            return parts;
        for (int i = 0; i < count; i++)
            parts[i] = min;
        for (int extra = total - count * min; extra > 0; extra--)
            parts[rng.Next(count)]++;
        return parts;
    }

    // 기준 방향 좌표를 실제 스폰 변 방향으로 변환. 0=아래, 1=위, 2=왼쪽, 3=오른쪽.
    static Vector2Int Orient(Vector2Int p, int size, int edge)
    {
        int n = size - 1;
        switch (edge)
        {
            case 1: return new Vector2Int(p.x, n - p.y);
            case 2: return new Vector2Int(p.y, p.x);
            case 3: return new Vector2Int(n - p.y, p.x);
            default: return p;
        }
    }

    public static bool Validate(List<Vector2Int> path, int size, out string reason)
    {
        reason = null;
        if (path == null || path.Count < 2) { reason = "경로가 비어 있음"; return false; }

        var spawn = path[0];
        var exit = path[path.Count - 1];

        if (!IsBorder(spawn, size) || IsCorner(spawn, size)) { reason = $"스폰 위치 오류 {spawn}"; return false; }
        if (!IsBorder(exit, size) || IsCorner(exit, size)) { reason = $"출구 위치 오류 {exit}"; return false; }
        if (!OnOppositeEdges(spawn, exit, size)) { reason = "출구가 스폰의 반대편 변에 있지 않음"; return false; }

        int turns = 0;
        int runLength = 0;
        Vector2Int prevDir = Vector2Int.zero;
        for (int i = 1; i < path.Count; i++)
        {
            var p = path[i];
            if (!InBounds(p, size)) { reason = $"그리드 밖 {p}"; return false; }

            var dir = p - path[i - 1];
            if (Manhattan(dir, Vector2Int.zero) != 1) { reason = $"끊긴 경로 {path[i - 1]}→{p}"; return false; }

            // 역주행 금지: 출구까지 거리가 매 칸 줄어들어야 한다.
            if (Manhattan(p, exit) >= Manhattan(path[i - 1], exit)) { reason = $"역주행 {p}"; return false; }

            if (prevDir != Vector2Int.zero && dir != prevDir)
            {
                if (runLength < MinRunLength) { reason = $"직진 {runLength}칸 후 회전 (계단형) {path[i - 1]}"; return false; }
                if (IsCorner(path[i - 1], size)) { reason = $"꼭짓점에서 회전 {path[i - 1]}"; return false; }
                turns++;
                runLength = 0;
            }
            runLength++;
            prevDir = dir;
        }
        if (runLength < MinRunLength) { reason = $"마지막 직진 {runLength}칸"; return false; }
        if (turns > MaxTurns) { reason = $"꺾임 {turns}회"; return false; }

        // 경로 간 직접 접촉 금지: 이웃한 순서가 아닌 경로 타일끼리 4방향으로 붙으면 안 된다.
        for (int i = 0; i < path.Count; i++)
            for (int j = i + 2; j < path.Count; j++)
                if (Manhattan(path[i], path[j]) <= 1) { reason = $"경로 접촉 {path[i]}-{path[j]}"; return false; }

        return true;
    }

    // 경로/스폰/출구를 찍고 나머지 칸을 타워 부지 등급으로 분류한다.
    static void ClassifyTiles(MapLayout layout)
    {
        int size = layout.Size;
        var path = layout.Path;

        // 각 경로 타일이 속한 직선 구간(선분) 번호. 회전 칸은 들어온 쪽 구간에 속한다.
        var segmentOf = new Dictionary<Vector2Int, int>();
        int segment = 0;
        segmentOf[path[0]] = 0;
        for (int i = 1; i < path.Count; i++)
        {
            if (i >= 2 && path[i] - path[i - 1] != path[i - 1] - path[i - 2])
                segment++;
            segmentOf[path[i]] = segment;
        }

        foreach (var p in path)
            layout.Tiles[p.x, p.y] = TileType.Path;
        layout.Tiles[layout.Spawn.x, layout.Spawn.y] = TileType.Spawn;
        layout.Tiles[layout.Exit.x, layout.Exit.y] = TileType.Exit;

        var touched = new HashSet<int>();
        for (int x = 0; x < size; x++)
        {
            for (int y = 0; y < size; y++)
            {
                if (segmentOf.ContainsKey(new Vector2Int(x, y)))
                    continue;

                var cell = new Vector2Int(x, y);
                touched.Clear();
                foreach (var d in Dirs)
                    if (segmentOf.TryGetValue(cell + d, out int seg))
                        touched.Add(seg);

                if (touched.Count >= 2)
                    layout.Tiles[x, y] = TileType.TowerStar;
                else if (touched.Count == 1)
                    layout.Tiles[x, y] = TileType.TowerT1;
                else
                    layout.Tiles[x, y] = TileType.Empty;
            }
        }
    }

    // 사각지대 제로: 모든 경로 타일이 4방향 중 하나 이상 타워 부지와 맞닿아야 한다.
    static bool HasNoBlindSpot(MapLayout layout)
    {
        foreach (var p in layout.Path)
        {
            bool covered = false;
            foreach (var d in Dirs)
            {
                var n = p + d;
                if (InBounds(n, layout.Size) && MapLayout.IsTower(layout[n])) { covered = true; break; }
            }
            if (!covered)
                return false;
        }
        return true;
    }

    static int Manhattan(Vector2Int a, Vector2Int b) => Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y);

    static bool InBounds(Vector2Int p, int size) => p.x >= 0 && p.y >= 0 && p.x < size && p.y < size;

    static bool IsBorder(Vector2Int p, int size) => p.x == 0 || p.y == 0 || p.x == size - 1 || p.y == size - 1;

    static bool IsCorner(Vector2Int p, int size) =>
        (p.x == 0 || p.x == size - 1) && (p.y == 0 || p.y == size - 1);

    static bool OnOppositeEdges(Vector2Int a, Vector2Int b, int size)
    {
        int n = size - 1;
        return (a.y == 0 && b.y == n) || (a.y == n && b.y == 0)
            || (a.x == 0 && b.x == n) || (a.x == n && b.x == 0);
    }
}
