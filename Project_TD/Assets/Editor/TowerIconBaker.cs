using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace TowerDefence.EditorTools
{
    // 타워 목록 아이콘을 에디터에서 미리 PNG로 구워 TowerDef.icon에 넣는다 (Assets/Art/TowerIcons/<id>.png).
    // 게임은 이 그림을 그대로 쓰므로, 실행 중 렌더 텍스처 촬영이 안 되는 그래픽카드·브라우저에서도 아이콘이 나온다.
    // 촬영 구도는 TowerIconRenderer(실행 중 촬영)와 같다. 타워 모델을 바꾸면 메뉴로 다시 굽는다.
    public static class TowerIconBaker
    {
        const string Folder = "Assets/Art/TowerIcons";
        const int Size = 256;
        const int IconLayer = 31;
        static readonly Vector3 Stage = new Vector3(0f, -500f, 0f);
        static readonly Vector3 ViewDirection = new Vector3(-0.55f, 0.6f, -0.75f);

        [MenuItem("TowerDefence/Setup/Bake Tower Icons")]
        public static void BakeAll()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Art")) AssetDatabase.CreateFolder("Assets", "Art");
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/Art", "TowerIcons");
            int n = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:TowerDef", new[] { "Assets/Data/Towers" }))
            {
                var def = AssetDatabase.LoadAssetAtPath<TowerDef>(AssetDatabase.GUIDToAssetPath(guid));
                if (def == null || def.viewPrefab == null)
                    continue;
                string path = $"{Folder}/{def.id}.png";
                if (!Bake(def, Path.GetFullPath(path)))
                    continue;
                AssetDatabase.ImportAsset(path);
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                importer.textureType = TextureImporterType.Default;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.textureCompression = TextureImporterCompression.Uncompressed; // 모든 그래픽카드에서 그대로 보이게
                importer.SaveAndReimport();
                def.icon = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                EditorUtility.SetDirty(def);
                n++;
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"[TowerIconBaker] 아이콘 {n}개 → {Folder}");
        }

        static bool Bake(TowerDef def, string file)
        {
            var model = Object.Instantiate(def.viewPrefab);
            model.transform.position = Stage;
            var camGo = new GameObject("IconBakeCamera");
            var lightGo = new GameObject("IconBakeLight");
            var oldAmbient = RenderSettings.ambientLight;
            var oldMode = RenderSettings.ambientMode;
            try
            {
                SetLayer(model.transform, IconLayer);
                Bounds b = default;
                bool any = false;
                foreach (var r in model.GetComponentsInChildren<Renderer>())
                {
                    if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
                }
                if (!any)
                    return false;

                var light = lightGo.AddComponent<Light>();
                light.type = LightType.Directional;
                light.intensity = 1.2f;
                light.color = new Color(1f, 0.97f, 0.92f);
                light.cullingMask = 1 << IconLayer;
                lightGo.transform.rotation = Quaternion.Euler(45f, -30f, 0f);
                RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
                RenderSettings.ambientLight = new Color(0.55f, 0.57f, 0.62f);

                var cam = camGo.AddComponent<Camera>();
                cam.enabled = false;
                cam.cullingMask = 1 << IconLayer;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
                cam.fieldOfView = 25f;
                float radius = b.extents.magnitude;
                float distance = radius / Mathf.Sin(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * 1.05f;
                cam.nearClipPlane = Mathf.Max(0.01f, distance - radius * 2f);
                cam.farClipPlane = distance + radius * 2f;
                cam.transform.position = b.center + ViewDirection.normalized * distance;
                cam.transform.LookAt(b.center);
                var urp = cam.GetUniversalAdditionalCameraData();
                urp.renderPostProcessing = false;
                urp.renderShadows = false;

                var msaa = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32) { antiAliasing = 8 };
                var plain = new RenderTexture(Size, Size, 0, RenderTextureFormat.ARGB32);
                cam.targetTexture = msaa;
                cam.Render();
                cam.targetTexture = null;
                Graphics.Blit(msaa, plain); // 안티에일리어싱 풀기
                var prev = RenderTexture.active;
                RenderTexture.active = plain;
                var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
                tex.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
                tex.Apply();
                RenderTexture.active = prev;
                File.WriteAllBytes(file, tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
                msaa.Release(); Object.DestroyImmediate(msaa);
                plain.Release(); Object.DestroyImmediate(plain);
                return true;
            }
            finally
            {
                RenderSettings.ambientLight = oldAmbient;
                RenderSettings.ambientMode = oldMode;
                Object.DestroyImmediate(model);
                Object.DestroyImmediate(camGo);
                Object.DestroyImmediate(lightGo);
            }
        }

        static void SetLayer(Transform t, int layer)
        {
            t.gameObject.layer = layer;
            foreach (Transform c in t)
                SetLayer(c, layer);
        }
    }
}
