using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using VirtualVessel.Core.Time;
using VirtualVessel.Diagnostics.Logging;

namespace VirtualVessel.Avatar
{
    /// <summary>How a registration ended.</summary>
    public sealed class AvatarImportResult
    {
        private AvatarImportResult(AvatarProblem problem, AvatarEntry entry, bool declined)
        {
            Problem = problem;
            Entry = entry;
            Declined = declined;
        }

        /// <summary>None when registered or declined; AlreadyImported with <see cref="Entry"/> the registered one.</summary>
        public AvatarProblem Problem { get; }

        /// <summary>The new avatar, or the one already registered from the same file.</summary>
        public AvatarEntry Entry { get; }

        /// <summary>The user chose not to register after seeing the model's conditions.</summary>
        public bool Declined { get; }

        public bool Imported => Problem == AvatarProblem.None && Entry != null;

        public static AvatarImportResult Succeeded(AvatarEntry entry)
        {
            return new AvatarImportResult(AvatarProblem.None, entry ?? throw new ArgumentNullException(nameof(entry)), false);
        }

        public static AvatarImportResult Duplicate(AvatarEntry existing)
        {
            return new AvatarImportResult(AvatarProblem.AlreadyImported, existing, false);
        }

        public static AvatarImportResult Failed(AvatarProblem problem)
        {
            return new AvatarImportResult(problem, null, false);
        }

        public static AvatarImportResult UserDeclined()
        {
            return new AvatarImportResult(AvatarProblem.None, null, true);
        }
    }

    /// <summary>
    /// Registers a picked model file (avatar detailed design 6.2): checks it, copies it into a temporary
    /// folder of the Data Root while hashing it, loads the copy to check it and read its information,
    /// lets the user confirm the model's conditions, and only then moves it in. Anything that fails on
    /// the way deletes the temporary folder, so the list never changes halfway.
    /// </summary>
    /// <remarks>
    /// Start on the main thread: loading builds Unity objects there. File work runs on the thread pool.
    /// </remarks>
    public sealed class AvatarImporter
    {
        private readonly AvatarStore _store;
        private readonly IAvatarLoader _loader;
        private readonly ISystemClock _clock;
        private readonly ILog _log;

        public AvatarImporter(AvatarStore store, IAvatarLoader loader, ISystemClock clock, ILog log)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _loader = loader ?? throw new ArgumentNullException(nameof(loader));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _log = log;
        }

        /// <param name="existing">The registered avatars, to find the same file registered before.</param>
        /// <param name="confirm">Shown the model's information before it is registered; false declines. Null registers without asking.</param>
        public async Task<AvatarImportResult> ImportAsync(string path, IReadOnlyList<AvatarEntry> existing, Func<AvatarModelInfo, Task<bool>> confirm, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(path))
            {
                throw new ArgumentException("Path must not be empty.", nameof(path));
            }

            // Only the file name is logged: the full path contains the user's name (avatar detailed design 11).
            string fileName = Path.GetFileName(path);
            AvatarProblem checkedProblem;
            try
            {
                checkedProblem = await Task.Run(() => CheckFile(path, _loader.Extensions), cancellationToken);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                _log?.Warning($"Could not read the picked avatar file {fileName}.", exception);
                return AvatarImportResult.Failed(AvatarProblem.LoadFailed);
            }

            if (checkedProblem != AvatarProblem.None)
            {
                _log?.Information($"Avatar file {fileName} was not registered: {checkedProblem}.");
                return AvatarImportResult.Failed(checkedProblem);
            }

