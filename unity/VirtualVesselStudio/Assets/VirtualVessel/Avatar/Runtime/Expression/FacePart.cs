using System;
using System.Collections.Generic;

namespace VirtualVessel.Avatar
{
    /// <summary>
    /// Parts of the face moved one by one on a model that has a shape for each (avatar detailed design
    /// 7.4), instead of whole-face expressions: a smile with the mouth closed, raised brows alone, and
    /// so on. Sides are the avatar's own. There are no smiling eyes: eyes closing into a smile along
    /// with the mouth looked unnatural (user's decision, 2026-10-21).
    /// </summary>
    public enum FacePart
    {
        EyeCloseLeft,
        EyeCloseRight,

        EyeWide,
        EyeAngry,

        /// <summary>Both brows raised, as in surprise.</summary>
        BrowUp,

        /// <summary>Brows drawn down, as in anger.</summary>
        BrowDown,

        /// <summary>The inner brows raised, as in sadness.</summary>
        BrowSad,

        MouthA,
        MouthI,
        MouthU,
        MouthE,
        MouthO,

        /// <summary>The corners raised with the mouth closed.</summary>
        MouthSmile,

        /// <summary>A smile with the mouth open.</summary>
        MouthOpenSmile,

        /// <summary>The corners drawn down.</summary>
        MouthFrown,
    }

    /// <summary>
    /// Which of a model's face shapes moves each <see cref="FacePart"/>: a naming convention that
    /// model makers follow, such as VRoid's. Pure data; the shapes come from <see cref="IAvatarFaceShapes"/>.
    /// </summary>
    public sealed class FacePartProfile
    {
        public static readonly int PartCount = Enum.GetValues(typeof(FacePart)).Length;

        /// <summary>
        /// VRoid Studio's face shapes, which its VRM files keep under these names. The Voom prototype
        /// moved the same shapes part by part.
        /// </summary>
        public static readonly FacePartProfile VRoid = new FacePartProfile("VRoid", new Dictionary<FacePart, string>
        {
            [FacePart.EyeCloseLeft] = "Fcl_EYE_Close_L",
            [FacePart.EyeCloseRight] = "Fcl_EYE_Close_R",
            [FacePart.EyeWide] = "Fcl_EYE_Spread",
            [FacePart.EyeAngry] = "Fcl_EYE_Angry",
            [FacePart.BrowUp] = "Fcl_BRW_Surprised",
            [FacePart.BrowDown] = "Fcl_BRW_Angry",
            [FacePart.BrowSad] = "Fcl_BRW_Sorrow",
            [FacePart.MouthA] = "Fcl_MTH_A",
            [FacePart.MouthI] = "Fcl_MTH_I",
            [FacePart.MouthU] = "Fcl_MTH_U",
            [FacePart.MouthE] = "Fcl_MTH_E",
            [FacePart.MouthO] = "Fcl_MTH_O",
            [FacePart.MouthSmile] = "Fcl_MTH_Fun",
            [FacePart.MouthOpenSmile] = "Fcl_MTH_Joy",
            [FacePart.MouthFrown] = "Fcl_MTH_Sorrow",
        });

        // Without these a face cannot blink and speak part by part, so the model keeps its expressions.
        private static readonly FacePart[] s_essential =
        {
            FacePart.EyeCloseLeft, FacePart.EyeCloseRight,
            FacePart.MouthA, FacePart.MouthI, FacePart.MouthU, FacePart.MouthE, FacePart.MouthO,
        };

        private static readonly FacePartProfile[] s_known = { VRoid };

        private readonly IReadOnlyDictionary<FacePart, string> _names;

        public FacePartProfile(string name, IReadOnlyDictionary<FacePart, string> names)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            _names = names ?? throw new ArgumentNullException(nameof(names));
        }

        public string Name { get; }

        /// <summary>The shape name of <paramref name="part"/>, or null when the profile has none.</summary>
        public string NameOf(FacePart part)
        {
            return _names.TryGetValue(part, out string name) ? name : null;
        }

        /// <summary>
        /// The index in <paramref name="shapeNames"/> of each part's shape, -1 for a part the model
        /// lacks; null when the model lacks an essential part (both eyes' closing and the five
        /// vowels), so it keeps whole-face expressions. Names match exactly. Pure.
        /// </summary>
        public int[] Resolve(IReadOnlyList<string> shapeNames)
        {
            var indices = new int[PartCount];
            for (int i = 0; i < indices.Length; i++)
            {
                string name = NameOf((FacePart)i);
                indices[i] = -1;
                for (int j = 0; name != null && shapeNames != null && j < shapeNames.Count; j++)
                {
                    if (string.Equals(shapeNames[j], name, StringComparison.Ordinal))
                    {
                        indices[i] = j;
                        break;
                    }
                }
            }

            foreach (FacePart part in s_essential)
            {
                if (indices[(int)part] < 0)
                {
                    return null;
                }
            }

            return indices;
        }

        /// <summary>The first known profile the model's shapes fit, with its indices; null when none fits. Pure.</summary>
        public static (FacePartProfile Profile, int[] Indices)? Find(IReadOnlyList<string> shapeNames)
        {
            foreach (FacePartProfile profile in s_known)
            {
                int[] indices = profile.Resolve(shapeNames);
                if (indices != null)
                {
                    return (profile, indices);
                }
            }

            return null;
        }
    }
}
