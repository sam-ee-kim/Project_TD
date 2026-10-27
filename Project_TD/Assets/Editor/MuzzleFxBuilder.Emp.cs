using System.IO;
using UnityEditor;
using UnityEngine;

namespace TowerDefence.EditorTools
{
    // EMP 타워 이펙트 (메뉴: TowerDefence/Build EMP FX).
    //   FX_EMPWave : 타워 발밑에서 원형으로 퍼지는 전자파. 바닥에 누운 빛 고리 + 고리를 따라 달리는 지지직 전기 줄기 + 불티 고리
    //   FX_EMPHit  : 파면에 닿은 몬스터에서 튀는 작은 전기
    // 파면 속도·반지름은 게임 판정(TowerDef.pulseSpeed·pulseRadius)과 같은 값(EmpWaveSpeed·EmpWaveRadius)으로 만든다.
    public static partial class MuzzleFxBuilder
    {
        public const string EmpWavePath = PrefabFolder + "/FX_EMPWave.prefab";
        public const string EmpHitPath = PrefabFolder + "/FX_EMPHit.prefab";
        public const float EmpWaveRadius = 2f;  // 칸 (= 월드 단위)
        public const float EmpWaveSpeed = 6f;   // 칸/초
        const string RingTexturePath = ArtFolder + "/FX_Ring.png";
        const string RingMatPath = ArtFolder + "/FX_RingAdditive.mat";
        const string RingBandTexturePath = ArtFolder + "/FX_RingBand.png";
        const string RingBandMatPath = ArtFolder + "/FX_RingBand.mat";

        static readonly Color EmpCore = new Color(0.86f, 0.92f, 1f, 1f);
        static readonly Color EmpGlow = new Color(0.5f, 0.64f, 1f, 1f);   // SF_EMP_Glow 쪽
        static readonly Color EmpDeep = new Color(0.45f, 0.38f, 1f, 1f);

        [MenuItem("TowerDefence/Build EMP FX")]
        public static void BuildEmp()
        {
            EnsureFolder("Assets/Art");
            EnsureFolder(ArtFolder);
            EnsureFolder("Assets/Prefabs");
            EnsureFolder(PrefabFolder);
            var dot = AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath);
            if (dot == null)
                dot = BuildSoftDotTexture();
            var mat = BuildMaterial(AdditiveMatPath, dot, true);
            var ringMat = BuildMaterial(RingMatPath, BuildRingTexture(RingTexturePath, 0.07f), true);
            // 밝은 땅(잔디·흙) 위에서도 보이게: 더하기가 아닌 반투명으로 덮는 짙은 파란 띠 (밝은 고리 밑에 깔린다)
            var bandMat = BuildMaterial(RingBandMatPath, BuildRingTexture(RingBandTexturePath, 0.14f), false);

            Save("FX_EMPWave", EmpWavePath, root => BuildEmpWave(root, mat, ringMat, bandMat));
            Save("FX_EMPHit", EmpHitPath, root => BuildEmpHit(root, mat));
            AssetDatabase.SaveAssets();
            Debug.Log("[MuzzleFxBuilder] EMP 이펙트 2종 생성");
        }

