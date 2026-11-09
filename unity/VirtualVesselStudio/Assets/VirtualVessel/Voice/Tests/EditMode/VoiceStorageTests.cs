using System;
using System.IO;
using NUnit.Framework;
using VirtualVessel.Audio;
using VirtualVessel.Core.Lifecycle;
using VirtualVessel.Core.Time;

namespace VirtualVessel.Voice.Tests
{
    /// <summary>The voice for the stream and the user's pitch, kept (Voice detailed design 6.2, 6.3).</summary>
    public sealed class VoiceStorageTests
    {
        [Test]
        public void TheService_StartsWithWhatWasSaved()
        {
            var storage = new VoiceStorage(new VoiceProfile { VoiceModelId = "voice-a", Semitones = 2f, AutoPitch = false }, new VoiceSettings { UserPitchHz = 180f }, null, null, null);

            var service = new VoiceService(new IdleStage(), new IdleLoop(), null, null, null, storage);

            Assert.That((service.VoiceId, service.InUseVoiceId), Is.EqualTo(("voice-a", "voice-a")));
            Assert.That((service.Semitones, service.AutoPitch), Is.EqualTo((2f, false)));
            Assert.That(service.UserPitchHz, Is.EqualTo(180f), "The measured pitch matches from the start.");
        }

        [Test]
        public void ChoosingSaves_ButATrialSavesNothing_AndPutsTheSavedBack()
        {
            VoiceStorage storage = VoiceStorage.InMemory();
            var service = new VoiceService(new IdleStage(), new IdleLoop(), null, null, null, storage);
            service.SelectVoice("voice-b");
            service.Semitones = 2f;

            service.BeginTrial("C:/runs/r/output");
            service.Semitones = 7f;
            service.AutoPitch = false;

            Assert.That(service.VoiceId, Is.EqualTo("folder:C:/runs/r/output"));
            Assert.That((storage.Profile.VoiceModelId, storage.Profile.Semitones, storage.Profile.AutoPitch), Is.EqualTo(("voice-b", 2f, true)), "The trial's own.");
            Assert.That(service.InUseVoiceId, Is.EqualTo("voice-b"));

            service.EndTrial();

            Assert.That((service.VoiceId, service.Semitones, service.AutoPitch), Is.EqualTo(("voice-b", 2f, true)));
            Assert.That(service.State.Value, Is.EqualTo(UI.Voice.VoiceConversionState.Off), "Off again, as before the trial.");
        }

        [Test]
        public void OnAndOff_AreSaved_AndComeBackAtStart_ButATrialsAreNot()
        {
            VoiceStorage storage = VoiceStorage.InMemory();
            var service = new VoiceService(new IdleStage(), new IdleLoop(), null, null, null, storage);
            service.BeginTrial("f");
            Assert.That(storage.Profile.Enabled, Is.False, "A trial's on is not saved.");
            service.EndTrial();

            service.SetEnabled(true);
            Assert.That(storage.Profile.Enabled, Is.True);

            var next = new VoiceService(new IdleStage(), new IdleLoop(), null, null, null, storage);
            Assert.That(next.State.Value, Is.EqualTo(UI.Voice.VoiceConversionState.Off));
            next.InitializeAsync(System.Threading.CancellationToken.None).Wait();
            Assert.That(next.State.Value, Is.Not.EqualTo(UI.Voice.VoiceConversionState.Off), "On again at start; unavailable here without voices.");
        }

        [Test]
        public void NoiseRemoval_IsOnByDefault_AndSavedForThePC()
        {
            VoiceStorage storage = VoiceStorage.InMemory();
            var service = new VoiceService(new IdleStage(), new IdleLoop(), null, null, null, storage);
            Assert.That(service.Denoise, Is.True);

            service.Denoise = false;

            Assert.That(storage.Settings.Denoise, Is.False);
        }

        [Test]
        public void TheGpu_MustComputeAndNotBeSoftware()
        {
            Assert.That(VoiceService.IsGpuUsable(true, UnityEngine.Rendering.GraphicsDeviceType.Direct3D11), Is.True);
            Assert.That(VoiceService.IsGpuUsable(false, UnityEngine.Rendering.GraphicsDeviceType.Direct3D11), Is.False);
            Assert.That(VoiceService.IsGpuUsable(true, UnityEngine.Rendering.GraphicsDeviceType.Null), Is.False);
        }

        [Test]
        public void TheFiles_ReadBackWhatWasSaved()
        {
            string folder = Path.Combine(Path.GetTempPath(), "vv-voice-" + Guid.NewGuid().ToString("N"));
            try
            {
                VoiceStorage first = Open(folder);
                first.SaveProfile(new VoiceProfile { VoiceModelId = "voice-a", Semitones = -3f, AutoPitch = false });
                first.SaveSettings(new VoiceSettings { UserPitchHz = 201f });
                first.FlushAsync().Wait();

                VoiceStorage second = Open(folder);

                Assert.That((second.Profile.VoiceModelId, second.Profile.Semitones, second.Profile.AutoPitch), Is.EqualTo(("voice-a", -3f, false)));
                Assert.That(second.Settings.UserPitchHz, Is.EqualTo(201f));
            }
            finally
            {
                Directory.Delete(folder, true);
            }
        }

        private static VoiceStorage Open(string folder)
        {
            return VoiceStorage.Open(Path.Combine(folder, "Profiles", "Voice"), "p.json", Path.Combine(folder, "Config"), new UtcSystemClock(), new StopwatchMonotonicClock(), null, null);
        }

        private sealed class IdleLoop : IFrameLoop
        {
            public IDisposable Register(Action tick)
            {
                return new Nothing();
            }

            private sealed class Nothing : IDisposable
            {
                public void Dispose()
                {
                }
            }
        }

        private sealed class IdleStage : IAudioStage
        {
            public bool IsAvailable => false;

            public bool IsCapturing => false;

            public AudioStageDelay Delay => default;

            public int Read(float[] frames, int capacity)
            {
                return 0;
            }

            public void Write(float[] frames, int count)
            {
            }

            public void SetEnabled(bool enabled)
            {
            }

            public void Denoise(float[] samples, int count)
            {
            }
        }
    }
}
