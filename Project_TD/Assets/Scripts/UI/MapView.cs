using UnityEngine;
using UnityEngine.Tilemaps;

namespace TowerDefence
{
    // 맵 3D 표시와 카메라, BuildMode 오버레이 (GDD 8-1 GameUI.MapView).
    // 그리기는 씬의 MapGenerator(3D 타일 모델이 아래에서 솟아오르는 연출), 카메라는 CameraController(탑다운/탑, Tab, WASD)가 한다.
    // 둘 다 Update가 없고 GameUI.Tick(7단계)에서 이 클래스를 통해 Tick된다.
    //
    // 오버레이: 맵과 별도의 Tilemap을 바닥(타일 윗면 바로 위)에 눕혀 칸마다 색을 칠한다 (GDD 5-3).
    // BuildMode 진입 때 1번 전체를 칠하고, 이후에는 바뀐 칸만 SetColor 한다 (GDD 8-5 #7: Hover 1회당 2칸 이하).
    public class MapView
    {
        const float OverlayHeight = 0.02f; // 타일 윗면(y = 0)보다 살짝 위

        // 칸 표시 색 (GDD 5-3). 설치 가능 칸의 "곱하기로 어둡게"는 검정 반투명으로 칠한다
        // (검정을 알파 a로 덮으면 바탕색 × (1 − a)와 같다).
        static readonly Color Darken = new Color(0f, 0f, 0f, 0.35f);
        static readonly Color HoverGreen = new Color(0.30f, 0.95f, 0.35f, 0.55f);
        static readonly Color SelectedBlue = new Color(0.30f, 0.55f, 1.00f, 0.65f);

        MapGenerator renderer;
        CameraController cameraRig;
        Board board;

        Tilemap overlay;
        Tile overlayTile;
        Color blocked;       // #E57373, 불투명도 0.28
        Color blockedHover;  // 마우스 올림 0.4
        bool overlayOn;
        int overlayN;
        bool hasHover, hasSelected;
        Vector2Int hover, selected;

        // 라운드 시작 전 몬스터 경로 미리보기. 타일이 다 솟아오른 뒤에 보인다.
        readonly PathPreviewView pathPreview = new PathPreviewView();
        // 사거리 그리드 (G키 / 설치 미리보기)
        readonly RangeGridView ranges = new RangeGridView();
        public RangeGridView Ranges => ranges;
        bool pathPreviewWanted;
        public PathPreviewView PathPreview => pathPreview;

        // 타일이 아직 솟아오르는 중인지 (이 동안 카메라는 탑다운뷰로 고정).
        public bool IsBuilding => renderer != null && renderer.IsBuilding;
        public bool OverlayVisible => overlayOn;

        // 오버레이 칸 색을 칠한 누적 횟수 (Hover 1회당 2칸 이하 확인용).
        public int OverlayCellWrites { get; private set; }

        public void Init(MapGenerator renderer, CameraController cameraRig, Board board, GameConfig.UiSettings ui)
        {
            this.renderer = renderer;
            this.cameraRig = cameraRig;
            this.board = board;
            if (renderer == null)
                Debug.LogWarning("[MapView] 씬에 MapGenerator(3D 맵 표시)가 연결되지 않았습니다.");

            var tint = ui != null ? ui.blockedTint : new Color(0xE5 / 255f, 0x73 / 255f, 0x73 / 255f);
            blocked = new Color(tint.r, tint.g, tint.b, ui != null ? ui.blockedAlpha : 0.28f);
            blockedHover = new Color(tint.r, tint.g, tint.b, ui != null ? ui.blockedHoverAlpha : 0.4f);
            CreateOverlay();
            pathPreview.Init(board, renderer != null ? renderer.PathPreviewMaterial : null);
            ranges.Init(board);
        }

        // 맵 교체 시 전체 다시 그리기: 이전 타일을 지우고 새 맵이 아래에서 솟아오른다.
        public void Rebuild(MapData map)
        {
            if (renderer != null)
                renderer.Show(map);
            if (overlayOn)
                PaintAll();
            pathPreview.SetVisible(false); // 새 타일이 다 올라온 뒤 다시 보인다
            pathPreview.Build(map);
            ranges.Rebuild();
        }

        // 라운드 시작 전(Ready·Break)에만 경로 미리보기를 켠다.
        public void SetPathPreviewWanted(bool wanted) => pathPreviewWanted = wanted;

