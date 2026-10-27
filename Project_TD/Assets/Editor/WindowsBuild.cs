using System.IO;
using UnityEditor;
using UnityEngine;

namespace TowerDefence.EditorTools
{
    // 윈도우 실행 파일 빌드: Builds/Windows/Project_TD.exe (깃 무시 폴더). 결과는 Builds/Windows_result.txt.
    public static class WindowsBuild
    {
        public const string OutputFolder = "Builds/Windows";
        public const string ResultFile = "Builds/Windows_result.txt";

        [MenuItem("TowerDefence/Build Windows")]
        public static void Build()
        {
            var scenes = new System.Collections.Generic.List<string>();
            foreach (var s in EditorBuildSettings.scenes)
                if (s.enabled)
                    scenes.Add(s.path);

            Directory.CreateDirectory("Builds");
            var options = new BuildPlayerOptions
            {
                scenes = scenes.ToArray(),
                locationPathName = OutputFolder + "/" + PlayerSettings.productName + ".exe",
                target = BuildTarget.StandaloneWindows64,
                targetGroup = BuildTargetGroup.Standalone,
                options = BuildOptions.None,
            };
            var summary = BuildPipeline.BuildPlayer(options).summary;
            File.WriteAllText(ResultFile,
                $"result:{summary.result}\nerrors:{summary.totalErrors}\nwarnings:{summary.totalWarnings}\n" +
                $"sizeMB:{summary.totalSize / (1024f * 1024f):F1}\nseconds:{summary.totalTime.TotalSeconds:F0}\n");
            Debug.Log($"[WindowsBuild] {summary.result} → {Path.GetFullPath(OutputFolder)}");
        }
    }
}
