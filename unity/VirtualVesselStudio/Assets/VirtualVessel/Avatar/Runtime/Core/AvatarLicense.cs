using System.Collections.Generic;

namespace VirtualVessel.Avatar
{
    /// <summary>Who may perform as the avatar.</summary>
    public enum AvatarPermission
    {
        OnlyAuthor,
        OnlySeparatelyLicensedPerson,
        Everyone,
    }

    public enum CommercialUsage
    {
        PersonalNonProfit,
        PersonalProfit,
        Corporation,
    }

    public enum ModificationPermission
    {
        Prohibited,
        AllowModification,
        AllowModificationRedistribution,
    }

    /// <summary>
    /// The conditions the model's author set for using it, independent of the model format (avatar
    /// detailed design 6.4), as written in avatar-profile.json. Null means the file does not say, as
    /// for conditions VRM 0.x has no field for (6.8).
    /// </summary>
    public sealed class AvatarLicense
    {
        public List<string> Authors { get; set; } = new List<string>();

        public string ModelVersion { get; set; }

        public string CopyrightInformation { get; set; }

        public string ContactInformation { get; set; }

        public List<string> References { get; set; } = new List<string>();

        public string ThirdPartyLicenses { get; set; }

        public string LicenseUrl { get; set; }

        public string OtherLicenseUrl { get; set; }

        public AvatarPermission? AvatarPermission { get; set; }

        public CommercialUsage? CommercialUsage { get; set; }

        public bool? AllowExcessivelyViolentUsage { get; set; }

        public bool? AllowExcessivelySexualUsage { get; set; }

        public bool? AllowPoliticalOrReligiousUsage { get; set; }

        public bool? AllowAntisocialOrHateUsage { get; set; }

        public bool? CreditRequired { get; set; }

        public bool? AllowRedistribution { get; set; }

        public ModificationPermission? Modification { get; set; }

        /// <summary>The VRM 0.x license name (such as CC_BY), which has no VRM 1.0 field; null for 1.0.</summary>
        public string LegacyLicenseName { get; set; }

        /// <summary>The VRM 0.x URL of other permissions, which has no VRM 1.0 field; null for 1.0.</summary>
        public string LegacyOtherPermissionUrl { get; set; }

        /// <summary>Whether to show a notice before registering: using it as an avatar is not open to everyone (6.4). Pure.</summary>
        public bool NeedsNotice => AvatarPermission != Avatar.AvatarPermission.Everyone;

        public AvatarLicense Clone()
        {
            var copy = (AvatarLicense)MemberwiseClone();
            copy.Authors = Authors == null ? new List<string>() : new List<string>(Authors);
            copy.References = References == null ? new List<string>() : new List<string>(References);
            return copy;
        }
    }
}
