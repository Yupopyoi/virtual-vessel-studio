using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using VirtualVessel.Core.Lifecycle;
using VirtualVessel.Core.State;
using VirtualVessel.UI.Lighting;
using VirtualVessel.UI.StageEditing;
using VirtualVessel.Video;

namespace VirtualVessel.Stage
{
    /// <summary>Where the stage view looks from: a pivot, and angles and a distance around it (Stage detailed design 7.4.4).</summary>
    public readonly struct StageViewPose : IEquatable<StageViewPose>
    {
        public StageViewPose(Vector3 pivot, float yaw, float pitch, float distance)
        {
            Pivot = pivot;
            Yaw = yaw;
            Pitch = pitch;
            Distance = distance;
        }

        public Vector3 Pivot { get; }

        /// <summary>Degrees around the pivot, as a light's: 0 on the stream camera's side, plus toward the screen's right.</summary>
        public float Yaw { get; }

        /// <summary>Degrees above the pivot.</summary>
        public float Pitch { get; }

        /// <summary>Meters from the pivot.</summary>
        public float Distance { get; }

        public bool Equals(StageViewPose other) => Pivot == other.Pivot && Yaw == other.Yaw && Pitch == other.Pitch && Distance == other.Distance;

        public override bool Equals(object obj) => obj is StageViewPose other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(Pivot, Yaw, Pitch, Distance);
    }

    /// <summary>The stage view's viewpoint, rays, and handles (Stage detailed design 7.4.4, 7.4.5). Pure.</summary>
    public static class StageViewMath
    {
        public const float FieldOfView = 40f;
        public const float MinPitch = -10f;
        public const float MaxPitch = 80f;
        public const float MinDistance = 1.5f;
        public const float MaxDistance = 10f;

        /// <summary>A wheel step brings the view 10% closer.</summary>
        public const float ZoomPerStep = 0.9f;

        /// <summary>The first view: the face, from the front left and a little above, with the whole avatar in sight.</summary>
        public static StageViewPose Default => new StageViewPose(StageLightPlacement.Target, -35f, 22f, 5.2f);

        public static StageViewPose Orbit(StageViewPose pose, Vector2 degrees)
        {
            float yaw = Mathf.Repeat(pose.Yaw + degrees.x + 180f, 360f) - 180f;
            return new StageViewPose(pose.Pivot, yaw, Mathf.Clamp(pose.Pitch + degrees.y, MinPitch, MaxPitch), pose.Distance);
        }

        public static StageViewPose Zoom(StageViewPose pose, float steps)
        {
            return new StageViewPose(pose.Pivot, pose.Yaw, pose.Pitch, Mathf.Clamp(pose.Distance * Mathf.Pow(ZoomPerStep, steps), MinDistance, MaxDistance));
        }

        /// <summary>
        /// The pose with its pivot moved so the stage follows the pointer: a pointer moving right by a fraction of the
        /// view moves the pivot left by that much of the view's width at the pivot's distance.
        /// </summary>
        public static StageViewPose Pan(StageViewPose pose, Vector2 viewFraction, float aspect)
        {
            Quaternion rotation = Rotation(pose);
            float height = 2f * pose.Distance * Mathf.Tan(FieldOfView * 0.5f * Mathf.Deg2Rad);
            Vector3 move = rotation * new Vector3(-viewFraction.x * height * aspect, viewFraction.y * height, 0f);
            return new StageViewPose(pose.Pivot + move, pose.Yaw, pose.Pitch, pose.Distance);
        }

        public static Vector3 Position(StageViewPose pose)
        {
            return pose.Pivot + StageLightPlacement.Offset(pose.Yaw, pose.Pitch) * pose.Distance;
        }

        public static Quaternion Rotation(StageViewPose pose)
        {
            return Quaternion.LookRotation(-StageLightPlacement.Offset(pose.Yaw, pose.Pitch), Vector3.up);
        }

