using System.Collections.Generic;
using UnityEngine;
using VirtualVessel.Core.State;

namespace VirtualVessel.Capture
{
    /// <summary>
    /// A capture source that provides video (system design 10.20, capture detailed design 5). Other
    /// modules use only this and <see cref="ICaptureSources"/>; how the video is acquired stays inside
    /// the Capture module.
    /// </summary>
    /// <remarks>Use from the main thread; all notifications come on the main thread.</remarks>
    public interface IVideoCaptureSource
    {
        /// <summary>A stable id, such as "game", "subscreen", or "camera", that other modules use to refer to the source.</summary>
        string Id { get; }

        CaptureSourceKind Kind { get; }

        IReadOnlyObservable<CaptureState> State { get; }

        /// <summary>What to tell the user; <see cref="CaptureProblem.None"/> when there is nothing.</summary>
        IReadOnlyObservable<CaptureProblem> Problem { get; }

        IReadOnlyObservable<CaptureSourceInfo> Info { get; }

        /// <summary>The video, or null while not capturing. A different texture when the resolution changes.</summary>
        IReadOnlyObservable<Texture> Texture { get; }

        /// <summary>
        /// Frames that have arrived since the source was created: it changes when <see cref="Texture"/>
        /// has a new image, so a reader such as Tracking takes each image once.
        /// </summary>
        long FrameCount { get; }

        /// <summary>When the latest frame arrived, in ticks of the application's monotonic clock; 0 before the first.</summary>
        long LastFrameTimestamp { get; }

        /// <summary>How the video is mirrored; <see cref="Texture"/> already shows it so.</summary>
        IReadOnlyObservable<CaptureFlip> Flip { get; }

        void SetFlip(CaptureFlip flip);

        /// <summary>Starts without waiting; the result comes through <see cref="State"/>.</summary>
        void Start();

        void Stop();
    }

    /// <summary>A capture source with a device to choose: a capture board or a monitor.</summary>
    public interface ICaptureDeviceSource : IVideoCaptureSource
    {
        /// <summary>The devices found by the last <see cref="RefreshDevices"/>.</summary>
        IReadOnlyList<CaptureDevice> Devices { get; }

        /// <summary>Checks the devices again. Main thread; may take a few milliseconds.</summary>
        void RefreshDevices();

        /// <summary>The chosen device, or null for the default (capture detailed design 6.3).</summary>
        string SelectedDeviceId { get; }

        /// <summary>Chooses a device; a running source captures again from it.</summary>
        void SelectDevice(string deviceId);
    }

    /// <summary>A source that can draw the mouse pointer into its video (SubScreenCapture).</summary>
    public interface IPointerOption
    {
        IReadOnlyObservable<bool> DrawPointer { get; }

        /// <summary>Changes it; a source that is capturing opens its device again.</summary>
        void SetDrawPointer(bool draw);
    }

    /// <summary>The capture sources, for other modules such as Stage and Tracking.</summary>
    public interface ICaptureSources
    {
        IReadOnlyList<IVideoCaptureSource> Sources { get; }

        /// <summary>The source with <paramref name="id"/>, or null.</summary>
        IVideoCaptureSource Find(string id);
    }
}
