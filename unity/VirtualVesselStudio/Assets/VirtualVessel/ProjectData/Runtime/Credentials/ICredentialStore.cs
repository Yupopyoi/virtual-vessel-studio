namespace VirtualVessel.ProjectData.Credentials
{
    public enum CredentialStatus
    {
        Ok,

        /// <summary>Nothing is stored under this ID.</summary>
        NotFound,

        /// <summary>The ID or the secret cannot be stored: nothing was changed.</summary>
        Invalid,

        /// <summary>The operating system refused; <see cref="CredentialResult.ErrorCode"/> says why.</summary>
        Failed,
    }

    /// <summary>
    /// How a store operation ended. It carries a system error code and never the secret, so it is safe to log.
    /// </summary>
    public readonly struct CredentialResult
    {
        public CredentialResult(CredentialStatus status, int errorCode = 0)
        {
            Status = status;
            ErrorCode = errorCode;
        }

        public CredentialStatus Status { get; }

        public int ErrorCode { get; }
    }

    /// <summary>
    /// Keeps secrets out of projects and profiles, which hold only a credential ID (system design 6.13).
    /// Implementations must not log a secret or put one in an error.
    /// </summary>
    public interface ICredentialStore
    {
        bool Exists(string credentialId);

        CredentialResult Write(string credentialId, string secret);

        CredentialResult Read(string credentialId, out string secret);

        /// <summary>Removing what is not there is <see cref="CredentialStatus.NotFound"/>, not a failure.</summary>
        CredentialResult Delete(string credentialId);
    }
}
