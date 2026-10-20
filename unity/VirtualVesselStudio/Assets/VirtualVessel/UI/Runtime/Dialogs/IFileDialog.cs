using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace VirtualVessel.UI.Dialogs
{
    /// <summary>One file type in the file-picking screen, such as "VRM (*.vrm)".</summary>
    public readonly struct FileFilter
    {
        public FileFilter(string name, IReadOnlyList<string> extensions)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Extensions = extensions ?? throw new ArgumentNullException(nameof(extensions));
        }

        /// <summary>Already translated.</summary>
        public string Name { get; }

        /// <summary>With the dot, such as ".vrm".</summary>
        public IReadOnlyList<string> Extensions { get; }
    }

    public sealed class FileDialogOptions
    {
        /// <summary>Already translated; null for the system's title.</summary>
        public string Title { get; set; }

        public IReadOnlyList<FileFilter> Filters { get; set; } = Array.Empty<FileFilter>();
    }

    /// <summary>
    /// The operating system's file-picking screen (avatar detailed design 6.7). It never holds the main
    /// thread, so rendering and the stream go on while it is open.
    /// </summary>
    public interface IFileDialog
    {
        /// <summary>The picked file's full path, or null when the user cancels.</summary>
        Task<string> OpenAsync(FileDialogOptions options);
    }

    public static class FileDialogFilters
    {
        /// <summary>
        /// The Win32 filter text: pairs of "name (*.ext)" and "*.ext;*.ext2", each ending in a null
        /// character, with one more at the end. Pure.
        /// </summary>
        public static string ToWin32(IReadOnlyList<FileFilter> filters)
        {
            var text = new StringBuilder();
            foreach (FileFilter filter in filters ?? Array.Empty<FileFilter>())
            {
                var patterns = new List<string>();
                foreach (string extension in filter.Extensions)
                {
                    patterns.Add("*" + extension);
                }

                string joined = string.Join(";", patterns);
                text.Append(filter.Name).Append(" (").Append(joined).Append(')').Append('\0');
                text.Append(joined).Append('\0');
            }

            return text.Append('\0').ToString();
        }
    }
}
