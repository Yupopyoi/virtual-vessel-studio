using System;

namespace VirtualVessel.Application.Build
{
    /// <summary>
    /// Version information recorded in logs and diagnostics (system design 5.29).
    /// </summary>
    public sealed class BuildInfo
    {
        /// <summary>Used when the build carries no identifier, as in the Editor and local builds.</summary>
        public const string DevelopmentBuildIdentifier = "development";

        /// <summary>The file in StreamingAssets where a release build records its identifier, such as "v0.2.0+3f2a1bc".</summary>
        public const string BuildIdFileName = "build-id.txt";

        // Far above any real identifier; a damaged file must not end up in every log line.
        private const int MaxBuildIdLength = 100;

        public BuildInfo(string applicationVersion, string unityVersion, string buildIdentifier, string operatingSystem)
        {
            ApplicationVersion = applicationVersion ?? throw new ArgumentNullException(nameof(applicationVersion));
            UnityVersion = unityVersion ?? throw new ArgumentNullException(nameof(unityVersion));
            BuildIdentifier = buildIdentifier ?? DevelopmentBuildIdentifier;
            OperatingSystem = operatingSystem ?? string.Empty;
        }

        public string ApplicationVersion { get; }

        public string UnityVersion { get; }

        public string BuildIdentifier { get; }

        public string OperatingSystem { get; }

        public static BuildInfo FromUnity()
        {
            string file = System.IO.Path.Combine(UnityEngine.Application.streamingAssetsPath, BuildIdFileName);
            string text = null;
            try
            {
                text = System.IO.File.Exists(file) ? System.IO.File.ReadAllText(file) : null;
            }
            catch (Exception exception) when (exception is System.IO.IOException || exception is UnauthorizedAccessException)
            {
                // A build that cannot be identified still runs; the logs say "development".
            }

            return new BuildInfo(
                UnityEngine.Application.version,
                UnityEngine.Application.unityVersion,
                ParseBuildId(text),
                UnityEngine.SystemInfo.operatingSystem);
        }

        /// <summary>The identifier in a build-id.txt, or null for none or one that cannot be right. Pure.</summary>
        public static string ParseBuildId(string text)
        {
            string id = text?.Trim();
            if (string.IsNullOrEmpty(id) || id.Length > MaxBuildIdLength)
            {
                return null;
            }

            foreach (char c in id)
            {
                if (!(char.IsLetterOrDigit(c) || c == '.' || c == '+' || c == '-' || c == '_'))
                {
                    return null;
                }
            }

            return id;
        }

        public override string ToString()
        {
            return $"Virtual Vessel Studio {ApplicationVersion} ({BuildIdentifier}), Unity {UnityVersion}, {OperatingSystem}";
        }
    }
}
