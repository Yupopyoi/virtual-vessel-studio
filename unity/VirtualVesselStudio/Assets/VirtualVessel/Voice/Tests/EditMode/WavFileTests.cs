using System;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;

namespace VirtualVessel.Voice.Tests
{
    public sealed class WavFileTests
    {
        [Test]
        public void Read_16Bit_MixesTheChannels()
        {
            byte[] bytes = Wav(1, 2, 48000, 16, Shorts(16384, 0, -16384, -16384));

            float[] samples = WavFile.Read(bytes, out int rate);

            Assert.That(rate, Is.EqualTo(48000));
            Assert.That(samples, Is.EqualTo(new[] { 0.25f, -0.5f }).Within(1e-4f));
        }

        [Test]
        public void Read_24Bit_KeepsTheSign()
        {
            byte[] data = { 0x00, 0x00, 0x40, 0x00, 0x00, 0xC0 };

            float[] samples = WavFile.Read(Wav(1, 1, 44100, 24, data), out _);

            Assert.That(samples, Is.EqualTo(new[] { 0.5f, -0.5f }).Within(1e-5f));
        }

        [Test]
        public void Read_Float()
        {
            byte[] data = BitConverter.GetBytes(0.75f).Concat(BitConverter.GetBytes(-0.25f)).ToArray();

            float[] samples = WavFile.Read(Wav(3, 1, 48000, 32, data), out _);

            Assert.That(samples, Is.EqualTo(new[] { 0.75f, -0.25f }));
        }

        [Test]
        public void Read_SkipsOtherChunks_WithTheirPadding()
        {
            byte[] bytes = Wav(1, 1, 48000, 16, Shorts(16384), Encoding.ASCII.GetBytes("LIST").Concat(BitConverter.GetBytes(3)).Concat(new byte[4]).ToArray());

            Assert.That(WavFile.Read(bytes, out _), Is.EqualTo(new[] { 0.5f }).Within(1e-4f));
        }

        [Test]
        public void Read_Refuses_WhatItCannotRead()
        {
            Assert.Throws<InvalidDataException>(() => WavFile.Read(new byte[20], out _), "Not a WAV file.");
            Assert.Throws<InvalidDataException>(() => WavFile.Read(Wav(85, 1, 48000, 16, Shorts(0)), out _), "Compressed.");
        }

        [Test]
        public void ToRate_SameRate_IsTheSameArray()
        {
            var samples = new float[4];

            Assert.That(WavFile.ToRate(samples, 48000, 48000), Is.SameAs(samples));
        }

        [TestCase(44100)]
        [TestCase(16000)]
        public void ToRate_KeepsATone(int from)
        {
            var samples = new float[from / 4];
            for (int i = 0; i < samples.Length; i++)
            {
                samples[i] = MathF.Sin(2f * MathF.PI * 440f * i / from);
            }

            float[] output = WavFile.ToRate(samples, from, 48000);

            Assert.That(output.Length, Is.EqualTo(12000));
            for (int i = 1000; i < output.Length - 1000; i++)
            {
                Assert.That(output[i], Is.EqualTo(MathF.Sin(2f * MathF.PI * 440f * i / 48000f)).Within(0.01f));
            }
        }

        private static byte[] Shorts(params short[] values)
        {
            return values.SelectMany(BitConverter.GetBytes).ToArray();
        }

        private static byte[] Wav(short format, short channels, int rate, short bits, byte[] data, byte[] before = null)
        {
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);
            writer.Write(Encoding.ASCII.GetBytes("RIFF"));
            writer.Write(0);
            writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
            writer.Write(16);
            writer.Write(format);
            writer.Write(channels);
            writer.Write(rate);
            writer.Write(rate * channels * bits / 8);
            writer.Write((short)(channels * bits / 8));
            writer.Write(bits);
            if (before != null)
            {
                writer.Write(before);
            }

            writer.Write(Encoding.ASCII.GetBytes("data"));
            writer.Write(data.Length);
            writer.Write(data);
            writer.Flush();
            return stream.ToArray();
        }
    }
}
