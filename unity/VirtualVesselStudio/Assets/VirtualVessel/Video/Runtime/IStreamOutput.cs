using UnityEngine;
using VirtualVessel.Core.State;

namespace VirtualVessel.Video
{
    /// <summary>The stream video and where flat videos go on it (video detailed design 5).</summary>
    public interface IStreamOutput
    {
        /// <summary>The Stream Render Target: what is streamed, recorded, and previewed. A new texture when the size changes.</summary>
        IReadOnlyObservable<Texture> Video { get; }

        VideoSize Size { get; }

        IStreamScreenLayer Screens { get; }
    }

    /// <summary>The layer of videos placed flat on the stream screen, above the stage and below the avatar.</summary>
    public interface IStreamScreenLayer
    {
        /// <summary>
        /// Children's local x goes from 0 (left) to 1 (right) and y from 0 (top) to 1 (bottom) over the
        /// stream screen. A smaller local z is drawn in front.
        /// </summary>
        Transform Root { get; }

        /// <summary>The Unity layer children must use, so that only the screen camera draws them.</summary>
        int Layer { get; }
    }

    public readonly struct VideoSize
    {
        public VideoSize(int width, int height)
        {
            Width = width;
            Height = height;
        }

        public int Width { get; }

        public int Height { get; }

        public float Aspect => Height > 0 ? (float)Width / Height : 1f;

        public override string ToString()
        {
            return $"{Width}×{Height}";
        }
    }
}
