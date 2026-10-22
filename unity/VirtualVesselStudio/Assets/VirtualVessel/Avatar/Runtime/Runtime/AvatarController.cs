using System;
using VirtualVessel.Tracking;

namespace VirtualVessel.Avatar
{
    /// <summary>
    /// Drives one avatar from tracking every frame (avatar detailed design 4, 8.3): the upper body (torso,
    /// shoulders, and arms), and the face, its expressions and gaze, and the head's turn shared between
    /// the neck and the head on top of the torso. Hands and fingers follow in step 6 (avatar detailed
    /// design 18).
    /// </summary>
    /// <remarks>Main thread only; allocates nothing per frame.</remarks>
    public sealed class AvatarController
    {
        private readonly IAvatarRig _rig;
        private readonly bool _hasNeck;
        private readonly FaceMotion _face;
        private readonly BodyMotion _body;

        public AvatarController(IAvatarModel model, ExpressionMapping mapping)
        {
            if (model == null)
            {
                throw new ArgumentNullException(nameof(model));
            }

            _rig = model.Rig;
            _hasNeck = _rig.Has(AvatarBone.Neck);
            _face = new FaceMotion(mapping);
            _body = new BodyMotion(_rig.Has(AvatarBone.UpperChest));
            Expressions = new ExpressionController(model.Expressions, model.FaceShapes);
        }

        public ExpressionController Expressions { get; }

        /// <summary>What the face shows this frame.</summary>
        public FaceTarget Face => _face.Current;

        /// <summary>What the upper body shows this frame.</summary>
        public BodyTarget Body => _body.Current;

        /// <summary>How the tracked face turns into expressions; changed as the user tunes the face.</summary>
        public ExpressionMapping Mapping
        {
            get => _face.Mapping;
            set => _face.Mapping = value;
        }

        /// <summary>
        /// Sets this frame's upper body, head, expressions, and gaze. The caller then applies the model
        /// (<see cref="IAvatarModel.Apply"/>).
        /// </summary>
        /// <param name="frame">The tracking source's current frame; null without tracking, which shows a neutral face.</param>
        /// <param name="now">Seconds on the clock of the tracking timestamps.</param>
        public void Update(TrackingFrame frame, bool mirror, double now)
        {
            _face.Update(frame?.Face, mirror, now);
            _body.Update(frame?.Body, mirror, now);
            FaceTarget face = _face.Current;
            BodyTarget body = _body.Current;
            for (int i = 0; i < BodyTarget.BoneCount; i++)
            {
                // Bones the model lacks are ignored by the rig.
                _rig.SetRotation(BodyTarget.ToAvatarBone((UpperBodyBone)i), body.Rotations[i]);
            }

            // Rotations are in the root's space, so the head keeps the whole turn and the neck takes its
            // share of the way from the top of the torso.
            if (_hasNeck)
            {
                _rig.SetRotation(AvatarBone.Neck, HeadPose.Neck(body.TorsoTop, face.Head));
            }

            _rig.SetRotation(AvatarBone.Head, face.Head);
            Expressions.Apply(face);
        }
    }
}
