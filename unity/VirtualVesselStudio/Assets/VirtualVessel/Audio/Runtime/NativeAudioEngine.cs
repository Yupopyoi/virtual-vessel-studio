using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace VirtualVessel.Audio
{
    /// <summary>A source's state as the engine reports it.</summary>
    public readonly struct AudioEngineStatus
    {
        public AudioEngineStatus(AudioSourceState state, AudioProblem problem, int lastError, float peakDb, long overruns, long underruns, long driftCorrections)
        {
            State = state;
            Problem = problem;
            LastError = lastError;
            PeakDb = peakDb;
            Overruns = overruns;
            Underruns = underruns;
            DriftCorrections = driftCorrections;
        }

        public AudioSourceState State { get; }

        public AudioProblem Problem { get; }

        /// <summary>The last failing HRESULT, or 0.</summary>
        public int LastError { get; }

        /// <summary>Since the last status.</summary>
        public float PeakDb { get; }

        public long Overruns { get; }

        public long Underruns { get; }

        public long DriftCorrections { get; }
    }

    /// <summary>
    /// The engine behind <see cref="AudioService"/>: the native one (<see cref="NativeAudioEngine"/>), or a
    /// fake in tests, so the service is checked without devices or the plugin.
    /// </summary>
    public interface IAudioEngine : IDisposable
    {
        IReadOnlyList<AudioDevice> ListInputs();

        IReadOnlyList<AudioDevice> ListOutputs();

        int AddSource();

        void RemoveSource(int source);

        void SetDevice(int source, string deviceId);

        void SetGain(int source, float gainDb, bool muted);

        void SetStreamSend(int source, float sendDb);

        void SetMonitorSend(int source, float sendDb);

        void SetMonitorDevice(string deviceId);

        void SetMonitorGain(float gainDb, bool muted);

        void SetStreamDelay(TimeSpan delay);

        AudioEngineStatus GetStatus(int source);

        AudioEngineStatus GetMonitorStatus();

        TimeSpan StreamLatency { get; }

        AudioStreamTap RecordingTap { get; }

        AudioStreamTap StreamingTap { get; }
    }

    /// <summary>The audio engine could not start (audio detailed design 12).</summary>
    public sealed class AudioEngineException : Exception
    {
        public AudioEngineException(string message, Exception innerException = null)
            : base(message, innerException)
        {
        }
    }

    /// <summary>
    /// <c>VirtualVessel.Audio.Native</c> (native/audio/include/vv_audio.h): WASAPI capture, the mix, and the
    /// stream tap, on threads of its own. Calls come from the main thread.
    /// </summary>
    public sealed class NativeAudioEngine : IAudioEngine
    {
        private const string Library = "VirtualVessel.Audio.Native";
        private const int ApiVersion = 4;
        private const int ReaderRecording = 0;
        private const int ReaderStreaming = 1;
        private const int Ok = 0;
        private const int StreamBus = 0;
        private const int MonitorBus = 1;
        private const int MaxDevices = 64;

        private IntPtr _engine;

        private NativeAudioEngine(IntPtr engine)
        {
            _engine = engine;
            vv_audio_get_stream_tap(engine, ReaderRecording, out Tap recording);
            RecordingTap = new AudioStreamTap(recording.Context, recording.Read);
            vv_audio_get_stream_tap(engine, ReaderStreaming, out Tap streaming);
            StreamingTap = new AudioStreamTap(streaming.Context, streaming.Read);
            StreamLatency = TimeSpan.FromTicks(vv_audio_stream_latency_us(engine) * 10);
        }

        public TimeSpan StreamLatency { get; }

        public AudioStreamTap RecordingTap { get; }

        public AudioStreamTap StreamingTap { get; }

        /// <exception cref="AudioEngineException">The plugin is missing or of another version, or the engine cannot start.</exception>
        public static NativeAudioEngine Create()
        {
            int version;
            try
            {
                version = vv_audio_api_version();
            }
            catch (Exception exception) when (exception is DllNotFoundException || exception is EntryPointNotFoundException)
            {
                throw new AudioEngineException($"The audio plugin {Library} is not installed; build it with tools/native/build-audio.ps1.", exception);
            }

            if (version != ApiVersion)
            {
                throw new AudioEngineException($"The audio plugin has API version {version}; version {ApiVersion} is needed.");
            }

            int result = vv_audio_create(out IntPtr engine);
            if (result != Ok || engine == IntPtr.Zero)
            {
                throw new AudioEngineException($"The audio engine did not start ({result}).");
            }

            return new NativeAudioEngine(engine);
        }

        public IReadOnlyList<AudioDevice> ListInputs()
        {
            var devices = new Device[MaxDevices];
            return vv_audio_list_inputs(devices, devices.Length, out int count) == Ok ? ToList(devices, count) : Array.Empty<AudioDevice>();
        }

        public IReadOnlyList<AudioDevice> ListOutputs()
        {
            var devices = new Device[MaxDevices];
            return vv_audio_list_outputs(devices, devices.Length, out int count) == Ok ? ToList(devices, count) : Array.Empty<AudioDevice>();
        }

        public int AddSource()
        {
            int result = vv_audio_add_source(_engine, out int source);
            if (result != Ok)
            {
                throw new AudioEngineException($"No source could be added ({result}).");
            }

            return source;
        }

        public void RemoveSource(int source)
        {
            vv_audio_remove_source(_engine, source);
        }

        public void SetDevice(int source, string deviceId)
        {
            vv_audio_set_device(_engine, source, deviceId);
        }

        public void SetGain(int source, float gainDb, bool muted)
        {
            vv_audio_set_gain(_engine, source, gainDb, muted ? 1 : 0);
        }

        public void SetStreamSend(int source, float sendDb)
        {
            vv_audio_set_send(_engine, source, StreamBus, sendDb);
        }

        public void SetMonitorSend(int source, float sendDb)
        {
            vv_audio_set_send(_engine, source, MonitorBus, sendDb);
        }

        public void SetMonitorDevice(string deviceId)
        {
            vv_audio_set_monitor_device(_engine, deviceId);
        }

        public void SetMonitorGain(float gainDb, bool muted)
        {
            vv_audio_set_monitor_gain(_engine, gainDb, muted ? 1 : 0);
        }

        public AudioEngineStatus GetMonitorStatus()
        {
            if (vv_audio_get_monitor_status(_engine, out MonitorStatus status) != Ok)
            {
                return new AudioEngineStatus(AudioSourceState.Off, AudioProblem.None, 0, float.NegativeInfinity, 0, 0, 0);
            }

            return new AudioEngineStatus(StateOf(status.State), ProblemOf(status.Problem), status.LastError, status.PeakDb, 0, status.Underruns, status.DriftCorrections);
        }

        public void SetStreamDelay(TimeSpan delay)
        {
            vv_audio_set_stream_delay(_engine, AudioSettings.ClampDelayMs(delay.TotalMilliseconds));
        }

        public AudioEngineStatus GetStatus(int source)
        {
            if (vv_audio_get_source_status(_engine, source, out Status status) != Ok)
            {
                return new AudioEngineStatus(AudioSourceState.Off, AudioProblem.None, 0, float.NegativeInfinity, 0, 0, 0);
            }

            return new AudioEngineStatus(StateOf(status.State), ProblemOf(status.Problem), status.LastError, status.PeakDb, status.Overruns, status.Underruns, status.DriftCorrections);
        }

        public void Dispose()
        {
            if (_engine != IntPtr.Zero)
            {
                vv_audio_destroy(_engine);
                _engine = IntPtr.Zero;
            }
        }

        private static IReadOnlyList<AudioDevice> ToList(Device[] devices, int count)
        {
            var list = new List<AudioDevice>(Math.Min(count, MaxDevices));
            for (int i = 0; i < count && i < MaxDevices; i++)
            {
                list.Add(new AudioDevice(devices[i].Id, devices[i].Name, devices[i].IsDefault != 0));
            }

            return list;
        }

        /// <summary>VV_AUDIO_SOURCE_*. Pure.</summary>
        public static AudioSourceState StateOf(int state)
        {
            return state switch
            {
                1 => AudioSourceState.Starting,
                2 => AudioSourceState.Running,
                3 => AudioSourceState.Unavailable,
                4 => AudioSourceState.Error,
                _ => AudioSourceState.Off,
            };
        }

        /// <summary>VV_AUDIO_PROBLEM_*. Pure.</summary>
        public static AudioProblem ProblemOf(int problem)
        {
            return problem switch
            {
                1 => AudioProblem.InUse,
                2 => AudioProblem.AccessDenied,
                3 => AudioProblem.Failed,
                _ => AudioProblem.None,
            };
        }

        // vv_audio_device.
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct Device
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string Id;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string Name;

            public int IsDefault;
        }

        // vv_audio_source_status.
        [StructLayout(LayoutKind.Sequential)]
        private struct Status
        {
            public int State;
            public int Problem;
            public int LastError;
            public float PeakDb;
            public long Overruns;
            public long Underruns;
            public long DriftCorrections;
        }

        // vv_audio_monitor_status.
        [StructLayout(LayoutKind.Sequential)]
        private struct MonitorStatus
        {
            public int State;
            public int Problem;
            public int LastError;
            public float PeakDb;
            public long Underruns;
            public long DriftCorrections;
        }

        // vv_audio_stream_tap.
        [StructLayout(LayoutKind.Sequential)]
        private struct Tap
        {
            public IntPtr Context;
            public IntPtr Read;
        }

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        private static extern int vv_audio_api_version();

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        private static extern int vv_audio_list_inputs([Out] Device[] devices, int capacity, out int count);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        private static extern int vv_audio_list_outputs([Out] Device[] devices, int capacity, out int count);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        private static extern int vv_audio_create(out IntPtr engine);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        private static extern void vv_audio_destroy(IntPtr engine);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        private static extern int vv_audio_add_source(IntPtr engine, out int source);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        private static extern int vv_audio_remove_source(IntPtr engine, int source);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        private static extern int vv_audio_set_device(IntPtr engine, int source, string deviceId);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        private static extern int vv_audio_set_gain(IntPtr engine, int source, float gainDb, int muted);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        private static extern int vv_audio_set_send(IntPtr engine, int source, int bus, float sendDb);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        private static extern int vv_audio_get_source_status(IntPtr engine, int source, out Status status);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        private static extern int vv_audio_set_monitor_device(IntPtr engine, string deviceId);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        private static extern int vv_audio_set_monitor_gain(IntPtr engine, float gainDb, int muted);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        private static extern int vv_audio_get_monitor_status(IntPtr engine, out MonitorStatus status);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        private static extern long vv_audio_stream_latency_us(IntPtr engine);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        private static extern int vv_audio_get_stream_tap(IntPtr engine, int reader, out Tap tap);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        private static extern int vv_audio_set_stream_delay(IntPtr engine, int delayMs);
    }
}
