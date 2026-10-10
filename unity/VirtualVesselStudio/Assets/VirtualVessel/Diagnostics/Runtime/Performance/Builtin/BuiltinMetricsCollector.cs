using System;
using System.Collections.Generic;
using Unity.Profiling;
using VirtualVessel.Diagnostics.Performance.Recording;

namespace VirtualVessel.Diagnostics.Performance.Builtin
{
    /// <summary>
    /// Frame time, GC, and memory from Unity's ProfilerRecorder.
    /// </summary>
    /// <remarks>
    /// Recorders keep their own samples, so nothing runs per frame; values are read when a snapshot is
    /// taken. Start and stop on the main thread. The owner serializes all calls.
    /// </remarks>
    internal sealed class BuiltinMetricsCollector : IDisposable
    {
        public const string Module = "Application";

        private const int RecorderCapacity = 300;

        private readonly List<Source> _sources = new List<Source>();
        private readonly List<string> _unavailable = new List<string>();

        public bool IsRunning => _sources.Count > 0;

        /// <summary>Names that could not be recorded on this platform at the last <see cref="Start"/>.</summary>
        public IReadOnlyList<string> Unavailable => _unavailable;

        public void Start()
        {
            if (IsRunning)
            {
                return;
            }

            _unavailable.Clear();
            Add("MainThreadFrameTime", "ms", ProfilerCategory.Internal, "Main Thread", 1e-6);
            Add("GcAllocatedInFrame", "KB", ProfilerCategory.Memory, "GC Allocated In Frame", 1.0 / 1024);
            Add("GcReservedMemory", "MB", ProfilerCategory.Memory, "GC Reserved Memory", 1.0 / (1024 * 1024));
            Add("SystemUsedMemory", "MB", ProfilerCategory.Memory, "System Used Memory", 1.0 / (1024 * 1024));
        }

        public void Dispose()
        {
            foreach (Source source in _sources)
            {
                source.Recorder.Dispose();
            }

            _sources.Clear();
        }

        public void AppendSnapshots(List<MetricSnapshot> target)
        {
            foreach (Source source in _sources)
            {
                ProfilerRecorder recorder = source.Recorder;
                int count = recorder.Valid ? recorder.Count : 0;
                if (count == 0)
                {
                    continue;
                }

                var values = new double[count];
                for (int i = 0; i < count; i++)
                {
                    values[i] = recorder.GetSample(i).Value * source.Scale;
                }

                SampleStatistics statistics = SampleStatistics.FromSamples(values, recorder.LastValue * source.Scale, count);
                target.Add(new MetricSnapshot(Module, source.Name, MetricKind.Gauge, source.Unit, statistics, 0, null));
            }
        }

        private void Add(string name, string unit, ProfilerCategory category, string statName, double scale)
        {
            ProfilerRecorder recorder = ProfilerRecorder.StartNew(category, statName, RecorderCapacity);
            if (!recorder.Valid)
            {
                // Not every statistic exists on every platform or build type; skip only this one.
                recorder.Dispose();
                _unavailable.Add(name);
                return;
            }

            _sources.Add(new Source(name, unit, recorder, scale));
        }

        private sealed class Source
        {
            public Source(string name, string unit, ProfilerRecorder recorder, double scale)
            {
                Name = name;
                Unit = unit;
                Recorder = recorder;
                Scale = scale;
            }

            public string Name { get; }

            public string Unit { get; }

            public ProfilerRecorder Recorder { get; }

            public double Scale { get; }
        }
    }
}
