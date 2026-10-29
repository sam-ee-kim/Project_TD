using UnityEditor;
using UnityEngine;

namespace TowerDefence.EditorTools
{
    // 화염 계열 2단계 타워 이펙트 (메뉴: TowerDefence/Build Flame Tier2 FX). 모두 파티클이라 스크립트 Update가 없다.
    // 블렌더에서 만든 불꽃 조각 모델(Assets/Models/FX/FX_PlasmaBeam · FX_Phosphorus · FX_Dust)을 메시 파티클로 뿜는다.
    // 층 구성·색·속도·퍼짐 각도는 모델과 함께 온 TowerFlameFX.cs 설계값 그대로이고, 길이만 각 타워 사거리에 맞춰 줄인다
    // (설계 사거리 → 게임 사거리 비율 k로 속도·크기·중력을 같이 줄여 모양을 유지).
    // 화염방사 타워처럼 한 번 발사(0.2초) 동안 이어서 뿜어 연달아 쏘면 끊기지 않는 불길이 된다.
    // 프리팹 원점 = 노즐, +Z = 뿜는 방향. 불길 길이 = 각 타워의 coneLength → coneFxReach도 같은 값 (크기 1배).
    //   플라즈마: 흰-하늘 줄기 + 파란 플라즈마 덩어리 + 끝에서 주황으로 식는 끝 덩어리, 가운데 떨리는 빔
    //   혼합백린: 주황 화염 + 하얗게 타며 처지는 백린 덩어리 + 짙은 흰 연기
    //   혼합분진: 짧고 넓은 화염 + 흙빛 분진 알갱이 + 튀는 스파크
    public static partial class MuzzleFxBuilder
    {
        public const string DustSprayPath = PrefabFolder + "/FX_DustSpray.prefab";
        public const string PhosphorusSprayPath = PrefabFolder + "/FX_PhosphorusSpray.prefab";
        public const string PlasmaJetPath = PrefabFolder + "/FX_PlasmaJet.prefab";

        const string PlasmaModelPath = "Assets/Models/FX/FX_PlasmaBeam.fbx";
        const string PhosphorusModelPath = "Assets/Models/FX/FX_Phosphorus.fbx";
        const string DustModelPath = "Assets/Models/FX/FX_Dust.fbx";
        const string ChunkMatPath = ArtFolder + "/FX_ChunkUnlit.mat";      // 정점색(파티클 색)을 곱하는 불투명 Unlit
        const string GlowMatPath = ArtFolder + "/FX_AdditiveSolid.mat";    // 텍스처 없는 가산 (빔)
        const string StreakMeshPath = ArtFolder + "/FX_StreakMesh.asset";
        const string SparkMeshPath = ArtFolder + "/FX_SparkMesh.asset";
        const string BeamMeshPath = ArtFolder + "/FX_BeamTubeMesh.asset";

        // 히트박스(TowerDef.coneAngle / coneLength) = 이펙트가 실제로 퍼지는 폭과 길이
        public const float DustArc = 60f, DustReach = 1.6f;
        public const float PhosphorusArc = 40f, PhosphorusReach = 1.8f;
        public const float PlasmaArc = 0f, PlasmaReach = 3f; // 플라즈마는 일직선 (AttackType.Line, 3칸)
        // 모델 설계 사거리 (TowerFlameFX.Range)
        const float PlasmaDesignRange = 6f, PhosphorusDesignRange = 3.6f, DustDesignRange = 2.4f;
        // 플라즈마 굵기 (길이는 그대로 3칸): 빔 바깥 지름 = 타일 1칸의 1/3, 플라즈마 덩어리·줄기 크기·퍼지는 폭은 설계값 ×4
        const float PlasmaThickness = 4f;
        const float PlasmaBeamDiameter = 1f / 3f, PlasmaCoreDiameter = 1f / 6f;

        // 한 층: mesh = 모델 안 메시 이름 또는 "#streak"/"#spark"/"#beam" (여기서 만든 메시)
        struct ChunkLayer
        {
            public string Mesh;
            public float Rate, LifeMin, LifeMax, SpeedMin, SpeedMax, SizeMin, SizeMax, Angle, Radius, Gravity, Drag;
            public Color C0, C1, C2;     // 수명에 따른 색 (시작 / 중간 / 끝)
            public bool Grow;            // 커지며 사라짐(연기) / 줄어들며 사라짐(불꽃)
            public bool AlignLocal, AlignVelocity, Local;
        }

