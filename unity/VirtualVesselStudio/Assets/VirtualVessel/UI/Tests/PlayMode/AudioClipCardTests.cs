using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using VirtualVessel.Audio;
using VirtualVessel.Audio.Clips;
using VirtualVessel.Audio.Playback;
using VirtualVessel.Core.State;
using VirtualVessel.Core.Time;
using VirtualVessel.ProjectData.Records;
using VirtualVessel.UI.Controls;
using VirtualVessel.UI.Dialogs;
using VirtualVessel.UI.Icons;
using VirtualVessel.UI.Screens;
using Object = UnityEngine.Object;

namespace VirtualVessel.UI.Tests
{
    /// <summary>The BGM and SE Cards on the setup screen's Audio page (BGM and SE detailed design; setup.md 14).</summary>
    public sealed class AudioClipCardTests
    {
        private string _directory;
        private GameObject _host;
        private UiRoot _root;
        private FakeClips _clips;
        private FakeBgm _bgm;
        private FakeEffects _effects;
        private FakeFiles _files;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _directory = Path.Combine(Path.GetTempPath(), "VirtualVesselTests", Guid.NewGuid().ToString("N"));
            _clips = new FakeClips();
            _clips.Add(AudioClipKind.Bgm, "bgm-1", "Fluffy afternoon", 95);
            _clips.Add(AudioClipKind.SoundEffect, "se-1", "Ding", 2);
            _bgm = new FakeBgm();
            _effects = new FakeEffects();
            _files = new FakeFiles();
            _host = new GameObject(nameof(AudioClipCardTests));
            _host.AddComponent<UIDocument>();
            _root = _host.AddComponent<UiRoot>();
            _root.SetAssets(UnityEditor.AssetDatabase.LoadAssetAtPath<UiAssets>("Assets/VirtualVessel/UI/UiAssets.asset"));
            _root.Build(new UiRootContext(_directory, new StopwatchMonotonicClock(), new UtcSystemClock(), null, null, false, screens: new ScreenServices
            {
                Audio = new AudioPanelTests.FakeAudio(),
                SystemSettings = new AudioPanelTests.FakeSettings(),
                AudioClips = _clips,
                Bgm = _bgm,
                SoundEffects = _effects,
                FileDialog = _files,
            }));
            yield return null;
        }

        [TearDown]
        public void TearDown()
        {
            Object.Destroy(_host);
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }

        [UnityTest]
        public IEnumerator ABgm_Plays_ShowsItPlays_AndCannotBeDeletedThen()
        {
            AudioClipCard card = Open(AudioClipKind.Bgm);
            yield return null;

            AudioClipCard.ClipRow row = card.Rows.Single();
            Assert.That(row.LoopToggle.value, Is.True);
            Submit(row.PlayButton);
            Assert.That(_bgm.Played, Is.EqualTo(("bgm-1", true)), "It plays as a single track, looped.");

            _bgm.Set(new BgmStatus(BgmPlayState.Playing, "bgm-1"));
            yield return null;
            Assert.That(row.PlayButton.IconName, Is.EqualTo(IconName.Stop), "The button stops it now.");
            Assert.That(row.State.style.display.value, Is.EqualTo(DisplayStyle.Flex));
            Assert.That(row.State.TextKey, Is.EqualTo(AudioClipStrings.BgmPlaying), "Playing, by its shape and its text.");
            Assert.That(row.DeleteButton.enabledSelf, Is.False);
            Assert.That(row.DeleteButton.TooltipKey, Is.EqualTo(AudioClipStrings.DeleteInUse), "And says why it cannot be deleted.");

            Submit(row.PlayButton);
            Assert.That(_bgm.Stopped, Is.True);
            _bgm.Set(new BgmStatus(BgmPlayState.Idle, null));
            yield return null;
            Assert.That(row.DeleteButton.enabledSelf, Is.True);
            Assert.That(row.State.style.display.value, Is.EqualTo(DisplayStyle.None));
        }

