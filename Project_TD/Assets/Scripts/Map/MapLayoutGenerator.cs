using System;
using System.Collections.Generic;
using UnityEngine;

// 맵 생성 입력 (복잡성 단계에서 온 값). TowerDefence.MapGenRules.ComplexityLevel + 공통 규칙을 담는다.
public struct MapLayoutParams
{
    public int MainTurnsMin;      // 본선 꺾임 수 범위 (짝수로 맞춘다. 맵이 작으면 들어가는 만큼)
    public int MainTurnsMax;
    public int Spawns;            // 스타트 지점 수 (1 = 본선만)
    public int BranchMaxTurns;    // 지선 꺾임 최대 (합류 지점의 꺾임은 제외)
    public int MinRun;            // 꺾은 뒤 최소 직진 칸 수
    public int SpawnMinDistance;  // 스타트 지점끼리 최소 맨해튼 거리

    // 예전 생성기와 같은 기본값: 스폰 1개, 꺾임 0~4.
    public static MapLayoutParams Default => new MapLayoutParams
    {
        MainTurnsMin = 0, MainTurnsMax = 4, Spawns = 1, BranchMaxTurns = 1, MinRun = 2, SpawnMinDistance = 3,
    };
}

// 규칙 기반 타워 디펜스 맵 생성기 (순수 로직, 씬 오브젝트 없음, 백그라운드 스레드에서 호출 가능).
//
// 1) 본선: 스폰을 아래 변(y=0), 출구를 위 변(y=N-1)에 둔 기준 방향에서 "세로 직진 → 가로 직진 → …" 계단으로 조립.
//    꺾임 수는 복잡성 범위에서 고른다 (가로 구간 k개 = 꺾임 2k). 출구 쪽으로만 전진하므로 역주행·자기 접촉이 없다.
// 2) 지선(스타트 지점 2, 3): 출구 변이 아닌 변의 칸에서 출발해, 본선의 직선 구간 칸(합류 지점)에 옆에서 수직으로 붙는다.
//    합류 지점 쪽으로만 전진하고(직진 / ㄱ자 / Z자), 다른 경로와는 합류 칸 말고는 닿지 않는다.
// 3) 넥서스 방향: 스타트 지점이 몰린 변의 반대쪽에 넥서스(출구)가 있어야 한다 (변별 스타트 지점 수로 판정).
//    상하·좌우 비율이 같아 한쪽으로 정할 수 없으면(비슷함) 본선이 가로로 끝까지 가며 꺾임 최대로 돌아가고,
//    모든 스타트 지점은 넥서스에서 N−1칸 이상 떨어진다. 맞지 않는 조합은 다시 만든다.
// 4) 기준 방향 좌표 전체를 4방향 중 하나로 돌리고, 규칙 검사(ValidatePaths) 후 부지를 분류한다.
public static class MapLayoutGenerator
{
    public const int MinSize = 5;
    public const int MinRunLength = 2; // 기본 최소 직진 칸 수 (MapLayoutParams.MinRun)

    static readonly Vector2Int[] Dirs = { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };

    public static MapLayout Generate(int size, System.Random rng, int maxAttempts = 200) =>
        Generate(size, MapLayoutParams.Default, rng, maxAttempts);

