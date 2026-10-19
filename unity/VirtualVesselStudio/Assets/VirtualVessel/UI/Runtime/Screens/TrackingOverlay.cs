using System;
using UnityEngine;
using UnityEngine.UIElements;
using VirtualVessel.Tracking;

namespace VirtualVessel.UI.Screens
{
    /// <summary>
    /// The tracked points and bones over the tracking camera's image on the setup screen, so the user can
    /// see that tracking works (tracking detailed design 11). Never on the stream. Draws from a copy of
    /// the image positions it is given; the colors come from tokens through USS.
    /// </summary>
    public sealed class TrackingOverlay : VisualElement
    {
        public const string UssClass = "vv-tracking-overlay";

        private const float PointRadius = 3f;
        private const float LineWidth = 2f;

        // Bones drawn between body joints: the upper body only, as it is tracked in v0.1.0.
        private static readonly (BodyJoint From, BodyJoint To)[] s_bones =
        {
            (BodyJoint.LeftShoulder, BodyJoint.RightShoulder),
            (BodyJoint.LeftShoulder, BodyJoint.LeftElbow),
            (BodyJoint.LeftElbow, BodyJoint.LeftWrist),
            (BodyJoint.RightShoulder, BodyJoint.RightElbow),
            (BodyJoint.RightElbow, BodyJoint.RightWrist),
            (BodyJoint.LeftShoulder, BodyJoint.LeftHip),
            (BodyJoint.RightShoulder, BodyJoint.RightHip),
            (BodyJoint.LeftHip, BodyJoint.RightHip),
            (BodyJoint.Neck, BodyJoint.Nose),
        };

        // Fingers drawn from the wrist through each joint to the tip.
        private static readonly int[][] s_fingers =
        {
            new[] { 0, 1, 2, 3, 4 },
            new[] { 0, 5, 6, 7, 8 },
            new[] { 0, 9, 10, 11, 12 },
            new[] { 0, 13, 14, 15, 16 },
            new[] { 0, 17, 18, 19, 20 },
        };

        private static readonly CustomStyleProperty<Color> s_pointColor = new CustomStyleProperty<Color>("--tracking-point");
        private static readonly CustomStyleProperty<Color> s_boneColor = new CustomStyleProperty<Color>("--tracking-bone");
        private static readonly CustomStyleProperty<Color> s_outlineColor = new CustomStyleProperty<Color>("--tracking-outline");

        private readonly Vector2[] _body = new Vector2[BodyPose.JointCount];
        private readonly Vector2[] _left = new Vector2[HandPose.JointCount];
        private readonly Vector2[] _right = new Vector2[HandPose.JointCount];
        private bool _hasBody;
        private bool _hasLeft;
        private bool _hasRight;
        private Rect _image;
        private Color _point = Color.white;
        private Color _bone = Color.white;
        private Color _outline = Color.black;

        public TrackingOverlay()
        {
            AddToClassList(UssClass);
            pickingMode = PickingMode.Ignore;
            generateVisualContent += Draw;
            RegisterCallback<CustomStyleResolvedEvent>(OnCustomStyle);
        }

        public bool HasBody => _hasBody;

        /// <summary>
        /// Where an image of <paramref name="imageWidth"/> × <paramref name="imageHeight"/> is shown inside
        /// a box of <paramref name="boxWidth"/> × <paramref name="boxHeight"/> when scaled to fit and
        /// centered, as the preview shows it. Pure.
        /// </summary>
        public static Rect FitRect(float boxWidth, float boxHeight, float imageWidth, float imageHeight)
        {
            if (boxWidth <= 0 || boxHeight <= 0 || imageWidth <= 0 || imageHeight <= 0)
            {
                return new Rect(0, 0, Math.Max(0, boxWidth), Math.Max(0, boxHeight));
            }

            float scale = Math.Min(boxWidth / imageWidth, boxHeight / imageHeight);
            float width = imageWidth * scale;
            float height = imageHeight * scale;
            return new Rect((boxWidth - width) / 2f, (boxHeight - height) / 2f, width, height);
        }

        /// <summary>Takes the image positions of <paramref name="frame"/> to draw; parts not tracked are not drawn.</summary>
        public void Show(TrackingFrame frame, Rect image)
        {
            _image = image;
            _hasBody = Copy(frame?.Body, frame?.Body.ImagePositions, _body);
            _hasLeft = Copy(frame?.LeftHand, frame?.LeftHand.ImagePositions, _left);
            _hasRight = Copy(frame?.RightHand, frame?.RightHand.ImagePositions, _right);
            MarkDirtyRepaint();
        }

        private static bool Copy(TrackedPart part, Vector2[] positions, Vector2[] into)
        {
            if (part == null || !part.IsUsable)
            {
                return false;
            }

            Array.Copy(positions, into, into.Length);
            return true;
        }

        private void OnCustomStyle(CustomStyleResolvedEvent evt)
        {
            evt.customStyle.TryGetValue(s_pointColor, out _point);
            evt.customStyle.TryGetValue(s_boneColor, out _bone);
            evt.customStyle.TryGetValue(s_outlineColor, out _outline);
            MarkDirtyRepaint();
        }

        private Vector2 At(Vector2 normalized)
        {
            return new Vector2(_image.x + (normalized.x * _image.width), _image.y + (normalized.y * _image.height));
        }

        private void Draw(MeshGenerationContext context)
        {
            Painter2D painter = context.painter2D;
            if (_hasBody)
            {
                foreach ((BodyJoint from, BodyJoint to) in s_bones)
                {
                    Line(painter, At(_body[(int)from]), At(_body[(int)to]));
                }
            }

            DrawHand(painter, _hasLeft, _left);
            DrawHand(painter, _hasRight, _right);
            if (_hasBody)
            {
                foreach ((BodyJoint from, BodyJoint to) in s_bones)
                {
                    Point(painter, At(_body[(int)from]));
                    Point(painter, At(_body[(int)to]));
                }
            }
        }

        private void DrawHand(Painter2D painter, bool tracked, Vector2[] hand)
        {
            if (!tracked)
            {
                return;
            }

            foreach (int[] finger in s_fingers)
            {
                for (int i = 1; i < finger.Length; i++)
                {
                    Line(painter, At(hand[finger[i - 1]]), At(hand[finger[i]]));
                }
            }

            foreach (Vector2 joint in hand)
            {
                Point(painter, At(joint));
            }
        }

        private void Line(Painter2D painter, Vector2 from, Vector2 to)
        {
            // An outline under each line keeps it visible on bright and dark camera images alike.
            painter.lineCap = LineCap.Round;
            painter.strokeColor = _outline;
            painter.lineWidth = LineWidth + 2f;
            Stroke(painter, from, to);
            painter.strokeColor = _bone;
            painter.lineWidth = LineWidth;
            Stroke(painter, from, to);
        }

        private static void Stroke(Painter2D painter, Vector2 from, Vector2 to)
        {
            painter.BeginPath();
            painter.MoveTo(from);
            painter.LineTo(to);
            painter.Stroke();
        }

        private void Point(Painter2D painter, Vector2 at)
        {
            painter.fillColor = _outline;
            painter.BeginPath();
            painter.Arc(at, PointRadius + 1f, 0f, 360f);
            painter.Fill();
            painter.fillColor = _point;
            painter.BeginPath();
            painter.Arc(at, PointRadius, 0f, 360f);
            painter.Fill();
        }
    }
}
