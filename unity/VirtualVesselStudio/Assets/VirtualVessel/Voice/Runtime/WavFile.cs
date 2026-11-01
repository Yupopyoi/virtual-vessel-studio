using System;
using System.IO;
using System.Text;
using VirtualVessel.Voice.Rvc;

namespace VirtualVessel.Voice
{
    /// <summary>Reads a WAV file's audio as mono floats at the device rate. Pure.</summary>
    public static class WavFile
    {
        private const int FormatPcm = 1;
        private const int FormatFloat = 3;
        private const int FormatExtensible = 0xFFFE;

        /// <summary>The file's samples mixed to mono, at the rate it was made.</summary>
        /// <exception cref="InvalidDataException">It is not a WAV file this can read.</exception>
        public static float[] Read(byte[] bytes, out int rate)
        {
            if (bytes.Length < 12 || Tag(bytes, 0) != "RIFF" || Tag(bytes, 8) != "WAVE")
            {
                throw new InvalidDataException("This is not a WAV file.");
            }

            int format = 0;
            int channels = 0;
            int bits = 0;
            rate = 0;
            int at = 12;
            while (at + 8 <= bytes.Length)
            {
                string tag = Tag(bytes, at);
                long size = BitConverter.ToUInt32(bytes, at + 4);
                int body = at + 8;
                long available = Math.Min(size, bytes.Length - body);
                if (tag == "fmt " && available >= 16)
                {
                    format = BitConverter.ToUInt16(bytes, body);
                    channels = BitConverter.ToUInt16(bytes, body + 2);
                    rate = BitConverter.ToInt32(bytes, body + 4);
                    bits = BitConverter.ToUInt16(bytes, body + 14);
                    if (format == FormatExtensible && available >= 26)
                    {
                        format = BitConverter.ToUInt16(bytes, body + 24);
                    }
                }
                else if (tag == "data")
                {
                    if (rate <= 0 || channels <= 0)
                    {
                        throw new InvalidDataException("The WAV file has no format.");
                    }

                    return Decode(new ReadOnlySpan<byte>(bytes, body, (int)available), format, channels, bits);
                }

                // Chunks are padded to an even size.
                at = (int)Math.Min(int.MaxValue, body + size + (size & 1));
            }

            throw new InvalidDataException("The WAV file has no audio.");
        }

        /// <summary>The samples at <paramref name="rate"/>, or the same array when they already are.</summary>
        public static float[] ToRate(float[] samples, int from, int rate)
        {
            if (from == rate)
            {
                return samples;
            }

            int divisor = Divisor(from, rate);
            int source = from / divisor;
            int target = rate / divisor;
            int reach = RvcDsp.ResampleReach(source, target);
            var padded = new float[samples.Length + 2 * reach];
            Array.Copy(samples, 0, padded, reach, samples.Length);
            var output = new float[(int)((long)samples.Length * target / source)];
            RvcDsp.Resample(padded, reach, source, target, RvcDsp.ResampleKernels(source, target), output);
            return output;
        }

        private static float[] Decode(ReadOnlySpan<byte> data, int format, int channels, int bits)
        {
            int bytesPerSample = bits / 8;
            bool supported = (format == FormatPcm && bits is 8 or 16 or 24 or 32) || (format == FormatFloat && bits == 32);
            if (!supported)
            {
                throw new InvalidDataException($"WAV format {format} with {bits} bits is not supported.");
            }

            int frames = data.Length / (bytesPerSample * channels);
            var mono = new float[frames];
            for (int frame = 0; frame < frames; frame++)
            {
                float sum = 0f;
                for (int channel = 0; channel < channels; channel++)
                {
                    sum += Sample(data.Slice((frame * channels + channel) * bytesPerSample, bytesPerSample), format);
                }

                mono[frame] = sum / channels;
            }

            return mono;
        }

        private static float Sample(ReadOnlySpan<byte> bytes, int format)
        {
            if (format == FormatFloat)
            {
                return BitConverter.ToSingle(bytes);
            }

            switch (bytes.Length)
            {
                case 1:
                    return (bytes[0] - 128) / 128f;
                case 2:
                    return BitConverter.ToInt16(bytes) / 32768f;
                case 3:
                    return ((bytes[2] << 24 | bytes[1] << 16 | bytes[0] << 8) >> 8) / 8388608f;
                default:
                    return BitConverter.ToInt32(bytes) / 2147483648f;
            }
        }

        private static string Tag(byte[] bytes, int at)
        {
            return Encoding.ASCII.GetString(bytes, at, 4);
        }

        private static int Divisor(int a, int b)
        {
            return b == 0 ? a : Divisor(b, a % b);
        }
    }
}
