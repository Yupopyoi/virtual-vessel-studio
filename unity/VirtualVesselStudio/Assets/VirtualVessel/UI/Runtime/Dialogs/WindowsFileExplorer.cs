using System.Diagnostics;
using System.IO;

namespace VirtualVessel.UI.Dialogs
{
    /// <summary>Windows Explorer, started for the user to look at a file; the application does not own or stop it.</summary>
    public sealed class WindowsFileExplorer : IFileExplorer
    {
        public void Show(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            // Paths on Windows cannot hold quotes, so quoting is enough to pass one with spaces.
            string arguments = File.Exists(path) ? $"/select,\"{path}\"" : Directory.Exists(path) ? $"\"{path}\"" : null;
            if (arguments == null)
            {
                return;
            }

            using (Process.Start(new ProcessStartInfo("explorer.exe", arguments) { UseShellExecute = false }))
            {
            }
        }
    }
}
