using System.Collections.Generic;
using UnityEngine;

namespace TowerDefence
{
    // 맵 생성 출력: 게임이 쓰는 맵 정보 전부 (GDD 8-2). Unity 객체를 담지 않는 순수 데이터.
    // Vector2Int는 값 타입 구조체라 백그라운드 스레드에서 만들어도 안전하다.
    public class MapData
    {
        public int N;
        public TileType[,] Tiles;
        public SiteGrade[,] Sites;
        public Vector2Int[] Spawns;
        public Vector2Int Exit;
        public List<Vector2Int> Junctions;
        public List<List<Vector2Int>> MonsterPaths; // 스폰별 "스폰 → 출구" 전체 칸 목록 = 경로 칸 번호의 기준
        public int Seed;                            // 재현용
    }
}
