using System.Collections;
using System.Collections.Generic;
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
        private readonly List<Object> _made = new List<Object>();

        [SetUp]
        public void SetUp()
        {
            _cameraObject = new GameObject("TestMainCamera");
            _cameraObject.transform.position = new Vector3(5f, 5f, 5f);
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
            foreach (Object made in _made)
            {
                Object.Destroy(made);
            }

            _made.Clear();
        }

        [UnityTest]
        public IEnumerator AFlatVideo_IsDrawnWhereItWasPlaced_OverTheBackground()
        {
            // A green quad over the left half of the stream screen.
            GameObject quad = Quad(_video.Screens.Layer, Color.green);
            quad.transform.SetParent(_video.Screens.Root, false);
            quad.transform.localPosition = new Vector3(0.25f, 0.5f, 0f);
            quad.transform.localScale = new Vector3(0.5f, 1f, 1f);

            yield return null;
            yield return null;

            Color32[] pixels = ReadBack((RenderTexture)_video.Video.Value);
            Color32 left = pixels[(18 * 64) + 16];
            Color32 right = pixels[(18 * 64) + 48];
            Assert.That((left.r, left.g, left.b), Is.EqualTo(((byte)0, (byte)255, (byte)0)), "The flat video.");
            Assert.That((right.r, right.g, right.b), Is.EqualTo(((byte)255, (byte)0, (byte)0)), "The background.");
        }

        [UnityTest]
        public IEnumerator AnAvatar_IsDrawnInFrontOfTheFlatVideos_FromTheMainCamera()
        {
            // A flat video over the whole screen, and a small blue quad on the avatar layer in front of
            // the Main Camera, where an avatar would stand.
            GameObject screen = Quad(_video.Screens.Layer, Color.green);
            screen.transform.SetParent(_video.Screens.Root, false);
            screen.transform.localPosition = new Vector3(0.5f, 0.5f, 0f);
            Transform camera = _cameraObject.transform;
            GameObject avatar = Quad(_video.AvatarLayer, Color.blue);
            avatar.transform.SetPositionAndRotation(camera.position + (camera.forward * 2f), camera.rotation);
            avatar.transform.localScale = new Vector3(0.3f, 0.3f, 1f);

            yield return null;
            yield return null;

            Color32[] pixels = ReadBack((RenderTexture)_video.Video.Value);
            Color32 center = pixels[(18 * 64) + 32];
            Color32 corner = pixels[(2 * 64) + 2];
            Assert.That((center.r, center.g, center.b), Is.EqualTo(((byte)0, (byte)0, (byte)255)), "The avatar, in front.");
            Assert.That((corner.r, corner.g, corner.b), Is.EqualTo(((byte)0, (byte)255, (byte)0)), "The flat video behind it.");
        }

        [Test]
        public void TheMainCamera_StartsWhereTheSettingsSay_AndTheAvatarCameraMatchesIt()
        {
            Camera camera = _cameraObject.GetComponent<Camera>();
            var defaults = new VideoSettings();

            Assert.That(camera.transform.position, Is.EqualTo(defaults.CameraPosition.Value));
            Assert.That(camera.fieldOfView, Is.EqualTo(defaults.CameraFieldOfView.Value));
            Assert.That(_video.AvatarCamera.transform.position, Is.EqualTo(camera.transform.position));
            Assert.That(_video.AvatarCamera.fieldOfView, Is.EqualTo(camera.fieldOfView));
            Assert.That(camera.cullingMask & (1 << _video.AvatarLayer), Is.Zero, "Only the avatar camera draws avatars.");
        }

        [UnityTest]
        public IEnumerator WithAFraming_TheAvatarIsDrawnWhereTheFramingPutsIt_AndWithoutOne_FollowsTheMainCamera()
        {
            // A blue quad standing at the origin, and a camera 2 m in front of it whose projection is shifted so the
            // origin lands three quarters across the stream, halfway down (video detailed design 7.6).
            GameObject screen = Quad(_video.Screens.Layer, Color.green);
            screen.transform.SetParent(_video.Screens.Root, false);
            screen.transform.localPosition = new Vector3(0.5f, 0.5f, 0f);
            GameObject avatar = Quad(_video.AvatarLayer, Color.blue);
            avatar.transform.localScale = new Vector3(0.3f, 0.3f, 1f);
            float half = 0.5f * 36f / 64f;
            _video.SetFraming(new AvatarFraming(new Vector3(0f, 0f, -2f), Quaternion.identity, -0.75f, 0.25f, -half, half));

            yield return null;
            yield return null;

            Color32[] pixels = ReadBack((RenderTexture)_video.Video.Value);
            Color32 framed = pixels[(18 * 64) + 48];
            Color32 middle = pixels[(18 * 64) + 32];
            Assert.That((framed.r, framed.g, framed.b), Is.EqualTo(((byte)0, (byte)0, (byte)255)), "The avatar, where the framing put it.");
            Assert.That((middle.r, middle.g, middle.b), Is.EqualTo(((byte)0, (byte)255, (byte)0)), "Not in the middle.");

            _video.SetFraming(null);
            Assert.That(_video.AvatarCamera.transform.localPosition, Is.EqualTo(Vector3.zero), "Back on the Main Camera.");
            Assert.That(_video.AvatarCamera.fieldOfView, Is.EqualTo(_cameraObject.GetComponent<Camera>().fieldOfView));
        }

        [UnityTest]
        public IEnumerator AFlatVideo_AtTheRightEdge_ReachesTheEdge_WhateverTheWindowShape()
        {
            // The layer's cameras took the window's shape, not the stream's: with the window no longer 16:9 (the
            // taskbar kept), a video placed at the edge was drawn short of it (the user's report, 2026-11-15).
            GameObject quad = Quad(_video.Screens.Layer, Color.green);
            quad.transform.SetParent(_video.Screens.Root, false);
            quad.transform.localPosition = new Vector3(0.95f, 0.5f, 0f);
            quad.transform.localScale = new Vector3(0.1f, 1f, 1f);

            yield return null;
            yield return null;

            Color32[] pixels = ReadBack((RenderTexture)_video.Video.Value);
            Color32 edge = pixels[(18 * 64) + 63];
            Color32 before = pixels[(18 * 64) + 56];
            Assert.That((edge.r, edge.g, edge.b), Is.EqualTo(((byte)0, (byte)255, (byte)0)), "The video reaches the last column.");
            Assert.That((before.r, before.g, before.b), Is.EqualTo(((byte)255, (byte)0, (byte)0)), "And starts where it was placed.");
        }

        [Test]
        public void TheLayerCameras_TakeTheStreamsShape_NotTheWindows()
        {
            Assert.That(_video.ScreenCamera.aspect, Is.EqualTo(64f / 36f).Within(1e-4f));
            Assert.That(_video.AvatarCamera.aspect, Is.EqualTo(64f / 36f).Within(1e-4f));
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
            Assert.That(camera.transform.position, Is.EqualTo(new Vector3(5f, 5f, 5f)), "The position is given back.");
            Assert.That(_video.AvatarCamera, Is.Null, "The avatar camera is removed.");
        }

        private GameObject Quad(int layer, Color color)
        {
            GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Object.Destroy(quad.GetComponent<Collider>());
            quad.layer = layer;
            var material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            material.SetColor("_BaseColor", color);
            quad.GetComponent<Renderer>().sharedMaterial = material;
            _made.Add(material);
            _made.Add(quad);
            return quad;
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
