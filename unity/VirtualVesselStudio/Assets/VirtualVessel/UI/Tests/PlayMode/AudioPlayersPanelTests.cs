using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using VirtualVessel.Audio.Clips;
using VirtualVessel.Audio.Playback;
using VirtualVessel.Core.Time;
using VirtualVessel.UI.Controls;
using VirtualVessel.UI.Screens;
using Object = UnityEngine.Object;

namespace VirtualVessel.UI.Tests
{
    /// <summary>BGM and SE on the live screen (BGM and SE detailed design; live.md 2).</summary>
    public sealed class AudioPlayersPanelTests
    {
        private string _directory;
        private GameObject _host;
        private UiRoot _root;
        private AudioClipCardTests.FakeClips _clips;
        private AudioClipCardTests.FakeBgm _bgm;
        private AudioClipCardTests.FakeEffects _effects;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _directory = Path.Combine(Path.GetTempPath(), "VirtualVesselTests", Guid.NewGuid().ToString("N"));
            _clips = new AudioClipCardTests.FakeClips();
            _clips.Add(AudioClipKind.Bgm, "bgm-1", "Fluffy afternoon", 47);
            _clips.Add(AudioClipKind.Bgm, "bgm-2", "Waltz", 72);
            _clips.Add(AudioClipKind.SoundEffect, "se-1", "<b>Ding</b>", 2);
            _bgm = new AudioClipCardTests.FakeBgm();
            _effects = new AudioClipCardTests.FakeEffects();
            _host = new GameObject(nameof(AudioPlayersPanelTests));
            _host.AddComponent<UIDocument>();
            _root = _host.AddComponent<UiRoot>();
            _root.SetAssets(UnityEditor.AssetDatabase.LoadAssetAtPath<UiAssets>("Assets/VirtualVessel/UI/UiAssets.asset"));
            _root.Build(new UiRootContext(_directory, new StopwatchMonotonicClock(), new UtcSystemClock(), null, null, false, screens: new ScreenServices
            {
                AudioClips = _clips,
                Bgm = _bgm,
                SoundEffects = _effects,
            }));
            _root.Shell.Open("live");
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
        public IEnumerator ATrack_IsChosen_Played_AndStopped()
        {
            AudioPlayersPanel panel = _root.Content.Q<AudioPlayersPanel>();
            yield return null;

            Assert.That(panel.Tracks.Choices.Select(c => c.Id), Is.EqualTo(new[] { "bgm-1", "bgm-2" }));
            Assert.That(panel.Tracks.value, Is.EqualTo("bgm-1"), "The first, until another is chosen.");
            panel.Tracks.value = "bgm-2";
            Submit(panel.PlayButton);
            Assert.That(_bgm.Played, Is.EqualTo(("bgm-2", true)));

            _bgm.Set(new BgmStatus(BgmPlayState.Playing, "bgm-2"));
            yield return null;
            Assert.That(panel.PlayButton.TextKey, Is.EqualTo(AudioPlayersStrings.Stop));
            Assert.That(panel.State.style.visibility.value, Is.EqualTo(Visibility.Visible), "Playing, by shape and text.");
            Assert.That(panel.Tracks.enabledSelf, Is.False, "Not chosen again while it plays.");

            Submit(panel.PlayButton);
            Assert.That(_bgm.Stopped, Is.True);
            _bgm.Set(new BgmStatus(BgmPlayState.Idle, null));
            yield return null;
            Assert.That(panel.State.style.visibility.value, Is.EqualTo(Visibility.Hidden), "Hidden, keeping its place.");
            Assert.That(panel.State.style.display.value, Is.Not.EqualTo(DisplayStyle.None));
        }

