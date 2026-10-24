using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;
using VirtualVessel.Avatar;
using VirtualVessel.Capture;
using VirtualVessel.Tracking;
using VirtualVessel.UI.Controls;
using VirtualVessel.UI.Localization;

namespace VirtualVessel.UI.Screens
{
    /// <summary>String table keys of the skeleton comparison Card (UI foundation detailed design 7.7).</summary>
    [StringKeys]
    public static class SkeletonStrings
    {
        public const string Title = "diagnostics.skeleton.title";
        public const string Legend = "diagnostics.skeleton.legend";
        public const string Front = "diagnostics.skeleton.front";
        public const string Side = "diagnostics.skeleton.side";
        public const string Camera = "diagnostics.skeleton.camera";
        public const string CameraNote = "diagnostics.skeleton.camera.note";
        public const string NoBody = "diagnostics.skeleton.no-body";
        public const string ColumnItem = "diagnostics.skeleton.column.item";
        public const string ColumnTracked = "diagnostics.skeleton.column.tracked";
        public const string ColumnAvatar = "diagnostics.skeleton.column.avatar";
        public const string LeftElevation = "diagnostics.skeleton.left.elevation";
        public const string LeftForward = "diagnostics.skeleton.left.forward";
        public const string LeftBend = "diagnostics.skeleton.left.bend";
        public const string RightElevation = "diagnostics.skeleton.right.elevation";
        public const string RightForward = "diagnostics.skeleton.right.forward";
        public const string RightBend = "diagnostics.skeleton.right.bend";
        public const string TorsoTilt = "diagnostics.skeleton.torso.tilt";
        public const string TorsoLean = "diagnostics.skeleton.torso.lean";
    }

    /// <summary>
    /// The tracked upper body beside the avatar's, from the front and from the side, with their angles and
    /// the tracking camera's image with the face covered (UI foundation detailed design 7.7), to tell
    /// whether tracking or the retargeting is off. Shown on the Diagnostics screen, so only in Developer
    /// Mode; it refreshes only while that screen is shown. The image never goes to the stream or a recording.
    /// </summary>
    public sealed class SkeletonCard : VisualElement
    {
        public const string UssClass = "vv-skeleton";
        public const string ViewsClass = UssClass + "__views";
        public const string PaneClass = UssClass + "__pane";
        public const string PaneTitleClass = UssClass + "__pane-title";
        public const string NoteClass = UssClass + "__note";

        /// <summary>About twenty times a second: enough to follow an arm, light on the frame.</summary>
        public const long RefreshMilliseconds = 50;

        private static readonly TableColumn[] s_columns =
        {
            TableColumn.Filling(SkeletonStrings.ColumnItem, 220),
            TableColumn.Number(SkeletonStrings.ColumnTracked, 120),
            TableColumn.Number(SkeletonStrings.ColumnAvatar, 120),
        };

        private static readonly string[] s_rowKeys =
        {
            SkeletonStrings.LeftElevation,
            SkeletonStrings.LeftForward,
            SkeletonStrings.LeftBend,
            SkeletonStrings.RightElevation,
            SkeletonStrings.RightForward,
            SkeletonStrings.RightBend,
            SkeletonStrings.TorsoTilt,
            SkeletonStrings.TorsoLean,
        };

        private readonly ITrackingSource _tracking;
        private readonly IAvatarService _avatars;
        private readonly SkeletonSegment[] _segments = new SkeletonSegment[SkeletonProjection.MaxSegments];
        private readonly KeyedText _note;
        private readonly IVideoCaptureSource _camera;
        private readonly Label _noVideo;
        private IVisualElementScheduledItem _refresh;
        private IDisposable _video;

