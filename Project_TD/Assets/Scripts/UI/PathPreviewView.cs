using System.Collections.Generic;
using UnityEngine;

namespace TowerDefence
{
    // 라운드 시작 전 몬스터 경로 미리보기 (GameUI.MapView가 소유, Update 없음).
    //   - 스폰 → 넥서스 경로를 따라 바닥에서 살짝 뜬 어두운 빨간 사각 레이저 (단면이 정사각형인 막대를 직선 구간마다)
    //   - 2초마다 밝은 빨간 빛이 스폰에서 넥서스 쪽으로 흐른다 (셰이더 TowerDefence/PathPreview, 시간은 Tick이 넘김)
    //   - 스폰 위치는 더 큰 사각형 (불투명도 70%)
    // 지선은 합류 지점까지만 그린다 (합류 뒤는 본선 선과 겹치므로). 메시는 맵이 바뀔 때 1번 만든다.
    public class PathPreviewView
    {
        const float Height = 0.16f;        // 타일 윗면(y = 0)에서 띄운 높이 (레이저 중심)
        const float Thickness = 0.12f;     // 레이저 단면 한 변
        const float SpawnSize = 0.82f;     // 스폰 표시 사각형 한 변
        const float SpawnThickness = 0.05f;
        const float SpawnAlpha = 0.7f;     // 스폰 표시 불투명도

        static readonly int PulseTimeId = Shader.PropertyToID("_PulseTime");

        readonly List<Vector3> vertices = new List<Vector3>();
        readonly List<Vector2> uvs = new List<Vector2>();
        readonly List<Color32> colors = new List<Color32>();
        readonly List<int> triangles = new List<int>();

        GameObject root;
        Mesh mesh;
        Material material;
        Board board;
        float time;

        public bool Visible => root != null && root.activeSelf;
        public int VertexCount => vertices.Count;

        // material = 씬에서 연결한 PathPreview 재질 (없으면 셰이더 이름으로 찾는다). 에디터 모드(단위 테스트)에서는 만들지 않는다.
        public void Init(Board board, Material template)
        {
            this.board = board;
            if (!Application.isPlaying)
                return;
            if (template == null)
            {
                var shader = Shader.Find("TowerDefence/PathPreview");
                if (shader == null)
                {
                    Debug.LogWarning("[PathPreviewView] PathPreview 셰이더를 찾지 못해 경로 미리보기를 끕니다.");
                    return;
                }
                template = new Material(shader);
            }
            material = new Material(template) { name = "PathPreview (instance)" };
            root = new GameObject("PathPreview");
            mesh = new Mesh { name = "PathPreview" };
            root.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = root.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            root.SetActive(false);
        }

        // 맵이 바뀔 때: 모든 스타트 지점의 경로 선과 스폰 사각형을 다시 만든다.
        public void Build(MapData map)
        {
            if (root == null || map == null || map.MonsterPaths == null || map.MonsterPaths.Count == 0)
                return;
            vertices.Clear();
            uvs.Clear();
            colors.Clear();
            triangles.Clear();

            var main = map.MonsterPaths[0];
            var mainCells = new HashSet<Vector2Int>(main);
            AddPath(main, main.Count);
            for (int k = 1; k < map.MonsterPaths.Count; k++)
            {
                var path = map.MonsterPaths[k];
                int q = 0;
                while (q < path.Count && !mainCells.Contains(path[q]))
                    q++;
                AddPath(path, Mathf.Min(q + 1, path.Count)); // 합류 지점까지
            }
            foreach (var spawn in map.Spawns)
            {
                var center = board.CellToWorld(spawn) + Vector3.up * Height;
                AddBox(center, Vector3.right, Vector3.up, Vector3.forward,
                    new Vector3(SpawnSize * 0.5f, SpawnThickness * 0.5f, SpawnSize * 0.5f), 0f, 0f, SpawnAlpha);
            }

            mesh.Clear();
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetColors(colors);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
        }

        public void SetVisible(bool visible)
        {
            if (root == null || root.activeSelf == visible)
                return;
            root.SetActive(visible);
            if (visible)
                time = 0f; // 보이기 시작하면 첫 빛이 바로 스폰에서 출발
        }

