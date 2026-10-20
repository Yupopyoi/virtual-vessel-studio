using System;
using VirtualVessel.Tracking;

namespace VirtualVessel.Avatar
{
    /// <summary>
    /// Drives one avatar from tracking every frame (avatar detailed design 4, 8.3): for now the face, its
    /// expressions and gaze, and the head's turn shared between the neck and the head. The body follows
    /// in step 5 (avatar detailed design 18).
    /// </summary>
    /// <remarks>Main thread only; allocates nothing per frame.</remarks>
    public sealed class AvatarController
    {
        private readonly IAvatarRig _rig;
        private readonly bool _hasNeck;
        private readonly FaceMotion _face;

        public AvatarController(IAvatarModel model, ExpressionMapping mapping)
        {
            if (model == null)
            {
                throw new ArgumentNullException(nameof(model));
            }

            _rig = model.Rig;
            _hasNeck = _rig.Has(AvatarBone.Neck);
            _face = new FaceMotion(mapping);
            Expressions = new ExpressionController(model.Expressions);
        }

        public ExpressionController Expressions { get; }

        /// <summary>What the face shows this frame.</summary>
        public FaceTarget Face => _face.Current;

        /// <summary>
        /// Sets this frame's head, expressions, and gaze. The caller then applies the model
        /// (<see cref="IAvatarModel.Apply"/>).
        /// </summary>
        /// <param name="frame">The tracking source's current frame; null without tracking, which shows a neutral face.</param>
        /// <param name="now">Seconds on the clock of the tracking timestamps.</param>
        public void Update(TrackingFrame frame, bool mirror, double now)
        {
            _face.Update(frame?.Face, mirror, now);
            FaceTarget face = _face.Current;

            // Rotations are in the root's space, so the head keeps the whole turn and the neck takes its share.
            if (_hasNeck)
            {
                _rig.SetRotation(AvatarBone.Neck, HeadPose.Neck(face.Head));
            }

            _rig.SetRotation(AvatarBone.Head, face.Head);
            Expressions.Apply(face);
        }
    }
}
