using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using VirtualVessel.Core.Time;
using VirtualVessel.Diagnostics.Logging;
using VirtualVessel.ExternalServices.Process;
using VirtualVessel.VoiceLab.Design;
using VirtualVessel.VoiceLab.Scripts;
using VirtualVessel.VoiceLab.Training;

namespace VirtualVessel.VoiceLab.Activation
{
    /// <summary>
    /// The trial run at the end of a build (Voice Lab detailed design 8.1): one candidate from VoxCPM2, then a voice built
    /// from it with the shortest plan (a few lines, one epoch), through the same designer real use goes through. It takes
    /// a few minutes and proves the GPU, the packages, RVC, and the template work together. Its files go when it is done.
    /// </summary>
    public static class VoiceActivationCheck
    {
        /// <param name="folder">Where the trial's candidate and run go; it is deleted afterwards.</param>
        /// <param name="runner">Runs Python; it must allow the environment's folder.</param>
        public static Func<VoiceLabEnvironment, CancellationToken, Task<string>> Create(string folder, IExternalProcessRunner runner, ISystemClock clock, ILog log)
        {
            return async (environment, cancellationToken) =>
            {
                try
                {
                    var runs = new TrainingRunStore(folder, clock);
                    var designer = new VoiceDesigner(new PythonScripts(environment, runner), runs, Path.Combine(folder, "Designs"), clock, log);
                    IReadOnlyList<VoiceCandidate> candidates = await designer.MakeCandidatesAsync(new VoiceDescription(null, null), 1, 1, null, null, cancellationToken).ConfigureAwait(false);
                    if (candidates.Count == 0)
                    {
                        return "VoxCPM2 made no candidate.";
                    }

                    TrainingRun run = await designer.BuildVoiceAsync(candidates[0], new TimbreSettings(0f, 0f, 0f, 0f), BuildPlan.EditorTest, null, cancellationToken).ConfigureAwait(false);
                    cancellationToken.ThrowIfCancellationRequested();
                    return run.GetState() == TrainingState.Completed ? null : $"The trial voice ended {run.GetState()}: {run.Error}";
                }
                catch (ScriptFailedException exception)
                {
                    return exception.Message;
                }
                finally
                {
                    try
                    {
                        if (Directory.Exists(folder))
                        {
                            Directory.Delete(folder, true);
                        }
                    }
                    catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
                    {
                        log?.Warning($"Could not delete the trial run in {folder}: {exception.Message}");
                    }
                }
            };
        }
    }
}
