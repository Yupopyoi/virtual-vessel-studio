using System;
using System.Collections;
using System.IO;
using System.Threading;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using VirtualVessel.Capture;
using VirtualVessel.Core.Geometry;
using VirtualVessel.Core.Lifecycle;
using VirtualVessel.Core.State;
using VirtualVessel.Core.Time;
using VirtualVessel.Video;

namespace VirtualVessel.Stage.Tests
{
    /// <summary>Capture video placed by Stage reaches the stream video where it was placed (Stage detailed design 14).</summary>
    public sealed class StageRenderingTests
    {
        private string _directory;
        private GameObject _cameraObject;
        private VideoService _video;
        private StageService _stage;
        private SolidSource _source;

        [SetUp]
        public void SetUp()
        {
            _directory = Path.Combine(Path.GetTempPath(), "VirtualVesselTests", Guid.NewGuid().ToString("N"));
            _cameraObject = new GameObject("TestMainCamera");
            var loop = new FrameLoop();
            var clock = new StopwatchMonotonicClock();
            _video = new VideoService(new VideoSettings { Width = 64, Height = 36, BackgroundColor = Color.black }, _cameraObject.AddComponent<Camera>(), loop, clock, null, null);
            _video.InitializeAsync(CancellationToken.None).Wait();
            _source = new SolidSource("subscreen", Color.blue);
            _stage = new StageService(new OneSource(_source), _video, loop, StageService.CreateStore(_directory, new UtcSystemClock()), clock, null);
            _stage.InitializeAsync(CancellationToken.None).Wait();
        }

        [TearDown]
        public void TearDown()
        {
            _stage.Dispose();
            _video.Dispose();
            _source.Dispose();
            UnityEngine.Object.Destroy(_cameraObject);
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }

        [UnityTest]
        public IEnumerator APlacedVideo_IsDrawnInItsPlace()
        {
            // The right half of the stream screen.
            _stage.Screens[0].CommitLayoutAsync(FrameLayout.Create(new NormalizedRect(0.5, 0, 0.5, 1)), CancellationToken.None).Wait();
            yield return null;
            yield return null;

            Color32[] pixels = ReadBack((RenderTexture)_video.Video.Value);
            Color32 left = pixels[(18 * 64) + 16];
            Color32 right = pixels[(18 * 64) + 48];
            Assert.That((right.r, right.g, right.b), Is.EqualTo(((byte)0, (byte)0, (byte)255)), "The video.");
            Assert.That(Math.Min(left.r, Math.Min(left.g, left.b)), Is.GreaterThan(190), "The pastel backdrop.");
        }

        [UnityTest]
        public IEnumerator TheBackdrop_FillsTheWholeStream_BehindTheVideos()
        {
            _stage.Screens[0].CommitLayoutAsync(FrameLayout.Create(new NormalizedRect(0.5, 0, 0.5, 1)), CancellationToken.None).Wait();
            yield return null;
            yield return null;

            Color32[] pixels = ReadBack((RenderTexture)_video.Video.Value);
            foreach (int index in new[] { 0, 31, 64 * 35, (64 * 18) + 2 })
            {
                Color32 pixel = pixels[index];
                Assert.That(Math.Min(pixel.r, Math.Min(pixel.g, pixel.b)), Is.GreaterThan(190), $"Pixel {index}: the backdrop, not the dark background color.");
            }
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
                UnityEngine.Object.Destroy(read);
            }
        }

        private sealed class SolidSource : IVideoCaptureSource, IDisposable
        {
            private readonly Texture2D _texture;

            public SolidSource(string id, Color color)
            {
                Id = id;
                _texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                _texture.SetPixels32(new Color32[] { color, color, color, color });
                _texture.Apply();
                Texture = new ObservableValue<Texture>(_texture);
            }

            public string Id { get; }

            public CaptureSourceKind Kind => CaptureSourceKind.SubScreenCapture;

            public IReadOnlyObservable<CaptureState> State { get; } = new ObservableValue<CaptureState>(CaptureState.Capturing);

            public IReadOnlyObservable<CaptureProblem> Problem { get; } = new ObservableValue<CaptureProblem>(CaptureProblem.None);

            public IReadOnlyObservable<CaptureSourceInfo> Info { get; } = new ObservableValue<CaptureSourceInfo>(new CaptureSourceInfo(2, 2, 60, "solid"));

            public IReadOnlyObservable<Texture> Texture { get; }

            public long FrameCount => 0;

            public long LastFrameTimestamp => 0;

            public IReadOnlyObservable<CaptureFlip> Flip { get; } = new ObservableValue<CaptureFlip>(CaptureFlip.None);

            public void SetFlip(CaptureFlip flip)
            {
            }

            public void Start()
            {
            }

            public void Stop()
            {
            }

            public void Dispose()
            {
                UnityEngine.Object.Destroy(_texture);
            }
        }

        private sealed class OneSource : ICaptureSources
        {
            private readonly IVideoCaptureSource _source;

            public OneSource(IVideoCaptureSource source)
            {
                _source = source;
            }

            public System.Collections.Generic.IReadOnlyList<IVideoCaptureSource> Sources => new[] { _source };

            public IVideoCaptureSource Find(string id)
            {
                return id == _source.Id ? _source : null;
            }
        }
    }
}
