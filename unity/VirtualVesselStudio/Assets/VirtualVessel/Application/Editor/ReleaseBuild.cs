using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using VirtualVessel.Application.Build;

namespace VirtualVessel.Application.Editor
{
    /// <summary>
    /// The Windows build of a release, run by tools/release/build-release.ps1 in batch mode
    /// (development workflow 21):
    /// <c>-executeMethod VirtualVessel.Application.Editor.ReleaseBuild.BuildWindows -vvOutput &lt;folder&gt; -vvVersion &lt;x.y.z&gt; -vvBuildId &lt;id&gt;</c>.
    /// </summary>
    public static class ReleaseBuild
    {
        public const string ExecutableName = "VirtualVesselStudio.exe";

        public static void BuildWindows()
        {
            string[] args = Environment.GetCommandLineArgs();
            string output = Argument(args, "-vvOutput");
            string version = Argument(args, "-vvVersion");
            string buildId = Argument(args, "-vvBuildId");
            try
            {
                Build(output, version, buildId);
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogError($"[ReleaseBuild] {exception.Message}");
                EditorApplication.Exit(1);
            }
        }

        /// <summary>The value after <paramref name="name"/> in the command line, or null. Pure.</summary>
        public static string Argument(string[] args, string name)
        {
            int index = Array.IndexOf(args, name);
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
        }

        private static void Build(string output, string version, string buildId)
        {
            if (string.IsNullOrWhiteSpace(output) || string.IsNullOrWhiteSpace(version) || string.IsNullOrWhiteSpace(buildId))
            {
                throw new ArgumentException("-vvOutput, -vvVersion, and -vvBuildId are required.");
            }

            // The version shown in the application is the project's; a release of another number is a mistake.
            if (!string.Equals(PlayerSettings.bundleVersion, version, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"The project version is {PlayerSettings.bundleVersion}, not {version}. Change it in a pull request to develop before releasing.");
            }

            string[] scenes = EditorBuildSettings.scenes.Where(scene => scene.enabled).Select(scene => scene.path).ToArray();
            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = Path.Combine(output, ExecutableName),
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result != BuildResult.Succeeded)
            {
                throw new InvalidOperationException($"The build ended with {report.summary.result} and {report.summary.totalErrors} errors.");
            }

            // Read by BuildInfo, so logs and diagnostics name the exact commit (system design 5.29).
            string data = Path.Combine(output, Path.GetFileNameWithoutExtension(ExecutableName) + "_Data", "StreamingAssets");
            Directory.CreateDirectory(data);
            File.WriteAllText(Path.Combine(data, BuildInfo.BuildIdFileName), buildId);
            Debug.Log($"[ReleaseBuild] Built {version} ({buildId}) to {output}: {report.summary.totalSize / (1024 * 1024)} MB.");
        }
    }
}
