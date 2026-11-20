using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using NUnit.Framework;
using VirtualVessel.Audio.Clips;
using VirtualVessel.Audio.Playback;
using VirtualVessel.Core.Lifecycle;
using VirtualVessel.Core.Time;
using VirtualVessel.ProjectData.Records;

namespace VirtualVessel.Audio.Tests
{
    /// <summary>The registered BGM and SE, and their players (BGM and SE detailed design 14).</summary>
    public sealed class AudioClipLibraryTests
    {
        private string _root;
        private string _sources;
        private AudioClipLibrary _library;
        private int _ids;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "vv-clips-" + Guid.NewGuid().ToString("N"));
            _sources = Path.Combine(_root, "from");
            Directory.CreateDirectory(_sources);
            _library = new AudioClipLibrary(Path.Combine(_root, "Audio"), new FixedClock(), Probe, () => (++_ids).ToString("x4"));
        }

        [TearDown]
        public void TearDown()
        {
            Directory.Delete(_root, true);
        }

        // A file's length is in its name, so the tests need no real audio: "a-45.mp3" is 45 seconds; "bad" cannot be read.
        private static AudioFileInfo Probe(string path)
        {
            string name = Path.GetFileNameWithoutExtension(path);
            if (name.Contains("bad"))
            {
                throw new InvalidDataException("Cannot be decoded.");
            }

            return new AudioFileInfo(TimeSpan.FromSeconds(int.Parse(name.Split('-').Last())), 44100, 2);
        }

        private string Source(string name)
        {
            string path = Path.Combine(_sources, name);
            File.WriteAllBytes(path, new byte[] { 1, 2, 3 });
            return path;
        }

        [Test]
        public void ABgm_IsCopiedIn_Looped_AndListed()
        {
            int changed = 0;
            _library.Changed += kind => changed += kind == AudioClipKind.Bgm ? 1 : 0;

            CrudResult<AudioClipRecord> added = _library.AddAsync(AudioClipKind.Bgm, Source("Fluffy-90.mp3"), null, CancellationToken.None).GetAwaiter().GetResult();

            Assert.That(added.Ok, Is.True, added.Detail);
            Assert.That(added.Value.Id, Does.StartWith("bgm-"));
            Assert.That(added.Value.Name, Is.EqualTo("Fluffy-90"), "Without a name, the file's.");
            Assert.That(added.Value.Loop, Is.True, "BGM loops by default.");
            Assert.That(added.Value.Duration, Is.EqualTo(TimeSpan.FromSeconds(90)));
            Assert.That(added.Value.OriginalName, Is.EqualTo("Fluffy-90.mp3"));
            Assert.That(File.Exists(added.Value.FilePath) && added.Value.FilePath != Path.Combine(_sources, "Fluffy-90.mp3"), Is.True, "Played from the copy.");
            Assert.That(Path.GetFileName(added.Value.FilePath), Is.EqualTo("audio.mp3"));
            Assert.That(_library.List(AudioClipKind.Bgm).Value.Select(c => c.Id), Is.EqualTo(new[] { added.Value.Id }));
            Assert.That(_library.List(AudioClipKind.SoundEffect).Value, Is.Empty, "Each kind is listed apart.");
            Assert.That(changed, Is.EqualTo(1));
        }

        [Test]
        public void WhatCannotBeRegistered_SaysWhy()
        {
            Assert.That(_library.CheckAsync(AudioClipKind.Bgm, Source("x-10.ogg"), CancellationToken.None).Result, Is.EqualTo(AudioClipProblem.Unsupported));
            Assert.That(_library.CheckAsync(AudioClipKind.Bgm, Source("bad-10.wav"), CancellationToken.None).Result, Is.EqualTo(AudioClipProblem.Unreadable));
            Assert.That(_library.CheckAsync(AudioClipKind.SoundEffect, Source("long-31.wav"), CancellationToken.None).Result, Is.EqualTo(AudioClipProblem.TooLong));
            Assert.That(_library.CheckAsync(AudioClipKind.Bgm, Source("long-31.wav"), CancellationToken.None).Result, Is.EqualTo(AudioClipProblem.None), "BGM may be long.");
            Assert.That(_library.CheckAsync(AudioClipKind.SoundEffect, Source("Ding-2.WAV"), CancellationToken.None).Result, Is.EqualTo(AudioClipProblem.None), "The extension's case does not matter.");

            CrudResult<AudioClipRecord> refused = _library.AddAsync(AudioClipKind.SoundEffect, Source("long-31.wav"), "Long", CancellationToken.None).GetAwaiter().GetResult();
            Assert.That(refused.Error, Is.EqualTo(CrudError.Invalid));
            Assert.That(Directory.Exists(Path.Combine(_root, "Audio", "SE")) && Directory.GetDirectories(Path.Combine(_root, "Audio", "SE")).Any(), Is.False, "Nothing is copied.");
        }

        [Test]
        public void Changes_AreKept_WithFieldsThisVersionDoesNotKnow()
        {
            AudioClipRecord added = _library.AddAsync(AudioClipKind.SoundEffect, Source("Ding-2.wav"), "Ding", CancellationToken.None).GetAwaiter().GetResult().Value;
            Assert.That(added.Loop, Is.False, "SE does not loop.");
            string profile = Path.Combine(Path.GetDirectoryName(added.FilePath), AudioClipLibrary.ProfileFile);
            File.WriteAllText(profile, File.ReadAllText(profile).Replace("\"name\"", "\"shortcut\": \"F1\",\n  \"name\""));

            CrudResult<AudioClipRecord> updated = _library.Update(added.Id, new AudioClipChange(name: " Chime ", gainDb: 40f, loop: true));

            Assert.That(updated.Ok, Is.True, updated.Detail);
            Assert.That(updated.Value.Name, Is.EqualTo("Chime"));
            Assert.That(updated.Value.GainDb, Is.EqualTo(AudioSourceOptions.MaxGainDb), "Clamped.");
            Assert.That(updated.Value.Loop, Is.False, "Loop is BGM's only.");
            Assert.That(File.ReadAllText(profile), Does.Contain("\"shortcut\""), "A later version's field is kept (CLAUDE.md 25).");
            Assert.That(_library.Update(added.Id, new AudioClipChange(name: "  ")).Error, Is.EqualTo(CrudError.Invalid));
            Assert.That(_library.Update(added.Id, new AudioClipChange(name: new string('a', 61))).Error, Is.EqualTo(CrudError.Invalid));
            Assert.That(_library.Update("se-none", new AudioClipChange(gainDb: 0f)).Error, Is.EqualTo(CrudError.NotFound));
        }

        [Test]
        public void APlayingTrack_IsNotDeleted_ButOthersAre()
        {
            AudioClipRecord playing = _library.AddAsync(AudioClipKind.Bgm, Source("a-60.mp3"), "A", CancellationToken.None).GetAwaiter().GetResult().Value;
            AudioClipRecord other = _library.AddAsync(AudioClipKind.Bgm, Source("b-60.mp3"), "B", CancellationToken.None).GetAwaiter().GetResult().Value;
            _library.InUse = id => id == playing.Id;

            Assert.That(_library.Delete(playing.Id).Error, Is.EqualTo(CrudError.Busy));
            Assert.That(_library.Delete(other.Id).Ok, Is.True);
            Assert.That(_library.List(AudioClipKind.Bgm).Value.Select(c => c.Id), Is.EqualTo(new[] { playing.Id }));
            Assert.That(_library.Delete(other.Id).Ok, Is.True, "Deleting what is not there succeeds.");
            Assert.That(_library.Delete("../x").Error, Is.EqualTo(CrudError.Invalid));
        }

        [Test]
        public void AHalfCopiedFolder_IsNotListed()
        {
            Directory.CreateDirectory(Path.Combine(_root, "Audio", "BGM", "bgm-9999.part"));
            File.WriteAllText(Path.Combine(_root, "Audio", "BGM", "bgm-9999.part", AudioClipLibrary.ProfileFile), "{}");

            Assert.That(_library.List(AudioClipKind.Bgm).Value, Is.Empty);
            Assert.That(AudioClipLibrary.KindOf("bgm-1"), Is.EqualTo(AudioClipKind.Bgm));
            Assert.That(AudioClipLibrary.KindOf("se-1"), Is.EqualTo(AudioClipKind.SoundEffect));
            Assert.That(AudioClipLibrary.KindOf("voice-1"), Is.Null);
            Assert.That(AudioClipLibrary.KindOf("bgm-"), Is.Null);
        }

        [Test]
        public void Moving_KeepsTheUsersOrder_WithNewOnesAfter()
        {
            string[] ids = new[] { "a-1.wav", "b-1.wav", "c-1.wav" }
                .Select(name => _library.AddAsync(AudioClipKind.SoundEffect, Source(name), null, CancellationToken.None).GetAwaiter().GetResult().Value.Id)
                .ToArray();
            int changed = 0;
            _library.Changed += kind => changed += kind == AudioClipKind.SoundEffect ? 1 : 0;

            Assert.That(_library.Move(ids[2], 0).Ok, Is.True);

            Assert.That(_library.List(AudioClipKind.SoundEffect).Value.Select(c => c.Id), Is.EqualTo(new[] { ids[2], ids[0], ids[1] }));
            Assert.That(changed, Is.EqualTo(1));
            string added = _library.AddAsync(AudioClipKind.SoundEffect, Source("d-1.wav"), null, CancellationToken.None).GetAwaiter().GetResult().Value.Id;
            Assert.That(_library.List(AudioClipKind.SoundEffect).Value.Select(c => c.Id), Is.EqualTo(new[] { ids[2], ids[0], ids[1], added }), "A new one comes last.");
            Assert.That(_library.Delete(ids[0]).Ok, Is.True);
            Assert.That(_library.Move(added, 99).Ok, Is.True, "Past the end is last.");
            Assert.That(_library.List(AudioClipKind.SoundEffect).Value.Select(c => c.Id), Is.EqualTo(new[] { ids[2], ids[1], added }), "A deleted one leaves no gap.");
            Assert.That(File.Exists(Path.Combine(_root, "Audio", "SE", AudioClipLibrary.OrderFile)), Is.True);
            Assert.That(_library.List(AudioClipKind.Bgm).Value, Is.Empty, "The order file is not a registration.");
            Assert.That(_library.Move("se-none", 0).Error, Is.EqualTo(CrudError.NotFound));
            Assert.That(_library.Move("../x", 0).Error, Is.EqualTo(CrudError.Invalid));
        }

        [Test]
        public void Arrange_PutsThePlacedFirst_ThenByWhenAdded()
        {
            var early = DateTimeOffset.Parse("2026-11-01T00:00:00Z");
            AudioClipRecord Clip(string id, int day) => new AudioClipRecord(id, AudioClipKind.Bgm, id, 0f, true, TimeSpan.Zero, id, early.AddDays(day), id);
            AudioClipRecord[] records = { Clip("bgm-a", 2), Clip("bgm-b", 1), Clip("bgm-c", 3), Clip("bgm-d", 0) };

            Assert.That(AudioClipLibrary.Arrange(records, new[] { "bgm-c", "bgm-gone", "bgm-a" }).Select(c => c.Id), Is.EqualTo(new[] { "bgm-c", "bgm-a", "bgm-d", "bgm-b" }));
            Assert.That(AudioClipLibrary.Arrange(records, null).Select(c => c.Id), Is.EqualTo(new[] { "bgm-d", "bgm-b", "bgm-a", "bgm-c" }), "Without an order, when they were added.");
            Assert.That(AudioClipLibrary.Moved(new[] { "a", "b", "c" }, "a", 2), Is.EqualTo(new[] { "b", "c", "a" }));
            Assert.That(AudioClipLibrary.Moved(new[] { "a", "b", "c" }, "c", -1), Is.EqualTo(new[] { "c", "a", "b" }), "Clamped.");
            Assert.That(AudioClipLibrary.Moved(new[] { "a", "b" }, "x", 0), Is.EqualTo(new[] { "a", "b" }), "Unknown IDs leave it as it was.");
        }

        private sealed class FixedClock : ISystemClock
        {
            private DateTimeOffset _now = new DateTimeOffset(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);

            public DateTimeOffset UtcNow
            {
                get
                {
                    // Each registration a second later, so they list in the order they were added.
                    _now = _now.AddSeconds(1);
                    return _now;
                }
            }
        }
    }

    public sealed class AudioPlayersTests
    {
        [Test]
        public void SingleTrack_PlaysOnce_ThenEnds()
        {
            var sequence = new SingleTrack("bgm-1", loop: false);

            BgmCue? first = sequence.Next(null);

            Assert.That(first?.TrackId, Is.EqualTo("bgm-1"));
            Assert.That(sequence.Next(first), Is.Null);
        }

        [Test]
        public void Progress_GoesRoundALoop()
        {
            Assert.That(BgmPlayer.ProgressOf(250, 1000, loop: false), Is.EqualTo(0.25f));
            Assert.That(BgmPlayer.ProgressOf(2250, 1000, loop: true), Is.EqualTo(0.25f));
            Assert.That(BgmPlayer.ProgressOf(2250, 1000, loop: false), Is.EqualTo(1f));
            Assert.That(BgmPlayer.ProgressOf(10, 0, loop: false), Is.Null, "Unknown length.");
        }

        [Test]
        public void TheDeck_IsShownForThePlayAsked()
        {
            Assert.That(BgmPlayer.StateOf(new BgmEngineStatus(BgmEngineState.Playing, 0, 1, 0, 0, 0, 0), 2), Is.EqualTo(BgmPlayState.Starting), "An earlier play is not this one.");
            Assert.That(BgmPlayer.StateOf(new BgmEngineStatus(BgmEngineState.Playing, 0, 2, 0, 0, 0, 0), 2), Is.EqualTo(BgmPlayState.Playing));
            Assert.That(BgmPlayer.StateOf(new BgmEngineStatus(BgmEngineState.Failed, 1, 2, 0, 0, 0, 0), 2), Is.EqualTo(BgmPlayState.Failed));
        }

        [Test]
        public void ABgm_Plays_FollowsTheDeck_AndStops()
        {
            var engine = new FakePlayerEngine();
            var library = new FakeLibrary();
            library.Add("bgm-1", @"C:\a.mp3", -3f);
            var loop = new FrameLoop();
            var clock = new ManualClock();
            var player = new BgmPlayer(() => engine, library, loop, clock, null);
            player.InitializeAsync(CancellationToken.None).Wait();

            player.Play(new SingleTrack("bgm-1", loop: true));

            Assert.That(engine.Played, Is.EqualTo((@"C:\a.mp3", true, -3f)), "The copy's path, the loop, and the track's volume.");
            Assert.That(player.Status.Value, Is.EqualTo(new BgmStatus(BgmPlayState.Starting, "bgm-1")));
            Assert.That(player.Status.Value.HoldsTrack, Is.True);

            engine.Status = new BgmEngineStatus(BgmEngineState.Playing, 0, engine.Generation, 0, 480, 4800, 0);
            Tick(loop, clock);
            Assert.That(player.Status.Value.State, Is.EqualTo(BgmPlayState.Playing));
            Assert.That(player.Progress, Is.EqualTo(0.1f));

            library.Add("bgm-1", @"C:\a.mp3", -9f);
            library.Raise(AudioClipKind.Bgm);
            Assert.That(engine.Gain, Is.EqualTo(-9f), "A volume changed while it plays is heard at once.");

            player.Stop();
            Assert.That(engine.Stopped, Is.True);
            Assert.That(player.Status.Value.State, Is.EqualTo(BgmPlayState.Stopping));
            engine.Status = new BgmEngineStatus(BgmEngineState.Idle, 0, engine.Generation, 0, 0, 0, 0);
            Tick(loop, clock);
            Assert.That(player.Status.Value, Is.EqualTo(new BgmStatus(BgmPlayState.Idle, null)));
            Assert.That(player.Status.Value.HoldsTrack, Is.False);
        }

        [Test]
        public void ATrackThatEnds_AsksTheSequenceForTheNext()
        {
            var engine = new FakePlayerEngine();
            var library = new FakeLibrary();
            library.Add("bgm-1", @"C:\a.mp3", 0f);
            library.Add("bgm-2", @"C:\b.mp3", 0f);
            var loop = new FrameLoop();
            var clock = new ManualClock();
            var player = new BgmPlayer(() => engine, library, loop, clock, null);
            player.InitializeAsync(CancellationToken.None).Wait();

            // A playlist of two, as a later version would give: the player itself does not change.
            player.Play(new TwoTracks());
            long first = engine.Generation;
            engine.Status = new BgmEngineStatus(BgmEngineState.Idle, 0, first, first, 4800, 4800, 0);
            Tick(loop, clock);

            Assert.That(engine.Played.Path, Is.EqualTo(@"C:\b.mp3"), "The next track plays.");
            Assert.That(player.Status.Value.TrackId, Is.EqualTo("bgm-2"));

            engine.Status = new BgmEngineStatus(BgmEngineState.Idle, 0, engine.Generation, engine.Generation, 4800, 4800, 0);
            Tick(loop, clock);
            Assert.That(player.Status.Value.State, Is.EqualTo(BgmPlayState.Idle), "And it ends after the last.");
        }

        [Test]
        public void ATrackThatIsGone_Fails_WithoutAskingTheDeck()
        {
            var engine = new FakePlayerEngine();
            var player = new BgmPlayer(() => engine, new FakeLibrary(), new FrameLoop(), new ManualClock(), null);

            player.Play(new SingleTrack("bgm-gone", loop: false));

            Assert.That(player.Status.Value, Is.EqualTo(new BgmStatus(BgmPlayState.Failed, "bgm-gone")));
            Assert.That(engine.Played.Path, Is.Null);
            Assert.That(player.Status.Value.HoldsTrack, Is.False, "A failed track can be deleted.");
        }

        [Test]
        public void SoundEffects_LoadWhatIsRegistered_AndPlayIt()
        {
            var engine = new FakePlayerEngine();
            var library = new FakeLibrary();
            library.Add("se-1", @"C:\ding.wav", -6f);
            library.Add("se-2", @"C:\bad.wav", 0f);
            var player = new SoundEffectPlayer(() => engine, library, null);
            player.InitializeAsync(CancellationToken.None).Wait();
            player.Syncing.Wait();

            Assert.That(player.IsReady("se-1"), Is.True);
            Assert.That(player.IsReady("se-2"), Is.False, "One that cannot be loaded is left out.");
            player.Play("se-1");
            Assert.That(engine.EffectsPlayed, Is.EqualTo(new[] { (0, -6f) }));
            player.Play("se-2");
            Assert.That(engine.EffectsPlayed.Count, Is.EqualTo(1), "Not loaded: not played.");

            library.Remove("se-1");
            library.Raise(AudioClipKind.SoundEffect);
            player.Syncing.Wait();
            Assert.That(player.IsReady("se-1"), Is.False);
            Assert.That(engine.Unloaded, Is.EqualTo(new[] { 0 }), "A deleted effect is given back.");
            player.ShutdownAsync(CancellationToken.None).Wait();
        }

        [Test]
        public void ThePlan_LoadsNewAndMoved_AndUnloadsGone()
        {
            var loaded = new Dictionary<string, string> { ["se-1"] = "a", ["se-2"] = "b" };
            var registered = new[] { Record("se-1", "a"), Record("se-2", "b2"), Record("se-3", "c") };

            (IReadOnlyList<AudioClipRecord> load, IReadOnlyList<string> unload) = SoundEffectPlayer.Plan(loaded, registered.Take(3).Where(r => r.Id != "se-1").ToArray());

            Assert.That(load.Select(r => r.Id), Is.EqualTo(new[] { "se-2", "se-3" }));
            Assert.That(unload, Is.EqualTo(new[] { "se-1" }));
        }

        [Test]
        public void TheService_MakesBgmAndSeSources_FromThePlayers()
        {
            var engine = new FakePlayerEngine();
            int next = 0;
            var service = new AudioService(new AudioSettings(), null, () => engine, new FrameLoop(), new ManualClock(), () => (++next).ToString("x32"), null, null);
            service.InitializeAsync(CancellationToken.None).Wait();

            Assert.That(service.Sources.Select(s => s.Role), Is.EqualTo(new[] { AudioSourceRole.Microphone, AudioSourceRole.GameAudio, AudioSourceRole.Music, AudioSourceRole.SoundEffect }));
            Assert.That(engine.Players, Is.EqualTo(new[] { AudioPlayer.Bgm, AudioPlayer.SoundEffects }));
            Assert.That(engine.Devices.Count, Is.EqualTo(2), "Only the devices' sources get a device.");
            Assert.That(service.PlayerEngine, Is.SameAs(engine));
            service.Dispose();
        }

        private static AudioClipRecord Record(string id, string path, float gainDb = 0f)
        {
            return new AudioClipRecord(id, AudioClipLibrary.KindOf(id).Value, id, gainDb, id.StartsWith("bgm"), TimeSpan.FromSeconds(10), id, DateTimeOffset.UnixEpoch, path);
        }

        private static void Tick(FrameLoop loop, ManualClock clock)
        {
            clock.Advance(0.11);
            loop.Tick(e => throw e);
        }

        private sealed class TwoTracks : IBgmSequence
        {
            public BgmCue? Next(BgmCue? finished)
            {
                if (finished == null)
                {
                    return new BgmCue("bgm-1", false);
                }

                return finished.Value.TrackId == "bgm-1" ? new BgmCue("bgm-2", false) : (BgmCue?)null;
            }
        }

        private sealed class FakeLibrary : IAudioClipLibrary
        {
            private readonly Dictionary<string, AudioClipRecord> _records = new Dictionary<string, AudioClipRecord>();

            public event Action<AudioClipKind> Changed;

            public IReadOnlyList<string> FileExtensions => new[] { ".wav", ".mp3" };

            public void Add(string id, string path, float gainDb)
            {
                _records[id] = Record(id, path, gainDb);
            }

            public void Remove(string id)
            {
                _records.Remove(id);
            }

            public void Raise(AudioClipKind kind)
            {
                Changed?.Invoke(kind);
            }

            public CrudResult<IReadOnlyList<AudioClipRecord>> List(AudioClipKind kind)
            {
                return CrudResult<IReadOnlyList<AudioClipRecord>>.Success(_records.Values.Where(r => r.Kind == kind).ToArray());
            }

            public CrudResult<AudioClipRecord> Get(string id)
            {
                return _records.TryGetValue(id, out AudioClipRecord record) ? CrudResult<AudioClipRecord>.Success(record) : CrudResult<AudioClipRecord>.Fail(CrudError.NotFound, "none");
            }

            public System.Threading.Tasks.Task<AudioClipProblem> CheckAsync(AudioClipKind kind, string sourcePath, CancellationToken cancellationToken)
            {
                return System.Threading.Tasks.Task.FromResult(AudioClipProblem.None);
            }

            public System.Threading.Tasks.Task<CrudResult<AudioClipRecord>> AddAsync(AudioClipKind kind, string sourcePath, string name, CancellationToken cancellationToken)
            {
                throw new NotSupportedException();
            }

            public CrudResult<AudioClipRecord> Update(string id, AudioClipChange change)
            {
                throw new NotSupportedException();
            }

            public CrudResult Delete(string id)
            {
                throw new NotSupportedException();
            }

            public CrudResult Move(string id, int position)
            {
                throw new NotSupportedException();
            }
        }

        private sealed class FakePlayerEngine : IAudioEngine, IAudioPlayerEngine
        {
            public List<AudioPlayer> Players { get; } = new List<AudioPlayer>();

            public Dictionary<int, string> Devices { get; } = new Dictionary<int, string>();

            public (string Path, bool Loop, float GainDb) Played { get; private set; }

            public long Generation { get; private set; }

            public float Gain { get; private set; }

            public bool Stopped { get; private set; }

            public BgmEngineStatus Status { get; set; }

            public List<(int, float)> EffectsPlayed { get; } = new List<(int, float)>();

            public List<int> Unloaded { get; } = new List<int>();

            private int _sources;
            private int _effects;

            public int AddPlayerSource(AudioPlayer player)
            {
                Players.Add(player);
                return _sources++;
            }

            public long PlayBgm(string path, bool loop, float gainDb)
            {
                Played = (path, loop, gainDb);
                Stopped = false;
                return ++Generation;
            }

            public void StopBgm()
            {
                Stopped = true;
            }

            public void SetBgmGain(float gainDb)
            {
                Gain = gainDb;
            }

            public BgmEngineStatus GetBgmStatus()
            {
                return Status;
            }

            public AudioEffectLoad LoadEffect(string path)
            {
                return path.Contains("bad") ? new AudioEffectLoad(-1, TimeSpan.Zero, "unreadable") : new AudioEffectLoad(_effects++, TimeSpan.FromSeconds(1), null);
            }

            public void UnloadEffect(int effect)
            {
                Unloaded.Add(effect);
            }

            public bool PlayEffect(int effect, float gainDb)
            {
                EffectsPlayed.Add((effect, gainDb));
                return true;
            }

            public void StopEffects()
            {
            }

            public long EffectsStolen => 0;

            public IReadOnlyList<AudioDevice> ListInputs()
            {
                return Array.Empty<AudioDevice>();
            }

            public IReadOnlyList<AudioDevice> ListOutputs()
            {
                return Array.Empty<AudioDevice>();
            }

            public int AddSource()
            {
                return _sources++;
            }

            public void RemoveSource(int source)
            {
            }

            public void SetDevice(int source, string deviceId)
            {
                Devices[source] = deviceId;
            }

            public void SetGain(int source, float gainDb, bool muted)
            {
            }

            public void SetStreamSend(int source, float sendDb)
            {
            }

            public void SetMonitorSend(int source, float sendDb)
            {
            }

            public bool SetDenoise(int source, bool enabled)
            {
                return true;
            }

            public void SetMonitorDevice(string deviceId)
            {
            }

            public void SetMonitorGain(float gainDb, bool muted)
            {
            }

            public void SetStreamDelay(TimeSpan delay)
            {
            }

            public AudioEngineStatus GetStatus(int source)
            {
                return default;
            }

            public AudioEngineStatus GetMonitorStatus()
            {
                return default;
            }

            public TimeSpan StreamLatency => TimeSpan.Zero;

            public AudioStreamTap RecordingTap => default;

            public AudioStreamTap StreamingTap => default;

            public void Dispose()
            {
            }
        }

        private sealed class ManualClock : IMonotonicClock
        {
            private long _now = 1_000_000;

            public long Frequency => 1_000_000;

            public long GetTimestamp()
            {
                return _now;
            }

            public TimeSpan GetElapsed(long startTimestamp, long endTimestamp)
            {
                return TimeSpan.FromTicks((endTimestamp - startTimestamp) * 10);
            }

            public void Advance(double seconds)
            {
                _now += (long)(seconds * Frequency);
            }
        }
    }
}
