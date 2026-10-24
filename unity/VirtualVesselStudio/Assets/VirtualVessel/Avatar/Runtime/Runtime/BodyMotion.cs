using System;
using UnityEngine;
using VirtualVessel.Tracking;

namespace VirtualVessel.Avatar
{
    /// <summary>
    /// The avatar's upper body over time (avatar detailed design 7.2, 7.3, 8.4), as <see cref="FaceMotion"/>
    /// is for the face: every render frame it moves toward the newest observation over one observation
    /// interval. The torso and each arm are found and lost on their own: a lost arm holds, then goes back
    /// to hanging from the torso, and fades back in when seen again.
    /// </summary>
    /// <remarks>
    /// The state lives here; the rotations are the pure <see cref="PoseRetargeting"/>, the timing the pure
    /// <see cref="MotionTiming"/>. Buffers are reused, so a frame allocates nothing (CLAUDE.md 23).
    /// </remarks>
    public sealed class BodyMotion
    {
        private readonly bool _hasUpperChest;
        private readonly float _armDownDegrees;
        private readonly BodyTarget _observed = new BodyTarget();
        private readonly BodyTarget _from = new BodyTarget();
        private readonly BodyTarget _to = new BodyTarget();
        private readonly BodyTarget _shown = new BodyTarget();
        private double _observedAt = double.NaN;
        private double _arrivedAt;
        private double _interval = MotionTiming.MinInterval;
        private double _lastNow = double.NaN;
        private double _torsoSeenAt = double.NaN;
        private double _leftSeenAt = double.NaN;
        private double _rightSeenAt = double.NaN;

        /// <param name="hasUpperChest">Whether the model has an UpperChest (avatar detailed design 7.2).</param>
        /// <param name="armDownDegrees">How far a resting arm hangs below level.</param>
        public BodyMotion(bool hasUpperChest, float armDownDegrees = AvatarRestPose.DefaultArmDownDegrees)
        {
            _hasUpperChest = hasUpperChest;
            _armDownDegrees = armDownDegrees;
            Rest(Quaternion.identity, Current);
        }

        /// <summary>What the avatar's upper body shows this frame.</summary>
        public BodyTarget Current { get; } = new BodyTarget();

        /// <summary>How much of the tracked torso is shown: 1 tracked, 0 straight.</summary>
        public float TorsoPresence { get; private set; }

        /// <summary>How much of the tracked arm is shown, per the avatar's own side: 1 tracked, 0 hanging.</summary>
        public float LeftArmPresence { get; private set; }

        public float RightArmPresence { get; private set; }

        /// <param name="body">The tracked body this frame; null when there is no tracking.</param>
        /// <param name="now">Seconds on the clock of the body's timestamps.</param>
        public void Update(BodyPose body, bool mirror, double now)
        {
            float elapsed = double.IsNaN(_lastNow) ? 0f : (float)Math.Max(0.0, now - _lastNow);
            _lastNow = now;

            bool usable = body != null && body.IsUsable;
            if (usable && body.Timestamp != _observedAt)
            {
                Observe(body, mirror, now);
            }

            BodyTarget.Lerp(_from, _to, MotionTiming.Progress(now, _arrivedAt, _interval), _shown);
            TorsoPresence = MotionTiming.NextPresence(TorsoPresence, usable && _to.TorsoSeen, LostFor(_torsoSeenAt, now, body), elapsed);
            LeftArmPresence = MotionTiming.NextPresence(LeftArmPresence, usable && _to.LeftArmSeen, LostFor(_leftSeenAt, now, body), elapsed);
            RightArmPresence = MotionTiming.NextPresence(RightArmPresence, usable && _to.RightArmSeen, LostFor(_rightSeenAt, now, body), elapsed);
            Compose(_shown, TorsoPresence, LeftArmPresence, RightArmPresence, _armDownDegrees, Current);
        }

        /// <summary>
        /// What is shown from <paramref name="shown"/> and each region's presence: the torso turned that
        /// share of the way from straight, and each arm that share of the way from hanging from the torso
        /// as it is now. Pure apart from writing the caller's buffer.
        /// </summary>
        public static void Compose(BodyTarget shown, float torso, float leftArm, float rightArm, float armDownDegrees, BodyTarget into)
        {
            into[UpperBodyBone.Spine] = Quaternion.Slerp(Quaternion.identity, shown[UpperBodyBone.Spine], torso);
            into[UpperBodyBone.Chest] = Quaternion.Slerp(Quaternion.identity, shown[UpperBodyBone.Chest], torso);
            into[UpperBodyBone.UpperChest] = Quaternion.Slerp(Quaternion.identity, shown[UpperBodyBone.UpperChest], torso);
            ComposeArm(shown, ArmSide.Left, leftArm, armDownDegrees, into);
            ComposeArm(shown, ArmSide.Right, rightArm, armDownDegrees, into);
            into.TorsoSeen = shown.TorsoSeen;
            into.LeftArmSeen = shown.LeftArmSeen;
            into.RightArmSeen = shown.RightArmSeen;
            into.LeftForearmSeen = shown.LeftForearmSeen;
            into.RightForearmSeen = shown.RightForearmSeen;
        }

