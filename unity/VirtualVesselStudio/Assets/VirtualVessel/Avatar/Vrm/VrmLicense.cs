using System.Collections.Generic;
using UniGLTF.Extensions.VRMC_vrm;
using UniVRM10.Migration;

namespace VirtualVessel.Avatar.Vrm
{
    /// <summary>
    /// Converts VRM meta to the format-free <see cref="AvatarLicense"/> (avatar detailed design 6.4, 6.8).
    /// Pure. Kept apart from loading so UniVRM's own 0.x conversion, which replaces the license URL with
    /// the VRM 1.0 license, does not decide what the user is shown.
    /// </summary>
    internal static class VrmLicense
    {
        public static AvatarLicense From(Meta meta)
        {
            if (meta == null)
            {
                return new AvatarLicense();
            }

            return new AvatarLicense
            {
                Authors = meta.Authors == null ? new List<string>() : new List<string>(meta.Authors),
                ModelVersion = meta.Version,
                CopyrightInformation = meta.CopyrightInformation,
                ContactInformation = meta.ContactInformation,
                References = meta.References == null ? new List<string>() : new List<string>(meta.References),
                ThirdPartyLicenses = meta.ThirdPartyLicenses,
                LicenseUrl = meta.LicenseUrl,
                OtherLicenseUrl = meta.OtherLicenseUrl,
                AvatarPermission = meta.AvatarPermission switch
                {
                    AvatarPermissionType.everyone => AvatarPermission.Everyone,
                    AvatarPermissionType.onlySeparatelyLicensedPerson => AvatarPermission.OnlySeparatelyLicensedPerson,
                    _ => AvatarPermission.OnlyAuthor,
                },
                CommercialUsage = meta.CommercialUsage switch
                {
                    CommercialUsageType.corporation => CommercialUsage.Corporation,
                    CommercialUsageType.personalProfit => CommercialUsage.PersonalProfit,
                    _ => CommercialUsage.PersonalNonProfit,
                },

                // VRM 1.0 defines a missing flag as false (not allowed).
                AllowExcessivelyViolentUsage = meta.AllowExcessivelyViolentUsage ?? false,
                AllowExcessivelySexualUsage = meta.AllowExcessivelySexualUsage ?? false,
                AllowPoliticalOrReligiousUsage = meta.AllowPoliticalOrReligiousUsage ?? false,
                AllowAntisocialOrHateUsage = meta.AllowAntisocialOrHateUsage ?? false,
                CreditRequired = meta.CreditNotation != CreditNotationType.unnecessary,
                AllowRedistribution = meta.AllowRedistribution ?? false,
                Modification = meta.Modification switch
                {
                    ModificationType.allowModificationRedistribution => ModificationPermission.AllowModificationRedistribution,
                    ModificationType.allowModification => ModificationPermission.AllowModification,
                    _ => ModificationPermission.Prohibited,
                },
            };
        }

        /// <summary>
        /// VRM 0.x fields mapped to their VRM 1.0 counterparts; conditions 0.x has no field for stay
        /// null (not stated), and the 0.x license name and permission URL are kept as written.
        /// </summary>
        public static AvatarLicense From(Vrm0Meta meta)
        {
            if (meta == null)
            {
                return new AvatarLicense();
            }

            return new AvatarLicense
            {
                Authors = string.IsNullOrEmpty(meta.author) ? new List<string>() : new List<string> { meta.author },
                ModelVersion = meta.version,
                ContactInformation = meta.contactInformation,
                References = string.IsNullOrEmpty(meta.reference) ? new List<string>() : new List<string> { meta.reference },
                OtherLicenseUrl = meta.otherLicenseUrl,
                AvatarPermission = meta.allowedUser switch
                {
                    AllowedUser.Everyone => AvatarPermission.Everyone,
                    AllowedUser.ExplicitlyLicensedPerson => AvatarPermission.OnlySeparatelyLicensedPerson,
                    _ => AvatarPermission.OnlyAuthor,
                },

                // 0.x allows or disallows commercial use without saying by whom; UniVRM's conversion
                // reads "allow" as personal profit, and so does this.
                CommercialUsage = meta.commercialUsage ? CommercialUsage.PersonalProfit : CommercialUsage.PersonalNonProfit,
                AllowExcessivelyViolentUsage = meta.violentUsage,
                AllowExcessivelySexualUsage = meta.sexualUsage,
                LegacyLicenseName = meta.licenseType.ToString(),
                LegacyOtherPermissionUrl = meta.otherPermissionUrl,
            };
        }
    }
}
