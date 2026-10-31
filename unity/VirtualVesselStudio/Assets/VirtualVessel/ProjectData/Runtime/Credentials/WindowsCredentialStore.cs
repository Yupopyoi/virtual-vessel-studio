using System;
using System.Runtime.InteropServices;
using System.Text;

namespace VirtualVessel.ProjectData.Credentials
{
    /// <summary>
    /// Windows Credential Manager, as generic credentials of the current user that stay on this PC
    /// (streaming detailed design 11). The user can see and remove them in Windows' own screen.
    /// </summary>
    public sealed class WindowsCredentialStore : ICredentialStore
    {
        private const string TargetPrefix = "VirtualVesselStudio/stream/";

        // CRED_MAX_CREDENTIAL_BLOB_SIZE.
        internal const int MaxSecretBytes = 2560;

        private const int CredTypeGeneric = 1;
        private const int CredPersistLocalMachine = 2;
        private const int ErrorNotFound = 1168;

        internal static string TargetName(string credentialId)
        {
            return TargetPrefix + credentialId.ToLowerInvariant();
        }

        internal static bool CanStore(string credentialId, byte[] secret)
        {
            return CredentialId.IsValid(credentialId) && secret.Length > 0 && secret.Length <= MaxSecretBytes;
        }

        public bool Exists(string credentialId)
        {
            if (!CredentialId.IsValid(credentialId) || !CredRead(TargetName(credentialId), CredTypeGeneric, 0, out IntPtr credential))
            {
                return false;
            }

            CredFree(credential);
            return true;
        }

        public CredentialResult Write(string credentialId, string secret)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(secret ?? string.Empty);
            IntPtr blob = IntPtr.Zero;
            try
            {
                if (!CanStore(credentialId, bytes))
                {
                    return new CredentialResult(CredentialStatus.Invalid);
                }

                blob = Marshal.AllocHGlobal(bytes.Length);
                Marshal.Copy(bytes, 0, blob, bytes.Length);
                var credential = new NativeCredential
                {
                    Type = CredTypeGeneric,
                    TargetName = TargetName(credentialId),
                    CredentialBlobSize = bytes.Length,
                    CredentialBlob = blob,
                    Persist = CredPersistLocalMachine,
                };

                return CredWrite(ref credential, 0)
                    ? new CredentialResult(CredentialStatus.Ok)
                    : new CredentialResult(CredentialStatus.Failed, Marshal.GetLastWin32Error());
            }
            finally
            {
                // The copies of the secret this method made do not stay in memory. The string it was given is
                // the caller's and cannot be cleared.
                Array.Clear(bytes, 0, bytes.Length);
                if (blob != IntPtr.Zero)
                {
                    Marshal.Copy(bytes, 0, blob, bytes.Length);
                    Marshal.FreeHGlobal(blob);
                }
            }
        }

        public CredentialResult Read(string credentialId, out string secret)
        {
            secret = null;
            if (!CredentialId.IsValid(credentialId))
            {
                return new CredentialResult(CredentialStatus.Invalid);
            }

            if (!CredRead(TargetName(credentialId), CredTypeGeneric, 0, out IntPtr pointer))
            {
                return FromLastError();
            }

            byte[] bytes = null;
            try
            {
                NativeCredential credential = Marshal.PtrToStructure<NativeCredential>(pointer);
                bytes = new byte[credential.CredentialBlobSize];
                Marshal.Copy(credential.CredentialBlob, bytes, 0, bytes.Length);
                secret = Encoding.UTF8.GetString(bytes);
                return new CredentialResult(CredentialStatus.Ok);
            }
            finally
            {
                if (bytes != null)
                {
                    Array.Clear(bytes, 0, bytes.Length);
                }

                CredFree(pointer);
            }
        }

        public CredentialResult Delete(string credentialId)
        {
            if (!CredentialId.IsValid(credentialId))
            {
                return new CredentialResult(CredentialStatus.Invalid);
            }

            return CredDelete(TargetName(credentialId), CredTypeGeneric, 0) ? new CredentialResult(CredentialStatus.Ok) : FromLastError();
        }

        private static CredentialResult FromLastError()
        {
            int error = Marshal.GetLastWin32Error();
            return error == ErrorNotFound ? new CredentialResult(CredentialStatus.NotFound) : new CredentialResult(CredentialStatus.Failed, error);
        }

        // CREDENTIALW. LastWritten is two 32-bit fields because FILETIME is aligned to 4 bytes, not 8.
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct NativeCredential
        {
            public int Flags;
            public int Type;
            public string TargetName;
            public string Comment;
            public uint LastWrittenLow;
            public uint LastWrittenHigh;
            public int CredentialBlobSize;
            public IntPtr CredentialBlob;
            public int Persist;
            public int AttributeCount;
            public IntPtr Attributes;
            public string TargetAlias;
            public string UserName;
        }

        [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CredWrite(ref NativeCredential credential, int flags);

        [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CredRead(string targetName, int type, int flags, out IntPtr credential);

        [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CredDelete(string targetName, int type, int flags);

        [DllImport("advapi32.dll", EntryPoint = "CredFree")]
        private static extern void CredFree(IntPtr buffer);
    }
}