        public void Tick(float dt)
        {
            if (renderer != null)
                renderer.Tick(dt);
            if (cameraRig != null)
                cameraRig.Tick(dt);
            pathPreview.SetVisible(pathPreviewWanted && board != null && board.Map != null && !IsBuilding);
            pathPreview.Tick(dt);
        }

        // BuildMode 오버레이 켜기/끄기. 켤 때 1번 전체를 칠한다.
        public void ShowBuildOverlay(bool show)
        {
            overlayOn = show;
            if (show)
            {
                PaintAll();
            }
            else
            {
                overlay.ClearAllTiles();
                overlayN = 0;
            }
            overlay.gameObject.SetActive(show);
        }

        // 마우스 올림 칸 변경: 이전 칸과 새 칸만 다시 칠한다 (최대 2칸).
        public void SetHover(bool has, Vector2Int cell)
        {
            bool hadOld = hasHover;
            var old = hover;
            hasHover = has;
            hover = cell;
            if (hadOld && (!has || old != cell))
                RefreshCell(old);
            if (has)
                RefreshCell(cell);
        }

        // 파란 선택 칸 변경.
        public void SetSelected(bool has, Vector2Int cell)
        {
            bool hadOld = hasSelected;
            var old = selected;
            hasSelected = has;
            selected = cell;
            if (hadOld && (!has || old != cell))
                RefreshCell(old);
            if (has)
                RefreshCell(cell);
        }

        // 한 칸의 오버레이만 갱신 (타워 설치·판매, Hover, Selected).
        public void RefreshCell(Vector2Int cell)
        {
            if (!overlayOn || !board.InBounds(cell))
                return;
            overlay.SetColor(new Vector3Int(cell.x, cell.y, 0), ColorOf(cell));
            OverlayCellWrites++;
        }

        // 맵 크기가 N이면 칸 (x, y) 중심이 월드 (x − (N−1)/2, ·, y − (N−1)/2) (Board 좌표와 같음).
        void PaintAll()
        {
            int n = board.N;
            if (n == 0)
                return;
            if (n != overlayN)
            {
                overlay.ClearAllTiles();
                var tiles = new TileBase[n * n];
                for (int i = 0; i < tiles.Length; i++)
                    tiles[i] = overlayTile;
                overlay.SetTilesBlock(new BoundsInt(0, 0, 0, n, n, 1), tiles);
                // Tilemap 셀 (x, y)는 로컬 (x .. x+1, y .. y+1). 바닥에 눕혀(X 90°) 맵 중앙에 맞춘다.
                overlay.transform.parent.position = new Vector3(-n * 0.5f, OverlayHeight, -n * 0.5f);
                overlayN = n;
            }
            for (int x = 0; x < n; x++)
                for (int y = 0; y < n; y++)
                    overlay.SetColor(new Vector3Int(x, y, 0), ColorOf(new Vector2Int(x, y)));
            OverlayCellWrites += n * n;
        }

        Color ColorOf(Vector2Int cell)
        {
            if (hasSelected && selected == cell)
                return SelectedBlue;
            bool hovered = hasHover && hover == cell;
            if (board.IsBuildable(cell))
                return hovered ? HoverGreen : Darken;
            // 설치 불가: 경로, 스폰, 넥서스, 이미 타워가 있는 칸, 부지가 아닌 칸
            return hovered ? blockedHover : blocked;
        }

        void CreateOverlay()
        {
            var gridGo = new GameObject("BuildOverlay");
            gridGo.transform.rotation = Quaternion.Euler(90f, 0f, 0f); // XY 격자를 바닥(XZ)에 눕힌다
            gridGo.AddComponent<Grid>();
            var tmGo = new GameObject("OverlayTilemap");
            tmGo.transform.SetParent(gridGo.transform, false);
            overlay = tmGo.AddComponent<Tilemap>();
            tmGo.AddComponent<TilemapRenderer>();

            var texture = new Texture2D(1, 1) { filterMode = FilterMode.Point, name = "OverlayWhite" };
            texture.SetPixel(0, 0, Color.white);
            texture.Apply();
            overlayTile = ScriptableObject.CreateInstance<Tile>();
            overlayTile.name = "OverlayCell";
            overlayTile.sprite = Sprite.Create(texture, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
            overlayTile.flags = TileFlags.None; // 칸마다 SetColor 가능
            overlayTile.colliderType = Tile.ColliderType.None;

            gridGo.SetActive(true);
            tmGo.SetActive(false);
        }
    }
}
