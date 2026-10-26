using System.Collections.Generic;
using UnityEngine;
using TowerDefence;
using TdTileType = TowerDefence.TileType;

// 맵 3D 표시: Board에 올라간 MapData를 3D 타일 모델로 바닥에 깔고, 타일이 아래에서 "뿅뿅" 솟아오르며 조립되게 한다.
// 맵 데이터는 GameLoop → MapProvider(정본 생성기) → Board가 만들고, 이 컴포넌트는 그리기만 한다 (GameUI.MapView가 호출).
// 타일은 저마다 다른 시간에 등장해 같은 속도로 솟아오르고, 살짝 튀어 올랐다가 눌리며 자리를 잡는다.
// 그리드 (x, y) → 월드 (X, Z). 맵 중앙이 이 오브젝트 위치, 타일 윗면이 이 오브젝트의 높이에 온다.
// (Board.CellToWorld와 같은 좌표가 되도록 이 오브젝트는 원점, tileSize는 1로 둔다.)
public class MapGenerator : MonoBehaviour
{
    [Header("타일 모델 (Assets/Models/Tiles, 피벗은 바닥 중앙)")]
    [SerializeField] GameObject pathTilePrefab;        // 흙길: 경로·스폰·넥서스
    [SerializeField] GameObject buildableTilePrefab;   // 잔디: 타워 설치 가능
    [SerializeField] GameObject[] blockedTilePrefabs;  // 잔디+나무: 타워 설치 불가, 무작위로 하나 선택
    [SerializeField] float tileSize = 1f;              // 타일 한 칸의 가로·세로 길이
    [SerializeField] float tileHeight = 0.4f;          // 타일 바닥에서 윗면까지 높이
    [SerializeField] bool randomRotateGrass = true;    // 잔디 타일을 90도 단위로 무작위 회전

    [Header("스폰·넥서스 표시 색 (흙길 타일에 덮어씀)")]
    [SerializeField] Color spawnColor = new Color(1f, 0.6f, 0.6f);
    [SerializeField] Color exitColor = new Color(1f, 0.6f, 0.6f);

    [Header("행성 (맵 아래, 자전)")]
    [SerializeField] Transform planet;                    // 행성 루트: 행성·대기·달이 함께 돈다 (달은 공전처럼 보인다)
    [SerializeField] Transform planetClouds;              // 구름 층: 행성 자전에 더해 조금 더 돌아 흘러가 보이게
    [SerializeField] Vector3 planetAxis = Vector3.up; // 자전축 = 행성 자체의 극축 (로컬). 행성을 45° 기울여 두었으니 축도 함께 45° 기울어 있다
    [SerializeField] float planetSpinDegPerSec = 1.2f;    // 자전 속도 (도/초)
    [SerializeField] float cloudSpinDegPerSec = 0.9f;     // 구름 추가 회전 (도/초, 구름 자체 축)

    [Header("경로 미리보기 (라운드 시작 전, 셰이더 TowerDefence/PathPreview)")]
    [SerializeField] Material pathPreviewMaterial;

    [Header("생성 연출 (타일이 아래에서 솟아오름)")]
    [SerializeField] float startDepth = 10f;       // 시작 높이: 최종 위치보다 이만큼 아래
    [SerializeField] float riseSpeed = 30f;        // 모든 타일 공통 상승 속도
    [SerializeField] Vector2 startDelayRange = new Vector2(0f, 1.5f); // 타일별 등장 시간
    [SerializeField] float overshootHeight = 0.3f; // 최종 위치보다 살짝 더 튀어 오르는 높이
    [SerializeField] float settleDuration = 0.35f; // 튀어 오른 뒤 출렁이며 자리 잡는 시간
    [SerializeField] float stretchAmount = 0.3f;   // 올라올 때 늘어나고 착지할 때 눌리는 정도

    class RisingTile
    {
        public GameObject prefab;      // 등장 시간이 되면 이 모델을 만든다 (한 프레임에 몰아서 만들지 않도록)
        public string name;
        public Vector3 localPosition;  // 최종 위치
        public Quaternion localRotation;
        public Color? tint;            // 스폰·넥서스 색
        public Transform transform;    // 등장 전에는 null
        public float targetY;
        public Vector3 baseScale;
        public float delay;
        public float settleTime = -1f; // 0 이상이면 착지(출렁임) 단계
    }

