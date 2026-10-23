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

        private ArmSide(bool left)
        {
            IsLeft = left;
            Shoulder = left ? UpperBodyBone.LeftShoulder : UpperBodyBone.RightShoulder;
            UpperArm = left ? UpperBodyBone.LeftUpperArm : UpperBodyBone.RightUpperArm;
            LowerArm = left ? UpperBodyBone.LeftLowerArm : UpperBodyBone.RightLowerArm;
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
