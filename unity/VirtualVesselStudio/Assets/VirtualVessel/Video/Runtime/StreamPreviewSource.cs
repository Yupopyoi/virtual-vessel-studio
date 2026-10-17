using UnityEngine;
using VirtualVessel.Core.State;
using VirtualVessel.UI.Preview;

namespace VirtualVessel.Video
{
    /// <summary>The UI's stream preview: the Stream Render Target and its numbers (UI foundation detailed design 9.9).</summary>
    internal sealed class StreamPreviewSource : IStreamPreviewSource
    {
        public StreamPreviewSource(IReadOnlyObservable<Texture> video, IReadOnlyObservable<PreviewStats> stats)
        {
            Video = video;
            Stats = stats;
        }

        public IReadOnlyObservable<Texture> Video { get; }

        public IReadOnlyObservable<PreviewStats> Stats { get; }
    }
}
