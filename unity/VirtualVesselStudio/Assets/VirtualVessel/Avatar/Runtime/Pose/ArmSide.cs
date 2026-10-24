using VirtualVessel.Tracking;

namespace VirtualVessel.Avatar
{
    /// <summary>
    /// One of the avatar's arms, by its own side: its bones in <see cref="BodyTarget"/> and the rig, and the
    /// tracked joints of the same name (avatar detailed design 7.2). Mirroring is not applied here; the
    /// joints are read through <see cref="PoseRetargeting.Joint"/>, which swaps the sides in a mirror.
    /// </summary>
    public readonly struct ArmSide
    {
        public static readonly ArmSide Left = new ArmSide(true);
        public static readonly ArmSide Right = new ArmSide(false);

        /// <summary>
        /// The bones that turn with the arms, from the shoulder out: the left upper arm, forearm, and hand,
        /// then the right's. The arms' later steps keep one value per bone in this order. Not to be written.
        /// </summary>
        internal static readonly UpperBodyBone[] LimbBones =
        {
            UpperBodyBone.LeftUpperArm,
            UpperBodyBone.LeftLowerArm,
            UpperBodyBone.LeftHand,
            UpperBodyBone.RightUpperArm,
            UpperBodyBone.RightLowerArm,
            UpperBodyBone.RightHand,
        };

        /// <summary>Whether <see cref="LimbBones"/>[<paramref name="index"/>] is the left arm's.</summary>
        internal static bool IsLeftLimb(int index)
        {
            return index < LimbBones.Length / 2;
        }

        private ArmSide(bool left)
        {
            IsLeft = left;
            Shoulder = left ? UpperBodyBone.LeftShoulder : UpperBodyBone.RightShoulder;
            UpperArm = left ? UpperBodyBone.LeftUpperArm : UpperBodyBone.RightUpperArm;
            LowerArm = left ? UpperBodyBone.LeftLowerArm : UpperBodyBone.RightLowerArm;
            Hand = left ? UpperBodyBone.LeftHand : UpperBodyBone.RightHand;
            ShoulderBone = left ? AvatarBone.LeftShoulder : AvatarBone.RightShoulder;
            UpperArmBone = left ? AvatarBone.LeftUpperArm : AvatarBone.RightUpperArm;
            LowerArmBone = left ? AvatarBone.LeftLowerArm : AvatarBone.RightLowerArm;
            HandBone = left ? AvatarBone.LeftHand : AvatarBone.RightHand;
            ShoulderJoint = left ? BodyJoint.LeftShoulder : BodyJoint.RightShoulder;
            ElbowJoint = left ? BodyJoint.LeftElbow : BodyJoint.RightElbow;
            WristJoint = left ? BodyJoint.LeftWrist : BodyJoint.RightWrist;
        }

        public bool IsLeft { get; }

        public UpperBodyBone Shoulder { get; }

        public UpperBodyBone UpperArm { get; }

        public UpperBodyBone LowerArm { get; }

        public UpperBodyBone Hand { get; }

        public AvatarBone ShoulderBone { get; }

        public AvatarBone UpperArmBone { get; }

        public AvatarBone LowerArmBone { get; }

        public AvatarBone HandBone { get; }

        public BodyJoint ShoulderJoint { get; }

        public BodyJoint ElbowJoint { get; }

        public BodyJoint WristJoint { get; }

        public static ArmSide Of(bool left)
        {
            return left ? Left : Right;
        }
    }
}
