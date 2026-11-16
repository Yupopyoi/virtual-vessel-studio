using System;
using System.Collections.Generic;

namespace VirtualVessel.ExternalServices.Process
{
    /// <summary>
    /// A process to run once (external services detailed design 4, system design 4.17). Knows nothing of what it
    /// runs: the adapter that builds it decides the program, its arguments, and what its output means.
    /// </summary>
    public sealed class ExternalProcessRequest
    {
        public ExternalProcessRequest(string fileName, IReadOnlyList<string> arguments, string workingDirectory, string logPath)
        {
            FileName = fileName ?? throw new ArgumentNullException(nameof(fileName));
            Arguments = arguments ?? Array.Empty<string>();
            WorkingDirectory = workingDirectory ?? throw new ArgumentNullException(nameof(workingDirectory));
            LogPath = logPath ?? throw new ArgumentNullException(nameof(logPath));
        }

        /// <summary>The program's full path; it must lie inside one of the runner's allowed folders.</summary>
        public string FileName { get; }

        /// <summary>Each argument as the program will see it; quoting is the runner's job.</summary>
        public IReadOnlyList<string> Arguments { get; }

        public string WorkingDirectory { get; }

        /// <summary>The file the process's output and errors are appended to, apart from the application log.</summary>
        public string LogPath { get; }

        /// <summary>Variables set on top of the application's own.</summary>
        public IReadOnlyDictionary<string, string> Environment { get; set; } = new Dictionary<string, string>();

        /// <summary>Names in <see cref="Environment"/> whose values never reach a log (system design 4.30).</summary>
        public IReadOnlyCollection<string> SecretVariables { get; set; } = Array.Empty<string>();

        /// <summary>How long the process may run; null for no limit.</summary>
        public TimeSpan? Timeout { get; set; }

        /// <summary>
        /// How long a cancelled process gets after its standard input closes before its tree is stopped
        /// (system design 4.26). Zero stops it at once.
        /// </summary>
        public TimeSpan CancelGrace { get; set; } = TimeSpan.FromSeconds(5);
    }
}
