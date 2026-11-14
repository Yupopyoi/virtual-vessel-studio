using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using VirtualVessel.Core.Time;
using VirtualVessel.UI.Controls;
using VirtualVessel.UI.Screens;
using VirtualVessel.UI.Theme;
using VirtualVessel.UI.Voice;
using VirtualVessel.UI.VoiceLab;
using Object = UnityEngine.Object;

namespace VirtualVessel.UI.Tests
{
    /// <summary>
    /// Every screen and setup page, on the dark and the light background: no text that cannot be read, and no controls
    /// touching in a row (<see cref="UiChecks"/>; the user's reviews, 2026-11-11).
    /// </summary>
    public sealed class UiReadabilityTests
    {
        // Longer than the UI's transitions (0.12 s for buttons), so colors are not caught halfway.
        private const float SettleSeconds = 0.3f;

        private GameObject _host;
        private UiRoot _root;
        private string _directory;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _directory = Path.Combine(Path.GetTempPath(), "VirtualVesselReadability", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
            var voice = new FakeVoice();
            voice.Voices.Add(new VoiceConversionVoice("voice-a", "Poyapoya", 285, true));
            _host = new GameObject(nameof(UiReadabilityTests));
            _host.AddComponent<UIDocument>();
            _root = _host.AddComponent<UiRoot>();
            _root.SetAssets(UnityEditor.AssetDatabase.LoadAssetAtPath<UiAssets>("Assets/VirtualVessel/UI/UiAssets.asset"));
            _root.Build(new UiRootContext(_directory, new StopwatchMonotonicClock(), new UtcSystemClock(), null, null, false, screens: new ScreenServices
            {
                Captures = new ScreenshotCaptureSources(),
                Streaming = new StreamPanelTests.FakeStreaming(),
                Audio = new AudioPanelTests.FakeAudio(),
                SystemSettings = new AudioPanelTests.FakeSettings(),
                Voice = voice,
                VoiceLab = new FakeLab(),
                VoiceLabLibrary = new FakeLibrary(),
            }));
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Object.Destroy(_host);
            yield return null;
            Directory.Delete(_directory, recursive: true);
        }

        [UnityTest]
        public IEnumerator EveryScreen_IsReadable_AndNotCramped_OnTheDarkBackground()
        {
            yield return CheckAll(ThemeBackground.Dark);
        }

        [UnityTest]
        public IEnumerator EveryScreen_IsReadable_AndNotCramped_OnTheLightBackground()
        {
            yield return CheckAll(ThemeBackground.Light);
        }

        [UnityTest]
        public IEnumerator PreparingVoiceLab_IsReadable_AndNotCramped_OnBothBackgrounds()
        {
            // Voice Lab not set up, on a PC that misses a condition, so the problems show too.
            Object.Destroy(_host);
            yield return null;
            var setup = new FakeSetup { Check = new VoiceLabPcCheck(null, false, "610.88", true, @"C:\Users\me\AppData\Local", 218L * 1024 * 1024 * 1024, true, true) };
            _host = new GameObject(nameof(UiReadabilityTests));
            _host.AddComponent<UIDocument>();
            _root = _host.AddComponent<UiRoot>();
            _root.SetAssets(UnityEditor.AssetDatabase.LoadAssetAtPath<UiAssets>("Assets/VirtualVessel/UI/UiAssets.asset"));
            _root.Build(new UiRootContext(_directory, new StopwatchMonotonicClock(), new UtcSystemClock(), null, null, false, screens: new ScreenServices
            {
                VoiceLab = new FakeLab { Availability = VoiceLabAvailability.NotSetUp },
                VoiceLabLibrary = new FakeLibrary(),
                VoiceLabSetup = setup,
            }));
            yield return null;
            _root.Shell.Open("lab");
            var view = ((VoiceLabScreen)_root.Shell.Host.GetScreen("lab")).Setup;
            var problems = new List<string>();
            foreach (ThemeBackground background in new[] { ThemeBackground.Dark, ThemeBackground.Light })
            {
                _root.Settings.SetBackground(background);
                setup.State = VoiceLabSetupState.NotStarted;
                view.Refresh();
                yield return new WaitForSeconds(SettleSeconds);
                Collect(problems, $"lab/check/{background}");

                if (setup.Log.Count == 0)
                {
                    setup.Log.Add(new VoiceLabSetupEntry(DateTime.Now, VoiceLabSetupEntryKind.Download, "voxcpm2-model", 4L * 1024 * 1024 * 1024));
                    setup.Log.Add(new VoiceLabSetupEntry(DateTime.Now, VoiceLabSetupEntryKind.Failed));
                }

                setup.State = VoiceLabSetupState.Failed;
                setup.LastFailure = VoiceLabSetupFailure.Check;
                view.Refresh();
                yield return new WaitForSeconds(SettleSeconds);
                Collect(problems, $"lab/failed/{background}");

                // Continuing without agreeing to the terms opens them.
                setup.TermsAgreed = false;
                Submit(view.ResumeButton);
                yield return new WaitForSeconds(SettleSeconds);
                Collect(problems, $"lab/terms/{background}");
                Submit(view.TermsCard.Query<ActionButton>().First());

                setup.State = VoiceLabSetupState.Ready;
                setup.UpdateAvailable = true;
                setup.LastFailure = VoiceLabSetupFailure.Network;
                view.Refresh();
                yield return new WaitForSeconds(SettleSeconds);
                Collect(problems, $"lab/update/{background}");
            }

            Assert.That(problems, Is.Empty, string.Join("\n", problems));
        }

