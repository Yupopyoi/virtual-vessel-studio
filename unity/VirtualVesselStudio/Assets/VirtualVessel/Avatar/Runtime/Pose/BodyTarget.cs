using System;
using UnityEngine;

namespace VirtualVessel.Avatar
{
    /// <summary>The bones the upper body moves (avatar detailed design 7.2), in the order the rig sets them.</summary>
    public enum UpperBodyBone
    {
        Spine,
        Chest,
        UpperChest,
        LeftShoulder,
        RightShoulder,
        LeftUpperArm,
        RightUpperArm,
        LeftLowerArm,
        RightLowerArm,
        LeftHand,
        RightHand,
    }

    /// <summary>
    /// What the avatar's upper body should show: each bone's rotation from the reference pose in the avatar
    /// root's space, and which regions the observation behind it saw (avatar detailed design 7.1, 7.2).
    /// Objects are reused every frame, never allocated per observation (CLAUDE.md 23).
    /// </summary>
    public sealed class BodyTarget
    {
        public static readonly int BoneCount = Enum.GetValues(typeof(UpperBodyBone)).Length;

        private static readonly AvatarBone[] s_bones =
        {
            AvatarBone.Spine,
            AvatarBone.Chest,
            AvatarBone.UpperChest,
            AvatarBone.LeftShoulder,
            AvatarBone.RightShoulder,
            AvatarBone.LeftUpperArm,
            AvatarBone.RightUpperArm,
            AvatarBone.LeftLowerArm,
            AvatarBone.RightLowerArm,
            AvatarBone.LeftHand,
            AvatarBone.RightHand,
        };

        public BodyTarget()
        {
            for (int i = 0; i < Rotations.Length; i++)
            {
                Rotations[i] = Quaternion.identity;
            }
        }

        /// <summary>Per <see cref="UpperBodyBone"/>, in the avatar root's space; identity is the reference pose.</summary>
        public Quaternion[] Rotations { get; } = new Quaternion[BoneCount];

        /// <summary>Whether the observation saw the torso (both shoulders).</summary>
        public bool TorsoSeen { get; set; }

        /// <summary>Whether the observation saw the avatar's own left upper arm (shoulder and elbow).</summary>
        public bool LeftArmSeen { get; set; }

        public bool RightArmSeen { get; set; }

        /// <summary>Whether it saw the left wrist too, so the forearm points where it is seen.</summary>
        public bool LeftForearmSeen { get; set; }

        public bool RightForearmSeen { get; set; }

        /// <summary>The top of the torso, which the shoulders, arms, and neck turn from.</summary>
        public Quaternion TorsoTop => Rotations[(int)UpperBodyBone.UpperChest];

        public Quaternion this[UpperBodyBone bone]
        {
            get => Rotations[(int)bone];
            set => Rotations[(int)bone] = value;
        }

        /// <summary>The rig's bone for <paramref name="bone"/>.</summary>
        public static AvatarBone ToAvatarBone(UpperBodyBone bone)
        {
            return s_bones[(int)bone];
        }

        public void CopyFrom(BodyTarget other)
        {
            Array.Copy(other.Rotations, Rotations, Rotations.Length);
            TorsoSeen = other.TorsoSeen;
            LeftArmSeen = other.LeftArmSeen;
            RightArmSeen = other.RightArmSeen;
            LeftForearmSeen = other.LeftForearmSeen;
            RightForearmSeen = other.RightForearmSeen;
        }

        /// <summary>
        /// Writes into <paramref name="into"/> the target <paramref name="t"/> of the way from
        /// <paramref name="from"/> to <paramref name="to"/> along the shortest arcs; what was seen is
        /// <paramref name="to"/>'s. <paramref name="into"/> may be either input. Pure apart from writing
        /// the caller's buffer, which is reused every frame (CLAUDE.md 15.1, 23).
        /// </summary>
        public static void Lerp(BodyTarget from, BodyTarget to, float t, BodyTarget into)
        {
            t = Mathf.Clamp01(t);
            for (int i = 0; i < into.Rotations.Length; i++)
            {
                into.Rotations[i] = Quaternion.Slerp(from.Rotations[i], to.Rotations[i], t);
            }

            into.TorsoSeen = to.TorsoSeen;
            into.LeftArmSeen = to.LeftArmSeen;
            into.RightArmSeen = to.RightArmSeen;
            into.LeftForearmSeen = to.LeftForearmSeen;
            into.RightForearmSeen = to.RightForearmSeen;
        }
    }
}
