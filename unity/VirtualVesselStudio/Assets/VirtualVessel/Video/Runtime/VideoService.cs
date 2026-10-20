using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using VirtualVessel.Core.Lifecycle;
using VirtualVessel.Core.State;
using VirtualVessel.Core.Time;
using VirtualVessel.Diagnostics.Logging;
using VirtualVessel.Diagnostics.Performance;
using VirtualVessel.UI.Preview;

namespace VirtualVessel.Video
{
    /// <summary>
    /// Makes the stream video: the persistent Main Camera renders into the Stream Render Target, with a
    /// screen camera on its camera stack drawing the flat video layer and an avatar camera drawing the
    /// avatar in front of it (video detailed design 7). An Optional application service: without it
    /// there is no preview, and everything else runs.
    /// </summary>
    public sealed class VideoService : IApplicationService, IStreamOutput, IStreamScreenLayer
    {
        public const string ServiceName = "Video";

        /// <summary>The Unity layer of the flat video layer, defined in the project's tags and layers.</summary>
        public const string ScreenLayerName = "StreamScreens";

        /// <summary>The Unity layer of avatars, defined in the project's tags and layers.</summary>
        public const string AvatarLayerName = "Avatar";

        private readonly VideoSettings _settings;
        private readonly Camera _mainCamera;
        private readonly IFrameLoop _frameLoop;
        private readonly IMonotonicClock _clock;
        private readonly ILog _log;
        private readonly ObservableValue<Texture> _video = new ObservableValue<Texture>(null);
        private readonly ObservableValue<PreviewStats> _stats = new ObservableValue<PreviewStats>(default);
        private readonly PerfTimer _frameInterval;
        private readonly PerfCounter _frames;
        private RenderTexture _target;
        private GameObject _screenCameraObject;
        private GameObject _windowCameraObject;
        private Camera _screenCamera;
        private Camera _avatarCamera;
        private GameObject _root;
        private IDisposable _tick;
        private StreamFrameMeter _meter;
        private long _lastFrame;
        private int _layer = -1;
        private int _avatarLayer = -1;

        // What the scene's camera had, given back at shutdown.
        private int _originalCullingMask;
        private CameraClearFlags _originalClearFlags;
        private Color _originalBackground;
        private Vector3 _originalPosition;
        private float _originalFieldOfView;

        public VideoService(VideoSettings settings, Camera mainCamera, IFrameLoop frameLoop, IMonotonicClock clock, IPerformanceMetrics metrics, ILog log)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _mainCamera = mainCamera != null ? mainCamera : throw new ArgumentNullException(nameof(mainCamera));
            _frameLoop = frameLoop ?? throw new ArgumentNullException(nameof(frameLoop));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _log = log;
            _frameInterval = metrics?.Timer("Video", "FrameInterval");
            _frames = metrics?.Counter("Video", "Frames");
            Size = new VideoSize(settings.Width, settings.Height);
            Preview = new StreamPreviewSource(_video, _stats);
        }

        public string Name => ServiceName;

        public IReadOnlyObservable<Texture> Video => _video;

        public VideoSize Size { get; }

        public IStreamScreenLayer Screens => this;

        public Transform Root => _root != null ? _root.transform : null;

        public int Layer => _layer;

        public int AvatarLayer => _avatarLayer;

        /// <summary>For the UI's stream preview: the same texture that is streamed (system design 10.6).</summary>
        public IStreamPreviewSource Preview { get; }

        /// <summary>The camera that keeps the window drawn so the UI shows; for tests.</summary>
        internal Camera WindowCamera => _windowCameraObject != null ? _windowCameraObject.GetComponent<Camera>() : null;

        /// <summary>The camera that draws avatars over the flat videos; for tests.</summary>
        internal Camera AvatarCamera => _avatarCamera;

