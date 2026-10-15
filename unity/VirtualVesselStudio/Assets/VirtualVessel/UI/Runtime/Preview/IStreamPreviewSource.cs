using UnityEngine;
using VirtualVessel.Core.State;

namespace VirtualVessel.UI.Preview
{
    /// <summary>
    /// The stream video for the in-app preview (system design 10.6). The Video module implements it with
    /// the same render target that goes to the encoder, so what the preview shows is what is streamed.
    /// </summary>
    public interface IStreamPreviewSource
    {
        /// <summary>The stream render target, or null before the video is ready.</summary>
        IReadOnlyObservable<Texture> Video { get; }

        /// <summary>Frame rate, resolution, and bitrate, updated about once a second.</summary>
        IReadOnlyObservable<PreviewStats> Stats { get; }
    }

    /// <summary>Numbers shown on the preview (components.md 3.17).</summary>
    public readonly struct PreviewStats
    {
        public PreviewStats(double framesPerSecond, double targetFramesPerSecond, int width, int height, int? bitrateKbps)
        {
            FramesPerSecond = framesPerSecond;
            TargetFramesPerSecond = targetFramesPerSecond;
            Width = width;
            Height = height;
            BitrateKbps = bitrateKbps;
        }

        public double FramesPerSecond { get; }

        /// <summary>The frame rate the stream is set to, such as 60; falling below it shows as a warning.</summary>
        public double TargetFramesPerSecond { get; }

        public int Width { get; }

        public int Height { get; }

        /// <summary>Encoder bitrate while streaming or recording; null when nothing is being encoded.</summary>
        public int? BitrateKbps { get; }
    }
}
