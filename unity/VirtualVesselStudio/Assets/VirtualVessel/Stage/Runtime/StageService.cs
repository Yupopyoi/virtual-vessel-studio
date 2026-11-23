using System;
using System.Collections.Generic;
using System.Linq;
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
    /// default spawn point and lights the user sets up (Stage detailed design 7.1, 7.3), and frames the
    /// avatar where its frame in the placement editor is (7.2). An Optional application service.
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
        private readonly IAvatarCamera _avatarCamera;
        private readonly IReadOnlyObservable<float?> _avatarHeight;
        private readonly List<CaptureScreen> _screens = new List<CaptureScreen>();
        private readonly List<IFrameSource> _frames = new List<IFrameSource>();
        private AvatarFrame _avatarFrame;
        private readonly ObservableValue<StageLighting> _lighting = new ObservableValue<StageLighting>(StageLighting.Default);
        private StageSettings _settings = new StageSettings();
        private StageLightRig _lights;
        private readonly StageView _view;
        private GameObject _backdrop;
        private Material _backdropMaterial;
        private Texture2D _backdropTexture;
        private IDisposable _tick;

        /// <param name="avatarCamera">Shoots the avatar to fit its frame; with <paramref name="avatarHeight"/>, the placement editor gets the frame (7.2).</param>
        /// <param name="avatarHeight">The avatar's height, null without one (avatar detailed design 6.5).</param>
        public StageService(ICaptureSources captures, IStreamOutput output, IFrameLoop frameLoop, ConfigFileStore<StageSettings> store, IMonotonicClock clock, ILog log, IAvatarCamera avatarCamera = null, IReadOnlyObservable<float?> avatarHeight = null)
        {
            _captures = captures ?? throw new ArgumentNullException(nameof(captures));
            _output = output ?? throw new ArgumentNullException(nameof(output));
            _frameLoop = frameLoop ?? throw new ArgumentNullException(nameof(frameLoop));
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _log = log;
            _avatarCamera = avatarCamera;
            _avatarHeight = avatarHeight;
            _view = new StageView(this, frameLoop, () => _output.Screens?.Layer ?? -1, () => _output.Camera);
            _saves = new ConfigSaveScheduler<StageSettings>(store, clock, s_saveDelay, failure => _log?.Warning($"Failed to save the stage settings to {store.FilePath}.", failure));
        }

        public string Name => ServiceName;

        /// <summary>The placement editor's videos in source order, then the avatar's frame.</summary>
        public IReadOnlyList<IFrameSource> Screens => _frames;

        /// <summary>The avatar's frame among <see cref="Screens"/>; null without Avatar or Video's avatar camera.</summary>
        public IFrameSource AvatarFrameSource => _avatarFrame;

        public IAvatarSpawnPoints SpawnPoints => this;

        public Pose Default => StageDefaults.AvatarSpawn;

        public IReadOnlyObservable<StageLighting> Lighting => _lighting;

        /// <summary>The 3D view for placing things in Setup (7.4); it draws only while the settings screen opens it.</summary>
        public StageView View => _view;

        /// <summary>The lights Unity renders, in order; for tests.</summary>
        internal IReadOnlyList<Light> Lights => _lights?.Lights ?? Array.Empty<Light>();

        /// <summary>The first light, which the stage always has; for tests.</summary>
        internal Light DefaultLight => Lights.Count > 0 ? Lights[0] : null;

        internal GameObject Backdrop => _backdrop;

        /// <summary>Creates the store of stage-settings.json in <paramref name="directory"/>, the project's folder (project data detailed design 6).</summary>
        public static ConfigFileStore<StageSettings> CreateStore(string directory, ISystemClock clock)
        {
            return new ConfigFileStore<StageSettings>(directory, StageSettings.FileName, StageSettings.SchemaVersion, () => new StageSettings(), clock);
        }

        /// <summary>The placement to use for a screen: the saved one when it can be used, else the default. Pure.</summary>
        public static FrameLayout LayoutFor(StageSettings settings, string screenId)
        {
            return Saved(settings, screenId) ?? StageDefaults.For(screenId);
        }

        /// <summary>The avatar frame's placement: the saved one when it can be used, else the default (7.2). Pure.</summary>
        public static FrameLayout AvatarLayoutFor(StageSettings settings, double streamAspect)
        {
            return Saved(settings, AvatarFrame.FrameId) ?? AvatarFramingMath.DefaultLayout(streamAspect);
        }

        private static FrameLayout? Saved(StageSettings settings, string id)
        {
            return settings?.Screens != null && settings.Screens.TryGetValue(id, out ScreenSettings saved) ? saved?.ToLayout() : null;
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
            _lighting.Value = LightingFor(_settings);
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

            _frames.AddRange(_screens);
            if (_avatarCamera != null && _avatarHeight != null)
            {
                double aspect = (double)_output.Size.Width / _output.Size.Height;
                _avatarFrame = new AvatarFrame(_avatarCamera, _avatarHeight, StageDefaults.AvatarSpawn, aspect, AvatarLayoutFor(_settings, aspect), OnCommitted);
                _frames.Add(_avatarFrame);
            }

            // The persistent scene has no light, and lit avatar materials would be black without one.
            _lights = new StageLightRig();
            _lights.Apply(_lighting.Value);


            CreateBackdrop(quad, shader);
            _tick = _frameLoop.Register(_saves.Tick);
            _log?.Information($"Stage started with {_screens.Count} stream screens{(_avatarFrame != null ? " and the avatar frame" : string.Empty)}; placements {loaded.Status} from {_store.FilePath}.");
            return Task.CompletedTask;
        }

        /// <summary>
        /// The lighting to use: the saved lights, else the one light of version 0.3 at its saved
        /// brightness, else the default (6.3). Pure.
        /// </summary>
        public static StageLighting LightingFor(StageSettings settings)
        {
            float ambient = settings?.AmbientLight ?? StageLighting.DefaultAmbient;
            if (settings?.Lights != null)
            {
                return new StageLighting(settings.Lights.Where(light => light != null).Select(light => light.ToLight()).ToArray(), ambient).Normalized();
            }

            StageLight first = settings?.LightIntensity is float saved ? StageLight.Default.WithIntensity(saved) : StageLight.Default;
            return new StageLighting(new[] { first }, ambient).Normalized();
        }

        /// <summary>Main thread: changes the lights, which Unity reads when it renders.</summary>
        public void SetLighting(StageLighting lighting)
        {
            if (lighting == null)
            {
                throw new ArgumentNullException(nameof(lighting));
            }

            StageLighting next = lighting.Normalized();
            if (!_lighting.Set(next))
            {
                return;
            }

            _lights?.Apply(next);

            // A slider gives many values while dragged; the save waits until they stop (6.3).
            StageSettings settings = _settings.Clone();
            settings.Lights = next.Lights.Select(LightSettings.From).ToList();
            settings.AmbientLight = next.Ambient;
            settings.LightIntensity = null;
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
            _avatarFrame?.Dispose();
            _avatarFrame = null;
            _frames.Clear();
            Destroy(_backdrop);
            Destroy(_backdropMaterial);
            Destroy(_backdropTexture);
            _backdrop = null;
            _backdropMaterial = null;
            _backdropTexture = null;
            _view.Dispose();
            _lights?.Dispose();
            _lights = null;
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

        private void OnCommitted(IFrameSource screen)
        {
            // A copy: the save runs on another thread while editing goes on.
            StageSettings next = _settings.Clone();
            next.Screens[screen.Id] = ScreenSettings.From(screen.Layout.Value);
            _settings = next;
            _saves.Changed(next.Clone());
        }
    }
}
