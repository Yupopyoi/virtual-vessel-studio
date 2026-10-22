using System;

namespace VirtualVessel.Tracking
{
    /// <summary>
    /// Body joints in the person's own left and right (tracking detailed design 6.2). Independent of
    /// any tracking library's point numbering.
    /// </summary>
    public enum BodyJoint
    {
        Head,
        Nose,
        LeftEar,
        RightEar,

        /// <summary>The midpoint of the shoulders.</summary>
        Neck,
        LeftShoulder,
        RightShoulder,
        LeftElbow,
        RightElbow,
        LeftWrist,
        RightWrist,

        /// <summary>The midpoint of the hips; the origin of person space.</summary>
        Hips,
        LeftHip,
        RightHip,
        LeftKnee,
        RightKnee,
        LeftAnkle,
        RightAnkle,
        LeftFoot,
        RightFoot,
    }

    /// <summary>The 21 hand joints (tracking detailed design 6.4).</summary>
    public enum HandJoint
    {
        Wrist,
        ThumbCmc,
        ThumbMcp,
        ThumbIp,
        ThumbTip,
        IndexMcp,
        IndexPip,
        IndexDip,
        IndexTip,
        MiddleMcp,
        MiddlePip,
        MiddleDip,
        MiddleTip,
        RingMcp,
        RingPip,
        RingDip,
        RingTip,
        LittleMcp,
        LittlePip,
        LittleDip,
        LittleTip,
    }

    /// <summary>
    /// Face movement values, 0 to 1 (tracking detailed design 6.3). The 52 names that come from ARKit:
    /// a widely shared list of face movements, not the type of any library. Left and right are the
    /// person's own.
    /// </summary>
    public enum FaceShape
    {
        BrowDownLeft,
        BrowDownRight,
        BrowInnerUp,
        BrowOuterUpLeft,
        BrowOuterUpRight,
        CheekPuff,
        CheekSquintLeft,
        CheekSquintRight,
        EyeBlinkLeft,
        EyeBlinkRight,
        EyeLookDownLeft,
        EyeLookDownRight,
        EyeLookInLeft,
        EyeLookInRight,
        EyeLookOutLeft,
        EyeLookOutRight,
        EyeLookUpLeft,
        EyeLookUpRight,
        EyeSquintLeft,
        EyeSquintRight,
        EyeWideLeft,
        EyeWideRight,
        JawForward,
        JawLeft,
        JawOpen,
        JawRight,
        MouthClose,
        MouthDimpleLeft,
        MouthDimpleRight,
        MouthFrownLeft,
        MouthFrownRight,
        MouthFunnel,
        MouthLeft,
        MouthLowerDownLeft,
        MouthLowerDownRight,
        MouthPressLeft,
        MouthPressRight,
        MouthPucker,
        MouthRight,
        MouthRollLower,
        MouthRollUpper,
        MouthShrugLower,
        MouthShrugUpper,
        MouthSmileLeft,
        MouthSmileRight,
        MouthStretchLeft,
        MouthStretchRight,
        MouthUpperUpLeft,
        MouthUpperUpRight,
        NoseSneerLeft,
        NoseSneerRight,
        TongueOut,
    }

    /// <summary>Whether a part's values can be used (tracking detailed design 6.1, 7.3).</summary>
    public enum TrackingStatus
    {
        /// <summary>Lost, or not seen yet: the values are the last usable ones.</summary>
        Lost,
        Tracked,

        /// <summary>Values are there, with low confidence; Avatar uses them less strongly.</summary>
        LowConfidence,
        NotSupported,

        /// <summary>The user turned the part off.</summary>
        Disabled,
    }

    /// <summary>Parts of a person: what a provider can track, and what the user enabled.</summary>
    [Flags]
    public enum TrackingRegions
    {
        None = 0,
        Face = 1,
        Body = 2,
        LeftHand = 4,
        RightHand = 8,
        Hands = LeftHand | RightHand,
        All = Face | Body | Hands,
    }

    /// <summary>
    /// What kind of observation a provider makes, which decides the corrections that suit it (tracking
    /// detailed design 5.2, 8.4). Common processing reads these, never a provider's name, so a new input
    /// such as mocopi only says what it is.
    /// </summary>
    [Flags]
    public enum ObservationTraits
    {
        /// <summary>Nothing to correct for, as from sensors on the body (mocopi, for example).</summary>
        None = 0,

        /// <summary>
        /// Depth along the camera's axis is estimated from one camera's image (MediaPipe): least reliable
        /// for a limb pointing at the camera. The camera is in front of the person, along +Z of person space.
        /// </summary>
        MonocularDepth = 1,
    }

    /// <summary>How much of the body moves (tracking detailed design 6.5).</summary>
    public enum BodyRange
    {
        /// <summary>Head, neck, torso, shoulders, arms; fixed below the hips.</summary>
        UpperBody,

        /// <summary>Adds hips, legs, and foot contact. Shown as coming soon; not available in v0.1.0.</summary>
        FullBody,
    }

    /// <summary>The state of a tracking source or provider (tracking detailed design 7.1).</summary>
    public enum TrackingState
    {
        Stopped,
        Starting,

        /// <summary>Producing observations, also while no one is in view (parts are then Lost).</summary>
        Running,

        /// <summary>No camera is chosen or it is missing; continues by itself when it returns.</summary>
        Unavailable,
        Error,
    }

    /// <summary>A problem to tell the user (tracking detailed design 11).</summary>
    public enum TrackingProblem
    {
        None,
        CameraNotChosen,
        CameraNotFound,

        /// <summary>The same camera as the game screen.</summary>
        CameraInUse,
        StartFailed,

        /// <summary>No one is in view. A status, not an error.</summary>
        NoPerson,
    }

    /// <summary>
    /// The smoothing levels the user chooses from (tracking detailed design 8.2): 1 is the steadiest,
    /// 10 follows movement fastest. Levels 3, 5, and 7 are the presets measured first.
    /// </summary>
    public static class SmoothingLevel
    {
        public const int Min = 1;
        public const int Max = 10;

        public const int Smooth = 3;
        public const int Standard = 5;
        public const int Responsive = 7;

        /// <summary>
        /// The level for new settings. The user tried 7 and settled on the standard 5 after more
        /// camera checks (2026-10-21); the tracking screen changes it.
        /// </summary>
        public const int Default = Standard;

        /// <summary>A level within <see cref="Min"/> to <see cref="Max"/>. Pure.</summary>
        public static int Clamp(int level)
        {
            return level < Min ? Min : level > Max ? Max : level;
        }
    }
}
