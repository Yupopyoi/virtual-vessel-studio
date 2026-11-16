using System;
using System.IO;
using NUnit.Framework;

namespace VirtualVessel.Audio.Tests
{
    /// <summary>
    /// Files through the audio plugin and Windows' decoders. The plugin is built outside Unity (it is not in Git),
    /// so without it these are ignored; its own tests cover the decoding.
    /// </summary>
    public sealed class AudioFileTests
    {
        [Test]
        public void ReadMono_KeepsTheRate_AndMixesTheChannels()
        {
            const int Rate = 22050;
            const int Frames = Rate / 4;
            string path = Path.Combine(Path.GetTempPath(), $"vv_audio_file_{Guid.NewGuid():N}.wav");
            File.WriteAllBytes(path, StereoWav(Rate, Frames, 0.25f, -0.75f));
            try
            {
                float[] samples = Read(path, out int rate);

                Assert.That(rate, Is.EqualTo(Rate));
                Assert.That(samples, Has.Length.EqualTo(Frames));
                Assert.That(samples, Has.All.EqualTo(-0.25f).Within(0.001f));
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Test]
        public void ReadMono_SaysAMissingFileIsUnreadable()
        {
            Assert.Throws<InvalidDataException>(() => Read(Path.Combine(Path.GetTempPath(), "vv no such file.m4a"), out _));
        }

        private static float[] Read(string path, out int rate)
        {
            try
            {
                return AudioFile.ReadMono(path, out rate);
            }
            catch (AudioEngineException exception)
            {
                Assert.Ignore(exception.Message);
                throw;
            }
        }

        // 16-bit PCM with a constant on each channel.
        private static byte[] StereoWav(int rate, int frames, float left, float right)
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
                writer.Write((short)(left * 32767));
                writer.Write((short)(right * 32767));
            }

            writer.Flush();
            return stream.ToArray();
        }
    }
}
