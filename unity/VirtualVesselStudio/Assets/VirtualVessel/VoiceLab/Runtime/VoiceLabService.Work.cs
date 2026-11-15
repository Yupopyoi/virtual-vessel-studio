using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using VirtualVessel.Diagnostics.Logging;
using VirtualVessel.ProjectData.Records;
using VirtualVessel.UI.VoiceLab;
using VirtualVessel.VoiceLab.Design;

namespace VirtualVessel.VoiceLab
{
    /// <summary>STEP 1 and STEP 2 left unfinished, kept in VoiceLab/work.json (Voice Lab detailed design 6.6).</summary>
    public sealed partial class VoiceLabService
    {
        private readonly WorkStore _work;

        public CrudResult<VoiceLabWork> LoadWork()
        {
            if (_designer == null)
            {
                return CrudResult<VoiceLabWork>.Fail(CrudError.Invalid, NotSetUpDetail);
            }

            CrudResult<WorkFile> loaded = _work.Load();
            if (!loaded.Ok)
            {
                return CrudResult<VoiceLabWork>.From(loaded.WithoutValue);
            }

            WorkFile file = loaded.Value;
            var candidates = new List<VoiceLabWorkCandidate>();
            lock (_gate)
            {
                // A candidate whose audio was deleted cannot be played or built; the others still can.
                foreach (WorkCandidate kept in file.Candidates.Where(c => !string.IsNullOrEmpty(c.File) && File.Exists(c.File)))
                {
                    VoiceCandidate candidate = WorkStore.FromFile(kept);
                    _candidates[candidate.Id] = candidate;
                    candidates.Add(new VoiceLabWorkCandidate(kept.Number, kept.From, ToScreen(candidate)));
                }
            }

            if (candidates.Count == 0)
            {
                return CrudResult<VoiceLabWork>.Fail(CrudError.NotFound, "The unfinished work's candidates are gone.");
            }

            return CrudResult<VoiceLabWork>.Success(ToScreen(file, candidates));
        }

        public CrudResult SaveWork(VoiceLabWork work)
        {
            if (work == null)
            {
                throw new ArgumentNullException(nameof(work));
            }

            if (_designer == null)
            {
                return CrudResult.Fail(CrudError.Invalid, NotSetUpDetail);
            }

            var kept = new List<WorkCandidate>();
            lock (_gate)
            {
                foreach (VoiceLabWorkCandidate shown in work.Candidates)
                {
                    if (shown.Candidate != null && _candidates.TryGetValue(shown.Candidate.Id, out VoiceCandidate candidate))
                    {
                        kept.Add(WorkStore.ToFile(shown.Number, shown.From, candidate));
                    }
                }
            }

            return _work.Save(ToFile(work, kept, _clock.UtcNow));
        }

        public CrudResult DeleteWork()
        {
            return _work.Delete();
        }

        /// <summary>The work as the screen shows it, from the file and the candidates still there. Pure.</summary>
        public static VoiceLabWork ToScreen(WorkFile file, IReadOnlyList<VoiceLabWorkCandidate> candidates)
        {
            // Without its picked candidate, the work goes back to STEP 1, where another is picked.
            int picked = candidates.Any(c => c.Number == file.Picked) ? file.Picked : 0;
            int step = picked > 0 ? Math.Max(0, Math.Min(1, file.Step)) : 0;
            var request = new VoiceLabRequest(file.Words, file.FreeWords, file.Prompt, file.Line, file.FirstSeed);
            return new VoiceLabWork(step, request, candidates, picked, ToScreen(file.Timbre ?? new Training.TimbreValues()), (file.Held ?? new List<Training.TimbreValues>()).Select(ToScreen).ToArray(), file.SavedAtUtc);
        }

        /// <summary>The file for the work, with the candidates already turned into what the file keeps. Pure.</summary>
        public static WorkFile ToFile(VoiceLabWork work, List<WorkCandidate> candidates, DateTimeOffset savedAt)
        {
            return new WorkFile
            {
                SavedAtUtc = savedAt,
                Step = work.Step,
                Words = new List<string>(work.Request.Words),
                FreeWords = work.Request.FreeWords,
                Prompt = work.Request.Prompt,
                Line = work.Request.Line,
                FirstSeed = work.Request.FirstSeed,
                Candidates = candidates,
                Picked = candidates.Any(c => c.Number == work.Picked) ? work.Picked : 0,
                Timbre = ToSettings(work.Timbre).ToValues(),
                Held = work.Held.Select(h => ToSettings(h).ToValues()).ToList(),
            };
        }

        // Building turns the work into a Training Run, which is kept and offered from there (Voice Lab detailed design
        // 6.6). A failure to forget it only leaves the work to be offered again.
        private void ForgetWork()
        {
            CrudResult deleted = _work.Delete();
            if (!deleted.Ok)
            {
                _log.Warning($"The unfinished work was not forgotten: {deleted.Detail}");
            }
        }
    }
}