        // 매 프레임 (MapView.Tick): 빛이 흐르는 시간만 넘긴다 (할당 없음).
        public void Tick(float dt)
        {
            if (root == null || !root.activeSelf)
                return;
            time += dt;
            material.SetFloat(PulseTimeId, time);
        }

        // 경로 앞 count칸을 직선 구간으로 나눠 막대를 붙인다. UV.x = 스폰부터 잰 거리(칸).
        void AddPath(List<Vector2Int> path, int count)
        {
            float distance = 0f;
            int start = 0;
            for (int i = 1; i < count; i++)
            {
                bool segmentEnd = i == count - 1 || path[i + 1] - path[i] != path[i] - path[i - 1];
                if (!segmentEnd)
                    continue;
                var a = board.CellToWorld(path[start]) + Vector3.up * Height;
                var b = board.CellToWorld(path[i]) + Vector3.up * Height;
                float length = i - start;
                var dir = (b - a).normalized;
                var right = Vector3.Cross(Vector3.up, dir);
                // 양 끝을 단면 절반만큼 늘려 꺾이는 곳이 빈틈 없이 이어지게
                AddBox((a + b) * 0.5f, right, Vector3.up, dir,
                    new Vector3(Thickness * 0.5f, Thickness * 0.5f, length * 0.5f + Thickness * 0.5f),
                    distance, distance + length, 1f);
                distance += length;
                start = i;
            }
        }

        // 축 3개로 놓인 상자 (면 6개, 바깥을 향하게). axisZ 방향으로 UV.x가 d0 → d1.
        void AddBox(Vector3 center, Vector3 axisX, Vector3 axisY, Vector3 axisZ, Vector3 half, float d0, float d1, float alpha)
        {
            var color = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(alpha * 255f));
            Vector3 X = axisX * half.x, Y = axisY * half.y, Z = axisZ * half.z;
            Vector3 C(int sx, int sy, int sz) => center + X * sx + Y * sy + Z * sz;
            float U(int sz) => sz < 0 ? d0 : d1;

            void Face(Vector3 normal, int[] sx, int[] sy, int[] sz)
            {
                int baseIndex = vertices.Count;
                for (int i = 0; i < 4; i++)
                {
                    vertices.Add(C(sx[i], sy[i], sz[i]));
                    uvs.Add(new Vector2(U(sz[i]), 0f));
                    colors.Add(color);
                }
                var v0 = vertices[baseIndex];
                var faceNormal = Vector3.Cross(vertices[baseIndex + 1] - v0, vertices[baseIndex + 2] - v0);
                if (Vector3.Dot(faceNormal, normal) >= 0f)
                    triangles.AddRange(new[] { baseIndex, baseIndex + 1, baseIndex + 2, baseIndex, baseIndex + 2, baseIndex + 3 });
                else
                    triangles.AddRange(new[] { baseIndex, baseIndex + 2, baseIndex + 1, baseIndex, baseIndex + 3, baseIndex + 2 });
            }

            Face(axisY, new[] { -1, 1, 1, -1 }, new[] { 1, 1, 1, 1 }, new[] { -1, -1, 1, 1 });      // 위
            Face(-axisY, new[] { -1, 1, 1, -1 }, new[] { -1, -1, -1, -1 }, new[] { -1, -1, 1, 1 }); // 아래
            Face(axisX, new[] { 1, 1, 1, 1 }, new[] { -1, 1, 1, -1 }, new[] { -1, -1, 1, 1 });      // 오른쪽
            Face(-axisX, new[] { -1, -1, -1, -1 }, new[] { -1, 1, 1, -1 }, new[] { -1, -1, 1, 1 }); // 왼쪽
            Face(axisZ, new[] { -1, 1, 1, -1 }, new[] { -1, -1, 1, 1 }, new[] { 1, 1, 1, 1 });      // 앞
            Face(-axisZ, new[] { -1, 1, 1, -1 }, new[] { -1, -1, 1, 1 }, new[] { -1, -1, -1, -1 }); // 뒤
        }
    }
}
