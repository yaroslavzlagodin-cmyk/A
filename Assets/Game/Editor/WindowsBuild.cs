using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace BrokenHeart.Editor
{
    public static class WindowsBuild
    {
        [MenuItem("Broken Heart/Build/Windows Development")]
        public static void BuildDevelopment() => Build("WindowsDevelopment", BuildOptions.Development);

        [MenuItem("Broken Heart/Build/Windows Release")]
        public static void BuildRelease() => Build("WindowsRelease", BuildOptions.None);

        public static void BuildPerformanceBaseline() => Build("WindowsPerformanceBaseline", BuildOptions.Development);

        private static void Build(string folder, BuildOptions options)
        {
            // The standalone starts in the same lobby as Play Mode.
            var scenes = EditorBuildSettings.scenes.Where(scene => scene.enabled).Select(scene => scene.path).ToArray();
            if (scenes.Length == 0 || scenes[0] != MenuSetup.Path || !scenes.Contains(PrototypeBuilder.ScenePath))
                throw new InvalidOperationException("Enable the Menu as the first build scene and include the Game scene.");

            string root = Path.GetDirectoryName(Application.dataPath);
            string output = Path.Combine(root, "Builds", folder, "Broken Heart.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = output,
                target = BuildTarget.StandaloneWindows64,
                options = options | BuildOptions.StrictMode
            });
            if (report.summary.result != BuildResult.Succeeded || !File.Exists(output))
                throw new InvalidOperationException($"Windows build {report.summary.result} ({report.summary.totalErrors} errors).");

            Debug.Log($"Windows build ready at {output}");
        }
    }
}
