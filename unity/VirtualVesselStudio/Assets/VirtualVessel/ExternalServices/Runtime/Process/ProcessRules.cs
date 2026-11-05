using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace VirtualVessel.ExternalServices.Process
{
    /// <summary>The decisions around running a process, without running one. Pure.</summary>
    public static class ProcessRules
    {
        /// <summary>
        /// One Windows command line from arguments, quoted so a program parsing it the usual way (CommandLineToArgvW,
        /// the C runtime, Python) gets each argument back unchanged. Pure.
        /// </summary>
        public static string CommandLine(IEnumerable<string> arguments)
        {
            return string.Join(" ", arguments.Select(Quote));
        }

        /// <summary>One argument, quoted when it needs to be. Pure.</summary>
        public static string Quote(string argument)
        {
            if (argument.Length > 0 && argument.IndexOfAny(new[] { ' ', '\t', '\n', '\v', '"' }) < 0)
            {
                return argument;
            }

            var quoted = new StringBuilder("\"");
            int backslashes = 0;
            foreach (char c in argument)
            {
                if (c == '\\')
                {
                    backslashes++;
                    continue;
                }

                // Backslashes count only before a quote: there they are doubled, and the quote escaped.
                quoted.Append('\\', c == '"' ? (backslashes * 2) + 1 : backslashes);
                quoted.Append(c);
                backslashes = 0;
            }

            // Before the closing quote they are doubled too, so it is not escaped.
            quoted.Append('\\', backslashes * 2);
            return quoted.Append('"').ToString();
        }

        /// <summary>
        /// Whether <paramref name="path"/> lies inside one of <paramref name="folders"/>, after resolving ".." and
        /// ignoring case, as Windows does. "C:\Tools2" is not inside "C:\Tools". Pure.
        /// </summary>
        public static bool IsInside(string path, IEnumerable<string> folders)
        {
            if (string.IsNullOrEmpty(path) || !Path.IsPathRooted(path))
            {
                return false;
            }

            string full = Path.GetFullPath(path);
            foreach (string folder in folders)
            {
                if (string.IsNullOrEmpty(folder) || !Path.IsPathRooted(folder))
                {
                    continue;
                }

                string root = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
                if (full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>How a run that started ended. Pure.</summary>
        public static ExternalProcessState Classify(int? exitCode, bool cancelled, bool timedOut)
        {
            if (timedOut)
            {
                return ExternalProcessState.Failed;
            }

            if (cancelled)
            {
                return ExternalProcessState.Cancelled;
            }

            return exitCode == 0 ? ExternalProcessState.Completed : ExternalProcessState.Failed;
        }

        /// <summary>
        /// The environment as the log shows it: secrets' values hidden (system design 4.30). Sorted, so runs
        /// compare line by line. Pure.
        /// </summary>
        public static string DescribeEnvironment(IReadOnlyDictionary<string, string> environment, IReadOnlyCollection<string> secrets)
        {
            return string.Join(", ", environment.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .Select(pair => pair.Key + "=" + (secrets.Contains(pair.Key) ? "(hidden)" : pair.Value)));
        }

        /// <summary>The last <paramref name="count"/> lines, oldest first, as a new list. Pure.</summary>
        public static IReadOnlyList<string> Tail(IReadOnlyCollection<string> lines, int count)
        {
            return lines.Skip(Math.Max(0, lines.Count - count)).ToArray();
        }
    }
}
