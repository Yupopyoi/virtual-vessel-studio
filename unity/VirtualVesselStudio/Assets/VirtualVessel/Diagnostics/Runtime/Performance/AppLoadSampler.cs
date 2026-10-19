using System;
using System.Diagnostics;
using UnityEngine;

namespace VirtualVessel.Diagnostics.Performance
{
    /// <summary>How busy the application is: frames per second, and its own share of the CPU and GPU.</summary>
    public readonly struct AppLoad
    {
        public AppLoad(double framesPerSecond, double cpuPercent, double gpuPercent)
        {
            FramesPerSecond = framesPerSecond;
            CpuPercent = cpuPercent;
            GpuPercent = gpuPercent;
        }

        public double FramesPerSecond { get; }

        /// <summary>This process's CPU time over the interval, as a share of all cores, 0–100.</summary>
        public double CpuPercent { get; }

        /// <summary>The share of each frame the GPU spent on this application's work, 0–100; NaN when unknown.</summary>
        public double GpuPercent { get; }
    }

    /// <summary>
    /// Samples <see cref="AppLoad"/> for display, such as on the tracking setup Card. Call about once a
    /// second on the main thread. The GPU share comes from Unity's FrameTimingManager (Frame Timing
    /// Stats in the player settings) and is this application's share, not the whole system's, which
    /// Unity cannot read; it is NaN where frame timing is not supported.
    /// </summary>
    public sealed class AppLoadSampler : IDisposable
    {
        private readonly Process _process = Process.GetCurrentProcess();
        private readonly FrameTiming[] _timings = new FrameTiming[1];
        private double _lastTime = -1;
        private int _lastFrame;
        private TimeSpan _lastCpu;

        /// <summary>The load since the last call; zeros on the first call.</summary>
        public AppLoad Sample()
        {
            double now = Time.realtimeSinceStartupAsDouble;
            int frame = Time.frameCount;
            _process.Refresh();
            TimeSpan cpu = _process.TotalProcessorTime;
            FrameTimingManager.CaptureFrameTimings();
            double gpuMilliseconds = FrameTimingManager.GetLatestTimings(1, _timings) > 0 ? _timings[0].gpuFrameTime : double.NaN;

            AppLoad load = _lastTime < 0
                ? default
                : Compute(now - _lastTime, frame - _lastFrame, (cpu - _lastCpu).TotalSeconds, Environment.ProcessorCount, gpuMilliseconds);
            _lastTime = now;
            _lastFrame = frame;
            _lastCpu = cpu;
            return load;
        }

        /// <summary>
        /// The load over <paramref name="seconds"/> in which <paramref name="frames"/> frames ran and the
        /// process used <paramref name="cpuSeconds"/> of CPU time on <paramref name="cores"/> cores; the GPU
        /// share is the GPU time of a frame over the frame's length. Pure.
        /// </summary>
        public static AppLoad Compute(double seconds, int frames, double cpuSeconds, int cores, double gpuFrameMilliseconds)
        {
            if (seconds <= 0)
            {
                return new AppLoad(0, 0, double.NaN);
            }

            double fps = frames / seconds;
            double cpu = Clamp(cpuSeconds / (seconds * Math.Max(1, cores)) * 100.0);
            double gpu = double.IsNaN(gpuFrameMilliseconds) || gpuFrameMilliseconds <= 0 || fps <= 0
                ? double.NaN
                : Clamp(gpuFrameMilliseconds / (1000.0 / fps) * 100.0);
            return new AppLoad(fps, cpu, gpu);
        }

        public void Dispose()
        {
            _process.Dispose();
        }

        private static double Clamp(double percent)
        {
            return Math.Max(0, Math.Min(100, percent));
        }
    }
}