        private static void ComposeArm(BodyTarget shown, ArmSide side, float presence, float armDownDegrees, BodyTarget into)
        {
            Quaternion top = into.TorsoTop;
            Quaternion hanging = top * AvatarRestPose.UpperArm(side.IsLeft, armDownDegrees);
            into[side.Shoulder] = Quaternion.Slerp(top, shown[side.Shoulder], presence);
            into[side.UpperArm] = Quaternion.Slerp(hanging, shown[side.UpperArm], presence);
            into[side.LowerArm] = Quaternion.Slerp(hanging, shown[side.LowerArm], presence);
            into[side.Hand] = Quaternion.Slerp(hanging, shown[side.Hand], presence);
        }

        private static double LostFor(double seenAt, double now, BodyPose body)
        {
            return body == null || double.IsNaN(seenAt) ? double.PositiveInfinity : now - seenAt;
        }

        /// <summary>Copies into <paramref name="observed"/> the bones of the parts it did not see from <paramref name="previous"/>.</summary>
        private static void KeepUnseen(BodyTarget previous, BodyTarget observed)
        {
            if (!observed.TorsoSeen)
            {
                observed[UpperBodyBone.Spine] = previous[UpperBodyBone.Spine];
                observed[UpperBodyBone.Chest] = previous[UpperBodyBone.Chest];
                observed[UpperBodyBone.UpperChest] = previous[UpperBodyBone.UpperChest];
            }

            KeepUnseenArm(previous, observed, ArmSide.Left, observed.LeftArmSeen, observed.LeftForearmSeen);
            KeepUnseenArm(previous, observed, ArmSide.Right, observed.RightArmSeen, observed.RightForearmSeen);
        }

        private static void KeepUnseenArm(BodyTarget previous, BodyTarget observed, ArmSide side, bool armSeen, bool forearmSeen)
        {
            UpperBodyBone upper = side.UpperArm;
            UpperBodyBone lower = side.LowerArm;
            if (!armSeen)
            {
                observed[side.Shoulder] = previous[side.Shoulder];
                observed[upper] = previous[upper];
                observed[lower] = previous[lower];
                observed[side.Hand] = previous[side.Hand];
            }
            else if (!forearmSeen)
            {
                // The wrist hidden: the upper arm follows the elbow, the forearm keeps its last bend.
                (Quaternion followed, Quaternion bent) = PoseRetargeting.WithoutWrist(side.IsLeft, observed[upper], previous[upper], previous[lower]);
                observed[upper] = followed;
                observed[lower] = bent;
                observed[side.Hand] = bent;
            }
        }

        private void Rest(Quaternion torso, BodyTarget into)
        {
            for (int i = 0; i < into.Rotations.Length; i++)
            {
                into.Rotations[i] = torso;
            }

            Quaternion left = torso * AvatarRestPose.UpperArm(true, _armDownDegrees);
            Quaternion right = torso * AvatarRestPose.UpperArm(false, _armDownDegrees);
            into[UpperBodyBone.LeftUpperArm] = left;
            into[UpperBodyBone.LeftLowerArm] = left;
            into[UpperBodyBone.LeftHand] = left;
            into[UpperBodyBone.RightUpperArm] = right;
            into[UpperBodyBone.RightLowerArm] = right;
            into[UpperBodyBone.RightHand] = right;
        }

        private void Observe(BodyPose body, bool mirror, double now)
        {
            PoseRetargeting.Retarget(body, mirror, _hasUpperChest, _armDownDegrees, _observed);
            if (_observed.TorsoSeen)
            {
                _torsoSeenAt = body.Timestamp;
            }

            if (_observed.LeftArmSeen)
            {
                _leftSeenAt = body.Timestamp;
            }

            if (_observed.RightArmSeen)
            {
                _rightSeenAt = body.Timestamp;
            }

            // Starting from nothing shown, there is no earlier pose to move from: begin at the observation
            // and let the presences fade it in.
            bool fresh = double.IsNaN(_observedAt) || (TorsoPresence <= 0f && LeftArmPresence <= 0f && RightArmPresence <= 0f);

            // A part this observation did not see keeps where it was last seen, so it holds while lost
            // instead of moving to the rest at once; the presence takes it back after the hold.
            if (!fresh)
            {
                KeepUnseen(_to, _observed);
            }

            if (!fresh && body.Status == TrackingStatus.LowConfidence)
            {
                // An unsure observation moves the body only part of the way (avatar detailed design 8.4).
                BodyTarget.Lerp(_to, _observed, body.Confidence, _observed);
            }

            _from.CopyFrom(fresh ? _observed : _shown);
            _to.CopyFrom(_observed);
            _interval = MotionTiming.Interval(fresh, body.Timestamp, _observedAt);
            _observedAt = body.Timestamp;
            _arrivedAt = now;
        }
    }
}
