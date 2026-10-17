using NUnit.Framework;
using UnityEngine;

namespace VirtualVessel.Video.Tests
{
    public sealed class ScreenLayerMathTests
    {
        private static Vector3 World(Vector3 local, float aspect)
        {
            (Vector3 position, Vector3 scale) = ScreenLayerMath.RootPlacement(aspect);
            return position + Vector3.Scale(scale, local);
        }

        [Test]
        public void TheLayer_SpansTheScreenCameraView_TopLeftFirst()
        {
            const float Aspect = 16f / 9f;

            Assert.That(World(Vector3.zero, Aspect), Is.EqualTo(new Vector3(-Aspect / 2, 0.5f, ScreenLayerMath.RootDistance)));
            Assert.That(World(new Vector3(1, 1, 0), Aspect), Is.EqualTo(new Vector3(Aspect / 2, -0.5f, ScreenLayerMath.RootDistance)));
            Assert.That(World(new Vector3(0.5f, 0.5f, 0), Aspect).x, Is.EqualTo(0).Within(1e-6));
        }

        [Test]
        public void ToPixels_CountsFromTheBottom_AsTexturesDo()
        {
            var size = new VideoSize(1920, 1080);

            Assert.That(ScreenLayerMath.ToPixels(new Vector2(0, 0), size), Is.EqualTo(new Vector2(0, 1080)));
            Assert.That(ScreenLayerMath.ToPixels(new Vector2(0.5f, 1), size), Is.EqualTo(new Vector2(960, 0)));
        }
    }

    public sealed class StreamFrameMeterTests
    {
        [Test]
        public void TheRate_IsReported_WhenASecondIsComplete()
        {
            const long Second = 1_000_000;
            long now = 10 * Second;
            StreamFrameMeter meter = StreamFrameMeter.Start(now);

            for (int i = 0; i < 24; i++)
            {
                now += Second / 25;
                meter = meter.Next(now, Second);
                Assert.That(meter.Completed, Is.Null);
            }

            now += Second / 25;
            meter = meter.Next(now, Second);
            Assert.That(meter.Completed, Is.EqualTo(25.0));

            meter = meter.Next(now + (Second / 25), Second);
            Assert.That(meter.Completed, Is.Null, "A new window starts.");
        }
    }
}
