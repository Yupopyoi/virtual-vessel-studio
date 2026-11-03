using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using VirtualVessel.Core.Time;
using VirtualVessel.Diagnostics.Logging;
using SystemProcess = System.Diagnostics.Process;

namespace VirtualVessel.ExternalServices.Process
{
    /// <summary>
    /// <see cref="IExternalProcessRunner"/> with a Windows job per run (external services detailed design 8.1).
    /// Each run waits on a background thread; standard output and error each have a reading thread.
    /// </summary>
    public sealed class ExternalProcessRunner : IExternalProcessRunner
    {
        private const int ErrorLinesKept = 20;
        private const int ExitPollMilliseconds = 100;

        // Reading threads end when the pipes close, which stopping the job ensures; this only bounds a stuck read.
        private const int ReaderJoinMilliseconds = 5000;

        // The exit code of a tree this runner stopped: STATUS_CONTROL_C_EXIT, as for a console program stopped by Ctrl+C.
        private const uint StoppedExitCode = 0xC000013A;

        private static readonly Encoding s_utf8 = new UTF8Encoding(false);

        private readonly IReadOnlyList<string> _allowedFolders;
        private readonly ILog _log;
        private readonly ISystemClock _clock;

        /// <param name="allowedFolders">Programs run only from inside these folders (system design 4.29).</param>
        public ExternalProcessRunner(IReadOnlyList<string> allowedFolders, ILog log, ISystemClock clock)
        {
            _allowedFolders = allowedFolders ?? throw new ArgumentNullException(nameof(allowedFolders));
            _log = log ?? throw new ArgumentNullException(nameof(log));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        }

        public Task<ExternalProcessResult> RunAsync(ExternalProcessRequest request, Action<string> onOutputLine, CancellationToken cancellationToken)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            string refusal = Refusal(request, _allowedFolders, File.Exists, Directory.Exists);
            if (refusal != null)
            {
                _log.Warning($"External process refused: {refusal}");
                return Task.FromResult(ExternalProcessResult.NotStarted(refusal, _clock.UtcNow));
            }

            return Task.Run(() => Run(request, onOutputLine, cancellationToken));
        }

        /// <summary>Why a request must not start, or null. Pure apart from the file checks passed in.</summary>
        public static string Refusal(ExternalProcessRequest request, IReadOnlyList<string> allowedFolders, Func<string, bool> fileExists, Func<string, bool> folderExists)
        {
            if (!ProcessRules.IsInside(request.FileName, allowedFolders))
            {
                return $"{request.FileName} is outside the folders programs may run from.";
            }

            if (!fileExists(request.FileName))
            {
                return $"{request.FileName} does not exist.";
            }

            if (!folderExists(request.WorkingDirectory))
            {
                return $"The working folder {request.WorkingDirectory} does not exist.";
            }

            return null;
        }

        private ExternalProcessResult Run(ExternalProcessRequest request, Action<string> onOutputLine, CancellationToken cancellationToken)
        {
            DateTimeOffset started = _clock.UtcNow;
            string folder = Path.GetDirectoryName(Path.GetFullPath(request.LogPath));
            Directory.CreateDirectory(folder);
            var gate = new object();
            using var file = new StreamWriter(request.LogPath, true, s_utf8) { AutoFlush = true };
            using var job = new JobObject();

            var info = new ProcessStartInfo(request.FileName, ProcessRules.CommandLine(request.Arguments))
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = request.WorkingDirectory,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = s_utf8,
                StandardErrorEncoding = s_utf8,
            };
            foreach (KeyValuePair<string, string> variable in request.Environment)
            {
                info.Environment[variable.Key] = variable.Value;
            }

            string environment = ProcessRules.DescribeEnvironment(request.Environment, request.SecretVariables);
            SystemProcess process;
            try
            {
                process = SystemProcess.Start(info);
                // Right after starting, before it has had time to start children of its own.
                job.Add(process.Handle);
            }
            catch (Exception exception) when (exception is Win32Exception || exception is InvalidOperationException)
            {
                _log.Error($"External process {request.FileName} could not start.", exception);
                return ExternalProcessResult.NotStarted(exception.Message, started);
            }

