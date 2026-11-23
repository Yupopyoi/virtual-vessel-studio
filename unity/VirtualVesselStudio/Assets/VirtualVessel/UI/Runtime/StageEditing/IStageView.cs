using System;
using System.Collections.Generic;
using UnityEngine;
using VirtualVessel.Core.State;

namespace VirtualVessel.UI.StageEditing
{
    /// <summary>
    /// A 3D view of the stage for building it in Setup, like Unity's Scene View (system design 11.15, Stage
    /// detailed design 7.4). Stage implements it: its own camera draws into a render target apart from the
    /// stream, with guides only it draws. The UI shows the image, places the markers, and turns input into
    /// these commands. Main thread only.
    /// </summary>
    public interface IStageView
    {
        /// <summary>The render target while the view is open; null while it is closed.</summary>
        IReadOnlyObservable<Texture> Image { get; }

        /// <summary>Where the editable things are on the view; refreshed every frame while open.</summary>
        IReadOnlyObservable<IReadOnlyList<StageViewMarker>> Markers { get; }

        /// <summary>Starts drawing at about this size in pixels; disposing the last one opened stops and releases it.</summary>
        IDisposable Open(int width, int height);

        /// <summary>Moves the viewpoint around the pivot by these degrees: x left and right, y up and down.</summary>
        void Orbit(Vector2 degrees);

        /// <summary>Comes closer by a plus number of wheel steps, goes back by a minus one.</summary>
        void Zoom(float steps);

        /// <summary>Moves the pivot by a fraction of the view's size: x to the right, y down, as the pointer moves.</summary>
        void Pan(Vector2 viewFraction);

        void ResetView();

        /// <summary>Puts that thing at the pivot.</summary>
        void Frame(string itemId);

        /// <summary>The chosen thing, drawn bolder; null for none.</summary>
        void Select(string itemId);

        /// <summary>Starts moving a thing from a point on the view (top left 0, bottom right 1); false for a thing without a handle.</summary>
        bool BeginDrag(string itemId, Vector2 uv);

        void DragTo(Vector2 uv);

        void EndDrag();
    }

    /// <summary>An editable thing's marker on the stage view.</summary>
    public readonly struct StageViewMarker : IEquatable<StageViewMarker>
    {
        public StageViewMarker(string itemId, string label, Vector2 uv, bool visible, Color color)
        {
            ItemId = itemId;
            Label = label;
            Uv = uv;
            Visible = visible;
            Color = color;
        }

        /// <summary>The kind and index, such as "light:0".</summary>
        public string ItemId { get; }

        /// <summary>The marker's text, such as "1".</summary>
        public string Label { get; }

        /// <summary>Where it is on the view: top left 0, bottom right 1.</summary>
        public Vector2 Uv { get; }

        /// <summary>False when it is not in view, such as behind the camera.</summary>
        public bool Visible { get; }

        public Color Color { get; }

        public bool Equals(StageViewMarker other)
        {
            return ItemId == other.ItemId && Label == other.Label && Uv == other.Uv && Visible == other.Visible && Color == other.Color;
        }

        public override bool Equals(object obj) => obj is StageViewMarker other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(ItemId, Uv);
    }

    /// <summary>
    /// The user's choice of which way a drag turns the stage view (setup.md 15.2): people expect either way, so each
    /// direction can be turned around (user's request, 2026-11-22). Saved with the UI settings.
    /// </summary>
    public interface IStageViewPreferences
    {
        bool StageViewInvertX { get; }

        bool StageViewInvertY { get; }

        void SetStageViewInvert(bool invertX, bool invertY);
    }

    /// <summary>The ids of the stage view's editable things.</summary>
    public static class StageViewItems
    {
        public const string LightPrefix = "light:";

        public static string Light(int index) => LightPrefix + index;

        /// <summary>The light's index for a light's id, else -1. Pure.</summary>
        public static int LightIndex(string itemId)
        {
            return itemId != null && itemId.StartsWith(LightPrefix, StringComparison.Ordinal) && int.TryParse(itemId.Substring(LightPrefix.Length), out int index) ? index : -1;
        }
    }
}
