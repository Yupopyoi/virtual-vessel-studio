using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using VirtualVessel.Capture;
using VirtualVessel.Core.Geometry;
using VirtualVessel.Core.Lifecycle;
using VirtualVessel.Core.Time;
using VirtualVessel.Diagnostics.Logging;
using VirtualVessel.ProjectData.Config;
using VirtualVessel.Core.State;
using VirtualVessel.UI.Frames;
using VirtualVessel.UI.Lighting;
using VirtualVessel.Video;

namespace VirtualVessel.Stage
{
    /// <summary>
    /// Puts each capture video on the stream screen where the placement editor put it, and saves the
    /// placements (Stage detailed design 7). Until Stage Scenes exist it also gives the avatar a
    /// default spawn point and a light, whose brightness the user may change (Stage detailed design
    /// 7.1). An Optional application service.
    /// </summary>
    public sealed class StageService : IApplicationService, IAvatarSpawnPoints, IStageLighting
    {
        public const string ServiceName = "Stage";

        /// <summary>The shader of the quads, in a Resources folder so that builds include it.</summary>
        public const string ScreenShaderPath = "StreamScreen";

        private static readonly TimeSpan s_saveDelay = TimeSpan.FromSeconds(0.5);

        private readonly ICaptureSources _captures;
        private readonly IStreamOutput _output;
        private readonly IFrameLoop _frameLoop;
        private readonly ConfigFileStore<StageSettings> _store;
        private readonly ConfigSaveScheduler<StageSettings> _saves;
        private readonly ILog _log;
        private readonly List<CaptureScreen> _screens = new List<CaptureScreen>();
        private readonly ObservableValue<float> _intensity = new ObservableValue<float>(StageLightIntensity.Default);
        private StageSettings _settings = new StageSettings();
        private GameObject _light;
        private GameObject _backdrop;
        private Material _backdropMaterial;
        private Texture2D _backdropTexture;
        private IDisposable _tick;

        public StageService(ICaptureSources captures, IStreamOutput output, IFrameLoop frameLoop, ConfigFileStore<StageSettings> store, IMonotonicClock clock, ILog log)
        {
            _captures = captures ?? throw new ArgumentNullException(nameof(captures));
            _output = output ?? throw new ArgumentNullException(nameof(output));
            _frameLoop = frameLoop ?? throw new ArgumentNullException(nameof(frameLoop));
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _log = log;
            _saves = new ConfigSaveScheduler<StageSettings>(store, clock, s_saveDelay, failure => _log?.Warning($"Failed to save the stage settings to {store.FilePath}.", failure));
        }

        public string Name => ServiceName;

        /// <summary>The placement editor's videos, in source order.</summary>
        public IReadOnlyList<IFrameSource> Screens => _screens;

        public IAvatarSpawnPoints SpawnPoints => this;

        public Pose Default => StageDefaults.AvatarSpawn;

        public IReadOnlyObservable<float> Intensity => _intensity;

        /// <summary>The default light; for tests.</summary>
        internal Light DefaultLight => _light != null ? _light.GetComponent<Light>() : null;

        internal GameObject Backdrop => _backdrop;

        /// <summary>Creates the store of stage-settings.json in <paramref name="directory"/>, the project's folder (project data detailed design 6).</summary>
        public static ConfigFileStore<StageSettings> CreateStore(string directory, ISystemClock clock)
        {
            return new ConfigFileStore<StageSettings>(directory, StageSettings.FileName, StageSettings.SchemaVersion, () => new StageSettings(), clock);
        }

        /// <summary>The placement to use for a screen: the saved one when it can be used, else the default. Pure.</summary>
        public static FrameLayout LayoutFor(StageSettings settings, string screenId)
        {
            if (settings?.Screens != null && settings.Screens.TryGetValue(screenId, out ScreenSettings saved) && saved?.ToLayout() is FrameLayout layout)
            {
                return layout;
            }

            return StageDefaults.For(screenId);
        }

