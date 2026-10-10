using System;

namespace VirtualVessel.Application.Build
{
    /// <summary>
    /// Version information recorded in logs and diagnostics (system design 5.29).
    /// </summary>
    public sealed class BuildInfo
    {
        /// <summary>Used until CI embeds a real build identifier such as the Git commit.</summary>
        public const string DevelopmentBuildIdentifier = "development";

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
            return new BuildInfo(
                UnityEngine.Application.version,
                UnityEngine.Application.unityVersion,
                DevelopmentBuildIdentifier,
                UnityEngine.SystemInfo.operatingSystem);
        }

        public override string ToString()
        {
            return $"Virtual Vessel Studio {ApplicationVersion} ({BuildIdentifier}), Unity {UnityVersion}, {OperatingSystem}";
        }
    }
}
