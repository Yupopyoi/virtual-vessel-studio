using System;
using System.Collections.Generic;
using NUnit.Framework;
using VirtualVessel.Audio;
using VirtualVessel.Core.Lifecycle;
using VirtualVessel.UI.Voice;

namespace VirtualVessel.Voice.Tests
{
    /// <summary>
    /// Keeping one's own voice off the stream while the conversion is on but not converting (Voice detailed design 12):
    /// the microphone's stage stays on with nothing written, which the mix plays as silence.
    /// </summary>
    public sealed class VoiceMuteTests
    {
        [Test]
        public void TurnedOn_WithoutAVoiceToLoad_KeepsTheMicrophoneOut()
        {
            var stage = new RecordingStage();
            var service = new VoiceService(stage, new ManualLoop(), null, null);

            service.SetEnabled(true);

            Assert.That(service.State.Value, Is.EqualTo(VoiceConversionState.Unavailable));
            Assert.That(stage.Enabled, Is.True, "The stage is on, so the microphone does not reach the mix.");
            Assert.That(stage.Written, Is.Zero, "Nothing is written: silence.");
            Assert.That(service.MutesMicrophone, Is.True);
        }

        [Test]
        public void TurnedOff_LetsTheMicrophoneInAgain()
        {
            var stage = new RecordingStage();
            var service = new VoiceService(stage, new ManualLoop(), null, null);
            service.SetEnabled(true);

            service.SetEnabled(false);

            Assert.That(stage.Enabled, Is.False, "Off is the user's choice of their own voice.");
            Assert.That(service.MutesMicrophone, Is.False);
        }

        [Test]
        public void OnAgainAtStart_KeepsTheMicrophoneOut_FromTheFirstFrame()
        {
            var stage = new RecordingStage();
            VoiceStorage storage = VoiceStorage.InMemory();
            storage.SaveProfile(new VoiceProfile { Enabled = true });
            var service = new VoiceService(stage, new ManualLoop(), null, null, null, storage);

            service.InitializeAsync(System.Threading.CancellationToken.None).Wait();

            Assert.That(stage.Enabled, Is.True);
        }

        [Test]
        public void PassThrough_LetsTheMicrophoneIn_AsBefore()
        {
            var stage = new RecordingStage();
            VoiceStorage storage = VoiceStorage.InMemory();
            storage.SaveProfile(new VoiceProfile { OnFailure = VoiceProfile.PassThrough });
            var service = new VoiceService(stage, new ManualLoop(), null, null, null, storage);

            service.SetEnabled(true);

            Assert.That(stage.Enabled, Is.False);
            Assert.That(service.MutesMicrophone, Is.False);
        }

        [Test]
        public void WhileMuted_WhatWasCapturedIsLetGo_EachFrame()
        {
            var stage = new RecordingStage();
            var loop = new ManualLoop();
            var service = new VoiceService(stage, loop, null, null);
            service.SetEnabled(true);

            loop.Tick();

            Assert.That(stage.Reads, Is.EqualTo(1), "Read and dropped, so the stage does not count it as overrun.");
        }

        [Test]
        public void OnlyPassThrough_LetsTheMicrophoneIn()
        {
            Assert.That(VoiceService.MutesOnFailure(VoiceProfile.Mute), Is.True);
            Assert.That(VoiceService.MutesOnFailure(VoiceProfile.PassThrough), Is.False);
            Assert.That(VoiceService.MutesOnFailure(null), Is.True, "Missing: the default.");
            Assert.That(VoiceService.MutesOnFailure("passthrough"), Is.True, "Unknown: the safer one.");
        }

        private sealed class ManualLoop : IFrameLoop
        {
            private readonly List<Action> _ticks = new List<Action>();

            public IDisposable Register(Action tick)
            {
                _ticks.Add(tick);
                return new Removal(() => _ticks.Remove(tick));
            }

            public void Tick()
            {
                foreach (Action tick in _ticks.ToArray())
                {
                    tick();
                }
            }

            private sealed class Removal : IDisposable
            {
                private readonly Action _remove;

                public Removal(Action remove)
                {
                    _remove = remove;
                }

                public void Dispose()
                {
                    _remove();
                }
            }
        }

        private sealed class RecordingStage : IAudioStage
        {
            public bool Enabled { get; private set; }

            public int Written { get; private set; }

            public int Reads { get; private set; }

            public bool IsAvailable => true;

            public bool IsCapturing => true;

            public bool Denoises => false;

            public AudioStageDelay Delay => default;

            public int Read(float[] frames, int capacity)
            {
                Reads++;
                return 0;
            }

            public void Write(float[] frames, int count)
            {
                Written += count;
            }

            public void SetEnabled(bool enabled)
            {
                Enabled = enabled;
            }

            public void Denoise(float[] samples, int count)
            {
            }
        }
    }
}
