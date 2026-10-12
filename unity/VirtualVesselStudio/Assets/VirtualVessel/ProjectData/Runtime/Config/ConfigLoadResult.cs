using System;
using System.Collections.Generic;

namespace VirtualVessel.ProjectData.Config
{
    public enum ConfigLoadStatus
    {
        /// <summary>There was no file; the value is the default.</summary>
        Created,

        /// <summary>The file was read as is.</summary>
        Loaded,

        /// <summary>The file was in an older format and was migrated.</summary>
        Migrated,

        /// <summary>
        /// The file was written by a newer version. Known fields were read; the rest are kept and
        /// written back unchanged.
        /// </summary>
        NewerVersion,

        /// <summary>The file could not be read. It was moved aside and the default is used.</summary>
        Recovered,
    }

    /// <summary>
    /// What happened when a settings file was read. ProjectData does not log; the caller records
    /// <see cref="Warnings"/> in the application log.
    /// </summary>
    public sealed class ConfigLoadResult<T>
    {
        internal ConfigLoadResult(T value, ConfigLoadStatus status, int fileVersion, IReadOnlyList<string> warnings, string movedAsidePath)
        {
            Value = value;
            Status = status;
            FileVersion = fileVersion;
            Warnings = warnings ?? Array.Empty<string>();
            MovedAsidePath = movedAsidePath;
        }

        public T Value { get; }

        public ConfigLoadStatus Status { get; }

        /// <summary>The schema version found in the file, or 0 when there was no readable file.</summary>
        public int FileVersion { get; }

        /// <summary>Developer-facing notes, such as a field that had the wrong type.</summary>
        public IReadOnlyList<string> Warnings { get; }

        /// <summary>Where a broken file was moved, for <see cref="ConfigLoadStatus.Recovered"/>.</summary>
        public string MovedAsidePath { get; }
    }
}