        /// <summary>The ray through a point on the view (top left 0, bottom right 1) as an origin and a unit direction.</summary>
        public static (Vector3 Origin, Vector3 Direction) Ray(StageViewPose pose, Vector2 uv, float aspect)
        {
            float tan = Mathf.Tan(FieldOfView * 0.5f * Mathf.Deg2Rad);
            var local = new Vector3((uv.x * 2f - 1f) * tan * aspect, (1f - uv.y * 2f) * tan, 1f);
            return (Position(pose), (Rotation(pose) * local).normalized);
        }

        /// <summary>Where a point is on the view (top left 0, bottom right 1), and whether it is in front of the camera.</summary>
        public static (Vector2 Uv, bool InFront) ToView(StageViewPose pose, Vector3 point, float aspect)
        {
            Vector3 local = Quaternion.Inverse(Rotation(pose)) * (point - Position(pose));
            if (local.z < 0.05f)
            {
                return (Vector2.zero, false);
            }

            float tan = Mathf.Tan(FieldOfView * 0.5f * Mathf.Deg2Rad);
            float x = local.x / (local.z * tan * aspect);
            float y = local.y / (local.z * tan);
            return (new Vector2((x + 1f) * 0.5f, (1f - y) * 0.5f), true);
        }

        /// <summary>
        /// The handle on a sphere (7.4.5): the angles, as a light's, of where the ray meets the sphere on its near side;
        /// when it misses, of the ray's nearest point to the center.
        /// </summary>
        public static Vector2 OnSphere(Vector3 center, float radius, Vector3 origin, Vector3 direction)
        {
            Vector3 toOrigin = origin - center;
            float b = Vector3.Dot(toOrigin, direction);
            float c = toOrigin.sqrMagnitude - radius * radius;
            float discriminant = b * b - c;
            Vector3 point = discriminant >= 0f ? origin + direction * (-b - Mathf.Sqrt(discriminant)) : origin + direction * -b;
            Vector3 n = (point - center).normalized;
            float pitch = Mathf.Asin(Mathf.Clamp(n.y, -1f, 1f)) * Mathf.Rad2Deg;
            float yaw = Mathf.Atan2(n.x, -n.z) * Mathf.Rad2Deg;
            return new Vector2(yaw, pitch);
        }
    }

    /// <summary>A thing the stage view can show and move (Stage detailed design 7.4.1, 7.4.5).</summary>
    internal interface IStageViewItem
    {
        string Id { get; }

        string Label { get; }

        Vector3 Position { get; }

        Color Color { get; }

        bool CanDrag { get; }

        void AddGuides(StageViewGuides guides, bool chosen);

        /// <summary>Moves the thing toward the pointer's ray, through its own interface, which applies and saves it.</summary>
        void DragTo(Vector3 origin, Vector3 direction);
    }

    /// <summary>A light in the stage view: a marker where it stands, its direction or cone, and the handle on a sphere.</summary>
    internal sealed class LightViewItem : IStageViewItem
    {
        private readonly IStageLighting _lighting;
        private readonly int _index;
        private readonly StageLight _light;

        public LightViewItem(IStageLighting lighting, int index)
        {
            _lighting = lighting;
            _index = index;
            _light = lighting.Lighting.Value.Lights[index];
        }

        public string Id => StageViewItems.Light(_index);

        public string Label => (_index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);

        public Vector3 Position => ShownAt(_light);

        public Color Color => StageLightColors.ColorOf(_light);

        public bool CanDrag => true;

        /// <summary>Where a light is shown: a spot where it stands, a whole-stage light at a fixed distance in its direction. Pure.</summary>
        public static Vector3 ShownAt(StageLight light)
        {
            return light.Kind == StageLightKind.Spot ? StageLightPlacement.Position(light) : StageLightPlacement.Target + StageLightPlacement.Offset(light.Yaw, light.Pitch) * StageViewGuides.DirectionalRadius;
        }

        public void AddGuides(StageViewGuides guides, bool chosen)
        {
            StageViewGuides.AddLight(guides, _light, chosen);
        }

