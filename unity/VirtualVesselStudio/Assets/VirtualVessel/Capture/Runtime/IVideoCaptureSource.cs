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
        /// <summary>A stable id, such as "game" or "subscreen", that Stage uses to refer to the source.</summary>
        string Id { get; }

        CaptureSourceKind Kind { get; }

        IReadOnlyObservable<CaptureState> State { get; }

        /// <summary>What to tell the user; <see cref="CaptureProblem.None"/> when there is nothing.</summary>
        IReadOnlyObservable<CaptureProblem> Problem { get; }

        IReadOnlyObservable<CaptureSourceInfo> Info { get; }

        /// <summary>The video, or null while not capturing. A different texture when the resolution changes.</summary>
        IReadOnlyObservable<Texture> Texture { get; }

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

    /// <summary>The capture sources, for other modules such as Stage.</summary>
    public interface ICaptureSources
    {
        IReadOnlyList<IVideoCaptureSource> Sources { get; }

        /// <summary>The source with <paramref name="id"/>, or null.</summary>
        IVideoCaptureSource Find(string id);
    }
}