        public Task InitializeAsync(CancellationToken cancellationToken)
        {
            _layer = LayerMask.NameToLayer(ScreenLayerName);
            if (_layer < 0)
            {
                throw new InvalidOperationException($"The layer '{ScreenLayerName}' is missing from the project's tags and layers.");
            }

            _avatarLayer = LayerMask.NameToLayer(AvatarLayerName);
            if (_avatarLayer < 0)
            {
                throw new InvalidOperationException($"The layer '{AvatarLayerName}' is missing from the project's tags and layers.");
            }

            if (Size.Width <= 0 || Size.Height <= 0)
            {
                throw new InvalidOperationException($"The stream size {Size} is not valid.");
            }

            // sRGB: the project renders in linear color, and a linear target would look washed out in the preview.
            _target = new RenderTexture(Size.Width, Size.Height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { name = "StreamRenderTarget" };
            _target.Create();

            _originalCullingMask = _mainCamera.cullingMask;
            _originalClearFlags = _mainCamera.clearFlags;
            _originalBackground = _mainCamera.backgroundColor;
            _originalPosition = _mainCamera.transform.position;
            _originalFieldOfView = _mainCamera.fieldOfView;
            _mainCamera.targetTexture = _target;
            _mainCamera.cullingMask &= ~((1 << _layer) | (1 << _avatarLayer));
            if (_settings.CameraPosition is Vector3 cameraPosition)
            {
                _mainCamera.transform.position = cameraPosition;
            }

            if (_settings.CameraFieldOfView is float fieldOfView)
            {
                _mainCamera.fieldOfView = fieldOfView;
            }

            _mainCamera.clearFlags = CameraClearFlags.SolidColor;
            _mainCamera.backgroundColor = _settings.BackgroundColor;
            UniversalAdditionalCameraData mainData = _mainCamera.GetUniversalAdditionalCameraData();
            mainData.renderType = CameraRenderType.Base;

            // The screen camera stays at the origin, not under the Main Camera, so flat videos keep
            // their place when the Main Camera moves (video detailed design 7.2).
            _screenCameraObject = new GameObject("StreamScreenCamera");
            _screenCamera = _screenCameraObject.AddComponent<Camera>();
            _screenCamera.orthographic = true;
            _screenCamera.orthographicSize = ScreenLayerMath.OrthographicSize;
            _screenCamera.nearClipPlane = 0.01f;
            _screenCamera.farClipPlane = ScreenLayerMath.RootDistance * 2f;
            _screenCamera.cullingMask = 1 << _layer;
            UniversalAdditionalCameraData screenData = _screenCamera.GetUniversalAdditionalCameraData();
            screenData.renderType = CameraRenderType.Overlay;
            mainData.cameraStack.Add(_screenCamera);

            // The avatar camera rides on the Main Camera, so the avatar is seen from where the stage
            // is, but drawn after the flat videos so it stays in front of them. An overlay camera clears
            // depth by default (URP sets it only in the inspector), so the stage's depth never hides
            // the avatar (video detailed design 7.2).
            var avatarCameraObject = new GameObject("AvatarCamera");
            avatarCameraObject.transform.SetParent(_mainCamera.transform, false);
            _avatarCamera = avatarCameraObject.AddComponent<Camera>();
            _avatarCamera.cullingMask = 1 << _avatarLayer;
            MatchMainCamera(_mainCamera, _avatarCamera);
            UniversalAdditionalCameraData avatarData = _avatarCamera.GetUniversalAdditionalCameraData();
            avatarData.renderType = CameraRenderType.Overlay;
            mainData.cameraStack.Add(_avatarCamera);

            // The Main Camera now draws into the stream target, so nothing would draw the window and
            // the UI, which is drawn on top of the window, would not show. This camera only clears it.
            _windowCameraObject = new GameObject("WindowCamera");
            Camera windowCamera = _windowCameraObject.AddComponent<Camera>();
            windowCamera.cullingMask = 0;
            windowCamera.clearFlags = CameraClearFlags.SolidColor;
            windowCamera.backgroundColor = Color.black;
            windowCamera.depth = _mainCamera.depth + 1;
            windowCamera.GetUniversalAdditionalCameraData().renderType = CameraRenderType.Base;

            _root = new GameObject("StreamScreens") { layer = _layer };
            (Vector3 position, Vector3 scale) = ScreenLayerMath.RootPlacement(Size.Aspect);
            _root.transform.SetPositionAndRotation(position, Quaternion.identity);
            _root.transform.localScale = scale;

            UnityEngine.Application.targetFrameRate = _settings.TargetFrameRate;
            QualitySettings.vSyncCount = 0;
            _video.Value = _target;
            _meter = StreamFrameMeter.Start(_clock.GetTimestamp());
            _stats.Value = new PreviewStats(0, _settings.TargetFrameRate, Size.Width, Size.Height, null);
            _tick = _frameLoop.Register(Tick);
            _log?.Information($"Video started: {Size} at {_settings.TargetFrameRate} fps, color space {QualitySettings.activeColorSpace}, target {_target.format} sRGB.");
            return Task.CompletedTask;
        }

        public Task ShutdownAsync(CancellationToken cancellationToken)
        {
            Release();
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            Release();
        }

        private void Tick()
        {
            if (_mainCamera == null)
            {
                // The scene's camera went away: nothing more is drawn into the target.
                _log?.Error("The Main Camera was destroyed; the stream video stops.");
                Release();
                return;
            }

            if (_avatarCamera != null)
            {
                MatchMainCamera(_mainCamera, _avatarCamera);
            }

            long now = _clock.GetTimestamp();
            _frames?.Increment();
            if (_lastFrame != 0)
            {
                _frameInterval?.Record(_clock.GetElapsed(_lastFrame, now));
            }

            _lastFrame = now;
            _meter = _meter.Next(now, _clock.Frequency);
            if (_meter.Completed is double rate)
            {
                _stats.Value = new PreviewStats(rate, _settings.TargetFrameRate, Size.Width, Size.Height, null);
            }
        }

        private void Release()
        {
            _tick?.Dispose();
            _tick = null;
            _video.Value = null;
            if (_mainCamera != null && _target != null)
            {
                _mainCamera.targetTexture = null;
                _mainCamera.cullingMask = _originalCullingMask;
                _mainCamera.clearFlags = _originalClearFlags;
                _mainCamera.backgroundColor = _originalBackground;
                _mainCamera.transform.position = _originalPosition;
                _mainCamera.fieldOfView = _originalFieldOfView;
                if (_screenCamera != null)
                {
                    _mainCamera.GetUniversalAdditionalCameraData().cameraStack.Remove(_screenCamera);
                }

                if (_avatarCamera != null)
                {
                    _mainCamera.GetUniversalAdditionalCameraData().cameraStack.Remove(_avatarCamera);
                }
            }

            if (_avatarCamera != null)
            {
                DestroyObject(_avatarCamera.gameObject);
            }

            _avatarCamera = null;

            DestroyObject(_root);
            DestroyObject(_screenCameraObject);
            DestroyObject(_windowCameraObject);
            _windowCameraObject = null;
            _root = null;
            _screenCameraObject = null;
            _screenCamera = null;
            if (_target != null)
            {
                _target.Release();
                DestroyObject(_target);
                _target = null;
            }
        }

        private static void MatchMainCamera(Camera main, Camera avatar)
        {
            avatar.fieldOfView = main.fieldOfView;
            avatar.nearClipPlane = main.nearClipPlane;
            avatar.farClipPlane = main.farClipPlane;
        }

        private static void DestroyObject(UnityEngine.Object value)
        {
            if (value == null)
            {
                return;
            }

            if (UnityEngine.Application.isPlaying)
            {
                UnityEngine.Object.Destroy(value);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(value);
            }
        }
    }
}
