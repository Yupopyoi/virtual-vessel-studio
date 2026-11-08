using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VirtualVessel.Audio;
using VirtualVessel.Core.State;
using VirtualVessel.ProjectData.Records;
using VirtualVessel.UI.Voice;
using VirtualVessel.UI.VoiceLab;

namespace VirtualVessel.UI.Tests
{
    internal sealed class FakeLab : IVoiceLab
    {
        private int _made;

        public VoiceLabAvailability Availability { get; set; } = VoiceLabAvailability.Ready;

        public List<VoiceLabRequest> Requests { get; } = new List<VoiceLabRequest>();

        public VoiceLabShelf Shelf { get; } = new VoiceLabShelf(
            new[]
            {
                new VoiceLabShelfGroup("voice", true, new[] { new VoiceLabShelfWord("female", "voice", true), new VoiceLabShelfWord("male", "voice", false) }),
                new VoiceLabShelfGroup("pitch", true, new[] { new VoiceLabShelfWord("light", "pitch", true), new VoiceLabShelfWord("deep", "pitch", false) }),
                new VoiceLabShelfGroup("quality", false, new[] { new VoiceLabShelfWord("clean", "breath", true), new VoiceLabShelfWord("airy", "breath", false) }),
            },
            new[] { new VoiceLabExample("low", new[] { "male", "deep" }) });

        public bool SamplesMade { get; set; }

        public int SampleRuns { get; private set; }

        public VoiceLabMaking SamplesProgress => default;

        public string SampleFile(string language, string name)
        {
            return SamplesMade ? language + "/" + name : null;
        }

        public Task MakeSamplesAsync(CancellationToken cancellationToken)
        {
            SampleRuns++;
            SamplesMade = true;
            return Task.CompletedTask;
        }

        public List<VoiceLabCandidate> Near { get; } = new List<VoiceLabCandidate>();

        public List<(VoiceLabCandidate Candidate, VoiceLabTimbre Timbre)> Built { get; } = new List<(VoiceLabCandidate, VoiceLabTimbre)>();

        public List<(string RunId, string Name)> Registered { get; } = new List<(string, string)>();

        public VoiceLabBuild BuildResult { get; set; } = new VoiceLabBuild("run-1", VoiceLabBuildState.Completed, false);

        public VoiceLabBuildProgress BuildProgress => new VoiceLabBuildProgress(BuildState, 0f);

        /// <summary>What BuildProgress says; a build in progress for the live screen's guard.</summary>
        public VoiceLabBuildState BuildState { get; set; }

        public int BuildsCancelled { get; private set; }

        /// <summary>What BuiltVoiceFolder gives; null fails as a run without a voice.</summary>
        public string BuiltFolder { get; set; } = "built";

        public CrudResult<string> BuiltVoiceFolder(string runId)
        {
            return BuiltFolder == null ? CrudResult<string>.Fail(CrudError.Invalid, "none") : CrudResult<string>.Success(BuiltFolder);
        }

        public void CancelBuild()
        {
            BuildsCancelled++;
            BuildState = VoiceLabBuildState.None;
        }

        public VoiceLabMaking CandidatesProgress => default;

        public List<int> Counts { get; } = new List<int>();

        public Task<IReadOnlyList<VoiceLabCandidate>> MakeCandidatesAsync(VoiceLabRequest request, int count, VoiceLabCandidate near, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            Near.Add(near);
            Counts.Add(count);
            var made = new List<VoiceLabCandidate>();
            for (int i = 0; i < count; i++, _made++)
            {
                made.Add(new VoiceLabCandidate("c" + _made, "c" + _made + ".wav", 200 + 10 * _made, 180, 300, "instruction"));
            }

            return Task.FromResult<IReadOnlyList<VoiceLabCandidate>>(made);
        }

        /// <summary>When set, previews wait until their source is completed, and cancelling cancels them.</summary>
        public bool HoldPreviews { get; set; }

        public List<(VoiceLabTimbre Timbre, TaskCompletionSource<VoiceLabPreview> Source)> Previews { get; } = new List<(VoiceLabTimbre, TaskCompletionSource<VoiceLabPreview>)>();

        public Task<VoiceLabPreview> PreviewAsync(VoiceLabCandidate candidate, VoiceLabTimbre timbre, CancellationToken cancellationToken)
        {
            var source = new TaskCompletionSource<VoiceLabPreview>();
            Previews.Add((timbre, source));
            if (!HoldPreviews)
            {
                source.SetResult(new VoiceLabPreview("p.wav", candidate.MedianHz));
            }

            cancellationToken.Register(() => source.TrySetCanceled());
            return source.Task;
        }

        /// <summary>The training length of the last build.</summary>
        public VoiceLabTrainingLength BuiltLength { get; private set; }

        public Task<VoiceLabBuild> BuildAsync(VoiceLabCandidate candidate, VoiceLabTimbre timbre, VoiceLabTrainingLength length, CancellationToken cancellationToken)
        {
            Built.Add((candidate, timbre));
            BuiltLength = length;
            return Task.FromResult(BuildResult);
        }

        /// <summary>What Remake puts back; null throws as a missing run does.</summary>
        public VoiceLabRemake RemakeResult { get; set; }

