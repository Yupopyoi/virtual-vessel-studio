using System;
using System.Threading;
using NUnit.Framework;
using VirtualVessel.Core.Lifecycle;

namespace VirtualVessel.Audio.Tests
{
    /// <summary>How BGM is mixed: ducking and the crossfade (BGM and SE detailed design 8.4, 8.5, 14).</summary>
    public sealed class BgmMixTests
    {
        [Test]
        public void TheDefaults_AreDuckingOff_12dB_AndATwoSecondCrossfade()
        {
            BgmMixOptions options = BgmMixOptions.Default;

            Assert.That(options.Ducking, Is.False, "The user's decision (2026-11-23).");
            Assert.That((options.DuckDepthDb, options.DuckThresholdDb, options.CrossfadeSeconds), Is.EqualTo((-12f, -40f, 2f)));
        }

        [Test]
        public void Values_AreKeptInRange()
        {
            var wild = new BgmMixOptions(true, -100f, 0f, 9f);
            Assert.That((wild.DuckDepthDb, wild.DuckThresholdDb, wild.CrossfadeSeconds), Is.EqualTo((-24f, -20f, 5f)));

            var tiny = new BgmMixOptions(true, 0f, -90f, -1f);
            Assert.That((tiny.DuckDepthDb, tiny.DuckThresholdDb, tiny.CrossfadeSeconds), Is.EqualTo((-3f, -60f, 0f)));

            var unknown = new BgmMixOptions(true, float.NaN, float.NaN, float.NaN);
            Assert.That((unknown.DuckDepthDb, unknown.DuckThresholdDb, unknown.CrossfadeSeconds), Is.EqualTo((-12f, -40f, 2f)));
        }

        [Test]
        public void AFileWithoutIt_ReadsTheDefaults_AndTheSavedOnesGoBothWays()
        {
            Assert.That(BgmMixSettings.ToOptions(null), Is.EqualTo(BgmMixOptions.Default));
            Assert.That(BgmMixSettings.ToOptions(new BgmMixSettings { Ducking = true }), Is.EqualTo(BgmMixOptions.Default.WithDucking(true)), "A field left out reads its default.");

            BgmMixOptions chosen = BgmMixOptions.Default.WithDucking(true).WithDuckDepth(-18f).WithDuckThreshold(-30f).WithCrossfade(0.5f);
            Assert.That(BgmMixSettings.ToOptions(BgmMixSettings.Of(chosen)), Is.EqualTo(chosen));
            Assert.That(BgmMixSettings.ToOptions(new BgmMixSettings { CrossfadeSeconds = 60f }).CrossfadeSeconds, Is.EqualTo(5f), "An edited file is brought into range.");
        }

        [Test]
        public void TheService_PassesTheMixToTheEngine_FromTheMicrophoneToTheBgm()
        {
            var engine = new AudioPlayersTests.FakePlayerEngine();
            using AudioService service = Start(engine, new AudioSettings());
            IAudioSource microphone = service.Sources[0];
            Assert.That(engine.Crossfade, Is.EqualTo(TimeSpan.FromSeconds(2)));
            Assert.That(engine.Ducking.Enabled, Is.False);

            service.SetBgmMix(service.BgmMix.Value.WithDucking(true).WithDuckDepth(-18f));

            Assert.That(engine.Ducking.Enabled, Is.True);
            Assert.That(engine.Ducking.DepthDb, Is.EqualTo(-18f));
            // The fake numbers sources as they are added: the microphone, the game audio, then BGM and SE.
            Assert.That(microphone.Role, Is.EqualTo(AudioSourceRole.Microphone));
            Assert.That(engine.Ducking.Key, Is.EqualTo(0), "The voice lowers it.");
            Assert.That(engine.Ducking.Ducked, Is.EqualTo(2), "The BGM is lowered.");

            service.SetBgmMix(service.BgmMix.Value.WithCrossfade(0f));
            Assert.That(engine.Crossfade, Is.EqualTo(TimeSpan.Zero));
        }

        [Test]
        public void ASavedMix_IsUsedAtTheStart()
        {
            var engine = new AudioPlayersTests.FakePlayerEngine();
            var saved = new AudioSettings { Bgm = new BgmMixSettings { Ducking = true, DuckThresholdDb = -35f, CrossfadeSeconds = 1f } };

            using AudioService service = Start(engine, saved);

            Assert.That(service.BgmMix.Value.Ducking, Is.True);
            Assert.That((engine.Ducking.Enabled, engine.Ducking.ThresholdDb), Is.EqualTo((true, -35f)));
            Assert.That(engine.Crossfade, Is.EqualTo(TimeSpan.FromSeconds(1)));
        }

        [Test]
        public void Ducking_FollowsTheEngine_EveryTenthOfASecond()
        {
            var engine = new AudioPlayersTests.FakePlayerEngine();
            var loop = new FrameLoop();
            var clock = new AudioPlayersTests.ManualClock();
            using AudioService service = Start(engine, new AudioSettings(), loop, clock);
            Assert.That(service.Ducking.Value, Is.False);

            engine.DuckingDb = -12f;
            clock.Advance(0.11);
            loop.Tick(e => throw e);
            Assert.That(service.Ducking.Value, Is.True);

            engine.DuckingDb = -0.5f;
            clock.Advance(0.11);
            loop.Tick(e => throw e);
            Assert.That(service.Ducking.Value, Is.False, "A trace left as it comes back is not shown.");
        }

        private static AudioService Start(AudioPlayersTests.FakePlayerEngine engine, AudioSettings settings, FrameLoop loop = null, AudioPlayersTests.ManualClock clock = null)
        {
            int next = 0;
            var service = new AudioService(settings, null, () => engine, loop ?? new FrameLoop(), clock ?? new AudioPlayersTests.ManualClock(), () => (++next).ToString("x32"), null, null);
            service.InitializeAsync(CancellationToken.None).GetAwaiter().GetResult();
            return service;
        }
    }
}