        static Color Rgb(float r, float g, float b) => new Color(r, g, b, 1f);

        [MenuItem("TowerDefence/Build Flame Tier2 FX")]
        public static void BuildFlameTier2()
        {
            EnsureFolder("Assets/Art");
            EnsureFolder(ArtFolder);
            EnsureFolder("Assets/Prefabs");
            EnsureFolder(PrefabFolder);
            var chunk = BuildMaterial(ChunkMatPath, null, false);
            chunk.SetFloat("_Surface", 0f); // 불투명: 조각은 단단한 저폴리 덩어리
            chunk.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
            chunk.SetOverrideTag("RenderType", "Opaque");
            chunk.SetFloat("_ZWrite", 1f);
            chunk.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.Zero);
            chunk.SetFloat("_DstBlendAlpha", (float)UnityEngine.Rendering.BlendMode.Zero);
            chunk.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.One);
            chunk.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Geometry;
            var glow = BuildMaterial(GlowMatPath, null, true);
            var streak = SaveMesh(StreakMeshPath, StreakMesh());
            var spark = SaveMesh(SparkMeshPath, BoxMesh(0.022f, 0.022f, 0.16f));
            var beam = SaveMesh(BeamMeshPath, TubeMesh(0.5f, 0.42f, 10));

            Save("FX_PlasmaJet", PlasmaJetPath, r => BuildChunkFx(r, PlasmaModelPath, PlasmaLayers(), PlasmaReach / PlasmaDesignRange, chunk, streak, spark, PlasmaThickness));
            Save("FX_PhosphorusSpray", PhosphorusSprayPath, r => BuildChunkFx(r, PhosphorusModelPath, PhosphorusLayers(), PhosphorusReach / PhosphorusDesignRange, chunk, streak, spark));
            Save("FX_DustSpray", DustSprayPath, r => BuildChunkFx(r, DustModelPath, DustLayers(), DustReach / DustDesignRange, chunk, streak, spark));
            var plasma = PrefabUtility.LoadPrefabContents(PlasmaJetPath);
            try
            {
                AddPlasmaBeam(plasma, beam, glow, PlasmaReach);
                PrefabUtility.SaveAsPrefabAsset(plasma, PlasmaJetPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(plasma);
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[MuzzleFxBuilder] 화염 계열 2단계 이펙트 3종 생성 (블렌더 조각 모델)");
        }

        // ---------- 층 설계 (TowerFlameFX.cs 값, 설계 사거리 R 기준) ----------

        static ChunkLayer[] PlasmaLayers()
        {
            const float R = PlasmaDesignRange;
            return new[]
            {
                // 토치처럼: 흰-하늘 코어 → 파란 몸통 → 끝에서 주황
                new ChunkLayer { Mesh = "#streak", Rate = 260, LifeMin = 0.22f, LifeMax = 0.3f, SpeedMin = R / 0.3f, SpeedMax = R / 0.26f, SizeMin = 0.7f, SizeMax = 1f, Angle = 1.2f, Radius = 0.03f,
                    C0 = Rgb(0.95f, 0.98f, 1f), C1 = Rgb(0.55f, 0.8f, 1f), C2 = Rgb(0.2f, 0.45f, 1f), AlignLocal = true, Local = true },
                new ChunkLayer { Mesh = "FX_Plasma_Chunk", Rate = 170, LifeMin = 0.28f, LifeMax = 0.4f, SpeedMin = R / 0.42f, SpeedMax = R / 0.36f, SizeMin = 0.45f, SizeMax = 0.8f, Angle = 2.5f, Radius = 0.05f, Drag = 0.3f,
                    C0 = Rgb(0.45f, 0.7f, 1f), C1 = Rgb(0.15f, 0.4f, 1f), C2 = Rgb(0.45f, 0.25f, 0.95f) },
                new ChunkLayer { Mesh = "FX_Plasma_Tip", Rate = 70, LifeMin = 0.35f, LifeMax = 0.45f, SpeedMin = R / 0.46f, SpeedMax = R / 0.4f, SizeMin = 0.5f, SizeMax = 0.9f, Angle = 3.5f, Radius = 0.06f, Drag = 0.6f, Gravity = -0.05f,
                    C0 = Rgb(0.2f, 0.45f, 1f), C1 = Rgb(0.35f, 0.45f, 1f), C2 = Rgb(1f, 0.55f, 0.25f) },
            };
        }

        static ChunkLayer[] PhosphorusLayers()
        {
            return new[]
            {
                // 주황 화염 + 곳곳에 하얗게 타는 백린 덩어리 + 짙은 흰 연기
                new ChunkLayer { Mesh = "FX_WP_Flame", Rate = 200, LifeMin = 0.35f, LifeMax = 0.55f, SpeedMin = 6.5f, SpeedMax = 8f, SizeMin = 0.5f, SizeMax = 1.1f, Angle = 13f, Radius = 0.08f, Drag = 0.6f, Gravity = -0.05f,
                    C0 = Rgb(1f, 0.85f, 0.4f), C1 = Rgb(1f, 0.5f, 0.1f), C2 = Rgb(0.55f, 0.12f, 0.04f) },
                new ChunkLayer { Mesh = "FX_WP_Phosphor", Rate = 70, LifeMin = 0.5f, LifeMax = 0.9f, SpeedMin = 5f, SpeedMax = 7.5f, SizeMin = 0.6f, SizeMax = 1.1f, Angle = 16f, Radius = 0.06f, Drag = 0.4f, Gravity = 0.6f,
                    C0 = Rgb(1f, 1f, 0.95f), C1 = Rgb(1f, 1f, 0.85f), C2 = Rgb(1f, 0.8f, 0.45f) },
                new ChunkLayer { Mesh = "FX_WP_Smoke", Rate = 45, LifeMin = 0.9f, LifeMax = 1.4f, SpeedMin = 2f, SpeedMax = 3.2f, SizeMin = 0.7f, SizeMax = 1.3f, Angle = 20f, Radius = 0.12f, Drag = 0.8f, Gravity = -0.12f,
                    C0 = Rgb(0.95f, 0.95f, 0.95f), C1 = Rgb(0.85f, 0.86f, 0.87f), C2 = Rgb(0.7f, 0.71f, 0.72f), Grow = true },
            };
        }

        static ChunkLayer[] DustLayers()
        {
            return new[]
            {
                // 짧고 넓은 화염 + 곳곳에 섞인 분진 알갱이 + 튀는 스파크
                new ChunkLayer { Mesh = "FX_Dust_Flame", Rate = 230, LifeMin = 0.25f, LifeMax = 0.4f, SpeedMin = 6f, SpeedMax = 7.5f, SizeMin = 0.6f, SizeMax = 1.2f, Angle = 22f, Radius = 0.1f, Drag = 0.9f, Gravity = -0.05f,
                    C0 = Rgb(1f, 0.75f, 0.3f), C1 = Rgb(1f, 0.38f, 0.08f), C2 = Rgb(0.45f, 0.1f, 0.05f) },
                new ChunkLayer { Mesh = "FX_Dust_Mote", Rate = 170, LifeMin = 0.3f, LifeMax = 0.5f, SpeedMin = 5f, SpeedMax = 7f, SizeMin = 0.7f, SizeMax = 1.4f, Angle = 26f, Radius = 0.1f, Drag = 0.7f, Gravity = 0.25f,
                    C0 = Rgb(0.42f, 0.36f, 0.28f), C1 = Rgb(0.33f, 0.28f, 0.22f), C2 = Rgb(0.25f, 0.21f, 0.17f) },
                new ChunkLayer { Mesh = "#spark", Rate = 70, LifeMin = 0.15f, LifeMax = 0.3f, SpeedMin = 8f, SpeedMax = 11f, SizeMin = 0.7f, SizeMax = 1.1f, Angle = 26f, Radius = 0.08f, Drag = 0.4f, Gravity = 0.3f,
                    C0 = Rgb(1f, 0.95f, 0.6f), C1 = Rgb(1f, 0.8f, 0.3f), C2 = Rgb(1f, 0.5f, 0.15f), AlignVelocity = true },
            };
        }

        // ---------- 구성 ----------

        // 첫 층이 루트 시스템 (자식까지 함께 재생). k = 게임 사거리 ÷ 설계 사거리
        // thickness = 크기·퍼지는 폭만 키우는 배율 (속도·길이는 k 그대로)
        static void BuildChunkFx(GameObject root, string modelPath, ChunkLayer[] layers, float k, Material mat, Mesh streak, Mesh spark, float thickness = 1f)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            if (model == null)
                Debug.LogError("[MuzzleFxBuilder] " + modelPath + " 없음");
            for (int i = 0; i < layers.Length; i++)
            {
                var go = i == 0 ? root : Child(root, layers[i].Mesh.TrimStart('#'));
                var mesh = layers[i].Mesh == "#streak" ? streak : layers[i].Mesh == "#spark" ? spark : FindMesh(model, layers[i].Mesh);
                AddChunkLayer(go, layers[i], k, mat, mesh, thickness);
            }
        }

