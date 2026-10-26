using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace VirtualVessel.UI.Dialogs
{
    /// <summary>
    /// The Windows file-picking screen, opened on its own single-threaded apartment thread so the main
    /// thread keeps rendering while it is open (avatar detailed design 6.7). Uses GetOpenFileNameW,
    /// which shows the same Explorer-style screen as the COM dialog and needs no COM interop, which the
    /// Mono and IL2CPP runtimes support unevenly. The application window owns the screen, so it stays
    /// in front and the window does not take clicks until it closes.
    /// </summary>
    public sealed class WindowsFileDialog : IFileDialog
    {
        private const int MaxPathChars = 32768;

        private const int OfnNoChangeDir = 0x00000008;
        private const int OfnHideReadOnly = 0x00000004;
        private const int OfnPathMustExist = 0x00000800;
        private const int OfnFileMustExist = 0x00001000;
        private const int OfnExplorer = 0x00080000;
        private const int OfnDontAddToRecent = 0x02000000;

        private int _open;

        public Task<string> OpenAsync(FileDialogOptions options)
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            options ??= new FileDialogOptions();

            // One screen at a time; a second request while one is open is answered as canceled.
            if (Interlocked.Exchange(ref _open, 1) == 1)
            {
                return Task.FromResult<string>(null);
            }

            // The owner is read on the main thread, where the application window belongs.
            IntPtr owner = GetActiveWindow();
            string filter = FileDialogFilters.ToWin32(options.Filters);
            string title = options.Title;
            var result = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            var thread = new Thread(() =>
            {
                try
                {
                    result.TrySetResult(Show(owner, filter, title));
                }
                catch (Exception exception)
                {
                    result.TrySetException(exception);
                }
                finally
                {
                    Interlocked.Exchange(ref _open, 0);
                }
            })
            {
                IsBackground = true,
                Name = "FileDialog",
            };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            return result.Task;
#else
            throw new PlatformNotSupportedException("The file-picking screen is implemented for Windows only.");
#endif
        }

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        private static string Show(IntPtr owner, string filter, string title)
        {
            IntPtr file = Marshal.AllocHGlobal(MaxPathChars * sizeof(char));
            IntPtr filterText = Marshal.StringToHGlobalUni(filter);
            IntPtr titleText = title == null ? IntPtr.Zero : Marshal.StringToHGlobalUni(title);
            try
            {
                Marshal.WriteInt16(file, 0);
                var dialog = new OpenFileName
                {
                    StructSize = Marshal.SizeOf<OpenFileName>(),
                    Owner = owner,
                    Filter = filterText,
                    FilterIndex = 1,
                    File = file,
                    MaxFile = MaxPathChars,
                    Title = titleText,
                    Flags = OfnExplorer | OfnFileMustExist | OfnPathMustExist | OfnNoChangeDir | OfnHideReadOnly | OfnDontAddToRecent,
                };
                if (GetOpenFileNameW(ref dialog))
                {
                    return Marshal.PtrToStringUni(file);
                }

                int error = CommDlgExtendedError();
                if (error != 0)
                {
                    throw new InvalidOperationException($"The file-picking screen failed with CommDlgExtendedError {error:X}.");
                }

                return null;
            }
            finally
            {
                Marshal.FreeHGlobal(file);
                Marshal.FreeHGlobal(filterText);
                if (titleText != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(titleText);
                }
            }
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetActiveWindow();

        [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetOpenFileNameW(ref OpenFileName openFileName);

        [DllImport("comdlg32.dll")]
        private static extern int CommDlgExtendedError();

        // OPENFILENAMEW. Strings are passed as pointers so the filter's embedded null characters survive.
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct OpenFileName
        {
            public int StructSize;
            public IntPtr Owner;
            public IntPtr Instance;
            public IntPtr Filter;
            public IntPtr CustomFilter;
            public int MaxCustomFilter;
            public int FilterIndex;
            public IntPtr File;
            public int MaxFile;
            public IntPtr FileTitle;
            public int MaxFileTitle;
            public IntPtr InitialDirectory;
            public IntPtr Title;
            public int Flags;
            public short FileOffset;
            public short FileExtension;
            public IntPtr DefaultExtension;
            public IntPtr CustomData;
            public IntPtr Hook;
            public IntPtr TemplateName;
            public IntPtr Reserved;
            public int ReservedNumber;
            public int FlagsEx;
        }
#endif
    }
}
