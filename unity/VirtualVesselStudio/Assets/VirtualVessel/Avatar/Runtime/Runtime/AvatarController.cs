using System;
using UnityEngine;
using VirtualVessel.Tracking;

namespace VirtualVessel.Avatar
{
    /// <summary>
    /// Drives one avatar from tracking every frame (avatar detailed design 4, 8.3): the upper body (torso,
    /// shoulders, arms, and the hands' turn with the forearms' twist), and the face, its expressions and
    /// gaze, and the head's turn shared between the neck and the head on top of the torso, and the fingers
    /// on the hands (avatar detailed design 7.5).
    /// </summary>
    /// <remarks>Main thread only; allocates nothing per frame.</remarks>
    public sealed class AvatarController
    {
        private readonly IAvatarRig _rig;
        private readonly bool _hasNeck;
        private readonly FaceMotion _face;
        private readonly BodyMotion _body;
        private readonly HandMotion _hands = new HandMotion();
        private readonly BodyTarget _withHands = new BodyTarget();
        private readonly ClearanceMotion _clearance;
        private readonly SteadyMotion _steady = new SteadyMotion();
        private readonly FingerRest _leftFingers;
        private readonly FingerRest _rightFingers;
        private readonly Quaternion[] _fingerRotations = new Quaternion[FingerPose.FingerCount * FingerPose.JointCount];

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
            _clearance = new ClearanceMotion(model.BodyShape);
            _clearance.Current.CopyFrom(_body.Current);
            _steady.Current.CopyFrom(_body.Current);
            _leftFingers = FingerRest.Of(model.BodyShape, ArmSide.Left);
            _rightFingers = FingerRest.Of(model.BodyShape, ArmSide.Right);
            Expressions = new ExpressionController(model.Expressions, model.FaceShapes);
        }

        public ExpressionController Expressions { get; }

        /// <summary>What the face shows this frame.</summary>
        public FaceTarget Face => _face.Current;

        /// <summary>What the upper body shows this frame, with the arms kept out of the head (avatar detailed design 7.6).</summary>
        public BodyTarget Body => _steady.Current;

        /// <summary>What the left hand's fingers show this frame (avatar detailed design 7.5).</summary>
        public FingerPose LeftFingers => _hands.LeftFingers;

        public FingerPose RightFingers => _hands.RightFingers;

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
        /// <param name="armsInFront">
        /// Keeps the arms in front of the body: tracking's 2D body (tracking detailed design 8.6) has no depth
        /// to tell front from back by (avatar detailed design 7.6).
        /// </param>
        public void Update(TrackingFrame frame, bool mirror, double now, bool armsInFront = false)
        {
            _face.Update(frame?.Face, mirror, now);
            _body.Update(frame?.Body, mirror, now);
            _hands.Update(frame, mirror, now);
            FaceTarget face = _face.Current;

            // The hands, inferred apart from the body, turn on the arms the body set; the arms then keep
            // out of the head and torso, hands and all.
            _withHands.CopyFrom(_body.Current);
            HandRetargeting.Apply(ArmSide.Left, _hands.LeftPalm, _hands.LeftPresence, _withHands);
            HandRetargeting.Apply(ArmSide.Right, _hands.RightPalm, _hands.RightPresence, _withHands);
            Quaternion neck = HeadPose.Neck(_withHands.TorsoTop, face.Head);
            _clearance.Update(_withHands, neck, face.Head, armsInFront, now);
            _steady.Update(_clearance.Current, now);
            BodyTarget body = _steady.Current;
            for (int i = 0; i < BodyTarget.BoneCount; i++)
            {
                // Bones the model lacks are ignored by the rig.
                _rig.SetRotation(BodyTarget.ToAvatarBone((UpperBodyBone)i), body.Rotations[i]);
            }

            // Rotations are in the root's space, so the head keeps the whole turn and the neck takes its
            // share of the way from the top of the torso.
            if (_hasNeck)
            {
                _rig.SetRotation(AvatarBone.Neck, neck);
            }

            _rig.SetRotation(AvatarBone.Head, face.Head);
            SetFingers(ArmSide.Left, _leftFingers, body[ArmSide.Left.Hand], _hands.LeftFingers);
            SetFingers(ArmSide.Right, _rightFingers, body[ArmSide.Right.Hand], _hands.RightFingers);
            Expressions.Apply(face);
        }

        private void SetFingers(ArmSide side, in FingerRest rest, Quaternion hand, FingerPose pose)
        {
            FingerRetargeting.Rotations(rest, hand, pose, _fingerRotations);
            for (Finger finger = Finger.Thumb; finger <= Finger.Little; finger++)
            {
                for (int joint = 0; joint < FingerPose.JointCount; joint++)
                {
                    _rig.SetRotation(FingerRetargeting.Bone(side, finger, joint), _fingerRotations[((int)finger * FingerPose.JointCount) + joint]);
                }
            }
        }
    }
}