            string id = AvatarStore.NewId();
            bool committed = false;
            try
            {
                string sha256 = await Task.Run(() => _store.CopyIn(path, id, _loader.Format, cancellationToken), cancellationToken);
                AvatarEntry same = AvatarEntries.FindBySha256(existing ?? Array.Empty<AvatarEntry>(), sha256);
                if (same != null)
                {
                    _log?.Information($"Avatar file {fileName} is already registered as {same.Id}.");
                    return AvatarImportResult.Duplicate(same);
                }

                AvatarModelInfo info = await ReadModelAsync(id, cancellationToken);
                if (confirm != null && !await confirm(info))
                {
                    _log?.Information($"Registering avatar file {fileName} was declined.");
                    return AvatarImportResult.UserDeclined();
                }

                cancellationToken.ThrowIfCancellationRequested();
                AvatarProfile profile = CreateProfile(id, info, fileName, sha256, _clock.UtcNow);
                AvatarEntry entry = await Task.Run(() =>
                {
                    _store.WriteImport(id, profile, info.Thumbnail);
                    return _store.Commit(id);
                });
                committed = true;
                _log?.Information($"Registered avatar {id} from {fileName}: {info.Format} {info.FormatVersion}{(info.Converted ? " (converted)" : string.Empty)}.");
                return AvatarImportResult.Succeeded(entry);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                AvatarProblem problem = ProblemOf(exception);
                _log?.Warning($"Registering avatar file {fileName} failed: {problem}.", exception);
                return AvatarImportResult.Failed(problem);
            }
            finally
            {
                if (!committed)
                {
                    TryDeleteImport(id);
                }
            }
        }

        /// <summary>The problem to show for an exception during registration or loading. Pure.</summary>
        public static AvatarProblem ProblemOf(Exception exception)
        {
            switch (exception)
            {
                case AvatarLoadException load:
                    return load.Problem;
                case IOException _:
                case UnauthorizedAccessException _:
                    return AvatarProblem.StorageFailed;
                default:
                    return AvatarProblem.LoadFailed;
            }
        }

        /// <summary>The profile of a new avatar (avatar detailed design 6.3). Pure.</summary>
        public static AvatarProfile CreateProfile(string id, AvatarModelInfo info, string sourceFileName, string sha256, DateTimeOffset now)
        {
            return new AvatarProfile
            {
                AvatarId = id,
                DisplayName = string.IsNullOrWhiteSpace(info.Name) ? Path.GetFileNameWithoutExtension(sourceFileName) : info.Name.Trim(),
                Format = info.Format,
                FormatVersion = info.FormatVersion,
                Converted = info.Converted,
                SourceFileName = sourceFileName,
                Sha256 = sha256,
                ImportedAt = now.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
                Thumbnail = info.Thumbnail == null ? null : AvatarStore.ThumbnailFileName + info.Thumbnail.Extension,
                License = info.License.Clone(),
            };
        }

        private static AvatarProblem CheckFile(string path, IReadOnlyList<string> extensions)
        {
            var file = new FileInfo(path);
            if (!file.Exists)
            {
                throw new FileNotFoundException("The picked file does not exist.", file.Name);
            }

            var header = new byte[AvatarFileCheck.HeaderLength];
            int read;
            using (FileStream stream = file.OpenRead())
            {
                read = stream.Read(header, 0, header.Length);
            }

            if (read < header.Length)
            {
                header = null;
            }

            return AvatarFileCheck.Check(file.Extension, file.Length, header, extensions);
        }

        private async Task<AvatarModelInfo> ReadModelAsync(string id, CancellationToken cancellationToken)
        {
            string model = Path.Combine(_store.ImportFolderOf(id), AvatarStore.ModelFile(_loader.Format));
            using (IAvatarModel loaded = await _loader.LoadAsync(model, cancellationToken))
            {
                IReadOnlyList<AvatarBone> missing = AvatarBones.MissingRequired(loaded.Rig.Has);
                if (missing.Count > 0)
                {
                    throw new AvatarLoadException(AvatarProblem.MissingBones, $"The model lacks the required bones {string.Join(", ", missing)}.");
                }

                return loaded.Info;
            }
        }

        private void TryDeleteImport(string id)
        {
            try
            {
                _store.DeleteImport(id);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                // Removed at the next start by CleanImports.
                _log?.Warning($"Could not delete the temporary folder of avatar {id}; it is removed at the next start.", exception);
            }
        }
    }
}
