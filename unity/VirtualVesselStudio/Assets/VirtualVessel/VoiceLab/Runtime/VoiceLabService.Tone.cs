using System.IO;
using VirtualVessel.ProjectData.Records;
using VirtualVessel.UI.Voice;
using VirtualVessel.Voice.Models;
using VirtualVessel.VoiceLab.Training;

namespace VirtualVessel.VoiceLab
{
    /// <summary>The finish chosen in STEP 4 (Voice detailed design 8.6): kept with the run, or with a registered voice.</summary>
    public sealed partial class VoiceLabService
    {
        public VoiceTone ToneOf(string runId, string voiceFolder)
        {
            string voiceId = VoiceIdOf(voiceFolder);
            if (voiceId != null && _voices != null)
            {
                CrudResult<VoiceModelRecord> voice = _voices.Get(voiceId);
                if (voice.Ok)
                {
                    return voice.Value.GetTone();
                }
            }

            if (runId != null && _runs != null)
            {
                CrudResult<TrainingRun> run = _runs.Get(runId);
                if (run.Ok)
                {
                    return run.Value.Design?.Tone?.ToTone() ?? default;
                }
            }

            return default;
        }

        public CrudResult KeepTone(string runId, string voiceFolder, VoiceTone tone)
        {
            if (_runs == null || _voices == null)
            {
                return CrudResult.Fail(CrudError.Invalid, "Voice Lab is not set up.");
            }

            string voiceId = VoiceIdOf(voiceFolder);
            if (voiceId != null)
            {
                CrudResult kept = _voices.SetTone(voiceId, tone);
                if (!kept.Ok)
                {
                    return kept;
                }
            }

            if (runId == null)
            {
                return voiceId != null ? CrudResult.Success : CrudResult.Fail(CrudError.Invalid, "There is no run or voice to keep the finish with.");
            }

            CrudResult<TrainingRun> run = _runs.Get(runId);
            if (!run.Ok)
            {
                // A registered voice whose run was deleted keeps its finish with the voice alone.
                return voiceId != null && run.Error == CrudError.NotFound ? CrudResult.Success : run.WithoutValue;
            }

            return _runs.Save(run.Value.WithTone(ToneValues.From(tone))).WithoutValue;
        }

        /// <summary>A registered voice's ID from its folder, or null for another folder, such as a run's output. Pure.</summary>
        public static string VoiceIdOf(string voiceFolder)
        {
            if (string.IsNullOrEmpty(voiceFolder))
            {
                return null;
            }

            string name = Path.GetFileName(voiceFolder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            return VoiceModelStore.IsVoiceId(name) ? name : null;
        }
    }
}
