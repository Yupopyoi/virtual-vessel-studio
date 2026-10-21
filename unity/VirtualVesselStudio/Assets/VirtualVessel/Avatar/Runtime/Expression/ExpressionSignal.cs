namespace VirtualVessel.Avatar
{
    /// <summary>
    /// The values of the Expression Signal (system design 7.7, 7.8; avatar detailed design 7.4): what
    /// the face shows, 0 to 1 each, independent of the model and of how its face is moved, for other
    /// control such as Extension Motion to react to. Sides are the avatar's own.
    /// </summary>
    public enum ExpressionSignal
    {
        Smile,
        Angry,
        Sad,
        Surprised,

        /// <summary>1 when the eye is fully open.</summary>
        EyeOpenLeft,

        /// <summary>1 when the eye is fully open.</summary>
        EyeOpenRight,

        /// <summary>How far the jaw is open.</summary>
        MouthOpen,
    }
}
