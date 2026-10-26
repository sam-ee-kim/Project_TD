using System.Collections.Generic;
using UnityEngine;

namespace TowerDefence
{
    // 타워 표시 메시 합치기 (작업 10 측정: 타워 1개 = 렌더러 27개 → 88개면 그림자 패스까지 드로우 콜 1만 개 이상).
    // 함께 움직이는 부품끼리(받침 / 회전판 / 목·머리 / 좌·우 반동) 메시를 하나로 합쳐 렌더러를 그룹 수(트윈캐논 5개)로 줄인다.
    // 그룹 = 각 렌더러에서 가장 가까운 "움직이는 축"(TowerAim의 yaw·pitch·반동 피벗, 없으면 루트).
    // 합친 메시는 타워 종류별로 1번만 만들어(Template) 같은 종류의 모든 타워가 공유한다. 원래 렌더러는 끄기만 한다
    // (Transform은 그대로라 조준·총구 위치·반동은 바뀌지 않는다). 메시가 Read/Write 가능해야 한다 (ProjectSetup이 켠다).
    public static class TowerViewMerger
    {
        public sealed class Template
        {
            public Group[] Groups;
            public string[] DisabledRenderers; // 루트 기준 경로
        }

        public sealed class Group
        {
            public string Path;                // 루트 기준 경로 ("" = 루트)
            public Mesh Mesh;
            public Material[] Materials;
        }

        // 막 만든(시작 자세) 표시에서 템플릿을 만든다. 합칠 수 없으면 null (원래 렌더러 그대로 사용).
        public static Template Build(GameObject view, TowerAim aim)
        {
            var root = view.transform;
            var pivots = new HashSet<Transform> { root };
            if (aim != null)
            {
                if (aim.yawPivot != null) pivots.Add(aim.yawPivot);
                if (aim.pitchPivot != null) pivots.Add(aim.pitchPivot);
                if (aim.recoilPivots != null)
                    foreach (var p in aim.recoilPivots)
                        if (p != null) pivots.Add(p);
                if (aim.animatedParts != null)
                    foreach (var p in aim.animatedParts)
                        if (p != null) pivots.Add(p);
            }

            // 그룹(피벗) → 재질 → 그 재질을 쓰는 서브메시 조각들
            var byGroup = new Dictionary<Transform, Dictionary<Material, List<CombineInstance>>>();
            var groupOrder = new List<Transform>();
            var disabled = new List<string>();
            foreach (var renderer in view.GetComponentsInChildren<MeshRenderer>(true))
            {
                var filter = renderer.GetComponent<MeshFilter>();
                var mesh = filter != null ? filter.sharedMesh : null;
                if (mesh == null)
                    continue;
                if (!mesh.isReadable)
                {
                    Debug.LogWarning($"[TowerViewMerger] '{view.name}' 메시가 Read/Write 꺼짐: 합치지 않고 그린다 (TowerDefence/Setup Project로 켤 수 있음).");
                    return null;
                }

                var group = renderer.transform;
                while (!pivots.Contains(group))
                    group = group.parent;
                if (!byGroup.TryGetValue(group, out var byMaterial))
                {
                    byMaterial = new Dictionary<Material, List<CombineInstance>>();
                    byGroup.Add(group, byMaterial);
                    groupOrder.Add(group);
                }

                var toGroup = group.worldToLocalMatrix * renderer.transform.localToWorldMatrix;
                var materials = renderer.sharedMaterials;
                for (int sub = 0; sub < mesh.subMeshCount; sub++)
                {
                    var material = materials[Mathf.Min(sub, materials.Length - 1)];
                    if (!byMaterial.TryGetValue(material, out var parts))
                        byMaterial.Add(material, parts = new List<CombineInstance>());
                    parts.Add(new CombineInstance { mesh = mesh, subMeshIndex = sub, transform = toGroup });
                }
                disabled.Add(PathOf(root, renderer.transform));
            }
            if (groupOrder.Count == 0)
                return null;

            var groups = new Group[groupOrder.Count];
            for (int g = 0; g < groupOrder.Count; g++)
            {
                var byMaterial = byGroup[groupOrder[g]];
                var pieces = new List<CombineInstance>();
                var mats = new List<Material>();
                foreach (var pair in byMaterial)
                {
                    // 같은 재질 조각을 서브메시 하나로
                    var piece = new Mesh { name = "piece" };
                    piece.CombineMeshes(pair.Value.ToArray(), true, true);
                    pieces.Add(new CombineInstance { mesh = piece, transform = Matrix4x4.identity });
                    mats.Add(pair.Key);
                }
                // 재질마다 서브메시 하나씩 = 그룹 메시 1개
                var merged = new Mesh { name = $"{view.name}_{groupOrder[g].name}_Merged" };
                merged.CombineMeshes(pieces.ToArray(), false, false);
                merged.RecalculateBounds();
                foreach (var piece in pieces)
                    DestroyMesh(piece.mesh);
                groups[g] = new Group { Path = PathOf(root, groupOrder[g]), Mesh = merged, Materials = mats.ToArray() };
            }
            return new Template { Groups = groups, DisabledRenderers = disabled.ToArray() };
        }

        // 표시 하나의 합친 렌더러 / 원래 렌더러. 건축 연출 동안은 부품이 따로 움직여야 하므로 원래 렌더러로 그린다.
        public sealed class Parts
        {
            public Renderer[] Merged;
            public Renderer[] Originals;
            public bool IsMerged { get; private set; } = true;

            public void SetMerged(bool merged)
            {
                if (IsMerged == merged)
                    return;
                IsMerged = merged;
                foreach (var r in Merged)
                    r.enabled = merged;
                foreach (var r in Originals)
                    r.enabled = !merged;
            }
        }

        // 표시 하나에 템플릿을 적용: 그룹마다 합친 메시 렌더러를 붙이고 원래 렌더러를 끈다.
        public static Parts Apply(GameObject view, Template template)
        {
            var root = view.transform;
            var merged = new List<Renderer>();
            var originals = new List<Renderer>();
            foreach (var group in template.Groups)
            {
                var parent = group.Path.Length == 0 ? root : root.Find(group.Path);
                var go = new GameObject("MergedMesh");
                go.transform.SetParent(parent, false);
                go.AddComponent<MeshFilter>().sharedMesh = group.Mesh;
                var renderer = go.AddComponent<MeshRenderer>();
                renderer.sharedMaterials = group.Materials;
                merged.Add(renderer);
            }
            foreach (var path in template.DisabledRenderers)
            {
                var t = path.Length == 0 ? root : root.Find(path);
                var renderer = t != null ? t.GetComponent<MeshRenderer>() : null;
                if (renderer == null)
                    continue;
                renderer.enabled = false;
                originals.Add(renderer);
            }
            return new Parts { Merged = merged.ToArray(), Originals = originals.ToArray() };
        }

        static void DestroyMesh(Mesh mesh)
        {
            if (Application.isPlaying) Object.Destroy(mesh); else Object.DestroyImmediate(mesh); // 에디터 모드 테스트에서도 동작
        }

        static string PathOf(Transform root, Transform t)
        {
            if (t == root)
                return "";
            string path = t.name;
            for (var p = t.parent; p != null && p != root; p = p.parent)
                path = p.name + "/" + path;
            return path;
        }
    }
}
