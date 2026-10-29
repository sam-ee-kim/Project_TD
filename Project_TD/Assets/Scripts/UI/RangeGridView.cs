using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace TowerDefence
{
    // 사거리 그리드 (GameUI.MapView 안): 타워가 공격하는 칸을 테두리 있는 칸으로 바닥에 칠한다.
    //   - G키: 설치된 모든 타워의 사거리 (주황)
    //   - 설치 중: 놓을 타워의 사거리와 방향 (하늘색, R키로 돌리면 바로 바뀐다)
    // 사거리 모양은 타겟팅과 같은 TowerFacing.InArea. 경로 칸은 진하게 (실제로 몬스터를 맞히는 칸).
    // 이벤트(설치·판매·이동, 미리보기, G키)가 올 때만 다시 칠한다 (매 프레임 칠하지 않음).
    public class RangeGridView
    {
        const float Height = 0.03f; // 설치 오버레이(0.02)보다 살짝 위
        static readonly Color Clear = new Color(0f, 0f, 0f, 0f);
        static readonly Color PlacedArea = new Color(1f, 0.62f, 0.2f, 0.35f);
        static readonly Color PlacedPath = new Color(1f, 0.55f, 0.15f, 0.8f);
        static readonly Color PreviewArea = new Color(0.3f, 0.85f, 1f, 0.45f);
        static readonly Color PreviewPath = new Color(0.35f, 0.9f, 1f, 0.95f);

        struct Placed
        {
            public Vector2Int Cell;
            public TowerDef Def;
            public int Facing;
        }

        Board board;
        Tilemap tilemap;
        Tile tile;
        int tilemapN;
        readonly List<Placed> towers = new List<Placed>();
        bool showPlaced;
        PlacementPreview preview;

        public bool ShowPlaced => showPlaced;
        public bool HasPreview => preview.Has;

        public void Init(Board board)
        {
            this.board = board;
            var gridGo = new GameObject("RangeGrid");
            gridGo.transform.rotation = Quaternion.Euler(90f, 0f, 0f); // XY 격자를 바닥(XZ)에 눕힌다
            gridGo.AddComponent<Grid>();
            var tmGo = new GameObject("RangeTilemap");
            tmGo.transform.SetParent(gridGo.transform, false);
            tilemap = tmGo.AddComponent<Tilemap>();
            tmGo.AddComponent<TilemapRenderer>().sortingOrder = 2; // 설치 오버레이(0)·드래그 그림자(1) 위
            tile = ScriptableObject.CreateInstance<Tile>();
            tile.name = "RangeCell";
            tile.sprite = CreateCellSprite();
            tile.flags = TileFlags.None; // 칸마다 SetColor
            tile.colliderType = Tile.ColliderType.None;
            tmGo.SetActive(false);
        }

        // 타워 설치·판매·이동 (TowerManager.TowerChanged).
        public void HandleTowerChanged(TowerChange e)
        {
            for (int i = towers.Count - 1; i >= 0; i--)
                if (towers[i].Cell == e.Cell)
                    towers.RemoveAt(i);
            if (e.Built)
                towers.Add(new Placed { Cell = e.Cell, Def = e.Def, Facing = e.Facing });
            if (showPlaced)
                Repaint();
        }

        public void SetShowPlaced(bool show)
        {
            showPlaced = show;
            Repaint();
        }

        public void SetPreview(PlacementPreview e)
        {
            preview = e;
            Repaint();
        }

        // 새 맵: 칸 수가 바뀌면 타일을 다시 깐다 (설치된 타워는 TowerChanged로 이미 빠져 있다).
        public void Rebuild()
        {
            tilemapN = 0;
            Repaint();
        }

        void Repaint()
        {
            int n = board.N;
            bool visible = n > 0 && (showPlaced || preview.Has);
            tilemap.gameObject.SetActive(visible);
            if (!visible)
                return;
            if (n != tilemapN)
            {
                tilemap.ClearAllTiles();
                var tiles = new TileBase[n * n];
                for (int i = 0; i < tiles.Length; i++)
                    tiles[i] = tile;
                tilemap.SetTilesBlock(new BoundsInt(0, 0, 0, n, n, 1), tiles);
                tilemap.transform.parent.position = new Vector3(-n * 0.5f, Height, -n * 0.5f);
                tilemapN = n;
            }
            for (int x = 0; x < n; x++)
                for (int y = 0; y < n; y++)
                    tilemap.SetColor(new Vector3Int(x, y, 0), Clear);
            if (showPlaced)
                for (int i = 0; i < towers.Count; i++)
                    Paint(towers[i].Def, towers[i].Cell, towers[i].Facing, PlacedArea, PlacedPath);
            if (preview.Has && preview.Def != null)
                Paint(preview.Def, preview.Cell, preview.Facing, PreviewArea, PreviewPath); // 놓을 타워가 위에
        }

        void Paint(TowerDef def, Vector2Int cell, int facing, Color area, Color path)
        {
            int n = board.N;
            int reach = Mathf.CeilToInt(Mathf.Max(def.range, Mathf.Max(def.coneLength, def.pulseRadius)) + 0.5f);
            for (int dx = -reach; dx <= reach; dx++)
                for (int dy = -reach; dy <= reach; dy++)
                {
                    var c = new Vector2Int(cell.x + dx, cell.y + dy);
                    if (c.x < 0 || c.y < 0 || c.x >= n || c.y >= n || !TowerFacing.InArea(def, facing, dx, dy))
                        continue;
                    tilemap.SetColor(new Vector3Int(c.x, c.y, 0), board.IsPathCell(c) ? path : area);
                }
        }

        // 칸 스프라이트: 안쪽은 옅게, 테두리는 진하게 (색은 칸마다 SetColor로 곱한다) → 칸이 격자로 구분된다.
        static Sprite CreateCellSprite()
        {
            const int size = 32, border = 2;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "RangeCell", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    bool edge = x < border || y < border || x >= size - border || y >= size - border;
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(edge ? 255 : 110));
                }
            texture.SetPixels32(pixels);
            texture.Apply();
            return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
        }
    }
}
