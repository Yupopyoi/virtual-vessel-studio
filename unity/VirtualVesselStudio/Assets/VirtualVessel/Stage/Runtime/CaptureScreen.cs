using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using VirtualVessel.Capture;
using VirtualVessel.Core.Geometry;
using VirtualVessel.Core.State;
using VirtualVessel.UI.Commands;
using VirtualVessel.UI.Frames;
using VirtualVessel.Video;

namespace VirtualVessel.Stage
{
    /// <summary>
    /// One capture source on the stream screen (Stage detailed design 6.1): its quad on the flat video
    /// layer, and the placement editor's view of it. Main thread only.
    /// </summary>
    internal sealed class CaptureScreen : IFrameSource, IDisposable
    {
        /// <summary>The message key for a placement Stage cannot use.</summary>
        public const string InvalidPlacementKey = "stage.placement.invalid";

        private static readonly int s_mainTex = Shader.PropertyToID("_MainTex");
        private static readonly int s_mainTexScaleOffset = Shader.PropertyToID("_MainTex_ST");

        private readonly IVideoCaptureSource _source;
        private readonly ObservableValue<FrameLayout> _layout;
        private readonly ObservableValue<bool> _visible = new ObservableValue<bool>(false);
        private readonly IReadOnlyObservable<string> _noName = new ObservableValue<string>(string.Empty);
        private readonly Action<CaptureScreen> _committed;
        private readonly GameObject _quad;
        private readonly MeshRenderer _renderer;
        private readonly Material _material;
        private readonly IDisposable _texture;

        public CaptureScreen(IVideoCaptureSource source, FrameLayout layout, IStreamScreenLayer layer, Mesh mesh, Shader shader, Action<CaptureScreen> committed)
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));
            _layout = new ObservableValue<FrameLayout>(layout);
            _committed = committed;

            _quad = new GameObject("StreamScreen " + source.Id) { layer = layer.Layer };
            _quad.transform.SetParent(layer.Root, false);
            _quad.AddComponent<MeshFilter>().sharedMesh = mesh;
            _renderer = _quad.AddComponent<MeshRenderer>();
            _material = new Material(shader) { name = "StreamScreen " + source.Id };
            _renderer.sharedMaterial = _material;
            Apply(layout);
            ShowVideo(source.Texture.Value);
            _texture = source.Texture.Subscribe(ShowVideo);
        }

        public string Id => _source.Id;

        public string NameKey => StageDefaults.NameKey(_source.Id);

        public IReadOnlyObservable<string> DisplayName => _noName;

        public IReadOnlyObservable<bool> Visible => _visible;

        public double SourceAspect
        {
            get
            {
                CaptureSourceInfo info = _source.Info.Value;
                return info.Width > 0 && info.Height > 0 ? (double)info.Width / info.Height : 16.0 / 9.0;
            }
        }

        public Texture Video => _source.Texture.Value;

        public IReadOnlyObservable<FrameLayout> Layout => _layout;

        /// <summary>For tests: what the quad shows now.</summary>
        internal Transform Quad => _quad.transform;

        internal bool QuadShown => _renderer.enabled;

        internal Vector4 TextureScaleOffset => _material.GetVector(s_mainTexScaleOffset);

        public void PreviewLayout(FrameLayout layout)
        {
            // Shown at once so the stream follows the pointer; never saved (UI foundation detailed design 9.5).
            if (ScreenPlacementMath.IsUsable(layout))
            {
                Apply(layout);
            }
        }

        public Task CommitLayoutAsync(FrameLayout layout, CancellationToken cancellationToken)
        {
            if (!ScreenPlacementMath.IsUsable(layout))
            {
                Apply(_layout.Value);
                throw new UserFacingException(InvalidPlacementKey, $"Stream screen '{Id}' got an unusable placement.");
            }

            _layout.Value = layout;
            Apply(layout);
            _committed?.Invoke(this);
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            _texture.Dispose();
            Destroy(_quad);
            Destroy(_material);
        }

        private void Apply(FrameLayout layout)
        {
            (Vector3 position, Vector3 scale) = ScreenPlacementMath.Quad(layout.Placement, layout.Order);
            _quad.transform.localPosition = position;
            _quad.transform.localScale = scale;
            _material.SetVector(s_mainTexScaleOffset, ScreenPlacementMath.TextureScaleOffset(layout.Crop, layout.FlipHorizontal));
        }

        private void ShowVideo(Texture texture)
        {
            _material.SetTexture(s_mainTex, texture);
            _renderer.enabled = texture != null;
            _visible.Value = texture != null;
        }

        private static void Destroy(UnityEngine.Object value)
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
