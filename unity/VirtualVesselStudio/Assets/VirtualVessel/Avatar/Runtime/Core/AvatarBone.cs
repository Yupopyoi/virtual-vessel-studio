using System;
using System.Collections.Generic;

namespace VirtualVessel.Avatar
{
    /// <summary>
    /// Standard human bones, independent of any model format (avatar detailed design 5.2). Same names
    /// and values as Unity's HumanBodyBones, so a loader can convert by casting; a test keeps them equal.
    /// The thumb's three bones follow Unity's names (VRM 1.0 calls them metacarpal, proximal, distal).
    /// </summary>
    public enum AvatarBone
    {
        Hips,
        LeftUpperLeg,
        RightUpperLeg,
        LeftLowerLeg,
        RightLowerLeg,
        LeftFoot,
        RightFoot,
        Spine,
        Chest,
        Neck,
        Head,
        LeftShoulder,
        RightShoulder,
        LeftUpperArm,
        RightUpperArm,
        LeftLowerArm,
        RightLowerArm,
        LeftHand,
        RightHand,
        LeftToes,
        RightToes,
        LeftEye,
        RightEye,
        Jaw,
        LeftThumbProximal,
        LeftThumbIntermediate,
        LeftThumbDistal,
        LeftIndexProximal,
        LeftIndexIntermediate,
        LeftIndexDistal,
        LeftMiddleProximal,
        LeftMiddleIntermediate,
        LeftMiddleDistal,
        LeftRingProximal,
        LeftRingIntermediate,
        LeftRingDistal,
        LeftLittleProximal,
        LeftLittleIntermediate,
        LeftLittleDistal,
        RightThumbProximal,
        RightThumbIntermediate,
        RightThumbDistal,
        RightIndexProximal,
        RightIndexIntermediate,
        RightIndexDistal,
        RightMiddleProximal,
        RightMiddleIntermediate,
        RightMiddleDistal,
        RightRingProximal,
        RightRingIntermediate,
        RightRingDistal,
        RightLittleProximal,
        RightLittleIntermediate,
        RightLittleDistal,
        UpperChest,
    }

    public static class AvatarBones
    {
        public const int Count = (int)AvatarBone.UpperChest + 1;

        /// <summary>
        /// Bones an avatar must have to be used (avatar detailed design 8.2): the upper-body bones VRM 1.0
        /// requires. Chest, upper chest, and neck are optional in VRM 1.0, so their share of a rotation
        /// goes to the nearest bone the model has.
        /// </summary>
        public static IReadOnlyList<AvatarBone> Required { get; } = new[]
        {
            AvatarBone.Hips,
            AvatarBone.Spine,
            AvatarBone.Head,
            AvatarBone.LeftUpperArm,
            AvatarBone.RightUpperArm,
            AvatarBone.LeftLowerArm,
            AvatarBone.RightLowerArm,
            AvatarBone.LeftHand,
            AvatarBone.RightHand,
        };

        /// <summary>
        /// The bones that continue <paramref name="bone"/>'s chain, nearest first, for measuring its
        /// length: the first one a model has is its child. Empty for chain ends. Pure.
        /// </summary>
        public static AvatarBone[] ChildCandidates(AvatarBone bone)
        {
            switch (bone)
            {
                case AvatarBone.Hips:
                    return new[] { AvatarBone.Spine };
                case AvatarBone.Spine:
                    return new[] { AvatarBone.Chest, AvatarBone.UpperChest, AvatarBone.Neck, AvatarBone.Head };
                case AvatarBone.Chest:
                    return new[] { AvatarBone.UpperChest, AvatarBone.Neck, AvatarBone.Head };
                case AvatarBone.UpperChest:
                    return new[] { AvatarBone.Neck, AvatarBone.Head };
                case AvatarBone.Neck:
                    return new[] { AvatarBone.Head };
                case AvatarBone.LeftShoulder:
                    return new[] { AvatarBone.LeftUpperArm };
                case AvatarBone.RightShoulder:
                    return new[] { AvatarBone.RightUpperArm };
                case AvatarBone.LeftUpperArm:
                    return new[] { AvatarBone.LeftLowerArm };
                case AvatarBone.RightUpperArm:
                    return new[] { AvatarBone.RightLowerArm };
                case AvatarBone.LeftLowerArm:
                    return new[] { AvatarBone.LeftHand };
                case AvatarBone.RightLowerArm:
                    return new[] { AvatarBone.RightHand };
                case AvatarBone.LeftHand:
                    return new[] { AvatarBone.LeftMiddleProximal };
                case AvatarBone.RightHand:
                    return new[] { AvatarBone.RightMiddleProximal };
                case AvatarBone.LeftUpperLeg:
                    return new[] { AvatarBone.LeftLowerLeg };
                case AvatarBone.RightUpperLeg:
                    return new[] { AvatarBone.RightLowerLeg };
                case AvatarBone.LeftLowerLeg:
                    return new[] { AvatarBone.LeftFoot };
                case AvatarBone.RightLowerLeg:
                    return new[] { AvatarBone.RightFoot };
                case AvatarBone.LeftFoot:
                    return new[] { AvatarBone.LeftToes };
                case AvatarBone.RightFoot:
                    return new[] { AvatarBone.RightToes };
            }

            // Finger joints: proximal, intermediate, distal are consecutive in each finger.
            if (bone >= AvatarBone.LeftThumbProximal && bone <= AvatarBone.RightLittleDistal)
            {
                int joint = (bone - AvatarBone.LeftThumbProximal) % 3;
                return joint < 2 ? new[] { bone + 1 } : Array.Empty<AvatarBone>();
            }

            return Array.Empty<AvatarBone>();
        }

        /// <summary>The required bones <paramref name="has"/> says are missing, in <see cref="Required"/> order. Pure.</summary>
        public static IReadOnlyList<AvatarBone> MissingRequired(Func<AvatarBone, bool> has)
        {
            if (has == null)
            {
                throw new ArgumentNullException(nameof(has));
            }

            var missing = new List<AvatarBone>();
            foreach (AvatarBone bone in Required)
            {
                if (!has(bone))
                {
                    missing.Add(bone);
                }
            }

            return missing;
        }
    }
}
