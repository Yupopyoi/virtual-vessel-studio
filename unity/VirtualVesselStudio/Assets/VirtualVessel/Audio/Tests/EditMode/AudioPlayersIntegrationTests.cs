using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using NUnit.Framework;
using VirtualVessel.Audio.Clips;
using VirtualVessel.Audio.Playback;
using VirtualVessel.Core.Lifecycle;
using VirtualVessel.Core.Time;
using VirtualVessel.ProjectData.Records;

namespace VirtualVessel.Audio.Tests
{
    /// <summary>
    /// BGM and SE through the real audio plugin (BGM and SE detailed design 14): a WAV registered, played by the BGM
    /// player to its end, and loaded and played as an effect. Ignored where the plugin is missing.
    /// </summary>
    public sealed class AudioPlayersIntegrationTests
    {
        private string _root;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "vv-players-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
        }

        [TearDown]
        public void TearDown()
        {
            Directory.Delete(_root, true);
        }

        [Test]
        public void AWav_IsRegistered_PlayedToItsEnd_AndPlayedAsAnEffect()
        {
            string source = Path.Combine(_root, "half-second.wav");
            File.WriteAllBytes(source, Wav(44100, 22050, 0.25f));
            NativeAudioEngine engine;
            try
            {
                engine = NativeAudioEngine.Create();
            }
            catch (AudioEngineException exception)
            {
                Assert.Ignore(exception.Message);
                return;
            }

            using (engine)
            {
                int bgmSource = engine.AddPlayerSource(AudioPlayer.Bgm);
                int effectSource = engine.AddPlayerSource(AudioPlayer.SoundEffects);
                Assert.That(engine.GetStatus(bgmSource).State, Is.EqualTo(AudioSourceState.Running), "A player has no device to wait for.");
                Assert.That(effectSource, Is.Not.EqualTo(bgmSource));

                var library = new AudioClipLibrary(Path.Combine(_root, "Audio"), new UtcSystemClock(), AudioFile.Probe, () => Guid.NewGuid().ToString("N").Substring(0, 8));
                CrudResult<AudioClipRecord> bgm = library.AddAsync(AudioClipKind.Bgm, source, null, null, CancellationToken.None).GetAwaiter().GetResult();
                Assert.That(bgm.Ok, Is.True, bgm.Detail);
                Assert.That(bgm.Value.File.Duration.TotalSeconds, Is.EqualTo(0.5).Within(0.01), "Its length, read without decoding it all.");

                var loop = new FrameLoop();
                var player = new BgmPlayer(() => engine, library, loop, new StopwatchMonotonicClock(), null);
                player.Play(new SingleTrack(bgm.Value.Id, loop: false));
                library.InUse = id => player.Status.Value.HoldsTrack && player.Status.Value.TrackId == id;
                Assert.That(library.Delete(bgm.Value.Id).Error, Is.EqualTo(CrudError.Busy), "Not deleted while it plays.");

                var watch = Stopwatch.StartNew();
                bool played = false;
                while (watch.Elapsed < TimeSpan.FromSeconds(5))
                {
                    loop.Tick(e => throw e);
                    played = played || player.Status.Value.State == BgmPlayState.Playing;
                    if (played && player.Status.Value.State == BgmPlayState.Idle)
                    {
                        break;
                    }

                    Thread.Sleep(10);
                }

                Assert.That(played, Is.True, "It played.");
                Assert.That(player.Status.Value.State, Is.EqualTo(BgmPlayState.Idle), "And ended by itself, in real time.");
                Assert.That(watch.Elapsed.TotalSeconds, Is.GreaterThan(0.4), "Not faster than the track.");
                Assert.That(library.Delete(bgm.Value.Id).Ok, Is.True, "Once it has stopped, it can be deleted.");

                AudioEffectLoad effect = engine.LoadEffect(source);
                Assert.That(effect.Effect, Is.GreaterThanOrEqualTo(0), effect.Problem);
                Assert.That(effect.Duration.TotalSeconds, Is.EqualTo(0.5).Within(0.01));
                Assert.That(engine.PlayEffect(effect.Effect, 0f), Is.True);
                engine.UnloadEffect(effect.Effect);
                Assert.That(engine.PlayEffect(effect.Effect, 0f), Is.False, "Unloaded, it no longer plays.");
            }
        }

        [Test]
        public void TheBuiltInFiles_AreEachAdded_ThroughThePlugin()
        {
            try
            {
                NativeAudioEngine.Create().Dispose();
            }
            catch (AudioEngineException exception)
            {
                Assert.Ignore(exception.Message);
                return;
            }

            string folder = Path.Combine(UnityEngine.Application.streamingAssetsPath, BuiltInAudioClips.Folder);
            string audio = Path.Combine(_root, "Audio");
            var library = new AudioClipLibrary(audio, new UtcSystemClock(), AudioFile.Probe, () => Guid.NewGuid().ToString("N").Substring(0, 8));
            using (var builtIn = new BuiltInAudioClips(library, folder, audio, "ja", new UtcSystemClock(), null))
            {
                builtIn.InitializeAsync(CancellationToken.None).GetAwaiter().GetResult();
                builtIn.Adding.GetAwaiter().GetResult();
            }

            var manifest = new ProjectData.Config.ConfigFileStore<BuiltInAudioManifest>(folder, BuiltInAudioClips.ManifestFile, BuiltInAudioManifest.SchemaVersion, () => new BuiltInAudioManifest(), new UtcSystemClock()).Load().Value;
            int bgm = library.List(AudioClipKind.Bgm).Value.Count;
            int effects = library.List(AudioClipKind.SoundEffect).Value.Count;
            Assert.That(bgm + effects, Is.EqualTo(manifest.Clips.Count), "Every built-in file decodes and is within its limits.");
            Assert.That(bgm, Is.GreaterThan(0));
            Assert.That(effects, Is.GreaterThan(0));
        }

        private static byte[] Wav(int rate, int frames, float level)
        {
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);
            int dataBytes = frames * 4;
            writer.Write(new[] { (byte)'R', (byte)'I', (byte)'F', (byte)'F' });
            writer.Write(36 + dataBytes);
            writer.Write(new[] { (byte)'W', (byte)'A', (byte)'V', (byte)'E', (byte)'f', (byte)'m', (byte)'t', (byte)' ' });
            writer.Write(16);
            writer.Write((short)1);
            writer.Write((short)2);
            writer.Write(rate);
            writer.Write(rate * 4);
            writer.Write((short)4);
            writer.Write((short)16);
            writer.Write(new[] { (byte)'d', (byte)'a', (byte)'t', (byte)'a' });
            writer.Write(dataBytes);
            for (int i = 0; i < frames; i++)
            {
                short sample = (short)(level * 32767 * Math.Sin(i * 2 * Math.PI * 440 / rate));
                writer.Write(sample);
                writer.Write(sample);
            }

            writer.Flush();
            return stream.ToArray();
        }
    }
}
