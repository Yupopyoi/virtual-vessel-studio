using UnityEngine;
using UnityEngine.UIElements;
using VirtualVessel.Tracking;

namespace VirtualVessel.UI.Screens
{
    /// <summary>
    /// Covers the face in the tracking camera's image on the Diagnostics screen (UI foundation detailed
    /// design 7.7), so the arms can be compared with the skeleton without showing who it is. An opaque
    /// ellipse over the head, sized from the shoulder width; when the head's place is not known, the whole
    /// image is covered. Colors come from tokens through USS.
    /// </summary>
    /// <remarks>
    /// The head's place arrives about one inference later than the image, so the ellipse is generous:
    /// the shoulder width, which stays when the head turns, unlike the distance between the ears.
    /// </remarks>
    public sealed class FaceCover : VisualElement
    {
        public const string UssClass = "vv-face-cover";

        /// <summary>The ellipse's half width and half height as shares of the shoulder width.</summary>
        public const float HalfWidthPerShoulder = 0.55f;
        public const float HalfHeightPerShoulder = 0.75f;

        private const int EllipsePoints = 40;

        private static readonly CustomStyleProperty<Color> s_coverColor = new CustomStyleProperty<Color>("--face-cover");

        private Rect _image;
        private bool _known;
        private bool _hasImage;
        private Vector2 _center;
        private Vector2 _radii;
        private Color _color = Color.black;

        public FaceCover()
        {
            AddToClassList(UssClass);
            pickingMode = PickingMode.Ignore;
            generateVisualContent += Draw;
            RegisterCallback<CustomStyleResolvedEvent>(evt =>
            {
                evt.customStyle.TryGetValue(s_coverColor, out _color);
                MarkDirtyRepaint();
            });
        }

        /// <summary>Whether the whole image is covered because the head's place is not known.</summary>
        public bool CoversAll => _hasImage && !_known;

        /// <summary>
        /// Where the face is in the image, in pixels of an image <paramref name="imageWidth"/> wide and
        /// <paramref name="imageHeight"/> high: false when the nose or a shoulder is not seen, and then the
        /// whole image is to be covered. Pure.
        /// </summary>
        public static bool TryLocate(BodyPose body, float imageWidth, float imageHeight, out Vector2 center, out Vector2 radii)
        {
            center = Vector2.zero;
            radii = Vector2.zero;
            if (body == null || !body.IsUsable || !Seen(body, BodyJoint.Nose) || !Seen(body, BodyJoint.LeftShoulder) || !Seen(body, BodyJoint.RightShoulder))
            {
                return false;
            }

            var size = new Vector2(imageWidth, imageHeight);
            Vector2 Pixels(BodyJoint joint) => Vector2.Scale(body.ImagePositions[(int)joint], size);
            float shoulders = Vector2.Distance(Pixels(BodyJoint.LeftShoulder), Pixels(BodyJoint.RightShoulder));
            if (shoulders <= 1f)
            {
                return false;
            }

            center = Seen(body, BodyJoint.LeftEar) && Seen(body, BodyJoint.RightEar)
                ? (Pixels(BodyJoint.LeftEar) + Pixels(BodyJoint.RightEar)) * 0.5f
                : Pixels(BodyJoint.Nose);
            radii = new Vector2(shoulders * HalfWidthPerShoulder, shoulders * HalfHeightPerShoulder);
            return true;
        }

        /// <summary>Covers the face of <paramref name="body"/> in a texture of the given size shown at <paramref name="image"/>; no texture covers nothing.</summary>
        public void Show(BodyPose body, Texture texture, Rect image)
        {
            _image = image;
            _hasImage = texture != null;
            _known = _hasImage && TryLocate(body, texture.width, texture.height, out _center, out _radii);
            if (_known)
            {
                // From the texture's pixels to the shown rectangle's.
                var scale = new Vector2(image.width / texture.width, image.height / texture.height);
                _center = image.position + Vector2.Scale(_center, scale);
                _radii = Vector2.Scale(_radii, scale);
            }

            MarkDirtyRepaint();
        }

        private static bool Seen(BodyPose body, BodyJoint joint)
        {
            return body.JointConfidence[(int)joint] >= BodyPose.MinVisibility;
        }

        private void Draw(MeshGenerationContext context)
        {
            if (!_hasImage)
            {
                return;
            }

            Painter2D painter = context.painter2D;
            painter.fillColor = _color;
            painter.BeginPath();
            if (!_known)
            {
                painter.MoveTo(_image.min);
                painter.LineTo(new Vector2(_image.xMax, _image.yMin));
                painter.LineTo(_image.max);
                painter.LineTo(new Vector2(_image.xMin, _image.yMax));
            }
            else
            {
                for (int i = 0; i < EllipsePoints; i++)
                {
                    float angle = i * 2f * Mathf.PI / EllipsePoints;
                    var point = _center + new Vector2(Mathf.Cos(angle) * _radii.x, Mathf.Sin(angle) * _radii.y);
                    if (i == 0)
                    {
                        painter.MoveTo(point);
                    }
                    else
                    {
                        painter.LineTo(point);
                    }
                }
            }

            painter.ClosePath();
            painter.Fill();
        }
    }
}
