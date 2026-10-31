using System;

namespace VirtualVessel.ProjectData.Credentials
{
    /// <summary>
    /// The ID a profile keeps instead of a secret: 32 hexadecimal digits.
    /// </summary>
    public static class CredentialId
    {
        private const int Length = 32;

        public static string New()
        {
            return Guid.NewGuid().ToString("N");
        }

        /// <summary>
        /// Profiles can be edited or come from someone else, so an ID is checked before it names a credential:
        /// it must not be able to reach a target outside this application's own prefix.
        /// </summary>
        public static bool IsValid(string credentialId)
        {
            if (credentialId == null || credentialId.Length != Length)
            {
                return false;
            }

            foreach (char c in credentialId)
            {
                if (!Uri.IsHexDigit(c))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
