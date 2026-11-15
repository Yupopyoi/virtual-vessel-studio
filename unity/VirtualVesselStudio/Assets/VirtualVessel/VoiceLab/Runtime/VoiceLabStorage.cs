using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using VirtualVessel.ProjectData.Records;
using VirtualVessel.UI.VoiceLab;
using VirtualVessel.VoiceLab.Training;

namespace VirtualVessel.VoiceLab
{
    /// <summary>What Voice Lab is doing now, so nothing it uses is deleted under it.</summary>
    public readonly struct VoiceLabActivity
    {
        public VoiceLabActivity(bool makingSamples, string session)
        {
            MakingSamples = makingSamples;
            Session = session;
        }

        public bool MakingSamples { get; }

        /// <summary>This session's folder of candidates and previews; null before Voice Lab is set up.</summary>
        public string Session { get; }
    }

    /// <summary>
    /// What Voice Lab keeps that can be deleted to free space (Voice Lab detailed design 8.6; the user's request,
    /// 2026-11-11): STEP 1's samples, which can be made again, and earlier sessions' candidates and previews. A
    /// candidate a run was built from stays, so Make again still finds its base. CRUD: it only reports failures
    /// (system design 6.31).
    /// </summary>
    public sealed class VoiceLabStorage
    {
        public const string SamplesFolderName = "Samples";
        public const string DesignsFolderName = "Designs";

        private readonly string _samples;
        private readonly string _designs;
        private readonly ITrainingRunRepository _runs;
        private readonly Func<VoiceLabActivity> _activity;

        public VoiceLabStorage(string voiceLabFolder, ITrainingRunRepository runs, Func<VoiceLabActivity> activity)
        {
            string root = voiceLabFolder ?? throw new ArgumentNullException(nameof(voiceLabFolder));
            _samples = Path.Combine(root, SamplesFolderName);
            _designs = Path.Combine(root, DesignsFolderName);
            _runs = runs ?? throw new ArgumentNullException(nameof(runs));
            _activity = activity ?? (() => default);
        }

        public CrudResult<VoiceLabStorageUse> Use()
        {
            try
            {
                CrudResult<IReadOnlyList<string>> candidates = OldCandidates();
                return candidates.Ok
                    ? CrudResult<VoiceLabStorageUse>.Success(new VoiceLabStorageUse(FolderBytes(_samples), candidates.Value.Sum(FileBytes)))
                    : CrudResult<VoiceLabStorageUse>.From(candidates.WithoutValue);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                return CrudResult<VoiceLabStorageUse>.Fail(CrudError.StorageFailed, exception.Message);
            }
        }

        /// <summary>Busy while the samples are being made.</summary>
        public CrudResult DeleteSamples()
        {
            if (_activity().MakingSamples)
            {
                return CrudResult.Fail(CrudError.Busy, "The samples are being made.");
            }

            try
            {
                if (Directory.Exists(_samples))
                {
                    Directory.Delete(_samples, true);
                }

                return CrudResult.Success;
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                return CrudResult.Fail(CrudError.StorageFailed, $"Deleting the samples in {_samples} failed: {exception.Message}");
            }
        }

        /// <summary>Earlier sessions' candidates and previews, but those runs were built from; folders left empty go too.</summary>
        public CrudResult DeleteOldCandidates()
        {
            CrudResult<IReadOnlyList<string>> files = OldCandidates();
            if (!files.Ok)
            {
                return files.WithoutValue;
            }

            try
            {
                foreach (string file in files.Value)
                {
                    File.Delete(file);
                }

                string session = _activity().Session;
                foreach (string folder in Directory.Exists(_designs) ? Directory.GetDirectories(_designs, "*", SearchOption.AllDirectories).OrderByDescending(f => f.Length) : Enumerable.Empty<string>())
                {
                    if (!Inside(folder, session) && !Directory.EnumerateFileSystemEntries(folder).Any())
                    {
                        Directory.Delete(folder);
                    }
                }

                return CrudResult.Success;
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                return CrudResult.Fail(CrudError.StorageFailed, $"Deleting candidates in {_designs} failed: {exception.Message}");
            }
        }

        /// <summary>
        /// The files that can go: those not kept, and not in this session's folder. Paths compare as Windows does,
        /// whole and without case. Pure.
        /// </summary>
        public static IReadOnlyList<string> Deletable(IEnumerable<string> files, IEnumerable<string> kept, string session)
        {
            var keep = new HashSet<string>(kept.Where(k => !string.IsNullOrEmpty(k)).Select(Full), StringComparer.OrdinalIgnoreCase);
            return files.Where(f => !keep.Contains(Full(f)) && !Inside(f, session)).ToArray();
        }

        private CrudResult<IReadOnlyList<string>> OldCandidates()
        {
            CrudResult<IReadOnlyList<TrainingRun>> runs = _runs.List();
            if (!runs.Ok)
            {
                return CrudResult<IReadOnlyList<string>>.From(runs.WithoutValue);
            }

            IEnumerable<string> kept = runs.Value.Select(r => r.Design).Where(d => d != null).SelectMany(d => new[] { d.Candidate, d.Reference });
            IEnumerable<string> files = Directory.Exists(_designs) ? Directory.GetFiles(_designs, "*", SearchOption.AllDirectories) : Array.Empty<string>();
            return CrudResult<IReadOnlyList<string>>.Success(Deletable(files, kept, _activity().Session));
        }

        private static bool Inside(string path, string folder)
        {
            if (string.IsNullOrEmpty(folder))
            {
                return false;
            }

            string full = Full(path);
            string root = Full(folder).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return full.StartsWith(root, StringComparison.OrdinalIgnoreCase) || string.Equals(full + Path.DirectorySeparatorChar, root, StringComparison.OrdinalIgnoreCase);
        }

        private static string Full(string path)
        {
            return Path.GetFullPath(path);
        }

        private static long FolderBytes(string folder)
        {
            return Directory.Exists(folder) ? Directory.GetFiles(folder, "*", SearchOption.AllDirectories).Sum(FileBytes) : 0L;
        }

        private static long FileBytes(string file)
        {
            var info = new FileInfo(file);
            return info.Exists ? info.Length : 0L;
        }
    }
}