        public Task InitializeAsync(CancellationToken cancellationToken)
        {
            if (_output.Screens?.Root == null)
            {
                throw new InvalidOperationException("The stream video has no flat video layer.");
            }

            Shader shader = Resources.Load<Shader>(ScreenShaderPath);
            if (shader == null)
            {
                throw new InvalidOperationException($"The shader '{ScreenShaderPath}' is missing from Resources.");
            }

            ConfigLoadResult<StageSettings> loaded = _store.Load();
            _settings = loaded.Value?.Clone() ?? new StageSettings();
            _intensity.Value = IntensityFor(_settings);
            Mesh quad = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
            foreach (IVideoCaptureSource source in _captures.Sources)
            {
                // The tracking camera shows the user's face and room; it never goes on the stream
                // (tracking detailed design 5.4, 13).
                if (!ShowsOnStream(source.Kind))
                {
                    continue;
                }

                _screens.Add(new CaptureScreen(source, LayoutFor(_settings, source.Id), _output.Screens, quad, shader, OnCommitted));
            }

            // The persistent scene has no light, and lit avatar materials would be black without one.
            _light = new GameObject("StageDefaultLight");
            _light.transform.rotation = StageDefaults.LightRotation;
            Light light = _light.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = _intensity.Value;
            light.shadows = LightShadows.None;

            CreateBackdrop(quad, shader);
            _tick = _frameLoop.Register(_saves.Tick);
            _log?.Information($"Stage started with {_screens.Count} stream screens; placements {loaded.Status} from {_store.FilePath}.");
            return Task.CompletedTask;
        }

        /// <summary>The light's scale to use: the saved one within the range, else the default. Pure.</summary>
        public static float IntensityFor(StageSettings settings)
        {
            return settings?.LightIntensity is float saved ? StageLightIntensity.Clamp(saved) : StageLightIntensity.Default;
        }

        /// <summary>Main thread: changes the light, which Unity reads when it renders.</summary>
        public void SetIntensity(float intensity)
        {
            float next = StageLightIntensity.Clamp(intensity);
            if (!_intensity.Set(next))
            {
                return;
            }

            Light light = DefaultLight;
            if (light != null)
            {
                light.intensity = next;
            }

            // A slider gives many values while dragged; the save waits until they stop (6.3).
            StageSettings settings = _settings.Clone();
            settings.LightIntensity = next;
            _settings = settings;
            _saves.Changed(settings.Clone());
        }

        /// <summary>Whether a kind of capture source gets a screen on the stream. Pure.</summary>
        internal static bool ShowsOnStream(CaptureSourceKind kind)
        {
            return kind != CaptureSourceKind.Camera;
        }

        public async Task ShutdownAsync(CancellationToken cancellationToken)
        {
            await _saves.FlushAsync().ConfigureAwait(false);
        }

        public void Dispose()
        {
            _tick?.Dispose();
            _tick = null;
            foreach (CaptureScreen screen in _screens)
            {
                screen.Dispose();
            }

            _screens.Clear();
            Destroy(_backdrop);
            Destroy(_backdropMaterial);
            Destroy(_backdropTexture);
            _backdrop = null;
            _backdropMaterial = null;
            _backdropTexture = null;
            Destroy(_light);
            _light = null;
        }

        /// <summary>
        /// The provisional backdrop behind the avatar, until a Stage Scene brings its own (Stage
        /// detailed design 7.1). The Main Camera draws it, under the flat videos and the avatar; it is
        /// unlit, with the stream screens' shader.
        /// </summary>
        private void CreateBackdrop(Mesh quad, Shader shader)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            _backdropTexture = new Texture2D(DefaultBackdrop.Width, DefaultBackdrop.Height, TextureFormat.RGBA32, mipChain: true, linear: false)
            {
                name = "StageBackdrop",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Trilinear,
            };
            _backdropTexture.SetPixels32(DefaultBackdrop.Render(DefaultBackdrop.Width, DefaultBackdrop.Height));
            _backdropTexture.Apply(updateMipmaps: true, makeNoLongerReadable: true);
            _backdropMaterial = new Material(shader) { name = "StageBackdrop", mainTexture = _backdropTexture };
            _backdrop = new GameObject("StageBackdrop");
            _backdrop.transform.SetPositionAndRotation(StageDefaults.BackdropPosition, Quaternion.identity);
            Vector2 size = StageDefaults.BackdropSize;
            _backdrop.transform.localScale = new Vector3(size.x, size.y, 1f);
            _backdrop.AddComponent<MeshFilter>().sharedMesh = quad;
            _backdrop.AddComponent<MeshRenderer>().sharedMaterial = _backdropMaterial;
            _log?.Information($"Stage backdrop drawn in {watch.Elapsed.TotalMilliseconds:F0} ms.");
        }

        private static void Destroy(UnityEngine.Object target)
        {
            if (target == null)
            {
                return;
            }

            if (UnityEngine.Application.isPlaying)
            {
                UnityEngine.Object.Destroy(target);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(target);
            }
        }

        private void OnCommitted(CaptureScreen screen)
        {
            // A copy: the save runs on another thread while editing goes on.
            StageSettings next = _settings.Clone();
            next.Screens[screen.Id] = ScreenSettings.From(screen.Layout.Value);
            _settings = next;
            _saves.Changed(next.Clone());
        }
    }
}