    public static MapLayout Generate(int size, MapLayoutParams p, System.Random rng, int maxAttempts = 200)
    {
        if (size < MinSize)
            throw new ArgumentException($"그리드 크기는 {MinSize} 이상이어야 합니다: {size}");
        if (p.MinRun < 1)
            p.MinRun = MinRunLength;

        string lastReason = null;
        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            // 절반은 "최대한 돌아가는" 본선으로 만든다 (스타트 지점 비율이 비슷하게 나왔을 때 쓸 수 있도록)
            bool detour = p.Spawns > 1 && rng.Next(2) == 0;
            var main = BuildMainPath(size, p, rng, detour, out int turns);
            if (main == null)
                continue;

            var branches = new List<List<Vector2Int>>();
            var junctions = new List<int>();
            bool ok = true;
            for (int b = 1; b < p.Spawns && ok; b++)
                ok = TryAddBranch(size, p, rng, main, branches, junctions);
            if (!ok)
                continue;
            if (!NexusSideOk(main, branches, size, detour, out lastReason))
                continue;

            int edge = rng.Next(4);
            for (int i = 0; i < main.Count; i++)
                main[i] = Orient(main[i], size, edge);
            foreach (var branch in branches)
                for (int i = 0; i < branch.Count; i++)
                    branch[i] = Orient(branch[i], size, edge);

            var layout = new MapLayout(size, main, turns, branches, junctions);
            var paths = new List<List<Vector2Int>>();
            for (int k = 0; k < layout.SpawnCount; k++)
                paths.Add(layout.MonsterPath(k));
            if (!ValidatePaths(paths, size, p, out lastReason))
                continue;

            ClassifyTiles(layout);
            if (!HasNoBlindSpot(layout))
                continue;
            return layout;
        }

