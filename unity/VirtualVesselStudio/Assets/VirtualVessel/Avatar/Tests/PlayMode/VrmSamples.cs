using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using VirtualVessel.Avatar.Vrm;

namespace VirtualVessel.Avatar.Tests
{
    /// <summary>
    /// The user's VRM sample models outside the repository (avatar detailed design 17): looked for in the
    /// folder named by VV_SAMPLES_DIR, else in "samples" next to the repository.
    /// </summary>
    internal static class VrmSamples
    {
        public const double TimeoutSeconds = 60;

        /// <summary>The samples' file names, in order; "none" when there are none, so the tests are inconclusive.</summary>
        public static IEnumerable<string> Names()
        {
            string folder = Folder();
            return Directory.Exists(folder) ? Directory.GetFiles(folder, "*.vrm").Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal) : new[] { "none" };
        }

        public static string Folder()
        {
            string configured = Environment.GetEnvironmentVariable("VV_SAMPLES_DIR");
            if (!string.IsNullOrEmpty(configured))
            {
                return configured;
            }

            // The Unity project is unity/VirtualVesselStudio in the repository.
            return Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "..", "samples"));
        }

        /// <summary>Loads <paramref name="sample"/> with UniVRM over frames, timing it into <paramref name="result"/>.</summary>
        public static IEnumerator Load(string sample, SampleLoad result)
        {
            string path = Path.Combine(Folder(), sample);
            Assume.That(File.Exists(path), Is.True, $"No sample models in {Folder()}.");
            var watch = Stopwatch.StartNew();
            Task<IAvatarModel> task = new VrmAvatarLoader().LoadAsync(path, CancellationToken.None);
            double last = 0;
            while (!task.IsCompleted && watch.Elapsed.TotalSeconds < TimeoutSeconds)
            {
                yield return null;
                double now = watch.Elapsed.TotalMilliseconds;
                result.LongestFrameMilliseconds = Math.Max(result.LongestFrameMilliseconds, now - last);
                last = now;
            }

            Assert.That(task.IsCompleted, Is.True, "Loading timed out.");
            if (task.IsFaulted)
            {
                throw task.Exception.InnerException;
            }

            result.Seconds = watch.Elapsed.TotalSeconds;
            result.Model = task.Result;
        }
    }

    /// <summary>A sample loaded by <see cref="VrmSamples.Load"/>, and how long it took.</summary>
    internal sealed class SampleLoad
    {
        public IAvatarModel Model { get; set; }

        public double Seconds { get; set; }

        public double LongestFrameMilliseconds { get; set; }
    }
}