        [UnityTest]
        public IEnumerator ChangesToAFile_AreKeptInTheLibrary()
        {
            AudioClipCard card = Open(AudioClipKind.Bgm);
            yield return null;

            AudioClipCard.ClipRow row = card.Rows.Single();
            row.Gain.value = -9f;
            row.LoopToggle.value = false;
            Assert.That(_clips.Changes, Is.EqualTo(new[] { ("bgm-1", (float?)-9f, (bool?)null, (string)null), ("bgm-1", null, false, null) }));

            Submit(row.RenameButton);
            Assert.That(row.NameInput.style.display.value, Is.EqualTo(DisplayStyle.Flex), "The name turns into a field where it is.");
            row.NameInput.value = "  ";
            Submit(row.SaveButton);
            Assert.That(row.NameInput.ErrorKey, Is.EqualTo(AudioClipStrings.NameRequired));
            row.NameInput.value = "Sunny";
            Submit(row.SaveButton);
            Assert.That(_clips.Changes.Last().Item4, Is.EqualTo("Sunny"));
        }

        [UnityTest]
        public IEnumerator AFile_IsAdded_OrTheCardSaysWhyNot()
        {
            AudioClipCard card = Open(AudioClipKind.SoundEffect);
            yield return null;

            _files.Path = @"C:\long.wav";
            _clips.Problem = AudioClipProblem.TooLong;
            Submit(card.AddButton);
            yield return WaitFor(() => card.Problem.style.display.value == DisplayStyle.Flex);
            Assert.That(card.Problem.TextKey, Is.EqualTo(AudioClipStrings.ProblemTooLong), "An effect over 30 s is refused, and the Card says so.");
            Assert.That(_clips.Added, Is.Empty);

            _files.Path = @"C:\boing.mp3";
            _clips.Problem = AudioClipProblem.None;
            Submit(card.AddButton);
            yield return WaitFor(() => card.Rows.Count == 2);
            Assert.That(_clips.Added, Is.EqualTo(new[] { (AudioClipKind.SoundEffect, @"C:\boing.mp3") }));
            Assert.That(card.Problem.style.display.value, Is.EqualTo(DisplayStyle.None));
            Assert.That(_files.Extensions, Is.EqualTo(new[] { ".wav", ".mp3" }), "The dialog offers the formats taken.");
        }

