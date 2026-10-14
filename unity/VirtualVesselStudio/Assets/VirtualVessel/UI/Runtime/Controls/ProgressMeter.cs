using UnityEngine;
using UnityEngine.UIElements;

namespace VirtualVessel.UI.Controls
{
    /// <summary>
    /// A progress bar with what is in progress and where it is, such as
    /// "RVC training · Epoch 120 / 300" (components.md 3.15). The bar is a Primary-to-Secondary gradient.
    /// </summary>
    [UxmlElement]
    public partial class ProgressMeter : VisualElement
    {
        public const string UssClass = "vv-progress";

        private static readonly CustomStyleProperty<Color> s_startColor = new CustomStyleProperty<Color>("--progress-start");
        private static readonly CustomStyleProperty<Color> s_endColor = new CustomStyleProperty<Color>("--progress-end");

        private readonly KeyedText _label;
        private readonly Label _separator;
        private readonly Label _detail;
        private readonly VisualElement _fill;
        private float _progress;
        private Color _start = Color.white;
        private Color _end = Color.white;

        public ProgressMeter()
        {
            AddToClassList(UssClass);
            var header = new VisualElement();
            header.AddToClassList(UssClass + "__header");
            var label = new Label();
            label.AddToClassList(UssClass + "__label");
            label.AddToClassList("vv-text-caption");
            _separator = new Label("·");
            _separator.AddToClassList(UssClass + "__separator");
            _separator.AddToClassList("vv-text-caption");
            _detail = new Label();
            _detail.AddToClassList(UssClass + "__detail");
            _detail.AddToClassList("vv-text-caption");
            header.Add(label);
            header.Add(_separator);
            header.Add(_detail);
            Add(header);

            var track = new VisualElement();
            track.AddToClassList(UssClass + "__track");
            _fill = new VisualElement { pickingMode = PickingMode.Ignore };
            _fill.AddToClassList(UssClass + "__fill");
            _fill.generateVisualContent += DrawGradient;
            track.Add(_fill);
            Add(track);
            RegisterCallback<CustomStyleResolvedEvent>(OnCustomStyle);

            _label = new KeyedText(this, value => label.text = value);
            Detail = null;
            Progress = 0f;
        }

        [UxmlAttribute("label-key")]
        public string LabelKey
        {
            get => _label.Key;
            set => _label.Key = value;
        }

        /// <summary>
        /// Where the work is, such as "Epoch 120 / 300". The caller builds it with the localizer, because
        /// it carries numbers.
        /// </summary>
        public string Detail
        {
            get => _detail.text;
            set
            {
                _detail.text = value ?? string.Empty;
                DisplayStyle display = string.IsNullOrEmpty(value) ? DisplayStyle.None : DisplayStyle.Flex;
                _detail.style.display = display;
                _separator.style.display = display;
            }
        }

        /// <summary>Progress from 0 to 1.</summary>
        [UxmlAttribute("progress")]
        public float Progress
        {
            get => _progress;
            set
            {
                _progress = Mathf.Clamp01(value);
                _fill.style.width = Length.Percent(_progress * 100f);
                _fill.MarkDirtyRepaint();
            }
        }

        /// <summary>
        /// The color at the end of the filled part. The gradient spans the whole track, so a bar that is
        /// half done ends halfway between the two colors. Pure.
        /// </summary>
        public static Color EndColor(Color start, Color end, float progress)
        {
            return Color.Lerp(start, end, Mathf.Clamp01(progress));
        }

        private void OnCustomStyle(CustomStyleResolvedEvent evt)
        {
            evt.customStyle.TryGetValue(s_startColor, out _start);
            evt.customStyle.TryGetValue(s_endColor, out _end);
            _fill.MarkDirtyRepaint();
        }

        /// <summary>USS has no gradients, so the fill is one quad with colored corners.</summary>
        private void DrawGradient(MeshGenerationContext context)
        {
            Rect rect = _fill.contentRect;
            if (rect.width <= 0f || rect.height <= 0f)
            {
                return;
            }

            Color end = EndColor(_start, _end, _progress);
            MeshWriteData mesh = context.Allocate(4, 6);
            mesh.SetNextVertex(new Vertex { position = new Vector3(rect.xMin, rect.yMin, Vertex.nearZ), tint = _start });
            mesh.SetNextVertex(new Vertex { position = new Vector3(rect.xMax, rect.yMin, Vertex.nearZ), tint = end });
            mesh.SetNextVertex(new Vertex { position = new Vector3(rect.xMax, rect.yMax, Vertex.nearZ), tint = end });
            mesh.SetNextVertex(new Vertex { position = new Vector3(rect.xMin, rect.yMax, Vertex.nearZ), tint = _start });
            mesh.SetNextIndex(0);
            mesh.SetNextIndex(1);
            mesh.SetNextIndex(2);
            mesh.SetNextIndex(0);
            mesh.SetNextIndex(2);
            mesh.SetNextIndex(3);
        }
    }
}