        // --- 전자파 (원점 = 타워 발밑 조금 위, 바닥 평면으로 퍼짐) ---
        static void BuildEmpWave(GameObject root, Material mat, Material ringMat, Material bandMat)
        {
            float travel = EmpWaveRadius / EmpWaveSpeed; // 파면이 끝까지 가는 시간

            // 1) 바닥에 누운 빛 고리: 0 → 반지름까지 커지며 지지직 깜빡이다 사라진다
            var ring = AddSystem(root, ringMat, ParticleSystemRenderMode.HorizontalBillboard, ParticleSystemSimulationSpace.Local);
            var main = ring.main;
            main.duration = travel + 0.1f;
            main.startLifetime = travel + 0.08f;
            main.startSpeed = 0f;
            main.startSize = (EmpWaveRadius + 0.25f) * 2f;
            main.startColor = EmpCore;
            main.maxParticles = 2;
            Burst(ring, 1, 1);
            NoShape(ring);
            var size = ring.sizeOverLifetime;
            size.enabled = true;
            var grow = AnimationCurve.Linear(0f, 0.04f, travel / (travel + 0.08f), 1f); // 파면과 같은 속도로 일정하게
            grow.AddKey(new Keyframe(1f, 1.03f));
            size.size = new ParticleSystem.MinMaxCurve(1f, grow);
            var color = ring.colorOverLifetime;
            color.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(EmpCore, 0f), new GradientColorKey(EmpGlow, 0.5f), new GradientColorKey(EmpDeep, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.45f, 0.12f), new GradientAlphaKey(1f, 0.24f), new GradientAlphaKey(0.5f, 0.38f),
                        new GradientAlphaKey(1f, 0.52f), new GradientAlphaKey(0.4f, 0.68f), new GradientAlphaKey(0.85f, 0.82f), new GradientAlphaKey(0f, 1f) });
            color.color = g; // 깜빡임 = 지지직
            ring.GetComponent<ParticleSystemRenderer>().sortingOrder = 2; // 짙은 띠 위
            AddElectricLight(root, ring, 6f);

            // 1-1) 짙은 파란 띠 (반투명): 밝은 바닥에서 고리의 윤곽을 잡아 준다
            var band = AddSystem(Child(root, "Band"), bandMat, ParticleSystemRenderMode.HorizontalBillboard, ParticleSystemSimulationSpace.Local);
            main = band.main;
            main.duration = travel + 0.1f;
            main.startLifetime = travel + 0.1f;
            main.startSpeed = 0f;
            main.startSize = (EmpWaveRadius + 0.25f) * 2f;
            main.startColor = new Color(0.16f, 0.24f, 0.95f, 0.8f);
            main.maxParticles = 2;
            Burst(band, 1, 1);
            NoShape(band);
            var bandSize = band.sizeOverLifetime;
            bandSize.enabled = true;
            bandSize.size = new ParticleSystem.MinMaxCurve(1f, grow);
            var bandColor = band.colorOverLifetime;
            bandColor.enabled = true;
            var bg = new Gradient();
            bg.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.9f, 0.7f), new GradientAlphaKey(0f, 1f) });
            bandColor.color = bg;
            band.GetComponent<ParticleSystemRenderer>().sortingOrder = 1;

            // 1-2) 메아리 고리: 조금 늦게 한 번 더 퍼져 "웅-웅" 두 겹 파동
            var echo = AddSystem(Child(root, "Echo"), ringMat, ParticleSystemRenderMode.HorizontalBillboard, ParticleSystemSimulationSpace.Local);
            main = echo.main;
            main.duration = travel + 0.2f;
            main.startDelay = 0.08f;
            main.startLifetime = travel + 0.08f;
            main.startSpeed = 0f;
            main.startSize = (EmpWaveRadius + 0.25f) * 2f;
            main.startColor = new Color(EmpGlow.r, EmpGlow.g, EmpGlow.b, 0.7f);
            main.maxParticles = 2;
            Burst(echo, 1, 1);
            NoShape(echo);
            var echoSize = echo.sizeOverLifetime;
            echoSize.enabled = true;
            echoSize.size = new ParticleSystem.MinMaxCurve(1f, grow);
            Fade(echo, EmpGlow, EmpDeep);
            echo.GetComponent<ParticleSystemRenderer>().sortingOrder = 2;

            // 1-3) 전자파 벽: 파면에서 위로 솟는 빛줄기 → 나무·타워 사이로도 보이는 높이
            var wall = AddSystem(Child(root, "Wall"), mat, ParticleSystemRenderMode.Stretch, ParticleSystemSimulationSpace.World);
            var wallRenderer = wall.GetComponent<ParticleSystemRenderer>();
            wallRenderer.velocityScale = 0.06f;
            wallRenderer.lengthScale = 2f;
            wallRenderer.sortingOrder = 3;
            main = wall.main;
            main.duration = 0.1f;
            main.startLifetime = travel;
            main.startSpeed = EmpWaveSpeed;
            main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.08f);
            main.startColor = EmpCore;
            main.maxParticles = 90;
            Burst(wall, 64, 72);
            FlatCircle(wall, 0.15f);
            var lift = wall.velocityOverLifetime;
            lift.enabled = true;
            lift.space = ParticleSystemSimulationSpace.World;
            lift.x = new ParticleSystem.MinMaxCurve(0f, 0f);
            lift.y = new ParticleSystem.MinMaxCurve(2.5f, 4.5f);
            lift.z = new ParticleSystem.MinMaxCurve(0f, 0f);
            Fade(wall, EmpCore, EmpGlow);

            // 2) 지지직 전기 줄기: 고리와 같은 속도로 바깥으로 달리며 노이즈로 꺾인 선을 남긴다
            var arcs = AddSystem(Child(root, "Crackle"), mat, ParticleSystemRenderMode.Billboard, ParticleSystemSimulationSpace.World);
            main = arcs.main;
            main.duration = 0.1f;
            main.startLifetime = travel;
            main.startSpeed = EmpWaveSpeed;
            main.startSize = new ParticleSystem.MinMaxCurve(0.02f, 0.035f);
            main.startColor = EmpCore;
            main.maxParticles = 90;
            Burst(arcs, 60, 70);
            FlatCircle(arcs, 0.15f);
            ElectricTrail(arcs, mat, 0.065f, 1f);
            arcs.GetComponent<ParticleSystemRenderer>().sortingOrder = 3;
            var trails = arcs.trails;
            trails.lifetime = 0.35f; // 짧은 꼬리 → 고리 근처에서만 지지직
            Noise(arcs, 2.4f, 10f);

            // 3) 불티 고리: 파면을 따라 점점이 빛나는 입자
            var sparks = AddSystem(Child(root, "Sparks"), mat, ParticleSystemRenderMode.Stretch, ParticleSystemSimulationSpace.World);
            sparks.GetComponent<ParticleSystemRenderer>().velocityScale = 0.025f;
            main = sparks.main;
            main.duration = 0.1f;
            main.startLifetime = travel;
            main.startSpeed = new ParticleSystem.MinMaxCurve(EmpWaveSpeed * 0.92f, EmpWaveSpeed);
            main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.05f);
            main.startColor = EmpCore;
            main.maxParticles = 120;
            Burst(sparks, 90, 110);
            FlatCircle(sparks, 0.15f);
            Fade(sparks, EmpCore, EmpGlow);

            // 4) 방출 순간 타워 발밑 섬광
            var flash = AddSystem(Child(root, "Flash"), mat, ParticleSystemRenderMode.Billboard, ParticleSystemSimulationSpace.Local);
            main = flash.main;
            main.duration = 0.1f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.08f, 0.12f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(1f, 1.3f);
            main.startColor = EmpCore;
            main.maxParticles = 2;
            Burst(flash, 1, 1);
            NoShape(flash);
            Fade(flash, EmpCore, EmpGlow);
            Shrink(flash, 1f, 0.4f);
        }

        // --- 맞은 몬스터의 지지직 ---
        static void BuildEmpHit(GameObject root, Material mat)
        {
            var flash = AddSystem(root, mat, ParticleSystemRenderMode.Billboard, ParticleSystemSimulationSpace.Local);
            var main = flash.main;
            main.duration = 0.15f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.06f, 0.1f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.5f, 0.65f);
            main.startColor = EmpCore;
            main.maxParticles = 2;
            Burst(flash, 1, 1);
            NoShape(flash);
            Fade(flash, EmpCore, EmpGlow);

            var arcs = AddSystem(Child(root, "Arcs"), mat, ParticleSystemRenderMode.Billboard, ParticleSystemSimulationSpace.World);
            main = arcs.main;
            main.duration = 0.15f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.08f, 0.16f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.2f, 2.2f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.01f, 0.02f);
            main.startColor = EmpCore;
            main.maxParticles = 12;
            Burst(arcs, 7, 9);
            Sphere(arcs, 0.08f);
            ElectricTrail(arcs, mat, 0.032f, 1f);
            Noise(arcs, 3f, 10f);
        }

        // 바닥에 눕힌 원 (가장자리에서 바깥으로 퍼짐)
        static void FlatCircle(ParticleSystem ps, float radius)
        {
            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = radius;
            shape.radiusThickness = 0f; // 가장자리에서
            shape.arc = 360f;
            shape.rotation = new Vector3(90f, 0f, 0f);
        }

        // 가운데가 비고 테두리가 밝은 고리 텍스처 (전자파 고리)
        static Texture2D BuildRingTexture(string path, float width)
        {
            const int size = 256;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f) / size * 2f - 1f;
                    float dy = (y + 0.5f) / size * 2f - 1f;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float edge = Mathf.Exp(-Mathf.Pow((d - 0.88f) / width, 2f));          // 밝은 테두리
                    float inner = Mathf.Clamp01((d - 0.35f) / 0.55f) * 0.18f * (d < 0.95f ? 1f : 0f); // 안쪽 옅은 빛
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(edge + inner)));
                }
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Default;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
    }
}