        [UnityTest]
        public IEnumerator VoiceLabWithCandidatesAndHeldTimbres_IsReadable_OnBothBackgrounds()
        {
            // Candidate tiles and held timbres appear only once made, so opening the screens did not reach them;
            // their names and numbers were Unity's default dark color (the user's review, 2026-11-15).
            _root.Shell.Open("lab");
            yield return null;
            var screen = (VoiceLabScreen)_root.Shell.Host.GetScreen("lab");
            var baseStep = (VoiceLabBaseStep)screen.Panel(VoiceLabScreen.BaseStep);
            Submit(baseStep.MakeButton);
            yield return null;
            Submit(baseStep.Tiles.Last().PickButton);
            yield return null;
            screen.GoTo(VoiceLabScreen.TimbreStep);
            var timbre = (VoiceLabTimbreStep)screen.Panel(VoiceLabScreen.TimbreStep);
            Submit(timbre.HoldButton);
            yield return null;
            Assert.That(timbre.HeldList.Held, Is.Not.Empty);

            var problems = new List<string>();
            foreach (ThemeBackground background in new[] { ThemeBackground.Dark, ThemeBackground.Light })
            {
                _root.Settings.SetBackground(background);
                screen.GoTo(VoiceLabScreen.BaseStep);
                yield return new WaitForSeconds(SettleSeconds);
                Collect(problems, $"lab/candidates/{background}");
                screen.GoTo(VoiceLabScreen.TimbreStep);
                yield return new WaitForSeconds(SettleSeconds);
                Collect(problems, $"lab/held/{background}");
            }

            Assert.That(problems, Is.Empty, string.Join("\n", problems));
        }

        [UnityTest]
        public IEnumerator TheResumeNotice_IsReadable_OnBothBackgrounds()
        {
            Object.Destroy(_host);
            yield return null;
            var candidate = new VoiceLabWorkCandidate(1, 0, new VoiceLabCandidate("work_1_500", "a.wav", 240, 200, 300, "calm", 500));
            var lab = new FakeLab { KeptWork = new VoiceLabWork(VoiceLabScreen.BaseStep, null, new[] { candidate }, 0, default, null, DateTimeOffset.UtcNow) };
            var library = new FakeLibrary();
            library.Runs.Add(new VoiceLabRunRecord("run-1", DateTimeOffset.UtcNow, VoiceLabBuildState.Completed, null, "", default, "", "a.wav", null));
            library.Runs.Add(new VoiceLabRunRecord("run-2", DateTimeOffset.UtcNow, VoiceLabBuildState.Completed, null, "", default, "", "b.wav", null));
            _host = new GameObject(nameof(UiReadabilityTests));
            _host.AddComponent<UIDocument>();
            _root = _host.AddComponent<UiRoot>();
            _root.SetAssets(UnityEditor.AssetDatabase.LoadAssetAtPath<UiAssets>("Assets/VirtualVessel/UI/UiAssets.asset"));
            _root.Build(new UiRootContext(_directory, new StopwatchMonotonicClock(), new UtcSystemClock(), null, null, false, screens: new ScreenServices { VoiceLab = lab, VoiceLabLibrary = library }));
            yield return null;
            _root.Shell.Open("lab");
            var screen = (VoiceLabScreen)_root.Shell.Host.GetScreen("lab");
            screen.ShowTab(VoiceLabScreen.MakeTab);
            Assert.That(screen.Resume.WorkCard.style.display.value, Is.EqualTo(DisplayStyle.Flex));
            Assert.That(screen.Resume.VoiceCard.style.display.value, Is.EqualTo(DisplayStyle.Flex));

            var problems = new List<string>();
            foreach (ThemeBackground background in new[] { ThemeBackground.Dark, ThemeBackground.Light })
            {
                _root.Settings.SetBackground(background);
                yield return new WaitForSeconds(SettleSeconds);
                Collect(problems, $"lab/resume/{background}");
            }

            Assert.That(problems, Is.Empty, string.Join("\n", problems));
        }

        private static void Submit(VisualElement element)
        {
            using (NavigationSubmitEvent evt = NavigationSubmitEvent.GetPooled())
            {
                evt.target = element;
                element.SendEvent(evt);
            }
        }

        private IEnumerator CheckAll(ThemeBackground background)
        {
            _root.Settings.SetBackground(background);
            var problems = new List<string>();
            foreach (string screen in UiShotPlan.AllScreens)
            {
                _root.Shell.Open(screen);
                yield return new WaitForSeconds(SettleSeconds);
                if (screen == "setup")
                {
                    var setup = (SetupScreen)_root.Shell.Host.GetScreen("setup");
                    for (int page = SetupScreen.VideoPage; page <= SetupScreen.AppPage; page++)
                    {
                        setup.Tabs.value = page;
                        yield return new WaitForSeconds(SettleSeconds);
                        Collect(problems, $"setup/{page}");
                    }
                }
                else
                {
                    Collect(problems, screen);
                }
            }

            Assert.That(problems, Is.Empty, string.Join("\n", problems));
        }

        // What the screens are drawn on: the shell's surface.
        private Color ScreenBackground()
        {
            Color color = _root.Content.panel.visualTree.Q(className: "vv-shell").resolvedStyle.backgroundColor;
            Assert.That(color.a, Is.GreaterThan(0.9f), "The shell has a solid background.");
            return color;
        }

        private void Collect(List<string> problems, string where)
        {
            foreach (string text in UiChecks.Unreadable(_root.Content, ScreenBackground()))
            {
                problems.Add($"{where}: unreadable {text}");
            }

            foreach (string pair in UiChecks.Cramped(_root.Content))
            {
                problems.Add($"{where}: cramped {pair}");
            }

            foreach (string pair in UiChecks.UnderDivider(_root.Content))
            {
                problems.Add($"{where}: under a divider {pair}");
            }
        }
    }
}