        [UnityTest]
        public IEnumerator AnEffect_PlaysOnlyOnceLoaded_AndAllStop()
        {
            AudioClipCard card = Open(AudioClipKind.SoundEffect);
            yield return null;

            AudioClipCard.ClipRow row = card.Rows.Single();
            Assert.That(row.LoopToggle, Is.Null, "Sound effects do not loop.");
            Assert.That(row.PlayButton.enabledSelf, Is.False, "Not loaded yet.");
            _effects.Ready.Add("se-1");
            yield return new WaitForSeconds(0.3f);
            Assert.That(row.PlayButton.enabledSelf, Is.True);
            Submit(row.PlayButton);
            Submit(row.PlayButton);
            Assert.That(_effects.Played, Is.EqualTo(new[] { "se-1", "se-1" }), "Each press plays it again, overlapping.");
            Submit(card.StopAllButton);
            Assert.That(_effects.Stops, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator Deleting_AsksFirst_AndTheRowGoes()
        {
            AudioClipCard card = Open(AudioClipKind.SoundEffect);
            yield return null;

            Submit(card.Rows.Single().DeleteButton);
            yield return null;
            Assert.That(_root.Dialogs.Current, Is.Not.Null, "It asks first.");
            Submit(_root.Dialogs.Current.Confirm);
            yield return WaitFor(() => card.Rows.Count == 0);
            Assert.That(_clips.Deleted, Is.EqualTo(new[] { "se-1" }));
        }

        [UnityTest]
        public IEnumerator Rows_AreReordered_ByCtrlArrows_AndByDraggingTheGrip()
        {
            _clips.Add(AudioClipKind.SoundEffect, "se-2", "Boing", 1);
            _clips.Add(AudioClipKind.SoundEffect, "se-3", "Coin", 1);
            AudioClipCard card = Open(AudioClipKind.SoundEffect);
            yield return null;

            Key(card.Rows[0].Grip, KeyCode.DownArrow);
            Assert.That(_clips.Moves, Is.Empty, "An arrow alone moves the focus, not the row.");
            Key(card.Rows[0].Grip, KeyCode.DownArrow, EventModifiers.Control);
            Assert.That(card.Rows.Select(r => r.Record.Id), Is.EqualTo(new[] { "se-2", "se-1", "se-3" }));
            Assert.That(card.Rows[1].Grip.focusController?.focusedElement, Is.EqualTo(card.Rows[1].Grip), "The moved row keeps the focus.");
            Key(card.Rows[0].Grip, KeyCode.UpArrow, EventModifiers.Control);
            Assert.That(_clips.Moves, Is.EqualTo(new[] { ("se-1", 1) }), "The first row does not go further up.");

            yield return null;
            VisualElement grip = card.Rows[2].Grip;
            Vector2 start = grip.worldBound.center;
            Vector2 top = new Vector2(start.x, card.Rows[0].Root.worldBound.yMin + 2);
            GripPointer(grip, EventType.MouseDown, start);
            GripPointer(grip, EventType.MouseDrag, top);
            Assert.That(card.Rows[2].Root.ClassListContains(AudioClipCard.DraggingClass), Is.True);
            GripPointer(grip, EventType.MouseUp, top);
            Assert.That(card.Rows.Select(r => r.Record.Id), Is.EqualTo(new[] { "se-3", "se-2", "se-1" }), "Dropped above the first row.");
            Assert.That(card.Rows.Any(r => r.Root.ClassListContains(AudioClipCard.DraggingClass)), Is.False);

            grip = card.Rows[0].Grip;
            GripPointer(grip, EventType.MouseDown, grip.worldBound.center);
            GripPointer(grip, EventType.MouseUp, grip.worldBound.center);
            Assert.That(_clips.Moves.Count, Is.EqualTo(2), "A click on the grip moves nothing.");
        }

        [Test]
        public void Lengths_ShowMinutesAndSeconds()
        {
            Assert.That(AudioClipCard.LengthText(TimeSpan.FromSeconds(95.4)), Is.EqualTo("1:35"));
            Assert.That(AudioClipCard.LengthText(TimeSpan.FromSeconds(2)), Is.EqualTo("0:02"));
            Assert.That(AudioClipCard.LengthText(TimeSpan.Zero), Is.Empty, "Unknown.");
            Assert.That(AudioClipCard.RowState(new BgmStatus(BgmPlayState.Starting, "bgm-1"), "bgm-1"), Is.EqualTo((true, false)));
            Assert.That(AudioClipCard.RowState(new BgmStatus(BgmPlayState.Playing, "bgm-2"), "bgm-1"), Is.EqualTo((false, false)), "Another track.");
            Assert.That(AudioClipCard.RowState(new BgmStatus(BgmPlayState.Failed, "bgm-1"), "bgm-1"), Is.EqualTo((false, true)));
        }

        private AudioClipCard Open(AudioClipKind kind)
        {
            _root.Shell.Open("setup");
            ((SetupScreen)_root.Shell.Host.GetScreen("setup")).Tabs.value = SetupScreen.AudioPage;
            return _root.Content.Query<AudioClipCard>().ToList()[kind == AudioClipKind.Bgm ? 0 : 1];
        }

        private static IEnumerator WaitFor(Func<bool> condition)
        {
            float until = Time.realtimeSinceStartup + 3f;
            while (!condition() && Time.realtimeSinceStartup < until)
            {
                yield return null;
            }

            Assert.That(condition(), Is.True, "It did not happen in time.");
        }

        private static void Key(VisualElement target, KeyCode key, EventModifiers modifiers = EventModifiers.None)
        {
            target.Focus();
            using (KeyDownEvent evt = KeyDownEvent.GetPooled('\0', key, modifiers))
            {
                evt.target = target;
                target.SendEvent(evt);
            }
        }

        private static void GripPointer(VisualElement grip, EventType type, Vector2 position)
        {
            var system = new Event { type = type, mousePosition = position, button = 0, clickCount = 1 };
            EventBase evt = type switch
            {
                EventType.MouseDown => PointerDownEvent.GetPooled(system),
                EventType.MouseUp => PointerUpEvent.GetPooled(system),
                _ => PointerMoveEvent.GetPooled(system),
            };
            using (evt)
            {
                evt.target = grip;
                grip.SendEvent(evt);
            }
        }

        private static void Submit(VisualElement target)
        {
            using (NavigationSubmitEvent evt = NavigationSubmitEvent.GetPooled())
            {
                evt.target = target;
                target.SendEvent(evt);
            }
        }

        internal sealed class FakeClips : IAudioClipLibrary
        {
            private readonly List<AudioClipRecord> _records = new List<AudioClipRecord>();

            public event Action<AudioClipKind> Changed;

            public IReadOnlyList<string> FileExtensions => new[] { ".wav", ".mp3" };

            public AudioClipProblem Problem { get; set; }

            public List<(string Id, float? GainDb, bool? Loop, string Name)> Changes { get; } = new List<(string, float?, bool?, string)>();

            public List<(AudioClipKind, string)> Added { get; } = new List<(AudioClipKind, string)>();

            public List<string> Deleted { get; } = new List<string>();

            public List<(string Id, int Position)> Moves { get; } = new List<(string, int)>();

            public void Add(AudioClipKind kind, string id, string name, int seconds)
            {
                _records.Add(new AudioClipRecord(id, kind, name, 0f, kind == AudioClipKind.Bgm, TimeSpan.FromSeconds(seconds), name + ".mp3", DateTimeOffset.UtcNow, @"C:\clips\" + id));
            }

            public CrudResult<IReadOnlyList<AudioClipRecord>> List(AudioClipKind kind)
            {
                return CrudResult<IReadOnlyList<AudioClipRecord>>.Success(_records.Where(r => r.Kind == kind).ToArray());
            }

            public CrudResult<AudioClipRecord> Get(string id)
            {
                AudioClipRecord record = _records.Find(r => r.Id == id);
                return record != null ? CrudResult<AudioClipRecord>.Success(record) : CrudResult<AudioClipRecord>.Fail(CrudError.NotFound, "none");
            }

            public Task<AudioClipProblem> CheckAsync(AudioClipKind kind, string sourcePath, CancellationToken cancellationToken)
            {
                return Task.FromResult(Problem);
            }

            public Task<CrudResult<AudioClipRecord>> AddAsync(AudioClipKind kind, string sourcePath, string name, CancellationToken cancellationToken)
            {
                Added.Add((kind, sourcePath));
                Add(kind, (kind == AudioClipKind.Bgm ? "bgm-" : "se-") + (_records.Count + 1), Path.GetFileNameWithoutExtension(sourcePath), 3);
                Changed?.Invoke(kind);
                return Task.FromResult(CrudResult<AudioClipRecord>.Success(_records.Last()));
            }

            public CrudResult<AudioClipRecord> Update(string id, AudioClipChange change)
            {
                Changes.Add((id, change.GainDb, change.Loop, change.Name));
                return Get(id);
            }

            public CrudResult Delete(string id)
            {
                Deleted.Add(id);
                AudioClipKind kind = _records.Find(r => r.Id == id).Kind;
                _records.RemoveAll(r => r.Id == id);
                Changed?.Invoke(kind);
                return CrudResult.Success;
            }

            public CrudResult Move(string id, int position)
            {
                Moves.Add((id, position));
                AudioClipRecord record = _records.Find(r => r.Id == id);
                List<AudioClipRecord> same = _records.Where(r => r.Kind == record.Kind).ToList();
                same.Remove(record);
                same.Insert(Math.Max(0, Math.Min(same.Count, position)), record);
                _records.RemoveAll(r => r.Kind == record.Kind);
                _records.AddRange(same);
                Changed?.Invoke(record.Kind);
                return CrudResult.Success;
            }
        }

        internal sealed class FakeBgm : IBgmPlayer
        {
            private readonly ObservableValue<BgmStatus> _status = new ObservableValue<BgmStatus>(new BgmStatus(BgmPlayState.Idle, null));

            public IReadOnlyObservable<BgmStatus> Status => _status;

            public (string, bool) Played { get; private set; }

            public bool Stopped { get; private set; }

            public float? Progress => null;

            public void Set(BgmStatus status)
            {
                _status.Set(status);
            }

            public void Play(IBgmSequence sequence)
            {
                BgmCue? cue = sequence.Next(null);
                Played = (cue?.TrackId, cue?.Loop ?? false);
            }

            public void Stop()
            {
                Stopped = true;
            }
        }

        internal sealed class FakeEffects : ISoundEffectPlayer
        {
            public HashSet<string> Ready { get; } = new HashSet<string>();

            public List<string> Played { get; } = new List<string>();

            public int Stops { get; private set; }

            public void Play(string id)
            {
                Played.Add(id);
            }

            public void StopAll()
            {
                Stops++;
            }

            public bool IsReady(string id)
            {
                return Ready.Contains(id);
            }
        }

        private sealed class FakeFiles : IFileDialog
        {
            public string Path { get; set; }

            public IReadOnlyList<string> Extensions { get; private set; }

            public Task<string> OpenAsync(FileDialogOptions options)
            {
                Extensions = options.Filters.Single().Extensions;
                return Task.FromResult(Path);
            }

            public Task<string> PickFolderAsync(string title)
            {
                return Task.FromResult<string>(null);
            }
        }
    }
}
