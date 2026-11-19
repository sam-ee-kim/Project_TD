using UnityEngine;

namespace TowerDefence
{
    // 맵 타일 모델 묶음 (Data/TileThemes). 풀밭(흙길·잔디·나무)과 기계(시멘트 길·합판·건물)처럼 같은 칸 종류에 다른 모델을 쓴다.
    // MapGenerator.UseTheme으로 바꾼다 (다음 Show부터). 지금 게임은 풀밭만 쓴다 — 기계 테마는 준비만 해 둔 상태.
    [CreateAssetMenu(menuName = "TowerDefence/Tile Theme")]
    public class TileTheme : ScriptableObject
    {
        public string displayName;
        public GameObject pathTile;        // 경로·스폰·넥서스
        public GameObject buildableTile;   // 타워 설치 가능
        public GameObject[] blockedTiles;  // 설치 불가 장식 (무작위로 하나)
    }
}