    MapData map;

    public MapData Map => map;
    public int GridSize => map != null ? map.N : 0;
    public Material PathPreviewMaterial => pathPreviewMaterial;

    // 타일이 아직 올라오는 중이면 true. 이 동안 카메라는 탑다운뷰로 고정된다.
    public bool IsBuilding => risingTiles.Count > 0;

    // 맵 한 변의 월드 길이.
    public Vector2 WorldExtent => new Vector2(GridSize * tileSize, GridSize * tileSize);

    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

    readonly List<RisingTile> risingTiles = new List<RisingTile>();
    System.Random visualRng; // 나무 종류·회전·등장 시간 선택용 (같은 맵 seed면 같은 모습)

    // 성능 (GDD 8-5, 작업 10 측정):
    //  - 착지한 타일은 CombineChunk개씩 모아 정적 배칭한다 (37×37 = 1369타일 × 서브메시 3~4개 드로우 → 수십 개).
    //    다 내려앉은 타일만 합치므로 연출은 그대로다. 메시가 Read/Write 가능해야 한다 (ProjectSetup이 켠다).
    //  - 맵 교체 때 이전 타일을 한 프레임에 Destroy하면 Tier 전환 프레임이 길어지므로,
    //    이전 맵 루트를 숨기기만 하고 프레임당 RetirePerFrame개씩 나눠 지운다 (합친 메시도 함께 해제).
    const int CombineChunk = 128;
    const int RetirePerFrame = 64;

    Transform mapRoot;                                                  // 지금 맵의 타일 부모
    readonly List<GameObject> retiring = new List<GameObject>();        // 숨긴 이전 맵 루트 (나눠 지우는 중)
    readonly List<Mesh> combinedMeshes = new List<Mesh>();              // 지금 맵의 정적 배칭 합친 메시
    readonly List<Mesh> retiringMeshes = new List<Mesh>();              // 이전 맵의 합친 메시 (루트를 다 지운 뒤 해제)
    readonly GameObject[] combineBuffer = new GameObject[CombineChunk];
    int combineCount;
    bool combineEnabled;

    // 최적화 규칙(GDD 8-5 #1): Update는 GameLoop 하나만. GameUI.MapView가 매 프레임 Tick(dt)을 부른다.
    public void Tick(float dt)
    {
        AnimateRise(dt);
        RetireStep();
        SpinPlanet(dt);
    }

    // 행성 자전 (MapView.Tick → 매 프레임). 행성 자체의 극축(로컬)으로 돈다. 행성이 기울어 있으니 극도 기운 채로 돈다.
    void SpinPlanet(float dt)
    {
        if (planet != null && planetSpinDegPerSec != 0f)
            planet.Rotate(planetAxis.normalized, planetSpinDegPerSec * dt, Space.Self);
        if (planetClouds != null && cloudSpinDegPerSec != 0f)
            planetClouds.Rotate(Vector3.up, cloudSpinDegPerSec * dt, Space.Self);
    }

    // 맵 교체: 이전 타일을 숨기고(나눠 지움) 새 맵의 타일을 모두 땅속에 예약한 뒤 차례로 솟아오르게 한다.
    public void Show(MapData newMap)
    {
        Clear();
        map = newMap;
        if (map == null)
            return;

        if (pathTilePrefab == null || buildableTilePrefab == null || blockedTilePrefabs == null || blockedTilePrefabs.Length == 0)
        {
            Debug.LogError("[MapGenerator] 타일 모델이 지정되지 않았습니다. Inspector에서 타일 프리팹을 넣어 주세요.");
            return;
        }

        mapRoot = new GameObject("Tiles").transform;
        mapRoot.SetParent(transform, false);
        combineCount = 0;
        combineEnabled = AllMeshesReadable();

        visualRng = new System.Random(map.Seed);
        for (int x = 0; x < map.N; x++)
            for (int y = 0; y < map.N; y++)
                ReserveTile(x, y, map.Tiles[x, y]);
    }