        public CrudResult<VoiceLabRemake> Remake(string runId)
        {
            return RemakeResult != null ? CrudResult<VoiceLabRemake>.Success(RemakeResult) : CrudResult<VoiceLabRemake>.Fail(CrudError.NotFound, "no run");
        }

        public CrudResult Register(string runId, string name)
        {
            lock (Registered)
            {
                Registered.Add((runId, name));
            }

            return CrudResult.Success;
        }
    }

    internal sealed class FakePreview : IAudioPreview
    {
        public List<string> Played { get; } = new List<string>();

        public float? Progress => Played.Count > 0 ? 0.5f : (float?)null;

        public Task<bool> PlayAsync(string path)
        {
            Played.Add(path);
            return Task.FromResult(true);
        }

        public void Stop()
        {
        }
    }

    internal sealed class FakeLibrary : IVoiceLabLibrary
    {
        public List<VoiceLabRunRecord> Runs { get; } = new List<VoiceLabRunRecord>();

        public List<VoiceLabVoiceRecord> Voices { get; } = new List<VoiceLabVoiceRecord>();

        public CrudResult<IReadOnlyList<VoiceLabRunRecord>> ListRuns()
        {
            return CrudResult<IReadOnlyList<VoiceLabRunRecord>>.Success(Runs.ToArray());
        }

        public CrudResult<IReadOnlyList<VoiceLabVoiceRecord>> ListVoices()
        {
            return CrudResult<IReadOnlyList<VoiceLabVoiceRecord>>.Success(Voices.ToArray());
        }

        public CrudResult SetComment(string runId, string comment)
        {
            int i = Runs.FindIndex(r => r.RunId == runId);
            VoiceLabRunRecord r0 = Runs[i];
            Runs[i] = new VoiceLabRunRecord(r0.RunId, r0.CreatedAt, r0.State, r0.Words, r0.FreeWords, r0.Timbre, comment, r0.BaseFile, r0.VoiceNames);
            return CrudResult.Success;
        }

        public CrudResult RenameVoice(string voiceId, string name)
        {
            int i = Voices.FindIndex(v => v.Id == voiceId);
            VoiceLabVoiceRecord v0 = Voices[i];
            Voices[i] = new VoiceLabVoiceRecord(v0.Id, name, v0.RunId, v0.RegisteredAt, v0.MedianHz, v0.BaseFile);
            return CrudResult.Success;
        }

        public CrudResult DeleteRun(string runId)
        {
            Runs.RemoveAll(r => r.RunId == runId);
            return CrudResult.Success;
        }

        /// <summary>When set, deleting a voice fails with it, as storage might.</summary>
        public CrudError DeleteFailure { get; set; }

        public CrudResult DeleteVoice(string voiceId)
        {
            if (DeleteFailure != CrudError.None)
            {
                return CrudResult.Fail(DeleteFailure, "disk full");
            }

            Voices.RemoveAll(v => v.Id == voiceId);
            return CrudResult.Success;
        }
    }

    internal sealed class FakeTrial : IVoiceTrial
    {
        public List<VoiceTrialRequest> Requests { get; } = new List<VoiceTrialRequest>();

        public Task<VoiceTrialResult> ConvertAsync(VoiceTrialRequest request, IProgress<float> progress, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(new VoiceTrialResult($"converted-{Requests.Count}.wav", 200f, 285f, request.AutoPitch ? 6f + request.Adjustment : request.Adjustment));
        }
    }

    /// <summary>The voice conversion as STEP 4 sees it: the choice, on and off, and the pitch.</summary>
    internal sealed class FakeVoice : IVoiceConversion
    {
        private readonly ObservableValue<VoiceConversionState> _state = new ObservableValue<VoiceConversionState>(VoiceConversionState.Off);

        public IReadOnlyObservable<VoiceConversionState> State => _state;

        public CrudResult<IReadOnlyList<VoiceConversionVoice>> ListVoices()
        {
            return CrudResult<IReadOnlyList<VoiceConversionVoice>>.Success(Array.Empty<VoiceConversionVoice>());
        }

        public string VoiceId { get; private set; }

        public void SelectVoice(string voiceId)
        {
            VoiceId = voiceId;
        }

        public void SelectVoiceFolder(string folder)
        {
            VoiceId = "folder:" + folder;
        }

        public bool IsSwitching => false;

        public float Semitones { get; set; }

        public bool AutoPitch { get; set; } = true;

        public float AutoSemitones => 0f;

        public float UserPitchHz => 0f;

        public float VoicePitchHz => 0f;

        public void RemeasurePitch()
        {
        }

        public bool Denoise { get; set; }

        public float LoadProgress => 0f;

        public string FileName => null;

        public (float Position, float Length) FileTime => (0f, 0f);

        public VoiceConversionStats Stats => default;

        public void SetEnabled(bool enabled)
        {
            _state.Set(enabled ? VoiceConversionState.Running : VoiceConversionState.Off);
        }

        public Task<bool> PlayFileAsync(string path)
        {
            return Task.FromResult(false);
        }

        public IReadOnlyList<string> FileExtensions => new[] { "wav" };

        public void StopFile()
        {
        }
    }
}
