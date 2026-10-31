using System.Collections;
using System.Linq;
using NUnit.Framework;
using Unity.Collections;
using UnityEngine;
using UnityEngine.TestTools;
using VirtualVessel.Video.Recording;

namespace VirtualVessel.Video.Tests
{
    /// <summary>The compute shader on the real GPU: colors, the rows' order, and widths that are not a multiple of 4.</summary>
    public sealed class Nv12FrameReadbackTests
    {
        private const int Height = 36;

        // BT.709 limited range, from the sRGB values.
        private static readonly Color32 s_red = new Color32(255, 0, 0, 255);
        private static readonly Color32 s_white = new Color32(255, 255, 255, 255);
        private static readonly Color32 s_gray = new Color32(128, 128, 128, 255);

        [UnityTest]
        public IEnumerator Colors_BecomeLimitedRangeBt709_WithTheTopRowsFirst([Values(64, 66)] int width)
        {
            byte[] frame = null;
            yield return Convert(width, s_red, s_white, bytes => frame = bytes);

            Assert.That(frame, Is.Not.Null);
            Assert.That(frame.Length, Is.EqualTo(Nv12Layout.FrameBytes(width, Height)));

            int chroma = width * Height;
            Assert.That(frame[0], Is.EqualTo(63).Within(1), "Y of red");
            Assert.That(frame[width - 1], Is.EqualTo(63).Within(1), "the last pixel of a row");
            Assert.That(frame[chroma], Is.EqualTo(102).Within(1), "U of red");
            Assert.That(frame[chroma + 1], Is.EqualTo(240).Within(1), "V of red");

            Assert.That(frame[chroma - 1], Is.EqualTo(235).Within(1), "Y of white");
            Assert.That(frame[frame.Length - 2], Is.EqualTo(128).Within(1), "U of white");
            Assert.That(frame[frame.Length - 1], Is.EqualTo(128).Within(1), "V of white");
        }

        [UnityTest]
        public IEnumerator Gray_KeepsItsValue_ThroughTheSrgbTarget()
        {
            byte[] frame = null;
            yield return Convert(64, s_gray, s_gray, bytes => frame = bytes);

            Assert.That(frame[0], Is.EqualTo(126).Within(1));
        }

        [Test]
        public void Layout_RoundsTheStrideUpToAWord()
        {
            Assert.That(Nv12Layout.Stride(1920), Is.EqualTo(1920));
            Assert.That(Nv12Layout.Stride(854), Is.EqualTo(856));
            Assert.That(Nv12Layout.FrameBytes(1920, 1080), Is.EqualTo(3110400));
            Assert.That(Nv12Layout.BufferBytes(854, 480), Is.EqualTo(856 * 720));
        }

        private static IEnumerator Convert(int width, Color32 top, Color32 bottom, System.Action<byte[]> done)
        {
            // Texture2D rows go from the bottom, as Unity keeps them.
            var pixels = Enumerable.Range(0, width * Height).Select(i => i / width >= Height / 2 ? top : bottom).ToArray();
            var image = new Texture2D(width, Height, TextureFormat.RGBA32, false, false);
            image.SetPixels32(pixels);
            image.Apply();
            var target = new RenderTexture(width, Height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            Graphics.Blit(image, target);

            var readback = new Nv12FrameReadback(width, Height);
            var sink = new CopyingSink();
            bool arrived = false;
            readback.Request(target, request =>
            {
                Assert.That(request.hasError, Is.False);
                readback.Deliver(request.GetData<byte>(), 1, sink);
                arrived = true;
            });

            for (int i = 0; i < 300 && !arrived; i++)
            {
                yield return null;
            }

            readback.Dispose();
            target.Release();
            Object.Destroy(target);
            Object.Destroy(image);
            done(sink.Frame);
        }

        private sealed class CopyingSink : IFrameSink
        {
            public byte[] Frame { get; private set; }

            public bool Push(System.IntPtr data, int length, long timestamp)
            {
                Frame = new byte[length];
                System.Runtime.InteropServices.Marshal.Copy(data, Frame, 0, length);
                return true;
            }
        }
    }
}