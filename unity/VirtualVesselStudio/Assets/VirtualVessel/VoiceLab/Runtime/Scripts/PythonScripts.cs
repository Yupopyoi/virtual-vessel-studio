using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using VirtualVessel.ExternalServices.Process;
using VirtualVessel.ProjectData.Config;

namespace VirtualVessel.VoiceLab.Scripts
{
    /// <summary>A script reported progress: its stage and how far along it is.</summary>
    public readonly struct ScriptProgress
    {
        public ScriptProgress(string script, string stage, int done, int total)
        {
            Script = script;
            Stage = stage;
            Done = done;
            Total = total;
        }

        public string Script { get; }

        public string Stage { get; }

        public int Done { get; }

        public int Total { get; }
    }

    /// <summary>A script failed; <see cref="Code"/> is its error code (Voice Lab detailed design 8.5) or "cancelled".</summary>
    public sealed class ScriptFailedException : Exception
    {
        public ScriptFailedException(string code, string message)
            : base(message)
        {
            Code = code;
        }

        public string Code { get; }
    }

    /// <summary>
    /// Runs services/voice-lab's scripts through the external process runner: writes the request file, reads the
    /// JSON Lines, and returns the result's fields (Voice Lab detailed design 8.5). The only place in Unity that
    /// knows the scripts' names and protocol.
    /// </summary>
    public sealed class PythonScripts
    {
        // The scripts do not watch standard input, so a cancel stops them after this.
        private static readonly TimeSpan s_cancelGrace = TimeSpan.FromSeconds(2);

        private readonly VoiceLabEnvironment _environment;
        private readonly IExternalProcessRunner _runner;

        public PythonScripts(VoiceLabEnvironment environment, IExternalProcessRunner runner)
        {
            _environment = environment ?? throw new ArgumentNullException(nameof(environment));
            _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        }

        public VoiceLabEnvironment Environment => _environment;

        /// <summary>
        /// Runs <paramref name="script"/>'s <paramref name="operation"/> with <paramref name="request"/>, keeping
        /// the request and the log in <paramref name="folder"/>. Progress comes on the reading thread.
        /// </summary>
        /// <exception cref="ScriptFailedException">The script reported an error, ended without a result, or was cancelled.</exception>
        public async Task<ConfigDocument> RunAsync(string script, string operation, ConfigDocument request, string folder, IProgress<ScriptProgress> progress, CancellationToken cancellationToken)
        {
            string requests = Path.Combine(folder, "requests");
            Directory.CreateDirectory(requests);
            // Numbered, so running the same operation again keeps the earlier request and log for diagnosis.
            string name = NumberedName($"{Path.GetFileNameWithoutExtension(script)}-{operation}", n => File.Exists(Path.Combine(requests, n + ".json")));
            string requestPath = Path.Combine(requests, name + ".json");
            File.WriteAllText(requestPath, request.ToJson());

            ConfigDocument result = null;
            ScriptLine error = null;
            ExternalProcessRequest process = Request(_environment, script, operation, requestPath, Path.Combine(folder, "logs", name + ".log"));
            ExternalProcessResult ended = await _runner.RunAsync(process, text =>
            {
                ScriptLine line = ScriptLine.Parse(text);
                switch (line.Kind)
                {
                    case ScriptLineKind.Progress:
                        progress?.Report(new ScriptProgress(script, line.Stage, line.Done, line.Total));
                        break;
                    case ScriptLineKind.Result:
                        result = line.Fields;
                        break;
                    case ScriptLineKind.Error:
                        error = line;
                        break;
                }
            }, cancellationToken).ConfigureAwait(false);

            return Outcome(ended, result, error, name);
        }

        /// <summary>The first of "name-1", "name-2", ... that is not taken. Pure apart from the check passed in.</summary>
        public static string NumberedName(string name, Func<string, bool> taken)
        {
            int number = 1;
            while (taken($"{name}-{number}"))
            {
                number++;
            }

            return $"{name}-{number}";
        }

        /// <summary>The process request for one script run. Pure.</summary>
        public static ExternalProcessRequest Request(VoiceLabEnvironment environment, string script, string operation, string requestPath, string logPath)
        {
            return new ExternalProcessRequest(environment.Python, new[] { "-I", Path.Combine(environment.Scripts, script), operation, requestPath }, environment.Scripts, logPath)
            {
                Environment = new Dictionary<string, string> { ["PYTHONUTF8"] = "1", ["PYTHONIOENCODING"] = "utf-8" },
                CancelGrace = s_cancelGrace,
            };
        }

        /// <summary>The result's fields, or why there are none. Pure.</summary>
        public static ConfigDocument Outcome(ExternalProcessResult ended, ConfigDocument result, ScriptLine error, string name)
        {
            if (ended.State == ExternalProcessState.Cancelled)
            {
                throw new ScriptFailedException("cancelled", $"{name} was cancelled.");
            }

            if (error != null)
            {
                throw new ScriptFailedException(error.ErrorCode ?? "failed", $"{name}: {error.ErrorMessage}");
            }

            if (ended.State != ExternalProcessState.Completed || result == null)
            {
                string why = ended.Failure ?? (ended.TimedOut ? "timed out" : $"exit code {ended.ExitCode}");
                string last = ended.LastErrorLines.Count > 0 ? " " + ended.LastErrorLines[ended.LastErrorLines.Count - 1] : string.Empty;
                throw new ScriptFailedException("failed", $"{name} ended without a result ({why}).{last}");
            }

            return result;
        }
    }
}
