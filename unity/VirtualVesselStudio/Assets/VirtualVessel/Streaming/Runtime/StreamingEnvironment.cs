using System;
using UnityEngine;
using VirtualVessel.Core.State;
using VirtualVessel.Core.Time;
using VirtualVessel.Video.Recording;

namespace VirtualVessel.Streaming
{
    /// <summary>
    /// What a stream needs from outside the service. <see cref="Native"/> gives the real ones; tests give
    /// fakes, so the service's states can be checked without a GPU, the plugin, or a server.
    /// </summary>
    public sealed class StreamingEnvironment
    {
        private const int ConnectionTestTimeoutMs = 10_000;

        public StreamingEnvironment(
            Func<StreamFormat, string, IStreamPublisher> openPublisher,
            Func<StreamFormat, IStreamPublisher, IStreamFrameReader> readFrames,
            Func<string, PublisherProblem> testConnection,
            bool av1Available,
            Func<RecordingAudio> audio = null)
        {
            OpenPublisher = openPublisher ?? throw new ArgumentNullException(nameof(openPublisher));
            ReadFrames = readFrames ?? throw new ArgumentNullException(nameof(readFrames));
            TestConnection = testConnection ?? throw new ArgumentNullException(nameof(testConnection));
            Av1Available = av1Available;
            Audio = audio ?? (() => default);
        }

        /// <summary>Starts connecting and encoding, with the key; throws <see cref="StreamPublisherException"/>.</summary>
        public Func<StreamFormat, string, IStreamPublisher> OpenPublisher { get; }

        public Func<StreamFormat, IStreamPublisher, IStreamFrameReader> ReadFrames { get; }

        /// <summary>Blocks; runs on a worker thread.</summary>
        public Func<string, PublisherProblem> TestConnection { get; }

        /// <summary>Whether the GPU has an AV1 encoder, read once at startup.</summary>
        public bool Av1Available { get; }

        /// <summary>The stream bus when a stream starts; default when Audio is not running.</summary>
        public Func<RecordingAudio> Audio { get; }

        public static StreamingEnvironment Native(IReadOnlyObservable<Texture> video, IMonotonicClock clock, Func<RecordingAudio> audio = null)
        {
            return new StreamingEnvironment(
                NativeStreamPublisher.Open,
                (format, publisher) => new StreamFrameReader(video, format.Width, format.Height, format.FrameRate, publisher, new Nv12FrameReadback(format.Width, format.Height), clock),
                url => NativeStreamPublisher.TestConnection(url, ConnectionTestTimeoutMs),
                NativeEncoder.HardwareAvailable(VirtualVessel.UI.Recording.RecordingCodec.Av1),
                audio);
        }
    }
}
