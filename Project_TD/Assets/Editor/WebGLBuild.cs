using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace TowerDefence.EditorTools
{
    // 웹 빌드: Builds/WebGL (깃 무시 폴더). GitHub Pages처럼 압축 헤더를 못 붙이는 호스트에서도 열리도록
    // Gzip + Decompression Fallback. 빌드 후 원래 플랫폼으로 돌려놓는다. 결과는 Builds/WebGL_result.txt.
    public static class WebGLBuild
    {
        public const string OutputFolder = "Builds/WebGL";
        public const string ResultFile = "Builds/WebGL_result.txt";

        [MenuItem("TowerDefence/Build WebGL")]
        public static void Build()
        {
            var previousTarget = EditorUserBuildSettings.activeBuildTarget;
            var previousGroup = BuildPipeline.GetBuildTargetGroup(previousTarget);

            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip;
            PlayerSettings.WebGL.decompressionFallback = true;
            PlayerSettings.WebGL.dataCaching = true;

            var scenes = new System.Collections.Generic.List<string>();
            foreach (var s in EditorBuildSettings.scenes)
                if (s.enabled)
                    scenes.Add(s.path);

            Directory.CreateDirectory("Builds");
            var options = new BuildPlayerOptions
            {
                scenes = scenes.ToArray(),
                locationPathName = OutputFolder,
                target = BuildTarget.WebGL,
                targetGroup = BuildTargetGroup.WebGL,
                options = BuildOptions.None,
            };
            var report = BuildPipeline.BuildPlayer(options);
            var summary = report.summary;
            File.WriteAllText(ResultFile,
                $"result:{summary.result}\nerrors:{summary.totalErrors}\nwarnings:{summary.totalWarnings}\n" +
                $"sizeMB:{summary.totalSize / (1024f * 1024f):F1}\nseconds:{summary.totalTime.TotalSeconds:F0}\n");
            Debug.Log($"[WebGLBuild] {summary.result} → {Path.GetFullPath(OutputFolder)}");

            if (summary.result == BuildResult.Succeeded && previousTarget != BuildTarget.WebGL)
                EditorUserBuildSettings.SwitchActiveBuildTarget(previousGroup, previousTarget);
        }
    }
}