        /// <param name="avatars">The avatars, to draw the one on the stream beside tracking; null draws tracking only.</param>
        /// <param name="camera">The tracking camera, to show the person's arms with the face covered; null leaves the image out.</param>
        public SkeletonCard(ITrackingSource tracking, IAvatarService avatars, IVideoCaptureSource camera = null)
        {
            _tracking = tracking;
            _avatars = avatars;
            _camera = camera;
            AddToClassList(UssClass);
            Card card = Ui.Card(SkeletonStrings.Title);
            Add(card);
            card.Add(Ui.Text(SkeletonStrings.Legend, NoteClass, "vv-text-caption"));

            var views = new VisualElement();
            views.AddToClassList(ViewsClass);
            Front = new SkeletonView(SkeletonPlane.Front);
            Side = new SkeletonView(SkeletonPlane.Side);
            if (camera != null)
            {
                // The image, the points over it, and the cover over the face flip together in a mirror.
                Preview = new Image { scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
                Preview.AddToClassList(SkeletonView.UssClass);
                Overlay = new TrackingOverlay();
                Overlay.StretchToParentSize();
                Cover = new FaceCover();
                Cover.StretchToParentSize();
                _noVideo = Ui.Text(CaptureStrings.NoVideo, CaptureSourceCard.PreviewEmptyClass, "vv-text-caption");
                Preview.Add(_noVideo);
                Preview.Add(Overlay);
                Preview.Add(Cover);
                VisualElement pane = Pane(SkeletonStrings.Camera, Preview);
                pane.Add(Ui.Text(SkeletonStrings.CameraNote, NoteClass, "vv-text-caption"));
                views.Add(pane);
            }

            views.Add(Pane(SkeletonStrings.Front, Front));
            views.Add(Pane(SkeletonStrings.Side, Side));
            card.Add(views);

            var note = new Label();
            note.AddToClassList(NoteClass);
            note.AddToClassList("vv-text-caption");
            _note = new KeyedText(note, text => note.text = text);
            card.Add(note);

            Table = Ui.Table(s_columns);
            card.Add(Table);
        }

        public SkeletonView Front { get; }

        /// <summary>The tracking camera's image; null without the camera.</summary>
        public Image Preview { get; }

        public TrackingOverlay Overlay { get; }

        public FaceCover Cover { get; }

        public SkeletonView Side { get; }

        public DataTable Table { get; }

        public bool IsRefreshing => _refresh != null;

        /// <summary>
        /// The rows of angles, in degrees with no decimals: each arm's elevation, its turn toward the
        /// front, and the elbow's bend, tracked and on the avatar; then the torso's tilt and lean. A value
        /// not known is "-". Pure.
        /// </summary>
        public static string[][] Rows(BodyPose body, BodyTarget avatar, bool mirror, IReadOnlyList<string> names)
        {
            var rows = new string[s_rowKeys.Length][];
            for (int i = 0; i < 2; i++)
            {
                bool left = i == 0;
                bool tracked = SkeletonProjection.TrackedArm(body, mirror, left, out Vector3 upper, out Vector3 fore);
                (Vector3 avatarUpper, Vector3 avatarFore) = avatar != null ? SkeletonProjection.AvatarArm(avatar, left) : (Vector3.zero, Vector3.zero);
                int row = i * 3;
                rows[row] = new[] { names[row], tracked ? Degrees(SkeletonProjection.Elevation(upper)) : "-", avatar != null ? Degrees(SkeletonProjection.Elevation(avatarUpper)) : "-" };
                rows[row + 1] = new[] { names[row + 1], tracked ? Degrees(SkeletonProjection.Forward(upper)) : "-", avatar != null ? Degrees(SkeletonProjection.Forward(avatarUpper)) : "-" };
                rows[row + 2] = new[] { names[row + 2], tracked ? Degrees(Vector3.Angle(upper, fore)) : "-", avatar != null ? Degrees(Vector3.Angle(avatarUpper, avatarFore)) : "-" };
            }

            bool hasBody = body != null && body.IsUsable;
            Vector3 shoulders = hasBody ? PoseRetargeting.Joint(body, BodyJoint.RightShoulder, mirror) - PoseRetargeting.Joint(body, BodyJoint.LeftShoulder, mirror) : Vector3.zero;
            Vector3 spine = hasBody ? PoseRetargeting.Joint(body, BodyJoint.Neck, mirror) - PoseRetargeting.Joint(body, BodyJoint.Hips, mirror) : Vector3.zero;
            rows[6] = new[] { names[6], hasBody ? Degrees(Tilt(shoulders)) : "-", avatar != null ? Degrees(Tilt(avatar.TorsoTop * Vector3.right)) : "-" };
            rows[7] = new[] { names[7], hasBody ? Degrees(Lean(spine)) : "-", avatar != null ? Degrees(Lean(avatar.TorsoTop * Vector3.up)) : "-" };
            return rows;
        }

        /// <summary>Starts refreshing, as the Diagnostics screen is shown.</summary>
        public void Start()
        {
            if (_refresh != null)
            {
                return;
            }

            _video = _camera?.Texture.Subscribe(ShowVideo);
            ShowVideo(_camera?.Texture.Value);
            Refresh();
            _refresh = schedule.Execute(Refresh).Every(RefreshMilliseconds);
        }

        public void Stop()
        {
            _refresh?.Pause();
            _refresh = null;
            _video?.Dispose();
            _video = null;

            // Nothing of the camera stays on screen once hidden.
            if (Preview != null)
            {
                Preview.image = null;
            }
        }

        internal void Refresh()
        {
            BodyPose body = _tracking?.Current.Body;
            bool mirror = _tracking?.Options.Value.Mirror ?? true;
            BodyTarget avatar = _avatars?.Body;
            ShowCamera(body, mirror);
            Front.Show(_segments, SkeletonProjection.Segments(body, avatar, mirror, SkeletonPlane.Front, _segments));
            Side.Show(_segments, SkeletonProjection.Segments(body, avatar, mirror, SkeletonPlane.Side, _segments));
            _note.Key = body != null && body.IsUsable ? null : SkeletonStrings.NoBody;

            ILocalizer localizer = UiServices.Find(this)?.Localizer;
            var names = new string[s_rowKeys.Length];
            for (int i = 0; i < names.Length; i++)
            {
                names[i] = localizer?.Get(s_rowKeys[i]) ?? s_rowKeys[i];
            }

            Table.SetRows(Rows(body, avatar, mirror, names));
        }

        private void ShowVideo(Texture texture)
        {
            if (Preview == null)
            {
                return;
            }

            Preview.image = texture;
            _noVideo.style.display = texture == null ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void ShowCamera(BodyPose body, bool mirror)
        {
            if (Preview == null)
            {
                return;
            }

            Texture texture = Preview.image;
            Rect image = texture == null ? default : TrackingOverlay.FitRect(Preview.contentRect.width, Preview.contentRect.height, texture.width, texture.height);
            Overlay.Show(_tracking.Current, image);
            Cover.Show(body, texture, image);
            Preview.style.scale = new Scale(new Vector3(mirror ? -1f : 1f, 1f, 1f));
            _noVideo.style.scale = new Scale(new Vector3(mirror ? -1f : 1f, 1f, 1f));
        }

        private static float Tilt(Vector3 right)
        {
            return Mathf.Atan2(right.y, right.x) * Mathf.Rad2Deg;
        }

        private static float Lean(Vector3 up)
        {
            return Mathf.Atan2(up.z, up.y) * Mathf.Rad2Deg;
        }

        private static string Degrees(float degrees)
        {
            return degrees.ToString("0", CultureInfo.InvariantCulture) + "°";
        }

        private static VisualElement Pane(string titleKey, VisualElement view)
        {
            var pane = new VisualElement();
            pane.AddToClassList(PaneClass);
            pane.Add(Ui.Text(titleKey, PaneTitleClass, "vv-text-body"));
            pane.Add(view);
            return pane;
        }
    }
}