        static void AddChunkLayer(GameObject go, ChunkLayer l, float k, Material mat, Mesh mesh, float thickness)
        {
            var ps = AddSystem(go, mat, ParticleSystemRenderMode.Mesh, l.Local ? ParticleSystemSimulationSpace.Local : ParticleSystemSimulationSpace.World);
            var main = ps.main;
            main.duration = 0.2f; // 발사 1번 = 0.2초 동안 뿜기
            main.startLifetime = new ParticleSystem.MinMaxCurve(l.LifeMin, l.LifeMax);
            main.startSpeed = new ParticleSystem.MinMaxCurve(l.SpeedMin * k, l.SpeedMax * k);
            main.startSize = new ParticleSystem.MinMaxCurve(l.SizeMin * k * thickness, l.SizeMax * k * thickness);
            main.gravityModifier = l.Gravity * k;
            main.maxParticles = Mathf.CeilToInt(l.Rate * 0.2f * 1.3f) + 2;
            if (!l.AlignLocal && !l.AlignVelocity)
            {
                main.startRotation3D = true;
                main.startRotationX = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
                main.startRotationY = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
                main.startRotationZ = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
                var rot = ps.rotationOverLifetime;
                rot.enabled = true;
                rot.separateAxes = true;
                rot.x = new ParticleSystem.MinMaxCurve(-4f, 4f);
                rot.y = new ParticleSystem.MinMaxCurve(-4f, 4f);
                rot.z = new ParticleSystem.MinMaxCurve(-4f, 4f);
            }
            Stream(ps, l.Rate);
            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = l.Angle;
            shape.radius = l.Radius * k * thickness;
            shape.radiusThickness = 1f;
            if (l.Drag > 0f)
            {
                var limit = ps.limitVelocityOverLifetime;
                limit.enabled = true;
                limit.limit = 100f;
                limit.drag = l.Drag;
            }

            var color = ps.colorOverLifetime;
            color.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(l.C0, 0f), new GradientColorKey(l.C1, 0.45f), new GradientColorKey(l.C2, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            color.color = g;
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, l.Grow
                ? new AnimationCurve(new Keyframe(0f, 0.5f), new Keyframe(0.6f, 1.1f), new Keyframe(1f, 0f))
                : new AnimationCurve(new Keyframe(0f, 0.7f), new Keyframe(0.25f, 1f), new Keyframe(1f, 0f)));

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.mesh = mesh;
            renderer.alignment = l.AlignVelocity ? ParticleSystemRenderSpace.Velocity : l.AlignLocal ? ParticleSystemRenderSpace.Local : ParticleSystemRenderSpace.World;
        }