            using (process)
            {
                int id = process.Id;
                _log.Information($"External process {id} started: {request.FileName}", new[]
                {
                    new LogProperty("arguments", info.Arguments),
                    new LogProperty("workingDirectory", request.WorkingDirectory),
                    new LogProperty("environment", environment),
                });
                lock (gate)
                {
                    file.WriteLine($"[{started:O}] start {request.FileName} {info.Arguments}");
                    file.WriteLine($"[{started:O}] environment {environment}");
                }

                var errors = new Queue<string>();
                Thread output = Reader(process.StandardOutput, line =>
                {
                    lock (gate)
                    {
                        file.WriteLine(line);
                    }

                    Deliver(onOutputLine, line, id);
                });
                Thread error = Reader(process.StandardError, line =>
                {
                    lock (gate)
                    {
                        file.WriteLine(line);
                        errors.Enqueue(line);
                        if (errors.Count > ErrorLinesKept)
                        {
                            errors.Dequeue();
                        }
                    }
                });

                (bool cancelled, bool timedOut, bool stopped) = Wait(process, job, request, cancellationToken);

                // Anything the process left behind is in its job and still ours: stop it, so the pipes it may
                // hold close and the readers finish.
                job.Terminate(StoppedExitCode);
                output.Join(ReaderJoinMilliseconds);
                error.Join(ReaderJoinMilliseconds);

                int? exitCode = stopped ? (int?)null : process.ExitCode;
                ExternalProcessState state = ProcessRules.Classify(exitCode, cancelled, timedOut);
                DateTimeOffset ended = _clock.UtcNow;
                string[] lastErrors;
                lock (gate)
                {
                    lastErrors = errors.ToArray();
                    file.WriteLine($"[{ended:O}] end {state}, exit code {(exitCode.HasValue ? exitCode.Value.ToString() : "none (stopped)")}{(timedOut ? ", timed out" : string.Empty)}");
                }

                LogLevel level = state == ExternalProcessState.Failed ? LogLevel.Warning : LogLevel.Information;
                _log.Write(level, $"External process {id} ended: {state}", new[]
                {
                    new LogProperty("exitCode", exitCode),
                    new LogProperty("timedOut", timedOut),
                    new LogProperty("seconds", Math.Round((ended - started).TotalSeconds, 1)),
                });
                return new ExternalProcessResult(state, exitCode, timedOut, null, started, ended, lastErrors);
            }
        }

        // Waits for the exit, a cancellation, or the time limit. A stop request closes standard input first and
        // gives the process its grace period before the tree is stopped (system design 4.14, 4.26).
        private static (bool Cancelled, bool TimedOut, bool Stopped) Wait(SystemProcess process, JobObject job, ExternalProcessRequest request, CancellationToken cancellationToken)
        {
            var clock = Stopwatch.StartNew();
            while (!process.WaitForExit(ExitPollMilliseconds))
            {
                bool timedOut = request.Timeout.HasValue && clock.Elapsed >= request.Timeout.Value;
                if (!timedOut && !cancellationToken.IsCancellationRequested)
                {
                    continue;
                }

                try
                {
                    process.StandardInput.Close();
                }
                catch (IOException)
                {
                    // The process closed its end first; stopping it below still applies.
                }

                bool stopped = !process.WaitForExit((int)Math.Max(0, request.CancelGrace.TotalMilliseconds));
                if (stopped)
                {
                    job.Terminate(StoppedExitCode);
                    process.WaitForExit();
                }

                return (!timedOut, timedOut, stopped);
            }

            return (false, false, false);
        }

        private static Thread Reader(StreamReader reader, Action<string> onLine)
        {
            var thread = new Thread(() =>
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    onLine(line);
                }
            })
            {
                IsBackground = true,
                Name = "External process output",
            };
            thread.Start();
            return thread;
        }

        private void Deliver(Action<string> onOutputLine, string line, int id)
        {
            try
            {
                onOutputLine?.Invoke(line);
            }
            catch (Exception exception)
            {
                // The pipe must keep being read, or the process blocks on a full buffer: report and go on.
                _log.Error($"The output handler of external process {id} failed.", exception);
            }
        }
    }
}