        [UnityTest]
        public IEnumerator EachEffect_HasAButton_ThatPlaysOnceLoaded()
        {
            AudioPlayersPanel panel = _root.Content.Q<AudioPlayersPanel>();
            yield return null;

            ActionButton ding = panel.EffectButtons.Single();
            Assert.That(ding.DisplayedText, Is.EqualTo("<b>Ding</b>"), "The user's name, as it is.");
            Assert.That(ding.enabledSelf, Is.False, "Not loaded yet.");
            _effects.Ready.Add("se-1");
            yield return new WaitForSeconds(0.35f);
            Assert.That(ding.enabledSelf, Is.True);
            Submit(ding);
            Submit(ding);
            Assert.That(_effects.Played, Is.EqualTo(new[] { "se-1", "se-1" }));
            Submit(panel.StopEffectsButton);
            Assert.That(_effects.Stops, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator AnEffectsKeys_AreInItsTooltip()
        {
            AudioPlayersPanel panel = _root.Content.Q<AudioPlayersPanel>();
            _clips.Add(AudioClipKind.SoundEffect, "se-2", "Chime", 1, shortcut: "ctrl+alt+2");
            _root.Localizer.SetLocale("en");
            yield return null;

            Assert.That(panel.EffectButtons.Select(b => b.Shortcut), Is.EqualTo(new[] { null, "Ctrl+Alt+2" }));
        }

        [UnityTest]
        public IEnumerator ABuiltInsName_FollowsTheScreensLanguage()
        {
            AudioPlayersPanel panel = _root.Content.Q<AudioPlayersPanel>();
            _clips.Add(AudioClipKind.SoundEffect, "se-2", "キラキラ１", 1, new Dictionary<string, string> { ["ja"] = "キラキラ１", ["en"] = "Sparkle 1" });
            _root.Localizer.SetLocale("en");
            yield return null;
            Assert.That(panel.EffectButtons.Select(b => b.PlainText), Is.EqualTo(new[] { "<b>Ding</b>", "Sparkle 1" }));

            _root.Localizer.SetLocale("ja");
            yield return null;
            Assert.That(panel.EffectButtons[1].PlainText, Is.EqualTo("キラキラ１"));
        }

        [UnityTest]
        public IEnumerator TheEffectsVolume_SetsTheEffectsSource_LikeTheBgms()
        {
            var music = new AudioPanelTests.FakeSource("music", VirtualVessel.Audio.AudioSourceRole.Music, null);
            var effects = new AudioPanelTests.FakeSource("se", VirtualVessel.Audio.AudioSourceRole.SoundEffect, null);
            var panel = new AudioPlayersPanel(_clips, _bgm, _effects, music, effects);
            _root.Content.Add(panel);
            yield return null;

            panel.EffectsVolume.value = -12f;
            Assert.That(effects.Options.Value.GainDb, Is.EqualTo(-12f));
            Assert.That(music.Options.Value.GainDb, Is.EqualTo(0f), "Each slider sets its own source.");
            effects.SetOptions(effects.Options.Value.WithGain(-3f));
            Assert.That(panel.EffectsVolume.value, Is.EqualTo(-3f), "A change from the mixer shows here too.");
            Assert.That(new AudioPlayersPanel(_clips, _bgm, _effects, music).EffectsVolume, Is.Null, "Left out without the source.");
        }

        [Test]
        public void TheTime_AndTheTrackShown()
        {
            Assert.That(AudioPlayersPanel.TimeText(0.25f, TimeSpan.FromSeconds(48)), Is.EqualTo("0:12 / 0:48"));
            Assert.That(AudioPlayersPanel.TimeText(null, TimeSpan.FromSeconds(48)), Is.Empty);
            Assert.That(AudioPlayersPanel.ChosenTrack(new[] { "a", "b" }, "b", "a"), Is.EqualTo("b"), "The one playing.");
            Assert.That(AudioPlayersPanel.ChosenTrack(new[] { "a", "b" }, null, "b"), Is.EqualTo("b"), "The one chosen.");
            Assert.That(AudioPlayersPanel.ChosenTrack(new[] { "a", "b" }, null, "gone"), Is.EqualTo("a"), "Else the first.");
            Assert.That(AudioPlayersPanel.ChosenTrack(Array.Empty<string>(), null, null), Is.Null);
        }

        private static void Submit(VisualElement target)
        {
            using (NavigationSubmitEvent evt = NavigationSubmitEvent.GetPooled())
            {
                evt.target = target;
                target.SendEvent(evt);
            }
        }
    }
}
