using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using NUnit.Framework;
using VirtualVessel.Audio.Clips;
using VirtualVessel.Core.Time;
using VirtualVessel.ProjectData.Config;

namespace VirtualVessel.Audio.Tests
{
    /// <summary>The BGM and SE that come with the app (BGM and SE detailed design 6.3).</summary>
    public sealed class BuiltInAudioClipsTests
    {
        private string _root;
        private string _builtIn;
        private string _audio;
        private AudioClipLibrary _library;
        private int _ids;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "vv-builtin-" + Guid.NewGuid().ToString("N"));
            _builtIn = Path.Combine(_root, "BuiltIn");
            _audio = Path.Combine(_root, "Audio");
            Directory.CreateDirectory(_builtIn);
            _library = new AudioClipLibrary(_audio, new StepClock(), Probe, () => (++_ids).ToString("x4"));
        }

        [TearDown]
        public void TearDown()
        {
            Directory.Delete(_root, true);
        }

        // As in AudioClipLibraryTests: a file's length is in its name, and "bad" cannot be read.
        private static AudioFileInfo Probe(string path)
        {
            string name = Path.GetFileNameWithoutExtension(path);
            if (name.Contains("bad"))
            {
                throw new InvalidDataException("Cannot be decoded.");
            }

            return new AudioFileInfo(TimeSpan.FromSeconds(int.Parse(name.Split('-').Last())), 48000, 1);
        }

        private static BuiltInAudioClip Clip(string id, string kind, string file, string ja = null, string en = null)
        {
            var names = new Dictionary<string, string>();
            if (ja != null)
            {
                names["ja"] = ja;
            }

            if (en != null)
            {
                names["en"] = en;
            }

            return new BuiltInAudioClip { Id = id, Kind = kind, File = file, Names = names };
        }

        private void Ship(params BuiltInAudioClip[] clips)
        {
            foreach (BuiltInAudioClip clip in clips)
            {
                string path = Path.Combine(_builtIn, clip.File);
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllBytes(path, new byte[] { 1, 2, 3 });
            }

            new ConfigFileStore<BuiltInAudioManifest>(_builtIn, BuiltInAudioClips.ManifestFile, BuiltInAudioManifest.SchemaVersion, () => new BuiltInAudioManifest(), new StepClock())
                .Save(new BuiltInAudioManifest { Clips = clips.ToList() });
        }

        private void Start(string language = "ja")
        {
            using (var builtIn = new BuiltInAudioClips(_library, _builtIn, _audio, language, new StepClock(), null))
            {
                builtIn.InitializeAsync(CancellationToken.None).GetAwaiter().GetResult();
                builtIn.Adding.GetAwaiter().GetResult();
                builtIn.ShutdownAsync(CancellationToken.None).GetAwaiter().GetResult();
            }
        }

        private string[] Names(AudioClipKind kind)
        {
            return _library.List(kind).Value.Select(clip => clip.Name).ToArray();
        }

        [Test]
        public void OnTheFirstStart_EachIsAdded_InTheLanguage_InOrder()
        {
            Ship(Clip("bgm-pop", "bgm", "bgm/pop-30.wav", "ポップ", "Pop"),
                 Clip("bgm-waltz", "bgm", "bgm/waltz-36.wav", "ワルツ", "Waltz"),
                 Clip("se-chime", "soundEffect", "se/chime-2.wav", "ピンポーン", "Chime"));

            Start();

            Assert.That(Names(AudioClipKind.Bgm), Is.EqualTo(new[] { "ポップ", "ワルツ" }));
            Assert.That(Names(AudioClipKind.SoundEffect), Is.EqualTo(new[] { "ピンポーン" }));
            Assert.That(_library.List(AudioClipKind.Bgm).Value.All(clip => clip.Loop), Is.True, "Built-in BGM loops, like any BGM.");
        }

        [Test]
        public void TheNextStart_AddsNothingAgain_NorWhatTheUserDeleted()
        {
            Ship(Clip("bgm-pop", "bgm", "bgm/pop-30.wav", "ポップ", "Pop"), Clip("se-chime", "soundEffect", "se/chime-2.wav", "ピンポーン", "Chime"));
            Start();
            string chime = _library.List(AudioClipKind.SoundEffect).Value.Single().Id;
            Assert.That(_library.Delete(chime).Ok, Is.True);

            Start();

            Assert.That(Names(AudioClipKind.Bgm), Is.EqualTo(new[] { "ポップ" }));
            Assert.That(Names(AudioClipKind.SoundEffect), Is.Empty, "A deleted one does not come back.");
        }

        [Test]
        public void ALaterVersion_AddsOnlyItsNewOnes()
        {
            Ship(Clip("bgm-pop", "bgm", "bgm/pop-30.wav", "ポップ", "Pop"));
            Start();

            Ship(Clip("bgm-pop", "bgm", "bgm/pop-30.wav", "ポップ", "Pop"), Clip("bgm-lofi", "bgm", "bgm/lofi-47.wav", "ローファイ", "Lo-fi"));
            Start();

            Assert.That(Names(AudioClipKind.Bgm), Is.EqualTo(new[] { "ポップ", "ローファイ" }));
        }

        [Test]
        public void OneThatCannotBeAdded_IsLeftForTheNextStart()
        {
            Ship(Clip("bgm-broken", "bgm", "bgm/bad-30.wav", "壊れた", "Broken"), Clip("bgm-pop", "bgm", "bgm/pop-30.wav", "ポップ", "Pop"));

            Start();

            Assert.That(Names(AudioClipKind.Bgm), Is.EqualTo(new[] { "ポップ" }), "The others are still added.");
            BuiltInAudioState state = new ConfigFileStore<BuiltInAudioState>(_audio, BuiltInAudioClips.StateFile, BuiltInAudioState.SchemaVersion, () => new BuiltInAudioState(), new StepClock()).Load().Value;
            Assert.That(state.Added, Is.EqualTo(new[] { "bgm-pop" }));
        }

        [Test]
        public void InEnglish_TheEnglishNames()
        {
            Ship(Clip("bgm-pop", "bgm", "bgm/pop-30.wav", "ポップ", "Pop"));

            Start("en");

            Assert.That(Names(AudioClipKind.Bgm), Is.EqualTo(new[] { "Pop" }));
        }

        [Test]
        public void WithoutTheFolder_NothingHappens()
        {
            Directory.Delete(_builtIn, true);

            Assert.DoesNotThrow(() => Start());
            Assert.That(Names(AudioClipKind.Bgm), Is.Empty);
        }

        [Test]
        public void Missing_SkipsWhatWasAdded_AndWhatIsMalformed()
        {
            var clips = new[]
            {
                Clip("bgm-a", "bgm", "bgm/a.wav"),
                Clip("bgm-b", "bgm", "bgm/b.wav"),
                Clip("x-kind", "video", "x.wav"),
                Clip("x-up", "bgm", "../outside.wav"),
                Clip("x-rooted", "bgm", @"C:\outside.wav"),
                Clip(null, "bgm", "bgm/c.wav"),
                null,
            };

            IReadOnlyList<BuiltInAudioClip> missing = BuiltInAudioClips.Missing(clips, new[] { "bgm-a" });

            Assert.That(missing.Select(clip => clip.Id), Is.EqualTo(new[] { "bgm-b" }));
        }

        [Test]
        public void NameFor_FallsBackToEnglish_ThenTheId()
        {
            Assert.That(BuiltInAudioClips.NameFor(Clip("a", "bgm", "a.wav", "あ", "A"), "ja"), Is.EqualTo("あ"));
            Assert.That(BuiltInAudioClips.NameFor(Clip("a", "bgm", "a.wav", "あ", "A"), "fr"), Is.EqualTo("A"));
            Assert.That(BuiltInAudioClips.NameFor(Clip("a", "bgm", "a.wav"), "ja"), Is.EqualTo("a"));
        }

        [Test]
        public void TheShippedList_NamesEveryFile_InBothLanguages()
        {
            string folder = Path.Combine(UnityEngine.Application.streamingAssetsPath, BuiltInAudioClips.Folder);
            BuiltInAudioManifest manifest = new ConfigFileStore<BuiltInAudioManifest>(folder, BuiltInAudioClips.ManifestFile, BuiltInAudioManifest.SchemaVersion, () => new BuiltInAudioManifest(), new StepClock()).Load().Value;

            Assert.That(manifest.Clips, Is.Not.Empty, "tools/audio/build_builtin_audio.py writes the list.");
            Assert.That(BuiltInAudioClips.Missing(manifest.Clips, Array.Empty<string>()), Has.Count.EqualTo(manifest.Clips.Count), "Every entry is well formed.");
            Assert.That(manifest.Clips.Select(clip => clip.Id).Distinct().Count(), Is.EqualTo(manifest.Clips.Count), "IDs are unique.");
            foreach (BuiltInAudioClip clip in manifest.Clips)
            {
                Assert.That(File.Exists(Path.Combine(folder, clip.File)), Is.True, clip.File);
                Assert.That(clip.Names.ContainsKey("ja") && clip.Names.ContainsKey("en"), Is.True, clip.Id);
                Assert.That(clip.Names.Values.All(name => AudioClipLibrary.CleanName(name) == name), Is.True, $"{clip.Id}: a name the library accepts as it is.");
            }
        }

        private sealed class StepClock : ISystemClock
        {
            private static DateTimeOffset s_now = new DateTimeOffset(2026, 11, 17, 12, 0, 0, TimeSpan.Zero);

            public DateTimeOffset UtcNow
            {
                get
                {
                    // Shared and stepping, so registrations from two starts still list in the order they were added.
                    s_now = s_now.AddSeconds(1);
                    return s_now;
                }
            }
        }
    }
}
