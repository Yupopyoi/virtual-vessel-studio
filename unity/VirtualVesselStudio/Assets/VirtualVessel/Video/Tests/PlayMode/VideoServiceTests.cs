using System.Collections;
using System.Threading;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using VirtualVessel.Core.Lifecycle;
using VirtualVessel.Core.Time;

namespace VirtualVessel.Video.Tests
{
    public sealed class VideoServiceTests
    {
        private GameObject _cameraObject;
        private FrameLoop _loop;
        private VideoService _video;
        private Material _material;

        [SetUp]
        public void SetUp()
        {
            _cameraObject = new GameObject("TestMainCamera");
            Camera camera = _cameraObject.AddComponent<Camera>();
            _loop = new FrameLoop();
            var settings = new VideoSettings { Width = 64, Height = 36, BackgroundColor = Color.red };
            _video = new VideoService(settings, camera, _loop, new StopwatchMonotonicClock(), null, null);
            _video.InitializeAsync(CancellationToken.None).Wait();
        }

        [TearDown]
        public void TearDown()
        {
            _video.Dispose();
            Object.Destroy(_cameraObject);
            if (_material != null)
            {
                Object.Destroy(_material);
            }
        }

        [UnityTest]
        public IEnumerator AFlatVideo_IsDrawnWhereItWasPlaced_OverTheBackground()
        {
            // A green quad over the left half of the stream screen.
            GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Object.Destroy(quad.GetComponent<Collider>());
            quad.layer = _video.Screens.Layer;
            quad.transform.SetParent(_video.Screens.Root, false);
            quad.transform.localPosition = new Vector3(0.25f, 0.5f, 0f);
            quad.transform.localScale = new Vector3(0.5f, 1f, 1f);
            _material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            _material.SetColor("_BaseColor", Color.green);
            quad.GetComponent<Renderer>().sharedMaterial = _material;

            yield return null;
            yield return null;

            Color32[] pixels = ReadBack((RenderTexture)_video.Video.Value);
            Color32 left = pixels[(18 * 64) + 16];
            Color32 right = pixels[(18 * 64) + 48];
            Assert.That((left.r, left.g, left.b), Is.EqualTo(((byte)0, (byte)255, (byte)0)), "The flat video.");
            Assert.That((right.r, right.g, right.b), Is.EqualTo(((byte)255, (byte)0, (byte)0)), "The background.");
        }

        [Test]
        public void TheStreamTarget_IsTheConfiguredSize_AndShownToThePreview()
        {
            var target = (RenderTexture)_video.Video.Value;

            Assert.That((target.width, target.height), Is.EqualTo((64, 36)));
            Assert.That(target.sRGB, Is.True);
            Assert.That(_video.Preview.Video.Value, Is.SameAs(target));
            Assert.That(_cameraObject.GetComponent<Camera>().targetTexture, Is.SameAs(target));
        }

        [Test]
        public void AWindowCamera_KeepsTheWindowDrawn_WithoutDrawingAnything()
        {
            Camera window = _video.WindowCamera;

            Assert.That(window, Is.Not.Null, "Without a camera on the window, the UI on top of it does not show.");
            Assert.That(window.targetTexture, Is.Null);
            Assert.That(window.cullingMask, Is.EqualTo(0));
        }

        [Test]
        public void Shutdown_GivesTheCameraBack_AndReleasesWhatItMade()
        {
            Camera camera = _cameraObject.GetComponent<Camera>();

            _video.ShutdownAsync(CancellationToken.None).Wait();

            Assert.That(camera.targetTexture, Is.Null);
            Assert.That(_video.Video.Value, Is.Null);
            Assert.That(_loop.Count, Is.EqualTo(0));
            Assert.That((camera.cullingMask & (1 << _video.Screens.Layer)) != 0, Is.True, "The culling mask is given back.");
        }

        private static Color32[] ReadBack(RenderTexture texture)
        {
            RenderTexture active = RenderTexture.active;
            var read = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, false);
            try
            {
                RenderTexture.active = texture;
                read.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0);
                read.Apply();
                return read.GetPixels32();
            }
            finally
            {
                RenderTexture.active = active;
                Object.Destroy(read);
            }
        }
    }
}