        Debug.LogError($"[MapLayoutGenerator] {maxAttempts}회 시도 안에 유효한 맵을 만들지 못했습니다 (N={size}, 스폰 {p.Spawns}). 마지막 위반: {lastReason}");
        return null;
    }

    // ---------- 본선 ----------

    // 기준 방향(스폰 y=0 → 출구 y=N-1) 계단 경로. 꺾임 2k: 세로 구간 k+1개, 가로 구간 k개 (각 MinRun 이상).
    // detour = 최대한 돌아가기: 꺾임은 범위의 최대, 가로 이동은 끝에서 끝(스폰 x=1 ↔ 출구 x=N-2)까지.
    static List<Vector2Int> BuildMainPath(int size, MapLayoutParams p, System.Random rng, bool detour, out int turns)
    {
        int forward = size - 1;
        int maxLateral = size - 3; // 스폰·출구 x는 꼭짓점을 피해 1 ~ N-2
        int maxK = Math.Max(0, Math.Min(forward / p.MinRun - 1, maxLateral / p.MinRun));

        int lo = Math.Max(0, (p.MainTurnsMin + 1) / 2);   // 가로 구간 수 범위
        int hi = Math.Max(0, p.MainTurnsMax / 2);
        hi = Math.Min(hi, maxK);
        lo = Math.Min(lo, hi);
        int k = detour ? hi : lo + rng.Next(hi - lo + 1);
        turns = k * 2;

        int sx, ex, lateral;
        if (k == 0)
        {
            sx = ex = rng.Next(1, size - 1);
            lateral = 0;
        }
        else
        {
            lateral = detour ? maxLateral : rng.Next(k * p.MinRun, maxLateral + 1);
            int start = rng.Next(1, size - 1 - lateral);
            bool right = rng.Next(2) == 0;
            sx = right ? start : start + lateral;
            ex = right ? start + lateral : start;
        }

        int[] forwardRuns = RandomPartition(forward, k + 1, p.MinRun, rng);
        int[] lateralRuns = RandomPartition(lateral, k, p.MinRun, rng);
        var side = new Vector2Int(Math.Sign(ex - sx), 0);

        var path = new List<Vector2Int>();
        var cur = new Vector2Int(sx, 0);
        path.Add(cur);
        for (int i = 0; i < forwardRuns.Length; i++)
        {
            for (int s = 0; s < forwardRuns[i]; s++) { cur += Vector2Int.up; path.Add(cur); }
            if (i < lateralRuns.Length)
                for (int s = 0; s < lateralRuns[i]; s++) { cur += side; path.Add(cur); }
        }
        return path;
    }

    // ---------- 넥서스 방향 ----------

    // 기준 방향(출구 = 위 변)에서: 스타트 지점이 몰린 변의 반대쪽이 위 변인지.
    // 변별 수로 방향 v = (왼쪽 − 오른쪽, 아래 − 위)를 만들고, 큰 축의 부호가 넥서스 방향이다.
    //   |v.y| > |v.x| (아래가 많다) → 위 넥서스 맞음 / |v.x| > |v.y| (좌우 한쪽이 많다) → 넥서스는 좌우여야 하므로 다시 만든다
    //   |v.x| = |v.y| (비슷함) → detour 본선이고 모든 스타트 지점이 넥서스에서 N−1칸 이상 떨어져 있어야 한다
    static bool NexusSideOk(List<Vector2Int> main, List<List<Vector2Int>> branches, int size, bool detour, out string reason)
    {
        reason = null;
        var spawns = new List<Vector2Int> { main[0] };
        foreach (var b in branches)
            spawns.Add(b[0]);
        var v = SpawnPull(spawns, size);
        int ax = Math.Abs(v.x), ay = Math.Abs(v.y);
        if (ax > ay) { reason = "스타트 지점이 좌우 한쪽에 몰렸는데 넥서스가 그 반대쪽이 아님"; return false; }
        if (ax < ay) return true;
        if (!detour) { reason = "스타트 지점 비율이 비슷한데 본선이 최대로 돌아가지 않음"; return false; }
        var exit = main[main.Count - 1];
        foreach (var s in spawns)
            if (Manhattan(s, exit) < size - 1) { reason = $"비슷한 비율인데 스타트 지점 {s}가 넥서스와 가까움"; return false; }
        return true;
    }

    // 스타트 지점이 미는 방향: 각 스타트 지점 변의 안쪽 방향 합 (왼쪽 변 → +x, 아래 변 → +y ...).
    // 넥서스는 이 방향의 변에 있어야 한다.
    static Vector2Int SpawnPull(List<Vector2Int> spawns, int size)
    {
        var v = Vector2Int.zero;
        foreach (var s in spawns)
            v += InwardNormal(s, size);
        return v;
    }

    // ---------- 지선 ----------

    // 지선 하나: 본선 직선 구간의 칸(합류 지점)과 그 옆 칸(진입 칸)을 고르고, 스타트 지점 후보에서 진입 칸까지 모양을 맞춰 본다.
    static bool TryAddBranch(int size, MapLayoutParams p, System.Random rng, List<Vector2Int> main,
        List<List<Vector2Int>> branches, List<int> junctions)
    {
        var occupied = new HashSet<Vector2Int>(main);
        foreach (var b in branches)
            foreach (var c in b)
                occupied.Add(c);
        var spawns = new List<Vector2Int> { main[0] };
        foreach (var b in branches)
            spawns.Add(b[0]);

        var candidates = new List<KeyValuePair<int, Vector2Int>>();
        for (int j = 2; j <= main.Count - 3; j++)
        {
            if (main[j] - main[j - 1] != main[j + 1] - main[j])
                continue; // 꺾이는 칸에는 합류하지 않는다
            if (junctions.Contains(j) || junctions.Contains(j - 1) || junctions.Contains(j + 1))
                continue;
            var dir = main[j + 1] - main[j];
            var perp = new Vector2Int(dir.y, dir.x);
            candidates.Add(new KeyValuePair<int, Vector2Int>(j, perp));
            candidates.Add(new KeyValuePair<int, Vector2Int>(j, -perp));
        }
        Shuffle(candidates, rng);

        var options = new List<List<Vector2Int>>();
        foreach (var candidate in candidates)
        {
            int j = candidate.Key;
            var junction = main[j];
            var entry = junction + candidate.Value;     // 합류 직전 칸
            var last = -candidate.Value;                // 진입 칸 → 합류 지점 방향
            if (!InBounds(entry, size) || occupied.Contains(entry))
                continue;

            options.Clear();
            foreach (var spawn in BranchSpawnCells(size))
            {
                if (occupied.Contains(spawn) || TooClose(spawn, spawns, p.SpawnMinDistance))
                    continue;
                var branch = BuildBranch(spawn, entry, InwardNormal(spawn, size), last, p, rng);
                if (branch != null && BranchIsClear(branch, occupied, junction, size))
                    options.Add(branch);
            }
            if (options.Count == 0)
                continue;
            branches.Add(options[rng.Next(options.Count)]);
            junctions.Add(j);
            return true;
        }
        return false;
    }

    // 스타트 지점 → 진입 칸. 진입 칸 쪽으로만 전진한다. first = 변에서 안쪽 방향, last = 진입 칸에서 합류 지점으로 가는 방향.
    //   같은 방향: 직진(꺾임 0) 또는 Z자(꺾임 2) / 수직: ㄱ자(꺾임 1) / 반대: 불가
    static List<Vector2Int> BuildBranch(Vector2Int spawn, Vector2Int entry, Vector2Int first, Vector2Int last,
        MapLayoutParams p, System.Random rng)
    {
        var d = entry - spawn;
        int alongFirst = Dot(d, first);
        int lastRunMin = Math.Max(1, p.MinRun - 1); // 진입 칸에서 합류 지점까지 1칸이 더해진다

        if (first == -last)
            return null;

        var path = new List<Vector2Int> { spawn };
        if (first == last)
        {
            var g = new Vector2Int(first.y != 0 ? 1 : 0, first.x != 0 ? 1 : 0); // first에 수직인 축
            int lateral = Dot(d, g);
            if (lateral == 0)
            {
                if (alongFirst < lastRunMin)
                    return null;
                Walk(path, first, alongFirst);
                return path;
            }
            if (p.BranchMaxTurns < 2 || Math.Abs(lateral) < p.MinRun || alongFirst < p.MinRun + lastRunMin)
                return null;
            var side = g * Math.Sign(lateral);
            int a1 = p.MinRun + rng.Next(alongFirst - p.MinRun - lastRunMin + 1);
            Walk(path, first, a1);
            Walk(path, side, Math.Abs(lateral));
            Walk(path, first, alongFirst - a1);
            return path;
        }

        // 수직: 처음 방향으로 진입 칸의 줄까지, 그다음 마지막 방향으로 진입 칸까지 (ㄱ자)
        int alongLast = Dot(d, last);
        if (p.BranchMaxTurns < 1 || alongFirst < p.MinRun || alongLast < lastRunMin)
            return null;
        Walk(path, first, alongFirst);
        Walk(path, last, alongLast);
        return path;
    }

    // 지선 칸은 기존 경로 칸이 아니고, 기존 경로와 상하좌우로 닿지 않는다 (진입 칸만 합류 지점에 닿는다).
    static bool BranchIsClear(List<Vector2Int> branch, HashSet<Vector2Int> occupied, Vector2Int junction, int size)
    {
        var entry = branch[branch.Count - 1];
        foreach (var c in branch)
        {
            if (!InBounds(c, size) || occupied.Contains(c))
                return false;
            foreach (var dir in Dirs)
            {
                var n = c + dir;
                if (occupied.Contains(n) && !(c == entry && n == junction))
                    return false;
            }
        }
        return true;
    }

    // 지선 스타트 지점 후보: 출구 변(위)을 뺀 세 변, 꼭짓점 제외 (기준 방향 좌표).
    static IEnumerable<Vector2Int> BranchSpawnCells(int size)
    {
        for (int i = 1; i < size - 1; i++)
        {
            yield return new Vector2Int(i, 0);
            yield return new Vector2Int(0, i);
            yield return new Vector2Int(size - 1, i);
        }
    }

    static Vector2Int InwardNormal(Vector2Int cell, int size)
    {
        if (cell.y == 0) return Vector2Int.up;
        if (cell.x == 0) return Vector2Int.right;
        if (cell.x == size - 1) return Vector2Int.left;
        return Vector2Int.down;
    }

    static bool TooClose(Vector2Int cell, List<Vector2Int> others, int minDistance)
    {
        foreach (var o in others)
            if (Manhattan(cell, o) < minDistance)
                return true;
        return false;
    }

    static void Walk(List<Vector2Int> path, Vector2Int dir, int steps)
    {
        var cur = path[path.Count - 1];
        for (int s = 0; s < steps; s++) { cur += dir; path.Add(cur); }
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

    static void Shuffle<T>(List<T> list, System.Random rng)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
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

    // ---------- 규칙 검사 ----------

    // 경로 1개 (예전 API): 스폰 1개 맵으로 검사.
    public static bool Validate(List<Vector2Int> path, int size, out string reason) =>
        ValidatePaths(new List<List<Vector2Int>> { path }, size, MapLayoutParams.Default, out reason);

    // 스타트 지점별 몬스터 경로(스폰 → 출구) 전체 검사. paths[0] = 본선.
    //   A: 스폰·출구는 변 위(꼭짓점 제외), 본선 스폰은 출구의 반대 변, 지선 스폰은 출구 변이 아님, 스폰끼리 거리
    //   B: 끊김 없음, 직진 MinRun 이상 후 꺾기, 꼭짓점에서 꺾지 않음, 본선은 출구 쪽으로만 / 지선은 합류 지점 쪽으로만,
    //      꺾임 수 (본선 ≤ MainTurnsMax, 지선 ≤ BranchMaxTurns), 지선은 본선 직선 구간에 수직으로 합류한 뒤 본선을 그대로 따라감
    //   B1: 상하좌우로 맞닿은 경로 칸은 어느 경로에서든 바로 이어지는 칸이어야 한다 (합류 지점 외 접촉 금지)
    //   N1: 넥서스는 스타트 지점이 몰린 변의 반대쪽. 비율이 비슷하면 본선이 최대 길이(2N−3칸)로 돌아가고 스타트 지점은 넥서스에서 N−1칸 이상
    public static bool ValidatePaths(IList<List<Vector2Int>> paths, int size, MapLayoutParams p, out string reason)
    {
        reason = null;
        if (paths == null || paths.Count == 0 || paths[0] == null || paths[0].Count < 2) { reason = "경로가 비어 있음"; return false; }
        int minRun = p.MinRun > 0 ? p.MinRun : MinRunLength;

        var main = paths[0];
        var spawn0 = main[0];
        var exit = main[main.Count - 1];
        if (!IsBorder(spawn0, size) || IsCorner(spawn0, size)) { reason = $"스폰 위치 오류 {spawn0}"; return false; }
        if (!IsBorder(exit, size) || IsCorner(exit, size)) { reason = $"출구 위치 오류 {exit}"; return false; }
        if (!OnOppositeEdges(spawn0, exit, size)) { reason = "출구가 첫 스폰의 반대편 변에 있지 않음"; return false; }
        if (!CheckRun(main, size, minRun, out int mainTurns, out reason, "본선"))
            return false;
        if (mainTurns > p.MainTurnsMax) { reason = $"본선 꺾임 {mainTurns}회 > {p.MainTurnsMax}"; return false; }

        var mainIndex = new Dictionary<Vector2Int, int>();
        for (int i = 0; i < main.Count; i++)
            mainIndex[main[i]] = i;

        var spawns = new List<Vector2Int> { spawn0 };
        for (int k = 1; k < paths.Count; k++)
        {
            var path = paths[k];
            if (path == null || path.Count < 2) { reason = $"경로 {k} 비어 있음"; return false; }
            var spawn = path[0];
            if (path[path.Count - 1] != exit) { reason = $"경로 {k}의 끝이 출구가 아님"; return false; }
            if (!IsBorder(spawn, size) || IsCorner(spawn, size)) { reason = $"스폰 {k} 위치 오류 {spawn}"; return false; }
            if (SameEdge(spawn, exit, size)) { reason = $"스폰 {k}가 출구 변에 있음 {spawn}"; return false; }
            if (TooClose(spawn, spawns, p.SpawnMinDistance)) { reason = $"스폰 {k}가 다른 스폰과 가까움 {spawn}"; return false; }
            spawns.Add(spawn);

            int q = 0;
            while (q < path.Count && !mainIndex.ContainsKey(path[q]))
                q++;
            if (q == 0 || q >= path.Count) { reason = $"경로 {k}가 본선에 합류하지 않음"; return false; }
            int j = mainIndex[path[q]];
            if (path.Count - q != main.Count - j) { reason = $"경로 {k}가 합류 뒤 본선을 벗어남"; return false; }
            for (int i = 0; i < path.Count - q; i++)
                if (path[q + i] != main[j + i]) { reason = $"경로 {k}가 합류 뒤 본선을 벗어남 {path[q + i]}"; return false; }
            if (j < 2 || j > main.Count - 3) { reason = $"경로 {k} 합류 지점이 본선 끝에 너무 가까움"; return false; }
            var mainDir = main[j + 1] - main[j];
            if (main[j] - main[j - 1] != mainDir) { reason = $"경로 {k}가 꺾이는 칸에 합류 {main[j]}"; return false; }
            if (Dot(path[q] - path[q - 1], mainDir) != 0) { reason = $"경로 {k}가 수직으로 합류하지 않음 {main[j]}"; return false; }

            var prefix = path.GetRange(0, q + 1); // 스폰 → 합류 지점
            if (!CheckRun(prefix, size, minRun, out int branchTurns, out reason, $"지선 {k}"))
                return false;
            if (branchTurns > p.BranchMaxTurns) { reason = $"지선 {k} 꺾임 {branchTurns}회 > {p.BranchMaxTurns}"; return false; }
        }

        // N1: 넥서스 방향
        var pull = SpawnPull(spawns, size);
        int px = Math.Abs(pull.x), py = Math.Abs(pull.y);
        if (px != py)
        {
            bool okSide = px > py
                ? (pull.x > 0 ? exit.x == size - 1 : exit.x == 0)
                : (pull.y > 0 ? exit.y == size - 1 : exit.y == 0);
            if (!okSide) { reason = $"넥서스 {exit}가 스타트 지점이 몰린 변의 반대쪽이 아님"; return false; }
        }
        else
        {
            if (main.Count < 2 * size - 3) { reason = $"스타트 지점 비율이 비슷한데 본선이 최대로 돌아가지 않음 ({main.Count}칸 < {2 * size - 3})"; return false; }
            foreach (var s in spawns)
                if (Manhattan(s, exit) < size - 1) { reason = $"스타트 지점 {s}가 넥서스와 가까움"; return false; }
        }

        // B1: 이어지지 않은 경로 칸끼리 맞닿음 금지
        var edges = new HashSet<long>();
        var cells = new HashSet<Vector2Int>();
        foreach (var path in paths)
            for (int i = 0; i < path.Count; i++)
            {
                cells.Add(path[i]);
                if (i > 0)
                {
                    edges.Add(EdgeKey(path[i - 1], path[i], size));
                    edges.Add(EdgeKey(path[i], path[i - 1], size));
                }
            }
        foreach (var c in cells)
            foreach (var d in Dirs)
            {
                var n = c + d;
                if (cells.Contains(n) && !edges.Contains(EdgeKey(c, n, size))) { reason = $"경로 접촉 {c}-{n}"; return false; }
            }
        return true;
    }

    // 경로 하나의 모양: 끊김 없음, 그리드 안, 마지막 칸 쪽으로만 전진, 직진 minRun 이상 후 꺾기, 꼭짓점에서 꺾지 않음.
    static bool CheckRun(List<Vector2Int> path, int size, int minRun, out int turns, out string reason, string label)
    {
        reason = null;
        turns = 0;
        var target = path[path.Count - 1];
        int runLength = 0;
        Vector2Int prevDir = Vector2Int.zero;
        if (!InBounds(path[0], size)) { reason = $"{label} 그리드 밖 {path[0]}"; return false; }
        for (int i = 1; i < path.Count; i++)
        {
            var c = path[i];
            if (!InBounds(c, size)) { reason = $"{label} 그리드 밖 {c}"; return false; }
            var dir = c - path[i - 1];
            if (Manhattan(dir, Vector2Int.zero) != 1) { reason = $"{label} 끊긴 경로 {path[i - 1]}→{c}"; return false; }
            if (Manhattan(c, target) >= Manhattan(path[i - 1], target)) { reason = $"{label} 역주행 {c}"; return false; }
            if (prevDir != Vector2Int.zero && dir != prevDir)
            {
                if (runLength < minRun) { reason = $"{label} 직진 {runLength}칸 후 회전 {path[i - 1]}"; return false; }
                if (IsCorner(path[i - 1], size)) { reason = $"{label} 꼭짓점에서 회전 {path[i - 1]}"; return false; }
                turns++;
                runLength = 0;
            }
            runLength++;
            prevDir = dir;
        }
        if (runLength < minRun) { reason = $"{label} 마지막 직진 {runLength}칸"; return false; }
        return true;
    }

    // ---------- 부지 분류 ----------

    // 경로/스폰/출구를 찍고 나머지 칸을 타워 부지 등급으로 분류한다 (모든 경로의 직선 구간 기준).
    static void ClassifyTiles(MapLayout layout)
    {
        int size = layout.Size;
        var segmentOf = new Dictionary<Vector2Int, int>();
        int segment = 0;
        AddSegments(layout.Path, segmentOf, ref segment);
        foreach (var branch in layout.Branches)
        {
            segment++;
            AddSegments(branch, segmentOf, ref segment);
        }

        foreach (var c in segmentOf.Keys)
            layout.Tiles[c.x, c.y] = TileType.Path;
        layout.Tiles[layout.Spawn.x, layout.Spawn.y] = TileType.Spawn;
        foreach (var branch in layout.Branches)
            layout.Tiles[branch[0].x, branch[0].y] = TileType.Spawn;
        layout.Tiles[layout.Exit.x, layout.Exit.y] = TileType.Exit;

        var touched = new HashSet<int>();
        for (int x = 0; x < size; x++)
        {
            for (int y = 0; y < size; y++)
            {
                var cell = new Vector2Int(x, y);
                if (segmentOf.ContainsKey(cell))
                    continue;
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

    // 직선 구간 번호. 꺾이는 칸은 들어온 쪽 구간에 속한다.
    static void AddSegments(List<Vector2Int> path, Dictionary<Vector2Int, int> segmentOf, ref int segment)
    {
        if (!segmentOf.ContainsKey(path[0]))
            segmentOf[path[0]] = segment;
        for (int i = 1; i < path.Count; i++)
        {
            if (i >= 2 && path[i] - path[i - 1] != path[i - 1] - path[i - 2])
                segment++;
            if (!segmentOf.ContainsKey(path[i]))
                segmentOf[path[i]] = segment;
        }
    }

    // 사각지대 제로: 모든 경로 타일이 4방향 중 하나 이상 타워 부지와 맞닿아야 한다.
    static bool HasNoBlindSpot(MapLayout layout)
    {
        if (!Covered(layout.Path, layout))
            return false;
        foreach (var branch in layout.Branches)
            if (!Covered(branch, layout))
                return false;
        return true;
    }

    static bool Covered(List<Vector2Int> path, MapLayout layout)
    {
        foreach (var p in path)
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

    // ---------- 도우미 ----------

    static long EdgeKey(Vector2Int a, Vector2Int b, int size) => ((long)(a.x * size + a.y) << 32) | (uint)(b.x * size + b.y);

    static int Dot(Vector2Int a, Vector2Int b) => a.x * b.x + a.y * b.y;

    static int Manhattan(Vector2Int a, Vector2Int b) => Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y);

    static bool InBounds(Vector2Int p, int size) => p.x >= 0 && p.y >= 0 && p.x < size && p.y < size;

    static bool IsBorder(Vector2Int p, int size) => p.x == 0 || p.y == 0 || p.x == size - 1 || p.y == size - 1;

    static bool IsCorner(Vector2Int p, int size) =>
        (p.x == 0 || p.x == size - 1) && (p.y == 0 || p.y == size - 1);

    // 같은 변 위에 있는지 (출구 변에 지선 스폰 금지).
    static bool SameEdge(Vector2Int a, Vector2Int b, int size)
    {
        int n = size - 1;
        return (a.y == 0 && b.y == 0) || (a.y == n && b.y == n) || (a.x == 0 && b.x == 0) || (a.x == n && b.x == n);
    }

    static bool OnOppositeEdges(Vector2Int a, Vector2Int b, int size)
    {
        int n = size - 1;
        return (a.y == 0 && b.y == n) || (a.y == n && b.y == 0)
            || (a.x == 0 && b.x == n) || (a.x == n && b.x == 0);
    }
}
