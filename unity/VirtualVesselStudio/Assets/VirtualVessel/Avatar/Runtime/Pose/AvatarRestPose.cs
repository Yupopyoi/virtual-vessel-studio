using UnityEngine;

namespace VirtualVessel.Avatar
{
    /// <summary>
    /// The pose an avatar holds when nothing moves it: the reference pose with the arms lowered, never
    /// the T pose (avatar detailed design 7.2). Pure.
    /// </summary>
    public static class AvatarRestPose
    {
        /// <summary>The default of pose.armDownAngle in the avatar profile (avatar detailed design 6.3).</summary>
        public const float DefaultArmDownDegrees = 70f;

        /// <summary>
        /// The upper arm's rotation in the root space. The avatar faces +Z, so its left arm points to -X
        /// and is lowered by turning around +Z; the right arm mirrors it.
        /// </summary>
        public static Quaternion UpperArm(bool left, float armDownDegrees)
        {
            return Quaternion.AngleAxis(left ? armDownDegrees : -armDownDegrees, Vector3.forward);
        }

        /// <summary>Puts <paramref name="rig"/> in the rest pose: the reference pose with the arms lowered.</summary>
        public static void Apply(IAvatarRig rig, float armDownDegrees = DefaultArmDownDegrees)
        {
            rig.ResetPose();
            rig.SetRotation(AvatarBone.LeftUpperArm, UpperArm(true, armDownDegrees));
            rig.SetRotation(AvatarBone.RightUpperArm, UpperArm(false, armDownDegrees));
        }
    }
}
