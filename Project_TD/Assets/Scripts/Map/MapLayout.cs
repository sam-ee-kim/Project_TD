using System.Collections.Generic;
using UnityEngine;

public enum TileType
{
    Empty,      // 경로와 맞닿지 않은 빈 땅
    Path,
    Spawn,
    Exit,       // 넥서스
    TowerT1,    // 경로와 맞닿은 1선 부지
    TowerStar,  // 서로 다른 경로 선분 2개 이상에 닿는 명당 부지
}

// 생성 결과. 좌표는 그리드 기준 (x, y), 0 ~ Size-1.
// 본선 = 첫 스타트 지점 → 출구. 지선 = 다른 스타트 지점 → 본선 합류 직전 칸 (합류 칸은 본선에 속한다).
public class MapLayout
{
    public readonly int Size;
    public readonly TileType[,] Tiles;
    public readonly List<Vector2Int> Path;            // 본선: 스폰 → 출구 순서
    public readonly List<List<Vector2Int>> Branches;  // 지선: 스폰 → 합류 직전 칸
    public readonly List<int> JunctionIndex;          // 지선마다 합류하는 본선 칸 번호
    public readonly int TurnCount;                    // 본선 꺾임 수

    public Vector2Int Spawn => Path[0];
    public Vector2Int Exit => Path[Path.Count - 1];
    public int SpawnCount => 1 + Branches.Count;

    public MapLayout(int size, List<Vector2Int> path, int turnCount)
        : this(size, path, turnCount, new List<List<Vector2Int>>(), new List<int>())
    {
    }

    public MapLayout(int size, List<Vector2Int> path, int turnCount, List<List<Vector2Int>> branches, List<int> junctionIndex)
    {
        Size = size;
        Tiles = new TileType[size, size];
        Path = path;
        TurnCount = turnCount;
        Branches = branches;
        JunctionIndex = junctionIndex;
    }

    public TileType this[Vector2Int p] => Tiles[p.x, p.y];

    // 스타트 지점 k의 몬스터 경로 (스폰 → 출구 전체). 0 = 본선, 1~ = 지선 + 합류 지점부터의 본선.
    public List<Vector2Int> MonsterPath(int k)
    {
        if (k == 0)
            return new List<Vector2Int>(Path);
        var branch = Branches[k - 1];
        var path = new List<Vector2Int>(branch);
        for (int i = JunctionIndex[k - 1]; i < Path.Count; i++)
            path.Add(Path[i]);
        return path;
    }

    public static bool IsTower(TileType t) =>
        t == TileType.TowerT1 || t == TileType.TowerStar;
}