    void AnimateRise(float dt)
    {
        for (int i = risingTiles.Count - 1; i >= 0; i--)
        {
            var tile = risingTiles[i];
            if (tile.delay > 0f)
            {
                tile.delay -= dt;
                continue;
            }
            if (tile.transform == null)
                Appear(tile);

            var pos = tile.transform.localPosition;
            float peakY = tile.targetY + overshootHeight;

            if (tile.settleTime < 0f)
            {
                // 상승: 모든 타일이 같은 속도로, 세로로 늘어난 채 최종 위치를 살짝 넘어서까지.
                pos.y = Mathf.Min(pos.y + riseSpeed * dt, peakY);
                tile.transform.localPosition = pos;
                tile.transform.localScale = PopScale(tile.baseScale, 1f);
                if (pos.y >= peakY)
                    tile.settleTime = 0f;
                continue;
            }

            // 착지: 제자리로 내려앉으며 눌렸다 튀었다를 반복하다 멈춘다 (뿅).
            tile.settleTime += dt;
            float p = Mathf.Clamp01(tile.settleTime / settleDuration);
            float ease = 1f - (1f - p) * (1f - p) * (1f - p);
            pos.y = Mathf.Lerp(peakY, tile.targetY, ease);
            tile.transform.localPosition = pos;
            tile.transform.localScale = PopScale(tile.baseScale, Mathf.Cos(p * Mathf.PI * 3f) * (1f - p));

            if (p >= 1f)
            {
                tile.transform.localScale = tile.baseScale;
                risingTiles.RemoveAt(i);
                if (!tile.tint.HasValue) // 색을 덮어쓴 스폰·넥서스 타일은 따로 둔다
                    AddToCombine(tile.transform.gameObject);
            }
        }
        if (risingTiles.Count == 0)
            FlushCombine();
    }

    void AddToCombine(GameObject go)
    {
        if (!combineEnabled)
            return;
        combineBuffer[combineCount++] = go;
        if (combineCount == CombineChunk)
        {
            StaticBatchingUtility.Combine(combineBuffer, mapRoot.gameObject);
            RememberCombined(combineBuffer, CombineChunk);
            combineCount = 0;
        }
    }

    // 합친 메시는 맵을 지울 때 해제해야 하므로 기록해 둔다 (원본 에셋 메시는 제외).
    void RememberCombined(GameObject[] gos, int count)
    {
        for (int i = 0; i < count; i++)
        {
            var mf = gos[i].GetComponentInChildren<MeshFilter>();
            var mesh = mf != null ? mf.sharedMesh : null;
            if (mesh != null && mesh.name.StartsWith("Combined Mesh") && !combinedMeshes.Contains(mesh))
                combinedMeshes.Add(mesh);
        }
    }

    void FlushCombine()
    {
        if (combineCount == 0)
            return;
        var rest = new GameObject[combineCount];
        System.Array.Copy(combineBuffer, rest, combineCount);
        StaticBatchingUtility.Combine(rest, mapRoot.gameObject);
        RememberCombined(rest, rest.Length);
        combineCount = 0;
    }

    bool AllMeshesReadable()
    {
        if (!IsReadable(pathTilePrefab) || !IsReadable(buildableTilePrefab))
            return false;
        foreach (var prefab in blockedTilePrefabs)
            if (!IsReadable(prefab))
                return false;
        return true;
    }

    static bool IsReadable(GameObject prefab)
    {
        foreach (var mf in prefab.GetComponentsInChildren<MeshFilter>(true))
            if (mf.sharedMesh == null || !mf.sharedMesh.isReadable)
            {
                Debug.LogWarning($"[MapGenerator] '{prefab.name}' 메시가 Read/Write 꺼짐: 정적 배칭 없이 그린다 (TowerDefence/Setup Project로 켤 수 있음).");
                return false;
            }
        return true;
    }

    // 숨긴 이전 맵 타일을 프레임당 RetirePerFrame개씩 지운다. 루트가 비면 합친 메시까지 해제.
    void RetireStep()
    {
        if (retiring.Count == 0)
            return;
        var root = retiring[0].transform;
        int n = root.childCount;
        if (n == 0)
        {
            Destroy(root.gameObject);
            retiring.RemoveAt(0);
            if (retiring.Count == 0)
            {
                foreach (var mesh in retiringMeshes)
                    Destroy(mesh);
                retiringMeshes.Clear();
            }
            return;
        }
        for (int i = n - 1; i >= 0 && i >= n - RetirePerFrame; i--)
        {
            var child = root.GetChild(i);
            child.SetParent(null, false); // childCount가 바로 줄도록 떼어 낸 뒤 지운다
            Destroy(child.gameObject);
        }
    }