        public void DragTo(Vector3 origin, Vector3 direction)
        {
            StageLighting lighting = _lighting.Lighting.Value;
            if (_index >= lighting.Lights.Count)
            {
                return;
            }

            StageLight light = lighting.Lights[_index];
            float radius = light.Kind == StageLightKind.Spot ? light.Distance : StageViewGuides.DirectionalRadius;
            Vector2 angles = StageViewMath.OnSphere(StageLightPlacement.Target, radius, origin, direction);
            _lighting.SetLighting(lighting.WithLight(_index, light.WithYaw(Mathf.Round(angles.x)).WithPitch(Mathf.Round(angles.y))));
        }
    }

    /// <summary>
    /// The stage view (Stage detailed design 7.4): a camera of its own drawing the stage and the avatar into a render
    /// target apart from the stream, never in the stream's camera stack, with guides that only it draws. It draws
    /// only while open. Main thread only.
    /// </summary>
    public sealed class StageView : IStageView, IDisposable
    {
        public const int MaxWidth = 960;
        public const int MaxHeight = 540;

        private static readonly Color s_background = new Color(0.09f, 0.08f, 0.11f);

        private readonly IStageLighting _lighting;
        private readonly IFrameLoop _frameLoop;
        private readonly Func<int> _hiddenLayer;
        private readonly Func<StreamCameraPose?> _streamCamera;
        private readonly ObservableValue<Texture> _image = new ObservableValue<Texture>(null);
        private readonly ObservableValue<IReadOnlyList<StageViewMarker>> _markers = new ObservableValue<IReadOnlyList<StageViewMarker>>(Array.Empty<StageViewMarker>());
        private StageViewPose _pose = StageViewMath.Default;
        private Camera _camera;
        private RenderTexture _target;
        private StageViewGuides _guides;
        private IDisposable _tick;
        private int _opens;
        private string _chosen;
        private IStageViewItem _dragged;

        /// <param name="hiddenLayer">
        /// The flat videos' layer (video detailed design 7.2), -1 for none: 2D over the stream, so not in a 3D view. Read
        /// when the view opens, since Video makes the layer at its start.
        /// </param>
        /// <param name="streamCamera">Where the stream is shot from, read every frame for its guide; null draws none.</param>
        public StageView(IStageLighting lighting, IFrameLoop frameLoop, Func<int> hiddenLayer, Func<StreamCameraPose?> streamCamera = null)
        {
            _lighting = lighting ?? throw new ArgumentNullException(nameof(lighting));
            _frameLoop = frameLoop ?? throw new ArgumentNullException(nameof(frameLoop));
            _hiddenLayer = hiddenLayer ?? (() => -1);
            _streamCamera = streamCamera ?? (() => null);
        }

        public IReadOnlyObservable<Texture> Image => _image;

        public IReadOnlyObservable<IReadOnlyList<StageViewMarker>> Markers => _markers;

        public StageViewPose Pose => _pose;

        /// <summary>The camera while open; for tests.</summary>
        internal Camera Camera => _camera;

        /// <summary>This frame's guides while open; for tests.</summary>
        internal StageViewGuides Guides => _guides;

        /// <summary>The render target's size for a view of this size: its shape, within <see cref="MaxWidth"/> × <see cref="MaxHeight"/>. Pure.</summary>
        public static Vector2Int TargetSize(int width, int height)
        {
            float w = Mathf.Max(16, width);
            float h = Mathf.Max(16, height);
            float scale = Mathf.Min(1f, Mathf.Min(MaxWidth / w, MaxHeight / h));
            return new Vector2Int(Mathf.RoundToInt(w * scale), Mathf.RoundToInt(h * scale));
        }

        public IDisposable Open(int width, int height)
        {
            if (_opens++ == 0)
            {
                Create(TargetSize(width, height));
            }

            return new Closer(this);
        }

        public void Orbit(Vector2 degrees) => _pose = StageViewMath.Orbit(_pose, degrees);

        public void Zoom(float steps) => _pose = StageViewMath.Zoom(_pose, steps);

        public void Pan(Vector2 viewFraction) => _pose = StageViewMath.Pan(_pose, viewFraction, Aspect);

        public void ResetView() => _pose = StageViewMath.Default;

        public void Frame(string itemId)
        {
            IStageViewItem item = Items().FirstOrDefault(i => i.Id == itemId);
            if (item != null)
            {
                _pose = new StageViewPose(item.Position, _pose.Yaw, _pose.Pitch, _pose.Distance);
            }
        }

