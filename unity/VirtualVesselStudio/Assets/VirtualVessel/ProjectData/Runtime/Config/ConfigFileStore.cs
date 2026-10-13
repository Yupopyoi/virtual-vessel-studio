using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using VirtualVessel.Core.Time;

namespace VirtualVessel.ProjectData.Config
{
    /// <summary>
    /// Reads and writes one settings file, such as ui-settings.json, as a user asset
    /// (UI foundation detailed design 8.3, CLAUDE.md 25).
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item>Every file carries <c>schemaVersion</c>; older files are migrated one version at a time.</item>
    /// <item>Fields this version does not know are kept from the last load and written back.</item>
    /// <item>Writes go to a temporary file that then replaces the original, so a crash never leaves a half-written file.</item>
    /// <item>A file that cannot be read is moved aside and the default is used, so one broken file never stops startup.</item>
    /// </list>
    /// Thread-safe. Not for hot paths: every call touches the file system.
    /// </remarks>
    public sealed class ConfigFileStore<T>
        where T : class
    {
        public const string VersionKey = "schemaVersion";

        private const string TemporarySuffix = ".tmp";

        private static readonly JsonSerializer s_serializer = JsonSerializer.Create(new JsonSerializerSettings
        {
            ContractResolver = new DefaultContractResolver { NamingStrategy = new CamelCaseNamingStrategy() },
            MissingMemberHandling = MissingMemberHandling.Ignore,
            NullValueHandling = NullValueHandling.Include,
            Culture = CultureInfo.InvariantCulture,
            FloatParseHandling = FloatParseHandling.Double,
            DateParseHandling = DateParseHandling.None,
        });

        private static readonly UTF8Encoding s_encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        private readonly object _gate = new object();
        private readonly int _currentVersion;
        private readonly Func<T> _createDefault;
        private readonly Dictionary<int, ConfigMigration> _migrations;
        private readonly ISystemClock _clock;

        // The last document read from or written to disk, without the version key. Saving merges into
        // it so that fields unknown to this version survive.
        private JObject _known;
        private int _fileVersion;

        /// <param name="directory">Folder of the file; created on the first save.</param>
        /// <param name="currentVersion">The schema version this code reads and writes. Starts at 1.</param>
        /// <param name="createDefault">Creates the value used when there is no readable file.</param>
        /// <param name="migrations">One step per older version, from 1 up to <paramref name="currentVersion"/> - 1.</param>
        public ConfigFileStore(
            string directory,
            string fileName,
            int currentVersion,
            Func<T> createDefault,
            ISystemClock clock,
            IEnumerable<ConfigMigration> migrations = null)
        {
            if (string.IsNullOrWhiteSpace(directory))
            {
                throw new ArgumentException("Directory must not be empty.", nameof(directory));
            }

            if (string.IsNullOrWhiteSpace(fileName))
            {
                throw new ArgumentException("File name must not be empty.", nameof(fileName));
            }

            if (currentVersion < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(currentVersion), currentVersion, "Versions start at 1.");
            }

            _currentVersion = currentVersion;
            _createDefault = createDefault ?? throw new ArgumentNullException(nameof(createDefault));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            FilePath = Path.Combine(directory, fileName);

            _migrations = new Dictionary<int, ConfigMigration>();
            foreach (ConfigMigration migration in migrations ?? Enumerable.Empty<ConfigMigration>())
            {
                if (migration.FromVersion >= currentVersion)
                {
                    throw new ArgumentException($"Migration from version {migration.FromVersion} is not older than the current version {currentVersion}.", nameof(migrations));
                }

                if (_migrations.ContainsKey(migration.FromVersion))
                {
                    throw new ArgumentException($"Two migrations start at version {migration.FromVersion}.", nameof(migrations));
                }

                _migrations.Add(migration.FromVersion, migration);
            }

            // A gap would silently skip a format change, so it is a development mistake.
            for (int version = 1; version < currentVersion; version++)
            {
                if (!_migrations.ContainsKey(version))
                {
                    throw new ArgumentException($"No migration from version {version} to {version + 1}.", nameof(migrations));
                }
            }
        }

        public string FilePath { get; }

        public ConfigLoadResult<T> Load()
        {
            lock (_gate)
            {
                _known = null;
                _fileVersion = 0;
                if (!File.Exists(FilePath))
                {
                    return new ConfigLoadResult<T>(_createDefault(), ConfigLoadStatus.Created, 0, null, null);
                }

                JObject root;
                try
                {
                    string text = File.ReadAllText(FilePath, s_encoding);
                    root = ConfigDocument.ParseObject(text);
                }
                catch (JsonException exception)
                {
                    return Recover($"Settings file {FilePath} is not valid JSON: {exception.Message}");
                }

                if (!(root[VersionKey] is JValue versionValue) || versionValue.Type != JTokenType.Integer || versionValue.Value<int>() < 1)
                {
                    return Recover($"Settings file {FilePath} has no valid {VersionKey}.");
                }

                int fileVersion = versionValue.Value<int>();
                root.Remove(VersionKey);
                ConfigLoadStatus status = ConfigLoadStatus.Loaded;
                if (fileVersion < _currentVersion)
                {
                    try
                    {
                        var document = new ConfigDocument(root);
                        for (int version = fileVersion; version < _currentVersion; version++)
                        {
                            _migrations[version].Apply(document);
                        }
                    }
                    catch (Exception exception)
                    {
                        return Recover($"Migrating settings file {FilePath} from version {fileVersion} failed: {exception.Message}");
                    }

                    status = ConfigLoadStatus.Migrated;
                }
                else if (fileVersion > _currentVersion)
                {
                    status = ConfigLoadStatus.NewerVersion;
                }

                var warnings = new List<string>();
                T value = Deserialize(root, warnings);
                if (status == ConfigLoadStatus.NewerVersion)
                {
                    warnings.Add($"Settings file {FilePath} is version {fileVersion}, newer than {_currentVersion}. Unknown fields are kept.");
                }

                _known = root;
                _fileVersion = fileVersion;
                return new ConfigLoadResult<T>(value, status, fileVersion, warnings, null);
            }
        }

