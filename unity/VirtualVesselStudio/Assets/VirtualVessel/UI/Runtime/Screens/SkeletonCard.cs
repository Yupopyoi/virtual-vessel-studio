using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;
using VirtualVessel.Avatar;
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
    /// The tracked upper body beside the avatar's, from the front and from the side, with their angles
    /// (UI foundation detailed design 7.7), to tell whether tracking or the retargeting is off. Shown on
    /// the Diagnostics screen, so only in Developer Mode; it refreshes only while that screen is shown.
    /// </summary>
    public sealed class SkeletonCard : VisualElement
    {
        public const string UssClass = "vv-skeleton";
        public const string ViewsClass = UssClass + "__views";
        public const string PaneClass = UssClass + "__pane";
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
        private IVisualElementScheduledItem _refresh;

        /// <param name="avatars">The avatars, to draw the one on the stream beside tracking; null draws tracking only.</param>
        public SkeletonCard(ITrackingSource tracking, IAvatarService avatars)
        {
            _tracking = tracking;
            _avatars = avatars;
            AddToClassList(UssClass);
            Card card = Ui.Card(SkeletonStrings.Title);
            Add(card);
            card.Add(Ui.Text(SkeletonStrings.Legend, NoteClass, "vv-text-caption"));

            var views = new VisualElement();
            views.AddToClassList(ViewsClass);
            Front = new SkeletonView(SkeletonPlane.Front);
            Side = new SkeletonView(SkeletonPlane.Side);
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

            Refresh();
            _refresh = schedule.Execute(Refresh).Every(RefreshMilliseconds);
        }

        public void Stop()
        {
            _refresh?.Pause();
            _refresh = null;
        }

        internal void Refresh()
        {
            BodyPose body = _tracking?.Current.Body;
            bool mirror = _tracking?.Options.Value.Mirror ?? true;
            BodyTarget avatar = _avatars?.Body;
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

        private static VisualElement Pane(string titleKey, SkeletonView view)
        {
            var pane = new VisualElement();
            pane.AddToClassList(PaneClass);
            pane.Add(Ui.Text(titleKey, "vv-text-caption"));
            pane.Add(view);
            return pane;
        }
    }
}