        public void Select(string itemId) => _chosen = itemId;

        public bool BeginDrag(string itemId, Vector2 uv)
        {
            _dragged = Items().FirstOrDefault(i => i.Id == itemId && i.CanDrag);
            if (_dragged == null)
            {
                return false;
            }

            DragTo(uv);
            return true;
        }

        public void DragTo(Vector2 uv)
        {
            if (_dragged == null)
            {
                return;
            }

            // The item is made again from the current lighting, so a drag follows changes made meanwhile.
            IStageViewItem item = Items().FirstOrDefault(i => i.Id == _dragged.Id);
            (Vector3 origin, Vector3 direction) = StageViewMath.Ray(_pose, uv, Aspect);
            item?.DragTo(origin, direction);
        }

        public void EndDrag() => _dragged = null;

        public void Dispose()
        {
            _opens = 0;
            Release();
        }

        private float Aspect => _target != null ? (float)_target.width / _target.height : 16f / 9f;

        private IEnumerable<IStageViewItem> Items()
        {
            int count = _lighting.Lighting.Value.Lights.Count;
            for (int i = 0; i < count; i++)
            {
                yield return new LightViewItem(_lighting, i);
            }
        }

        private void Create(Vector2Int size)
        {
            _target = new RenderTexture(size.x, size.y, 24, RenderTextureFormat.ARGB32) { name = "StageView", antiAliasing = 4 };
            _target.Create();
            var cameraObject = new GameObject("StageViewCamera");
            _camera = cameraObject.AddComponent<Camera>();
            _camera.targetTexture = _target;
            _camera.fieldOfView = StageViewMath.FieldOfView;
            _camera.nearClipPlane = 0.05f;
            _camera.farClipPlane = 50f;
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = s_background;
            int hidden = _hiddenLayer();
            _camera.cullingMask = hidden >= 0 ? ~(1 << hidden) : ~0;
            _camera.allowHDR = false;
            _guides = new StageViewGuides();
            _tick = _frameLoop.Register(Tick);
            Tick();
            _image.Value = _target;
        }

        private void Close()
        {
            if (_opens > 0 && --_opens == 0)
            {
                Release();
            }
        }

        private void Release()
        {
            _tick?.Dispose();
            _tick = null;
            _dragged = null;
            _image.Value = null;
            _markers.Value = Array.Empty<StageViewMarker>();
            _guides?.Dispose();
            _guides = null;
            if (_camera != null)
            {
                _camera.targetTexture = null;
                Destroy(_camera.gameObject);
                _camera = null;
            }

            if (_target != null)
            {
                _target.Release();
                Destroy(_target);
                _target = null;
            }
        }

        // Every frame while open: the camera to the pose, the guides for this frame, and where the markers are.
        private void Tick()
        {
            if (_camera == null)
            {
                return;
            }

            _camera.transform.SetPositionAndRotation(StageViewMath.Position(_pose), StageViewMath.Rotation(_pose));
            _guides.Clear();
            var markers = new List<StageViewMarker>();
            foreach (IStageViewItem item in Items())
            {
                bool chosen = item.Id == _chosen;
                item.AddGuides(_guides, chosen);
                (Vector2 uv, bool inFront) = StageViewMath.ToView(_pose, item.Position, Aspect);
                markers.Add(new StageViewMarker(item.Id, item.Label, uv, inFront, item.Color));
            }

            if (_streamCamera() is StreamCameraPose stream)
            {
                StageViewGuides.AddCamera(_guides, stream);
            }

            _guides.Draw(_camera);
            _markers.Value = markers;
        }

        private static void Destroy(UnityEngine.Object target)
        {
            if (UnityEngine.Application.isPlaying)
            {
                UnityEngine.Object.Destroy(target);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(target);
            }
        }

        private sealed class Closer : IDisposable
        {
            private StageView _view;

            public Closer(StageView view)
            {
                _view = view;
            }

            public void Dispose()
            {
                _view?.Close();
                _view = null;
            }
        }
    }
}