        // 플라즈마 빔: 노즐에서 사거리 끝까지 뻗는 가는 관 2겹(흰 심 + 파란 빛). 발사 1번에 1개, 굵기가 떨린다.
        static void AddPlasmaBeam(GameObject root, Mesh tube, Material mat, float reach)
        {
            AddBeamPart(Child(root, "BeamCore"), tube, mat, Rgb(0.9f, 0.96f, 1f), PlasmaCoreDiameter, reach);
            AddBeamPart(Child(root, "BeamGlow"), tube, mat, Rgb(0.2f, 0.45f, 1f), PlasmaBeamDiameter, reach * 0.97f);
        }

        static void AddBeamPart(GameObject go, Mesh tube, Material mat, Color color, float width, float length)
        {
            var ps = AddSystem(go, mat, ParticleSystemRenderMode.Mesh, ParticleSystemSimulationSpace.Local);
            var main = ps.main;
            main.duration = 0.2f;
            main.startLifetime = 0.22f; // 다음 발사와 살짝 겹쳐 끊기지 않게
            main.startSpeed = 0f;
            main.startSize3D = true;
            main.startSizeX = width;
            main.startSizeY = width;
            main.startSizeZ = length;
            main.startColor = color;
            main.maxParticles = 2;
            Burst(ps, 1, 1);
            NoShape(ps);
            var size = ps.sizeOverLifetime; // 떨림: 굵기만 출렁인다
            size.enabled = true;
            size.separateAxes = true;
            var wobble = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.17f, 1.25f), new Keyframe(0.33f, 0.85f),
                new Keyframe(0.5f, 1.2f), new Keyframe(0.67f, 0.9f), new Keyframe(0.83f, 1.15f), new Keyframe(1f, 0.6f));
            size.x = new ParticleSystem.MinMaxCurve(1f, wobble);
            size.y = new ParticleSystem.MinMaxCurve(1f, wobble);
            size.z = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Constant(0f, 1f, 1f));
            go.GetComponent<ParticleSystemRenderer>().mesh = tube;
            go.GetComponent<ParticleSystemRenderer>().alignment = ParticleSystemRenderSpace.Local;
        }

        static Mesh FindMesh(GameObject model, string name)
        {
            if (model != null)
                foreach (var mf in model.GetComponentsInChildren<MeshFilter>(true))
                    if (mf.name == name)
                        return mf.sharedMesh;
            Debug.LogWarning("[MuzzleFxBuilder] 조각 메시 없음: " + name);
            return null;
        }

        static Mesh SaveMesh(string path, Mesh mesh)
        {
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(mesh, path);
                return mesh;
            }
            existing.Clear();
            existing.SetVertices(mesh.vertices);
            existing.SetTriangles(mesh.triangles, 0);
            existing.SetColors(mesh.colors);
            existing.SetNormals(mesh.normals);
            existing.RecalculateBounds();
            Object.DestroyImmediate(mesh);
            EditorUtility.SetDirty(existing);
            return existing;
        }

        // +Z 방향으로 길쭉한 마름모 (플라즈마 줄기)
        static Mesh StreakMesh()
        {
            const float L = 0.5f, w = 0.06f;
            var v = new[] { new Vector3(0, 0, L / 2), new Vector3(0, 0, -L / 2), new Vector3(w, 0, 0), new Vector3(-w, 0, 0), new Vector3(0, w, 0), new Vector3(0, -w, 0) };
            var t = new[] { 0, 2, 4, 0, 4, 3, 0, 3, 5, 0, 5, 2, 1, 4, 2, 1, 3, 4, 1, 5, 3, 1, 2, 5 };
            var m = new Mesh { name = "FX_Streak", vertices = v, triangles = t, colors = White(v.Length) };
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        // 가는 막대 (튀는 스파크)
        static Mesh BoxMesh(float x, float y, float z)
        {
            var v = new Vector3[8];
            for (int i = 0; i < 8; i++)
                v[i] = new Vector3((i & 1) == 0 ? -x / 2 : x / 2, (i & 2) == 0 ? -y / 2 : y / 2, (i & 4) == 0 ? -z / 2 : z / 2);
            var t = new[] { 0, 2, 1, 1, 2, 3, 4, 5, 6, 5, 7, 6, 0, 1, 4, 1, 5, 4, 2, 6, 3, 3, 6, 7, 0, 4, 2, 2, 4, 6, 1, 3, 5, 3, 7, 5 };
            var m = new Mesh { name = "FX_Spark", vertices = v, triangles = t, colors = White(v.Length) };
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        // 원점(노즐)에서 +Z로 길이 1 뻗는 관 (지름 1 → 끝 0.84). 파티클 크기 X·Y = 지름, Z = 사거리
        static Mesh TubeMesh(float r0, float r1, int n)
        {
            var v = new Vector3[n * 2];
            var t = new int[n * 6];
            for (int i = 0; i < n; i++)
            {
                float a = Mathf.PI * 2f * i / n;
                v[i * 2] = new Vector3(Mathf.Cos(a) * r0, Mathf.Sin(a) * r0, 0f);
                v[i * 2 + 1] = new Vector3(Mathf.Cos(a) * r1, Mathf.Sin(a) * r1, 1f);
                int p = i * 2, q = (i + 1) % n * 2;
                t[i * 6] = p; t[i * 6 + 1] = q; t[i * 6 + 2] = p + 1;
                t[i * 6 + 3] = q; t[i * 6 + 4] = q + 1; t[i * 6 + 5] = p + 1;
            }
            var m = new Mesh { name = "FX_Beam", vertices = v, triangles = t, colors = White(v.Length) };
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        static Color[] White(int count)
        {
            var c = new Color[count];
            for (int i = 0; i < count; i++)
                c[i] = Color.white;
            return c;
        }
    }
}
