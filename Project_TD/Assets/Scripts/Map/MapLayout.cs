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
public class MapLayout
{
    public readonly int Size;
    public readonly TileType[,] Tiles;
    public readonly List<Vector2Int> Path; // 스폰 → 출구 순서
    public readonly int TurnCount;

    public Vector2Int Spawn => Path[0];
    public Vector2Int Exit => Path[Path.Count - 1];

    public MapLayout(int size, List<Vector2Int> path, int turnCount)
    {
        Size = size;
        Tiles = new TileType[size, size];
        Path = path;
        TurnCount = turnCount;
    }

    public TileType this[Vector2Int p] => Tiles[p.x, p.y];

    public static bool IsTower(TileType t) =>
        t == TileType.TowerT1 || t == TileType.TowerStar;
}
