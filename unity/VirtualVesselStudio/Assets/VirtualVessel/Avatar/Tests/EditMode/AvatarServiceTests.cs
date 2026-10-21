using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using VirtualVessel.Core.Lifecycle;
using VirtualVessel.Core.State;
using VirtualVessel.Core.Time;
using VirtualVessel.ProjectData.Config;
using VirtualVessel.Tracking;

namespace VirtualVessel.Avatar.Tests
{
    /// <summary>Choosing, placing, and removing avatars (avatar detailed design 5.1, 8, 12) with a fake loader.</summary>
    public sealed class AvatarServiceTests
    {
        private static readonly byte[] s_glb = { 0x67, 0x6C, 0x54, 0x46, 2, 0, 0, 0 };
        private static readonly Pose s_spawn = new Pose(new Vector3(1f, 0f, 2f), Quaternion.Euler(0f, 180f, 0f));

        private string _root;
        private string _config;
        private FakeLoader _loader;
        private AvatarService _service;
        private int _files;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "vv-avatar-service-" + Guid.NewGuid().ToString("N"));
            _config = Path.Combine(_root, "config");
            Directory.CreateDirectory(_root);
            _loader = new FakeLoader();
        }

        [TearDown]
        public void TearDown()
        {
            _service?.Dispose();
            _loader.DestroyAll();
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }

        [Test]
        public async Task TheFirstAvatarRegistered_IsPutOnTheStream_AtTheSpawnPoint_InTheRestPose()
        {
            _service = await StartAsync();
            Assert.That(_service.State.Value, Is.EqualTo(AvatarState.None));

            AvatarImportResult result = await _service.ImportAsync(Source(), null, CancellationToken.None);

            Assert.That(result.Imported, Is.True);
            Assert.That(_service.Avatars.Value, Has.Count.EqualTo(1));
            Assert.That(_service.CurrentAvatarId.Value, Is.EqualTo(result.Entry.Id));
            Assert.That(_service.State.Value, Is.EqualTo(AvatarState.Ready));
            FakeModel model = (FakeModel)_service.Model;
            Assert.That(model.Visible, Is.True);
            Assert.That(model.Root.position, Is.EqualTo(s_spawn.position));
            Assert.That(model.Child.layer, Is.EqualTo(5), "Every part is on the avatar layer.");
            Assert.That(model.Rotations[AvatarBone.LeftUpperArm], Is.EqualTo(AvatarRestPose.UpperArm(true, AvatarRestPose.DefaultArmDownDegrees)), "Arms down, not the T pose.");
        }

        [Test]
        public async Task TheAvatarInUse_IsSaved_AndLoadedAtTheNextStart()
        {
            _service = await StartAsync();
            AvatarImportResult result = await _service.ImportAsync(Source(), null, CancellationToken.None);
            await _service.ShutdownAsync(CancellationToken.None);
            _service.Dispose();

            _service = await StartAsync();

            Assert.That(_service.CurrentAvatarId.Value, Is.EqualTo(result.Entry.Id));
            Assert.That(_service.State.Value, Is.EqualTo(AvatarState.Ready));
            Assert.That(_service.Model, Is.Not.Null);
        }

        [Test]
        public async Task ASecondAvatar_IsRegisteredWithoutBeingPutInUse_AndSelectingItSwapsTheAvatars()
        {
            _service = await StartAsync();
            AvatarImportResult first = await _service.ImportAsync(Source(), null, CancellationToken.None);
            AvatarImportResult second = await _service.ImportAsync(Source(), null, CancellationToken.None);
            FakeModel old = (FakeModel)_service.Model;

            Assert.That(_service.CurrentAvatarId.Value, Is.EqualTo(first.Entry.Id));

            await _service.SelectAsync(second.Entry.Id, CancellationToken.None);

            Assert.That(_service.CurrentAvatarId.Value, Is.EqualTo(second.Entry.Id));
            Assert.That(old.Disposed, Is.True, "The old avatar goes once the new one is ready.");
            Assert.That(_service.Model, Is.Not.SameAs(old));
        }

        [Test]
        public async Task ASwapThatFails_KeepsTheAvatarOnTheStream()
        {
            _service = await StartAsync();
            AvatarImportResult first = await _service.ImportAsync(Source(), null, CancellationToken.None);
            AvatarImportResult second = await _service.ImportAsync(Source(), null, CancellationToken.None);
            IAvatarModel old = _service.Model;
            _loader.Failure = new AvatarLoadException(AvatarProblem.LoadFailed, "broken");

            await _service.SelectAsync(second.Entry.Id, CancellationToken.None);

            Assert.That(_service.Model, Is.SameAs(old));
            Assert.That(_service.CurrentAvatarId.Value, Is.EqualTo(first.Entry.Id));
            Assert.That(_service.State.Value, Is.EqualTo(AvatarState.Ready));
            Assert.That(_service.Problem.Value, Is.EqualTo(AvatarProblem.LoadFailed));
        }

        [Test]
        public async Task TheAvatarInUse_FailingAtStart_IsAnError_AndAnotherCanBeChosen()
        {
            _service = await StartAsync();
            AvatarImportResult first = await _service.ImportAsync(Source(), null, CancellationToken.None);
            AvatarImportResult second = await _service.ImportAsync(Source(), null, CancellationToken.None);
            await _service.ShutdownAsync(CancellationToken.None);
            _service.Dispose();
            _loader.Failure = new AvatarLoadException(AvatarProblem.LoadFailed, "broken");

            _service = await StartAsync();

            Assert.That(_service.State.Value, Is.EqualTo(AvatarState.Error));
            Assert.That(_service.CurrentAvatarId.Value, Is.EqualTo(first.Entry.Id), "Still the chosen one, so the user sees which failed.");
            _loader.Failure = null;
            await _service.SelectAsync(second.Entry.Id, CancellationToken.None);
            Assert.That(_service.State.Value, Is.EqualTo(AvatarState.Ready));
        }

        [Test]
        public async Task RemovingTheAvatarInUse_TakesItOffTheStream_AndDeletesItsFolder()
        {
            _service = await StartAsync();
            AvatarImportResult result = await _service.ImportAsync(Source(), null, CancellationToken.None);
            FakeModel model = (FakeModel)_service.Model;
            string folder = Path.Combine(_root, "Avatars", result.Entry.Id);

            await _service.RemoveAsync(result.Entry.Id, CancellationToken.None);

            Assert.That(model.Disposed, Is.True);
            Assert.That(_service.Model, Is.Null);
            Assert.That(_service.CurrentAvatarId.Value, Is.Null);
            Assert.That(_service.State.Value, Is.EqualTo(AvatarState.None));
            Assert.That(_service.Avatars.Value, Is.Empty);
            Assert.That(Directory.Exists(folder), Is.False);
        }

        [Test]
        public async Task SelectingNone_TakesTheAvatarAway()
        {
            _service = await StartAsync();
            await _service.ImportAsync(Source(), null, CancellationToken.None);

            await _service.SelectAsync(null, CancellationToken.None);

            Assert.That(_service.Model, Is.Null);
            Assert.That(_service.State.Value, Is.EqualTo(AvatarState.None));
        }

        [Test]
        public async Task AnAvatarInUseThatIsNoLongerRegistered_IsForgotten()
        {
            Directory.CreateDirectory(_config);
            File.WriteAllText(Path.Combine(_config, AvatarSettings.FileName), "{\"schemaVersion\": 1, \"currentAvatarId\": \"" + AvatarStore.NewId() + "\"}");

            _service = await StartAsync();

            Assert.That(_service.CurrentAvatarId.Value, Is.Null);
            Assert.That(_service.State.Value, Is.EqualTo(AvatarState.None));
        }

        [Test]
        public void ListHelpers_AreCopies()
        {
            var a = new AvatarEntry("a", null, null);
            var b = new AvatarEntry("b", null, null);
            IReadOnlyList<AvatarEntry> list = new[] { a };

            Assert.That(AvatarService.With(list, b), Is.EqualTo(new[] { a, b }));
            Assert.That(AvatarService.Without(new[] { a, b }, "a"), Is.EqualTo(new[] { b }));
            Assert.That(list, Is.EqualTo(new[] { a }));
        }

        [Test]
        public async Task FaceTuning_AppliesToTheAvatarAtOnce_AndIsSavedForTheNextStart()
        {
            _service = await StartAsync();
            await _service.ImportAsync(Source(), null, CancellationToken.None);
            FaceTuning tuning = new FaceTuning().With(FaceTuningPart.Eyes, 2f);

            _service.SetFaceTuning(tuning);

            Assert.That(_service.FaceTuning.Value, Is.EqualTo(tuning));
            Assert.That(_service.Controller.Mapping.Blink, Is.EqualTo(ExpressionMapping.Default.Blink.Sensitive(2f)), "The avatar on the stream uses it now.");
            await _service.ShutdownAsync(CancellationToken.None);
            _service.Dispose();

            _service = await StartAsync();
            Assert.That(_service.FaceTuning.Value, Is.EqualTo(tuning), "It belongs to the user and stays across starts.");
            Assert.That(_service.Controller.Mapping.Blink, Is.EqualTo(ExpressionMapping.Default.Blink.Sensitive(2f)));
        }

        [Test]
        public async Task EveryFrame_TheTrackedFaceTurnsTheHead_MirroredByTheTrackingOption()
        {
            var tracking = new FakeTracking();
            var loop = new FrameLoop();
            var clock = new ManualClock();
            _service = await StartAsync(tracking, loop, clock);
            await _service.ImportAsync(Source(), null, CancellationToken.None);
            FakeModel model = (FakeModel)_service.Model;
            Assert.That(model.Rotations.ContainsKey(AvatarBone.Head), Is.False, "Placing leaves the head as it is.");

            FacePose face = tracking.Current.Face;
            face.Status = TrackingStatus.Tracked;
            face.Timestamp = 10.0;
            face.HeadRotation = Quaternion.AngleAxis(30f, Vector3.up);
            clock.Seconds = 10.0;
            loop.Tick(null);
            clock.Seconds = 10.5;
            loop.Tick(null);

            Assert.That(Quaternion.Angle(model.Rotations[AvatarBone.Head], Quaternion.AngleAxis(-30f, Vector3.up)), Is.LessThan(0.01f), "Mirrored by default.");

            tracking.SetOptions(TrackingOptions.Default.WithMirror(false));
            face.Timestamp = 10.6;
            clock.Seconds = 10.6;
            loop.Tick(null);
            clock.Seconds = 11.0;
            loop.Tick(null);
            Assert.That(Quaternion.Angle(model.Rotations[AvatarBone.Head], Quaternion.AngleAxis(30f, Vector3.up)), Is.LessThan(0.01f), "Not mirrored once the option is off.");

            // Ticking also saves the settings on a worker thread; let it finish before the folder goes.
            await _service.ShutdownAsync(CancellationToken.None);
        }

        private async Task<AvatarService> StartAsync(ITrackingSource tracking = null, FrameLoop loop = null, IMonotonicClock clock = null)
        {
            ConfigFileStore<AvatarSettings> store = AvatarService.CreateStore(_config, new UtcSystemClock());
            var saves = new ConfigSaveScheduler<AvatarSettings>(store, new StopwatchMonotonicClock(), TimeSpan.Zero, null);
            var service = new AvatarService(
                _loader,
                new AvatarStore(Path.Combine(_root, "Avatars"), new UtcSystemClock()),
                store.Load().Value,
                saves,
                () => new AvatarPlacement(5, s_spawn),
                loop ?? new FrameLoop(),
                clock ?? new StopwatchMonotonicClock(),
                new UtcSystemClock(),
                null,
                null,
                tracking);
            await service.InitializeAsync(CancellationToken.None);
            return service;
        }

        private string Source()
        {
            // Different bytes each time, so each counts as a different file.
            string path = Path.Combine(_root, $"source{_files}.vrm");
            var bytes = new byte[s_glb.Length + 1];
            s_glb.CopyTo(bytes, 0);
            bytes[s_glb.Length] = (byte)_files++;
            File.WriteAllBytes(path, bytes);
            return path;
        }

        private sealed class FakeLoader : IAvatarLoader
        {
            private readonly List<FakeModel> _made = new List<FakeModel>();

            public Exception Failure { get; set; }

            public string Format => "vrm";

            public IReadOnlyList<string> Extensions { get; } = new[] { ".vrm" };

            public Task<IAvatarModel> LoadAsync(string path, CancellationToken cancellationToken)
            {
                if (Failure != null)
                {
                    throw Failure;
                }

                var model = new FakeModel(new AvatarModelInfo("Fake", "vrm", "1.0", false, null, null));
                _made.Add(model);
                return Task.FromResult<IAvatarModel>(model);
            }

            public void DestroyAll()
            {
                foreach (FakeModel model in _made)
                {
                    model.Dispose();
                }
            }
        }

        private sealed class FakeModel : IAvatarModel, IAvatarRig, IAvatarExpressions
        {
            private readonly GameObject _root = new GameObject("FakeAvatar");

            public FakeModel(AvatarModelInfo info)
            {
                Info = info;
                Child = new GameObject("Part");
                Child.transform.SetParent(_root.transform, false);
            }

            public GameObject Child { get; }

            public bool Visible { get; private set; }

            public bool Disposed { get; private set; }

            public Dictionary<AvatarBone, Quaternion> Rotations { get; } = new Dictionary<AvatarBone, Quaternion>();

            public Transform Root => _root.transform;

            public IAvatarRig Rig => this;

            public IAvatarExpressions Expressions => this;

            public IAvatarFaceShapes FaceShapes => null;

            public AvatarModelInfo Info { get; }

            public IReadOnlyList<string> CustomNames { get; } = Array.Empty<string>();

            public bool Has(AvatarBone bone)
            {
                return true;
            }

            public float BoneLength(AvatarBone bone)
            {
                return 0.3f;
            }

            public void SetRotation(AvatarBone bone, Quaternion rotation)
            {
                Rotations[bone] = rotation;
            }

            public void SetRootPosition(Vector3 position)
            {
            }

            public void ResetPose()
            {
                Rotations.Clear();
            }

            public bool Has(AvatarExpression expression)
            {
                return true;
            }

            public void SetWeight(AvatarExpression expression, float weight)
            {
            }

            public void SetLook(float yaw, float pitch)
            {
            }

            public void SetVisible(bool visible)
            {
                Visible = visible;
            }

            public void Apply()
            {
            }

            public void Dispose()
            {
                Disposed = true;
                if (_root != null)
                {
                    UnityEngine.Object.DestroyImmediate(_root);
                }
            }
        }
    
        private sealed class ManualClock : IMonotonicClock
        {
            public double Seconds { get; set; }

            public long Frequency => 1_000_000;

            public long GetTimestamp()
            {
                return (long)(Seconds * Frequency);
            }

            public TimeSpan GetElapsed(long startTimestamp, long endTimestamp)
            {
                return TimeSpan.FromSeconds((endTimestamp - startTimestamp) / (double)Frequency);
            }
        }

        private sealed class FakeTracking : ITrackingSource
        {
            private readonly ObservableValue<TrackingState> _state = new ObservableValue<TrackingState>(TrackingState.Running);
            private readonly ObservableValue<TrackingProblem> _problem = new ObservableValue<TrackingProblem>(TrackingProblem.None);
            private readonly ObservableValue<TrackingOptions> _options = new ObservableValue<TrackingOptions>(TrackingOptions.Default);

            public IReadOnlyObservable<TrackingState> State => _state;

            public IReadOnlyObservable<TrackingProblem> Problem => _problem;

            public TrackingRegions Supported => TrackingRegions.All;

            public IReadOnlyObservable<TrackingOptions> Options => _options;

            public double ObservationsPerSecond => 30;

            public TrackingFrame Current { get; } = new TrackingFrame();

            public void Start()
            {
            }

            public void Stop()
            {
            }

            public void SetOptions(TrackingOptions options)
            {
                _options.Value = options;
            }
        }
    }
}
