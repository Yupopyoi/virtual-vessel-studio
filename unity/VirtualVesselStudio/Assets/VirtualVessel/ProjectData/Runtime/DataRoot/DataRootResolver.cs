using System;
using System.Collections.Generic;
using System.IO;

namespace VirtualVessel.ProjectData.DataRoot
{
    /// <summary>
    /// Decides where the Data Root is, creates the directories the foundation needs, and verifies
    /// that the location is writable.
    /// </summary>
    /// <remarks>
    /// Resolution order: explicit override, then bootstrap.json, then the default location under the
    /// application data directory.
    /// </remarks>
    public sealed class DataRootResolver
    {
        public const string ApplicationFolderName = "VirtualVesselStudio";

        public const string DefaultDataFolderName = "Data";

        public const string ConfigFolderName = "Config";

        private static readonly DataRootDirectory[] s_foundationDirectories =
        {
            DataRootDirectory.Logs,
            DataRootDirectory.Cache,
        };

        private readonly string _applicationDataDirectory;

        /// <param name="localApplicationDataPath">
        /// The OS user data directory, normally %LOCALAPPDATA%. Injected so tests can use a temporary directory.
        /// </param>
        public DataRootResolver(string localApplicationDataPath)
        {
            if (string.IsNullOrWhiteSpace(localApplicationDataPath))
            {
                throw new ArgumentException("Local application data path must not be empty.", nameof(localApplicationDataPath));
            }

            _applicationDataDirectory = Path.Combine(localApplicationDataPath, ApplicationFolderName);
        }

        public static DataRootResolver ForCurrentUser()
        {
            return new DataRootResolver(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
        }

        public string ApplicationDataDirectory => _applicationDataDirectory;

        public string BootstrapFilePath => Path.Combine(_applicationDataDirectory, BootstrapSettings.FileName);

        public string DefaultDataRootPath => Path.Combine(_applicationDataDirectory, DefaultDataFolderName);

        /// <summary>
        /// Folder for small application settings such as ui-settings.json (system design 6.8). It stays
        /// in the OS user data area, separate from the Data Root.
        /// </summary>
        public string ConfigDirectory => Path.Combine(_applicationDataDirectory, ConfigFolderName);

        /// <exception cref="DataRootUnavailableException">
        /// The resolved location cannot be created or written to.
        /// </exception>
        public DataRootResolution Resolve(string overridePath)
        {
            // The file is read only when there is no override, so a broken file cannot affect an explicit choice.
            BootstrapReadResult? bootstrap = string.IsNullOrWhiteSpace(overridePath)
                ? BootstrapSettings.Read(BootstrapFilePath)
                : (BootstrapReadResult?)null;
            (string rootPath, DataRootSource source, List<string> warnings) = Choose(overridePath, bootstrap, DefaultDataRootPath);

            DataRootPaths dataRoot;
            try
            {
                dataRoot = new DataRootPaths(rootPath);
            }
            catch (Exception exception) when (exception is ArgumentException || exception is NotSupportedException || exception is PathTooLongException)
            {
                throw new DataRootUnavailableException(rootPath, $"The Data Root path is invalid: {exception.Message}", exception);
            }

            EnsureWritable(dataRoot);
            return new DataRootResolution(dataRoot, source, warnings);
        }

        /// <summary>
        /// Picks the Data Root: the override, then bootstrap.json, then the default. Pure; reading the
        /// file and checking the folder are done by <see cref="Resolve"/>.
        /// </summary>
        internal static (string RootPath, DataRootSource Source, List<string> Warnings) Choose(string overridePath, BootstrapReadResult? bootstrap, string defaultPath)
        {
            var warnings = new List<string>();
            if (!string.IsNullOrWhiteSpace(overridePath))
            {
                return (overridePath, DataRootSource.Override, warnings);
            }

            BootstrapReadResult file = bootstrap ?? BootstrapReadResult.Missing();
            if (file.Warning != null)
            {
                warnings.Add(file.Warning);
            }

            if (file.IsValid && file.DataRoot != null)
            {
                return (file.DataRoot, DataRootSource.BootstrapFile, warnings);
            }

            if (!file.IsValid)
            {
                warnings.Add($"Using the default Data Root because {BootstrapSettings.FileName} could not be used. The file was left unchanged.");
            }

            return (defaultPath, DataRootSource.Default, warnings);
        }

        private static void EnsureWritable(DataRootPaths dataRoot)
        {
            try
            {
                foreach (DataRootDirectory directory in s_foundationDirectories)
                {
                    Directory.CreateDirectory(dataRoot.GetDirectory(directory));
                }

                // Creating a directory can succeed on read-only media that later rejects files, so
                // verify by writing and removing a probe file.
                string probePath = Path.Combine(dataRoot.GetDirectory(DataRootDirectory.Cache), ".write-probe");
                File.WriteAllText(probePath, string.Empty);
                File.Delete(probePath);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                throw new DataRootUnavailableException(dataRoot.RootPath, $"The Data Root is not writable: {exception.Message}", exception);
            }
        }
    }

    public enum DataRootSource
    {
        Override,
        BootstrapFile,
        Default,
    }

    public sealed class DataRootResolution
    {
        internal DataRootResolution(IDataRoot dataRoot, DataRootSource source, IReadOnlyList<string> warnings)
        {
            DataRoot = dataRoot;
            Source = source;
            Warnings = warnings;
        }

        public IDataRoot DataRoot { get; }

        public DataRootSource Source { get; }

        /// <summary>Non-fatal problems found during resolution, for diagnostics.</summary>
        public IReadOnlyList<string> Warnings { get; }
    }

    public sealed class DataRootUnavailableException : Exception
    {
        public DataRootUnavailableException(string path, string message, Exception innerException)
            : base(message, innerException)
        {
            Path = path;
        }

        public string Path { get; }
    }
}
