using System.Threading.Tasks;

namespace VirtualVessel.Audio
{
    /// <summary>
    /// Plays an audio file to the user only, on the monitor bus and never the stream (audio detailed design 5.5):
    /// Voice Lab's candidates and previews. One file at a time; playing another replaces it.
    /// </summary>
    public interface IAudioPreview
    {
        /// <summary>Reads the file off the main thread and plays it. False when it cannot be read or played.</summary>
        Task<bool> PlayAsync(string path);

        void Stop();

        /// <summary>How far the preview is, 0 to 1; null when nothing plays.</summary>
        float? Progress { get; }
    }

    /// <summary>An engine that can play a preview on the monitor bus (vv_audio_preview_play).</summary>
    public interface IAudioPreviewEngine
    {
        /// <summary>Mono samples at <paramref name="rate"/>; the engine resamples and copies them.</summary>
        void PlayPreview(float[] samples, int rate);

        void StopPreview();

        /// <summary>Frames played and the length; both 0 when nothing plays.</summary>
        (long Played, long Length) GetPreviewPosition();
    }
}
