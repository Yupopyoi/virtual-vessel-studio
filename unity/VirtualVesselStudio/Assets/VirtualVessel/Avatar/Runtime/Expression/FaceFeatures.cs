namespace VirtualVessel.Avatar
{
    /// <summary>
    /// The tracked face read once through an <see cref="ExpressionMapping"/>'s curves, 0 to 1 unless
    /// said otherwise, in the avatar's own sides: what the whole-face expressions, the face parts, and
    /// the Expression Signal are all made from (avatar detailed design 7.4). Reading it once keeps the
    /// three from drifting apart when a curve is tuned.
    /// </summary>
    /// <remarks>A struct, so reading a face allocates nothing (CLAUDE.md 23).</remarks>
    internal struct FaceFeatures
    {
        /// <summary>The avatar's left eye's closing, mirrored and kept together with the right unless it winks.</summary>
        public float CloseLeft { get; set; }

        public float CloseRight { get; set; }

        /// <summary>Whether both eyes close alike, rather than one winking.</summary>
        public bool Together { get; set; }

        public float Jaw { get; set; }

        /// <summary>How far the lips are apart; the rounded and wide mouths show only this far.</summary>
        public float Apart { get; set; }

        public float Funnel { get; set; }

        public float Pucker { get; set; }

        /// <summary>The lips pulled sideways.</summary>
        public float Stretch { get; set; }

        /// <summary>The corners raised, read for the "i" mouth of a whole-face expression.</summary>
        public float Smile { get; set; }

        /// <summary>The corners raised, read for a smiling mouth on a face moved part by part.</summary>
        public float Corners { get; set; }

        /// <summary>The inner brows raised.</summary>
        public float BrowUp { get; set; }

        /// <summary>The inner or outer brows raised, whichever is more.</summary>
        public float BrowRaised { get; set; }

        public float BrowDown { get; set; }

        /// <summary>The mouth's corners drawn down.</summary>
        public float Frown { get; set; }

        public float EyeWide { get; set; }

        /// <summary>The feelings, already weighed: what the whole-face expressions and the Expression Signal show.</summary>
        public float Happy { get; set; }

        public float Surprised { get; set; }

        public float Sad { get; set; }

        public float Angry { get; set; }

        /// <summary>A share of the model's eye range, -1 to 1, positive toward the avatar's own right.</summary>
        public float LookYaw { get; set; }

        /// <summary>A share of the model's eye range, -1 to 1, positive up.</summary>
        public float LookPitch { get; set; }
    }
}
