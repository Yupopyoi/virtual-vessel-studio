using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace VirtualVessel.ExternalServices.Process
{
    /// <summary>
    /// A Windows job holding a process and the children it starts (external services detailed design 8.1). The
    /// job is set to stop everything in it when its last handle closes, so when this object is disposed, or the
    /// application ends however it ends, nothing it started is left running (system design 4.33).
    /// </summary>
    internal sealed class JobObject : IDisposable
    {
        private const int ExtendedLimitInformation = 9;
        private const uint KillOnJobClose = 0x2000;

        private IntPtr _handle;

        public JobObject()
        {
            _handle = CreateJobObjectW(IntPtr.Zero, null);
            if (_handle == IntPtr.Zero)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateJobObject failed.");
            }

            var limits = new ExtendedLimits { Basic = new BasicLimits { LimitFlags = KillOnJobClose } };
            if (!SetInformationJobObject(_handle, ExtendedLimitInformation, ref limits, (uint)Marshal.SizeOf<ExtendedLimits>()))
            {
                int error = Marshal.GetLastWin32Error();
                CloseHandle(_handle);
                _handle = IntPtr.Zero;
                throw new Win32Exception(error, "SetInformationJobObject failed.");
            }
        }

        /// <summary>
        /// Puts a started process in the job; children it starts from then on join it too. A child started before
        /// this call is not held, so call it right after starting.
        /// </summary>
        public void Add(IntPtr process)
        {
            if (!AssignProcessToJobObject(_handle, process))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "AssignProcessToJobObject failed.");
            }
        }

        /// <summary>Stops every process in the job.</summary>
        public void Terminate(uint exitCode)
        {
            if (_handle != IntPtr.Zero)
            {
                TerminateJobObject(_handle, exitCode);
            }
        }

        public void Dispose()
        {
            if (_handle != IntPtr.Zero)
            {
                CloseHandle(_handle);
                _handle = IntPtr.Zero;
            }
        }

        // JOBOBJECT_BASIC_LIMIT_INFORMATION.
        [StructLayout(LayoutKind.Sequential)]
        private struct BasicLimits
        {
            public long PerProcessUserTimeLimit;
            public long PerJobUserTimeLimit;
            public uint LimitFlags;
            public UIntPtr MinimumWorkingSetSize;
            public UIntPtr MaximumWorkingSetSize;
            public uint ActiveProcessLimit;
            public UIntPtr Affinity;
            public uint PriorityClass;
            public uint SchedulingClass;
        }

        // IO_COUNTERS.
        [StructLayout(LayoutKind.Sequential)]
        private struct IoCounters
        {
            public ulong ReadOperationCount;
            public ulong WriteOperationCount;
            public ulong OtherOperationCount;
            public ulong ReadTransferCount;
            public ulong WriteTransferCount;
            public ulong OtherTransferCount;
        }

        // JOBOBJECT_EXTENDED_LIMIT_INFORMATION.
        [StructLayout(LayoutKind.Sequential)]
        private struct ExtendedLimits
        {
            public BasicLimits Basic;
            public IoCounters Io;
            public UIntPtr ProcessMemoryLimit;
            public UIntPtr JobMemoryLimit;
            public UIntPtr PeakProcessMemoryUsed;
            public UIntPtr PeakJobMemoryUsed;
        }

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateJobObjectW(IntPtr attributes, string name);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetInformationJobObject(IntPtr job, int infoClass, ref ExtendedLimits info, uint length);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool TerminateJobObject(IntPtr job, uint exitCode);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr handle);
    }
}
