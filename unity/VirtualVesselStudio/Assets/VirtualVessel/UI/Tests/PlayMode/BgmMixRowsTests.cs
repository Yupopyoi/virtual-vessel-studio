using System;
using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using VirtualVessel.Audio;
using VirtualVessel.Core.Time;
using VirtualVessel.UI.Controls;
using VirtualVessel.UI.Screens;
using Object = UnityEngine.Object;

namespace VirtualVessel.UI.Tests
{
    /// <summary>How BGM is mixed, on the setup screen (setup.md 14.1; BGM and SE detailed design 8.4, 8.5).</summary>
    public sealed class BgmMixRowsTests
    {
        private string _directory;
        private GameObject _host;
        private UiRoot _root;
        private AudioPanelTests.FakeAudio _audio;
        private BgmMixRows _rows;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _directory = Path.Combine(Path.GetTempPath(), "VirtualVesselTests", Guid.NewGuid().ToString("N"));
            _audio = new AudioPanelTests.FakeAudio();
            _host = new GameObject(nameof(BgmMixRowsTests));
            _host.AddComponent<UIDocument>();
            _root = _host.AddComponent<UiRoot>();
            _root.SetAssets(UnityEditor.AssetDatabase.LoadAssetAtPath<UiAssets>("Assets/VirtualVessel/UI/UiAssets.asset"));
            _root.Build(new UiRootContext(_directory, new StopwatchMonotonicClock(), new UtcSystemClock(), null, null, false, screens: new ScreenServices()));
            _rows = new BgmMixRows(_audio);
            _root.Content.Add(_rows);
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
        public IEnumerator Ducking_IsOffAtFirst_AndItsSlidersWaitForIt()
        {
            yield return null;
            Assert.That(_rows.DuckingToggle.value, Is.False, "The user's decision (2026-11-23).");
            Assert.That(_rows.DuckDepth.enabledSelf, Is.False);
            Assert.That(_rows.DuckDepth.TooltipKey, Is.EqualTo(BgmMixStrings.DuckingOnly), "Why, when it cannot be used.");
            Assert.That(_rows.DuckingState.resolvedStyle.display, Is.EqualTo(DisplayStyle.None));
            Assert.That(_rows.Crossfade.value, Is.EqualTo(2f));
            Assert.That(_rows.Crossfade.enabledSelf, Is.True, "The crossfade does not depend on ducking.");

            _rows.DuckingToggle.value = true;
            yield return null;
            Assert.That(_audio.BgmMix.Value.Ducking, Is.True);
            Assert.That((_rows.DuckDepth.enabledSelf, _rows.DuckThreshold.enabledSelf), Is.EqualTo((true, true)));
            Assert.That(_rows.DuckingState.resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));
            Assert.That(_rows.DuckingState.TextKey, Is.EqualTo(BgmMixStrings.DuckingNotNow));
        }

        [UnityTest]
        public IEnumerator TheSliders_SetTheMix_AndShowWhetherItIsLoweringNow()
        {
            _audio.SetBgmMix(_audio.BgmMix.Value.WithDucking(true));
            yield return null;

            _rows.DuckDepth.value = -18f;
            _rows.DuckThreshold.value = -30f;
            _rows.Crossfade.value = 0.5f;
            Assert.That((_audio.BgmMix.Value.DuckDepthDb, _audio.BgmMix.Value.DuckThresholdDb, _audio.BgmMix.Value.CrossfadeSeconds), Is.EqualTo((-18f, -30f, 0.5f)));

            _audio.DuckingNow.Set(true);
            yield return null;
            Assert.That((_rows.DuckingState.Status, _rows.DuckingState.TextKey), Is.EqualTo((StatusKind.Success, BgmMixStrings.DuckingNow)), "By shape and text.");
        }
    }
}
