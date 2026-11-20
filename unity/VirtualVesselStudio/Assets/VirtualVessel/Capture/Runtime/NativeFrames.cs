using System;
using UnityEngine;
using VirtualVessel.Capture.SubScreen;
using VirtualVessel.Diagnostics.Logging;
using VirtualVessel.Diagnostics.Performance;

namespace VirtualVessel.Capture
{
    /// <summary>What a native capture session reported in one frame (<see cref="NativeFrames.Tick"/>).</summary>
    internal enum NativeTick
    {
        /// <summary>Still opening the monitor or the window.</summary>
        Starting,

        /// <summary>Running, with nothing new: a still screen or window sends no frames, which is not a failure.</summary>
        Running,

        /// <summary>A new frame was uploaded to <see cref="NativeFrames.Texture"/>.</summary>
        NewFrame,

        /// <summary>The monitor was disconnected or the window closed; the session has stopped.</summary>
        Lost,

        /// <summary>The session gave up; the session has stopped.</summary>
        Failed,
    }

    /// <summary>
    /// A native capture session's frames on the main thread: reads its status, uploads a new frame to a texture
    /// only when there is one, and counts what the native side reports (capture detailed design 8.2, 8.6). Shared
    /// by the sub monitor and a game's window, which the same plugin captures the same way.
    /// </summary>
    internal sealed class NativeFrames : IDisposable
    {
        private readonly string _sourceId;
        private readonly string _textureName;
        private readonly ILog _log;
        private readonly PerfTimer _upload;
        private readonly PerfCounter _skipped;
        private readonly PerfCounter _recovered;
        private IDuplicationSession _session;
        private string _target;
        private DuplicationStatus _status;
        private ulong _uploaded;
        private FrameRateMeter _rate;

        public NativeFrames(string sourceId, string textureName, IPerformanceMetrics metrics, ILog log)
        {
            _sourceId = sourceId;
            _textureName = textureName;
            _log = log;
            _upload = metrics?.Timer("Capture", sourceId + ".Upload");
            _skipped = metrics?.Counter("Capture", sourceId + ".Skipped");
            _recovered = metrics?.Counter("Capture", sourceId + ".Recovered");
        }

        /// <summary>The newest frame, or null before the first; a new texture when the size changes.</summary>
        public Texture2D Texture { get; private set; }

        /// <summary>The frame rate that actually arrives.</summary>
        public double Rate { get; private set; }

        /// <summary>The last failure's HRESULT, for the developer log.</summary>
        public int LastError => _status.LastError;

        public bool IsOpen => _session != null;

        /// <summary>Takes over a session just opened; <paramref name="target"/> names what it captures in logs.</summary>
        public void Begin(IDuplicationSession session, string target)
        {
            Close();
            _session = session ?? throw new ArgumentNullException(nameof(session));
            _target = target;
            _status = default;
            _uploaded = 0;
            _rate = default;
            Rate = 0;
        }

        /// <summary>Per-frame work on the main thread; <paramref name="now"/> in ticks of <paramref name="frequency"/>.</summary>
        public NativeTick Tick(long now, long frequency)
        {
            if (_session == null)
            {
                return NativeTick.Starting;
            }

            DuplicationStatus status = _session.GetStatus();
            Note(_status, status);
            _status = status;
            switch (status.State)
            {
                case DuplicationState.Starting:
                    return NativeTick.Starting;
                case DuplicationState.MonitorLost:
                    return NativeTick.Lost;
                case DuplicationState.Failed:
                    return NativeTick.Failed;
            }

            // Running, or recovering with the last image kept.
            if (status.FrameNumber == _uploaded || !_session.TryTakeFrame(_uploaded, out DuplicationFrame frame))
            {
                Rate = _rate.RateAt(now, frequency);
                return NativeTick.Running;
            }

            Upload(frame);
            _uploaded = frame.Number;
            _rate = _rate.Next(now, frequency);
            Rate = _rate.Rate;
            return NativeTick.NewFrame;
        }

        /// <summary>Closes the session (waiting for its thread) and releases the texture. Safe when nothing is open.</summary>
        public void Close()
        {
            try
            {
                _session?.Dispose();
            }
            finally
            {
                _session = null;
                DestroyTexture(Texture);
                Texture = null;
            }
        }

        public void Dispose()
        {
            Close();
        }

        private void Upload(DuplicationFrame frame)
        {
            if (frame.Data == IntPtr.Zero || frame.Width <= 0 || frame.Height <= 0)
            {
                throw new InvalidOperationException($"The capture gave an empty frame ({frame.Width}×{frame.Height}).");
            }

            if (Texture == null || Texture.width != frame.Width || Texture.height != frame.Height)
            {
                // A different texture for a different size (capture detailed design 5). Desktop and window images
                // are sRGB, as Unity expects of color textures.
                Texture2D previous = Texture;
                Texture = new Texture2D(frame.Width, frame.Height, TextureFormat.BGRA32, mipChain: false, linear: false)
                {
                    name = _textureName,
                    wrapMode = TextureWrapMode.Clamp,
                };
                DestroyTexture(previous);
            }

            // The frame is B8G8R8A8, bottom row first like Unity's textures, so the bytes are uploaded as they are.
            long token = _upload?.Begin() ?? 0;
            Texture.LoadRawTextureData(frame.Data, frame.ByteCount);
            Texture.Apply(updateMipmaps: false);
            _upload?.End(token);
        }

        /// <summary>Counts what the native side reports and logs its state changes, without logging every frame.</summary>
        private void Note(DuplicationStatus previous, DuplicationStatus current)
        {
            if (current.Overwritten > previous.Overwritten)
            {
                _skipped?.Add((long)(current.Overwritten - previous.Overwritten));
            }

            if (current.Recoveries > previous.Recoveries)
            {
                _recovered?.Add(current.Recoveries - previous.Recoveries);
            }

            if (current.State != previous.State || current.LastError != previous.LastError)
            {
                string error = current.LastError != 0 ? $" (last HRESULT 0x{current.LastError:X8})" : string.Empty;
                _log?.Information($"Capture source '{_sourceId}': the capture of '{_target}' is {current.State}{error}.");
            }
        }

        private static void DestroyTexture(Texture2D texture)
        {
            if (texture == null)
            {
                return;
            }

            if (UnityEngine.Application.isPlaying)
            {
                UnityEngine.Object.Destroy(texture);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }
    }
}