        /// <summary>
        /// Writes <paramref name="value"/>. Fields read by the last <see cref="Load"/> that this version
        /// does not know are kept.
        /// </summary>
        /// <exception cref="IOException">The file could not be written; the previous file is unchanged.</exception>
        /// <exception cref="UnauthorizedAccessException">The folder or file is not writable; the previous file is unchanged.</exception>
        public void Save(T value)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            lock (_gate)
            {
                JObject written = JObject.FromObject(value, s_serializer);
                if (_known != null)
                {
                    KeepUnknown(written, _known);
                }

                // A file from a newer version keeps its version, so that version does not migrate it again.
                int version = Math.Max(_currentVersion, _fileVersion);
                var output = new JObject { [VersionKey] = version };
                foreach (JProperty property in written.Properties())
                {
                    output.Add(property.Name, property.Value.DeepClone());
                }

                WriteAtomically(output.ToString(Formatting.Indented));
                _known = written;
                _fileVersion = version;
            }
        }

        private T Deserialize(JObject root, List<string> warnings)
        {
            // A field with the wrong type falls back to its default instead of discarding the whole file.
            var settings = new JsonSerializerSettings
            {
                ContractResolver = s_serializer.ContractResolver,
                MissingMemberHandling = MissingMemberHandling.Ignore,

                // Lists in the default value are replaced, not appended to.
                ObjectCreationHandling = ObjectCreationHandling.Replace,
                Culture = CultureInfo.InvariantCulture,
                Error = (_, args) =>
                {
                    warnings.Add($"Settings file {FilePath}: ignored {args.ErrorContext.Path}: {args.ErrorContext.Error.Message}");
                    args.ErrorContext.Handled = true;
                },
            };

            T value = _createDefault();
            using (JsonReader reader = root.CreateReader())
            {
                JsonSerializer.Create(settings).Populate(reader, value);
            }

            return value;
        }

        private ConfigLoadResult<T> Recover(string reason)
        {
            string movedTo = MoveAside();
            var warnings = new List<string> { reason };
            warnings.Add(movedTo != null
                ? $"Moved the unreadable file to {movedTo} and started with defaults."
                : "Could not move the unreadable file aside; started with defaults and it will be overwritten on the next save.");
            return new ConfigLoadResult<T>(_createDefault(), ConfigLoadStatus.Recovered, 0, warnings, movedTo);
        }

        private string MoveAside()
        {
            string directory = Path.GetDirectoryName(FilePath);
            string name = Path.GetFileNameWithoutExtension(FilePath);
            string extension = Path.GetExtension(FilePath);
            string stamp = _clock.UtcNow.ToLocalTime().ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            string target = Path.Combine(directory, $"{name}.broken-{stamp}{extension}");
            for (int i = 1; File.Exists(target); i++)
            {
                target = Path.Combine(directory, $"{name}.broken-{stamp}-{i}{extension}");
            }

            try
            {
                File.Move(FilePath, target);
                return target;
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                return null;
            }
        }

        private void WriteAtomically(string text)
        {
            string directory = Path.GetDirectoryName(FilePath);
            Directory.CreateDirectory(directory);
            string temporary = FilePath + TemporarySuffix;
            try
            {
                using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    byte[] bytes = s_encoding.GetBytes(text);
                    stream.Write(bytes, 0, bytes.Length);

                    // Make sure the data is on disk before the old file is replaced.
                    stream.Flush(flushToDisk: true);
                }

                if (File.Exists(FilePath))
                {
                    File.Replace(temporary, FilePath, destinationBackupFileName: null);
                }
                else
                {
                    File.Move(temporary, FilePath);
                }
            }
            catch
            {
                TryDelete(temporary);
                throw;
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                // A leftover temporary file is harmless: the next save overwrites it.
            }
        }

        /// <summary>Adds properties of <paramref name="previous"/> that <paramref name="written"/> lacks, recursively.</summary>
        private static void KeepUnknown(JObject written, JObject previous)
        {
            foreach (JProperty property in previous.Properties())
            {
                JToken current = written[property.Name];
                if (current == null)
                {
                    written[property.Name] = property.Value.DeepClone();
                }
                else if (current is JObject currentObject && property.Value is JObject previousObject)
                {
                    KeepUnknown(currentObject, previousObject);
                }
            }
        }
    }
}
