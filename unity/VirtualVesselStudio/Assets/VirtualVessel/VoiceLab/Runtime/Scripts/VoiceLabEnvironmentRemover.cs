using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VirtualVessel.ProjectData.Records;

namespace VirtualVessel.VoiceLab.Scripts
{
    /// <summary>
    /// Deletes the training environment the app built, to free space (Voice Lab detailed design 11; the user's request,
    /// 2026-11-11). Streaming never uses it, so registered voices keep working. Only an environment wholly inside the
    /// folder activation builds in (VoiceActivationFolders.RootIn the chosen location) is deleted: one set up by hand
    /// elsewhere, such as a developer's, is not the app's to delete (CLAUDE.md 26).
    /// </summary>
    public sealed class VoiceLabEnvironmentRemover
    {
        private readonly string _environmentFile;

        /// <param name="environmentFile">The settings file that points at the environment; it goes with it.</param>
        /// <param name="ownedFolder">The environment's folder when the app built it, else null.</param>
        public VoiceLabEnvironmentRemover(string environmentFile, string ownedFolder)
        {
            _environmentFile = environmentFile;
            OwnedFolder = ownedFolder;
        }

        /// <summary>The environment's folder when the app built it; null for one set up elsewhere.</summary>
        public string OwnedFolder { get; }

        /// <summary>
        /// The version folder under <paramref name="managedRoot"/> that holds every part of the environment, or null when
        /// any part is outside it, or the parts are in different versions. The scripts are not a part: they come with the
        /// app, wherever it is installed. Pure.
        /// </summary>
        public static string Owned(VoiceLabEnvironment environment, string managedRoot)
        {
            if (environment == null || string.IsNullOrEmpty(managedRoot))
            {
                return null;
            }

            string root = Path.GetFullPath(managedRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string[] parts = new[] { environment.Python, environment.Rvc, environment.VoxCpmModel, environment.Template, environment.Manifest }
                .Where(p => !string.IsNullOrEmpty(p)).Select(Path.GetFullPath).ToArray();
            if (parts.Length == 0 || !parts.All(p => p.StartsWith(root, StringComparison.OrdinalIgnoreCase)))
            {
                return null;
            }

            string[] versions = parts.Select(p => p.Substring(root.Length).Split(Path.DirectorySeparatorChar)[0]).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            return versions.Length == 1 && versions[0].Length > 0 && parts.All(p => p.Length > root.Length + versions[0].Length) ? root + versions[0] : null;
        }

        /// <summary>The environment's size in bytes, read off the main thread; it has many files. 0 when it is not the app's.</summary>
        public Task<long> SizeAsync(CancellationToken cancellationToken)
        {
            string folder = OwnedFolder;
            return folder == null || !Directory.Exists(folder)
                ? Task.FromResult(0L)
                : Task.Run(() => Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Sum(f =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    return new FileInfo(f).Length;
                }), cancellationToken);
        }

        /// <summary>Deletes the environment and its settings file. Invalid when it is not the app's.</summary>
        public async Task<CrudResult> RemoveAsync()
        {
            string folder = OwnedFolder;
            if (folder == null)
            {
                return CrudResult.Fail(CrudError.Invalid, "The training environment was not built by the app, so it is not deleted.");
            }

            try
            {
                // The settings file goes first: a folder half deleted is then never taken for a working environment.
                if (File.Exists(_environmentFile))
                {
                    File.Delete(_environmentFile);
                }

                await Task.Run(() =>
                {
                    if (Directory.Exists(folder))
                    {
                        Directory.Delete(folder, true);
                    }
                }).ConfigureAwait(false);
                return CrudResult.Success;
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                return CrudResult.Fail(CrudError.StorageFailed, $"Deleting the training environment in {folder} failed: {exception.Message}");
            }
        }
    }
}
