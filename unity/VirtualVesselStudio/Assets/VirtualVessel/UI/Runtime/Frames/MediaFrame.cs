using System;
using UnityEngine.UIElements;
using VirtualVessel.Core.Geometry;
using VirtualVessel.UI.Binding;
using VirtualVessel.UI.Controls;

namespace VirtualVessel.UI.Frames
{
    /// <summary>
    /// The editing frame of one video (components.md 3.19): an outline, eight handles, and a "Locked"
    /// badge. It does not draw the video; the stream preview behind it does (UI foundation detailed
    /// design 9.2). Pointer input is handled by <see cref="FrameCanvas"/>.
    /// </summary>
    public sealed class MediaFrame : VisualElement
    {
        public const string UssClass = "vv-media-frame";

        public const string SelectedClass = UssClass + "--selected";

        public const string LockedClass = UssClass + "--locked";

        public const string OutlineClass = UssClass + "__outline";

        public const string HandleClass = UssClass + "__handle";

        public const string HandleHoverClass = HandleClass + "--hover";

        public const string LockBadgeClass = UssClass + "__lock";

        private readonly VisualElement[] _handles = new VisualElement[FrameCanvasMath.Handles.Length];
        private FrameLayout _confirmed;
        private FrameLayout? _pending;
        private int _pendingVersion;
        private FrameHandle? _hoverHandle;

        public MediaFrame(IFrameSource source)
        {
            Source = source ?? throw new ArgumentNullException(nameof(source));
            pickingMode = PickingMode.Ignore;
            AddToClassList(UssClass);
            style.position = Position.Absolute;

            var outline = new VisualElement { pickingMode = PickingMode.Ignore };
            outline.AddToClassList(OutlineClass);
            Add(outline);
            for (int i = 0; i < _handles.Length; i++)
            {
                (double fx, double fy) = FrameCanvasMath.HandleFraction(FrameCanvasMath.Handles[i]);
                var handle = new VisualElement { pickingMode = PickingMode.Ignore };
                handle.AddToClassList(HandleClass);
                handle.style.position = Position.Absolute;
                handle.style.left = Length.Percent((float)(fx * 100));
                handle.style.top = Length.Percent((float)(fy * 100));
                _handles[i] = handle;
                Add(handle);
            }

            Label badge = QuietText.Of(new Label { pickingMode = PickingMode.Ignore });
            badge.AddToClassList(LockBadgeClass);
            badge.AddToClassList("vv-text-caption");
            badge.AddToClassList("vv-bold");
            new KeyedText(badge, text => badge.text = text) { Key = FrameStrings.Locked };
            Add(badge);

            _confirmed = source.Layout.Value;
            Refresh();
            this.Bind(source.Layout, layout =>
            {
                _confirmed = layout;
                Refresh();
            });
        }

        public IFrameSource Source { get; }

        /// <summary>The layout on screen: an edit in progress or waiting for Stage, else the confirmed one.</summary>
        public FrameLayout Shown => _pending ?? _confirmed;

        /// <summary>Whether an edit has not been confirmed or rejected yet.</summary>
        public bool IsPending => _pending.HasValue;

        public bool Selected
        {
            get => ClassListContains(SelectedClass);
            set => EnableInClassList(SelectedClass, value);
        }

        /// <summary>The handle under the pointer, drawn larger.</summary>
        public FrameHandle? HoverHandle
        {
            get => _hoverHandle;
            set
            {
                _hoverHandle = value;
                for (int i = 0; i < _handles.Length; i++)
                {
                    _handles[i].EnableInClassList(HandleHoverClass, value == FrameCanvasMath.Handles[i]);
                }
            }
        }

        /// <summary>Places <paramref name="rect"/> in its parent, which spans the stream screen.</summary>
        public static void Place(VisualElement element, NormalizedRect rect)
        {
            element.style.left = Length.Percent((float)(rect.X * 100));
            element.style.top = Length.Percent((float)(rect.Y * 100));
            element.style.width = Length.Percent((float)(rect.Width * 100));
            element.style.height = Length.Percent((float)(rect.Height * 100));
        }

        /// <summary>Shows an edit that is not confirmed yet. Returns a version for <see cref="EndPending"/>.</summary>
        internal int ShowPending(FrameLayout layout)
        {
            _pending = layout;
            Refresh();
            return ++_pendingVersion;
        }

        /// <summary>
        /// Goes back to the confirmed layout, unless a newer edit started meanwhile: a slow commit must not
        /// hide the edit after it.
        /// </summary>
        internal void EndPending(int version)
        {
            if (version != _pendingVersion)
            {
                return;
            }

            _pending = null;
            Refresh();
        }

        private void Refresh()
        {
            FrameLayout shown = Shown;
            Place(this, shown.Placement);
            EnableInClassList(LockedClass, shown.Locked);
        }
    }
}
