using System;
using System.Collections.Generic;
using UnityEngine.UIElements;
using VirtualVessel.Tracking;
using VirtualVessel.UI.Controls;
using VirtualVessel.UI.Frames;
using VirtualVessel.UI.Localization;
using VirtualVessel.UI.Preview;

namespace VirtualVessel.UI.Screens
{
    /// <summary>String table keys of the live screen (docs/&lt;lang&gt;/ui/screens/live.md).</summary>
    [StringKeys]
    public static class LiveStrings
    {
        public const string Layers = "live.layers";
        public const string Hint = "live.hint";
    }

    /// <summary>
    /// The live screen: the stream preview with placement editing on it, and the stacking order
    /// (docs/&lt;lang&gt;/ui/screens/live.md). Capture is started and stopped on the setup screen.
    /// </summary>
    public sealed class LiveScreen : VisualElement
    {
        public const string UssClass = "vv-live";
        public const string PreviewClass = UssClass + "__preview";
        public const string LayersClass = UssClass + "__layers";
        public const string HintClass = UssClass + "__hint";

        /// <param name="preview">The Video module's stream; null shows the preview's "no video".</param>
        /// <param name="frames">Stage's videos to place; null or empty leaves out placement editing.</param>
        /// <param name="tracking">Tracking, for the arms and hands in view; null leaves them out.</param>
        public LiveScreen(IStreamPreviewSource preview, IReadOnlyList<IFrameSource> frames, ITrackingSource tracking = null)
        {
            AddToClassList(UssClass);
            Preview = new StreamPreview();
            Preview.AddToClassList(PreviewClass);
            if (preview != null)
            {
                Preview.Bind(preview);
            }

            // The screen scrolls, so nothing gives the preview a height to fill: it takes the stream's
            // shape from its width. A size, so code rather than USS (UI foundation detailed design 9.9).
            Preview.RegisterCallback<GeometryChangedEvent>(evt => Preview.style.height = evt.newRect.width * 9f / 16f);
            Add(Preview);

            // Right under the stream, so an arm that rests can be told from one the camera does not see.
            if (tracking != null)
            {
                Limbs = new LimbStatusRow(tracking);
                Add(Limbs);
            }

            Canvas = new FrameCanvas();
            Canvas.SetSources(frames ?? Array.Empty<IFrameSource>());
            if (preview != null && frames != null && frames.Count > 0)
            {
                Preview.Overlay.Add(Canvas);
            }

            Card layers = Ui.Card(LiveStrings.Layers);
            layers.AddToClassList(LayersClass);
            Layers = new LayerList(Canvas);
            layers.Add(Layers);
            Add(layers);
            Add(Ui.Text(LiveStrings.Hint, HintClass, "vv-text-body"));
        }

        public StreamPreview Preview { get; }

        /// <summary>The arms and hands in view; null without tracking.</summary>
        public LimbStatusRow Limbs { get; }

        public FrameCanvas Canvas { get; }

        public LayerList Layers { get; }
    }
}