    // k > 0: 세로로 늘어남, k < 0: 납작하게 눌림, k = 0: 원래 크기.
    Vector3 PopScale(Vector3 baseScale, float k)
    {
        float y = 1f + stretchAmount * k;
        float xz = 1f - stretchAmount * 0.5f * k;
        return new Vector3(baseScale.x * xz, baseScale.y * y, baseScale.z * xz);
    }

    // 그리드 좌표 → 타일 윗면 중심의 월드 좌표 (맵 중앙이 이 오브젝트 위치).
    public Vector3 GridToWorld(Vector2Int p)
    {
        float half = (GridSize - 1) * 0.5f;
        return transform.TransformPoint(new Vector3((p.x - half) * tileSize, 0f, (p.y - half) * tileSize));
    }

    // 타일 하나의 모델·위치·회전·등장 시간을 정해 둔다. 실제 모델은 등장 시간이 되면 만든다.
    void ReserveTile(int x, int y, TdTileType type)
    {
        bool isGrass = type != TdTileType.Path && type != TdTileType.Spawn && type != TdTileType.Exit;
        // 피벗이 바닥이므로 tileHeight만큼 내려 윗면을 y=0에 맞춘다.
        var finalPos = transform.InverseTransformPoint(GridToWorld(new Vector2Int(x, y)) + Vector3.down * tileHeight);

        var tile = new RisingTile
        {
            prefab = PrefabOf(type),
            name = $"Tile_{x}_{y}_{type}",
            localPosition = finalPos,
            localRotation = isGrass && randomRotateGrass ? Quaternion.Euler(0f, 90f * visualRng.Next(4), 0f) : Quaternion.identity,
            tint = type == TdTileType.Spawn ? spawnColor : type == TdTileType.Exit ? exitColor : (Color?)null,
            targetY = finalPos.y,
            baseScale = Vector3.one * tileSize,
            delay = Mathf.Lerp(startDelayRange.x, startDelayRange.y, (float)visualRng.NextDouble()),
        };
        risingTiles.Add(tile);
    }

    // 등장 시간이 된 타일의 모델을 만들어 땅속(startDepth 아래)에 놓는다.
    // 물리 엔진(Collider) 사용 금지 규칙에 따라 콜라이더는 두지 않는다 (칸 판정은 Board 좌표 계산).
    void Appear(RisingTile tile)
    {
        var go = Instantiate(tile.prefab, mapRoot, false);
        go.name = tile.name;
        var t = go.transform;
        t.localRotation = tile.localRotation;
        t.localScale = tile.baseScale;
        var pos = tile.localPosition;
        pos.y -= startDepth;
        t.localPosition = pos;

        // 평평한 바닥 타일(흙길·잔디)은 그림자를 드리우지 않는다 (받기만 함). 나무·장식 타일만 그림자를 만든다.
        if (tile.prefab == pathTilePrefab || tile.prefab == buildableTilePrefab)
        {
            var flat = go.GetComponentInChildren<Renderer>();
            if (flat != null)
                flat.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        if (tile.tint.HasValue)
        {
            var block = new MaterialPropertyBlock();
            block.SetColor(BaseColorId, tile.tint.Value);
            foreach (var r in go.GetComponentsInChildren<Renderer>())
                r.SetPropertyBlock(block);
        }
        tile.transform = t;
    }

    GameObject PrefabOf(TdTileType type)
    {
        switch (type)
        {
            case TdTileType.Path:
            case TdTileType.Spawn:
            case TdTileType.Exit:
                return pathTilePrefab;
            case TdTileType.Empty:
                return blockedTilePrefabs[visualRng.Next(blockedTilePrefabs.Length)];
            default:
                return buildableTilePrefab; // BuildSite (T1, T★)
        }
    }

    void Clear()
    {
        risingTiles.Clear();
        combineCount = 0;
        if (mapRoot == null)
            return;
        retiringMeshes.AddRange(combinedMeshes);
        combinedMeshes.Clear();
        mapRoot.gameObject.SetActive(false); // 한 번에 숨기고 지우기는 RetireStep이 나눠서
        retiring.Add(mapRoot.gameObject);
        mapRoot = null;
    }
}
