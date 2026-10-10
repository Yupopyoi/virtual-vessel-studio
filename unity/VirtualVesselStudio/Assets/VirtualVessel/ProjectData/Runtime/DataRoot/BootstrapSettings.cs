using System;
using System.IO;
using UnityEngine;

namespace VirtualVessel.ProjectData.DataRoot
{
    /// <summary>
    /// Reads bootstrap.json, the small file in the OS user data area that points to the Data Root
    /// (system design 6.8, 6.9).
    /// </summary>
    /// <remarks>
    /// The file is only read here, never rewritten, so unknown properties written by newer versions
    /// are preserved on disk.
    /// </remarks>
    internal static class BootstrapSettings
    {
        public const string FileName = "bootstrap.json";

        public const int CurrentSchemaVersion = 1;

        public static BootstrapReadResult Read(string filePath)
        {
            if (!File.Exists(filePath))
            {
                return BootstrapReadResult.Missing();
            }

            string json;
            try
            {
                json = File.ReadAllText(filePath);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                return BootstrapReadResult.Invalid($"Failed to read {filePath}: {exception.Message}");
            }

            BootstrapFile data;
            try
            {
                data = JsonUtility.FromJson<BootstrapFile>(json);
            }
            catch (ArgumentException exception)
            {
                return BootstrapReadResult.Invalid($"{filePath} is not valid JSON: {exception.Message}");
            }

            if (data == null)
            {
                return BootstrapReadResult.Invalid($"{filePath} is empty.");
            }

            if (data.schemaVersion > CurrentSchemaVersion)
            {
                // A newer application wrote this file. Its dataRoot field is still expected to be
                // compatible, so use it but record the mismatch for diagnostics.
                return BootstrapReadResult.Read(
                    data.dataRoot,
                    $"{filePath} has schema version {data.schemaVersion}, newer than supported version {CurrentSchemaVersion}.");
            }

            return BootstrapReadResult.Read(data.dataRoot, null);
        }

        [Serializable]
        private sealed class BootstrapFile
        {
            // Field names match the JSON property names because JsonUtility maps by field name.
            public int schemaVersion;
            public string dataRoot;
        }
    }

    internal readonly struct BootstrapReadResult
    {
        private BootstrapReadResult(bool exists, bool isValid, string dataRoot, string warning)
        {
            Exists = exists;
            IsValid = isValid;
            DataRoot = dataRoot;
            Warning = warning;
        }

        public bool Exists { get; }

        public bool IsValid { get; }

        /// <summary>The configured Data Root, or null when the file does not specify one.</summary>
        public string DataRoot { get; }

        public string Warning { get; }

        public static BootstrapReadResult Missing()
        {
            return new BootstrapReadResult(false, true, null, null);
        }

        public static BootstrapReadResult Invalid(string warning)
        {
            return new BootstrapReadResult(true, false, null, warning);
        }

        public static BootstrapReadResult Read(string dataRoot, string warning)
        {
            string normalized = string.IsNullOrWhiteSpace(dataRoot) ? null : dataRoot;
            return new BootstrapReadResult(true, true, normalized, warning);
        }
    }
}
