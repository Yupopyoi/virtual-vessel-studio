using System.Collections.Generic;
using UnityEngine;
using VirtualVessel.Capture;
using VirtualVessel.Core.State;

namespace VirtualVessel.UI.Tests
{
    /// <summary>
    /// Sample capture sources for the screenshots, so the setup screen shows its Cards in two states:
    /// a board waiting to be chosen and a monitor capturing. Nothing is opened.
    /// </summary>
    internal sealed class ScreenshotCaptureSources : ICaptureSources
    {
        public ScreenshotCaptureSources()
        {
            Sources = new IVideoCaptureSource[]
            {
                new Sample("game", CaptureSourceKind.GameCapture, CaptureState.Stopped, CaptureProblem.DeviceNotChosen, default, null),
                new PointerSample("subscreen", CaptureSourceKind.SubScreenCapture, CaptureState.Capturing, CaptureProblem.None, new CaptureSourceInfo(1920, 1080, 60, "DISPLAY2 (1920×1080)"), @"\\.\DISPLAY2"),
            };
        }

        public IReadOnlyList<IVideoCaptureSource> Sources { get; }

        public IVideoCaptureSource Find(string id)
        {
            foreach (IVideoCaptureSource source in Sources)
            {
                if (source.Id == id)
                {
                    return source;
                }
            }

            return null;
        }

        private class Sample : ICaptureDeviceSource
        {
            public Sample(string id, CaptureSourceKind kind, CaptureState state, CaptureProblem problem, CaptureSourceInfo info, string selected)
            {
                Id = id;
                Kind = kind;
                State = new ObservableValue<CaptureState>(state);
                Problem = new ObservableValue<CaptureProblem>(problem);
                Info = new ObservableValue<CaptureSourceInfo>(info);
                SelectedDeviceId = selected;
                Devices = kind == CaptureSourceKind.GameCapture
                    ? new[] { new CaptureDevice("USB Video", "USB Video"), new CaptureDevice("Integrated Camera", "Integrated Camera") }
                    : new[] { new CaptureDevice(@"\\.\DISPLAY1", "DISPLAY1 (3840×2160)"), new CaptureDevice(@"\\.\DISPLAY2", "DISPLAY2 (1920×1080)") };
            }

            public string Id { get; }

            public CaptureSourceKind Kind { get; }

            public IReadOnlyObservable<CaptureState> State { get; }

            public IReadOnlyObservable<CaptureProblem> Problem { get; }

            public IReadOnlyObservable<CaptureSourceInfo> Info { get; }

            public IReadOnlyObservable<Texture> Texture { get; } = new ObservableValue<Texture>(null);

            public IReadOnlyObservable<CaptureFlip> Flip { get; } = new ObservableValue<CaptureFlip>(CaptureFlip.None);

            public IReadOnlyList<CaptureDevice> Devices { get; }

            public string SelectedDeviceId { get; }

            public void SetFlip(CaptureFlip flip)
            {
            }

            public void Start()
            {
            }

            public void Stop()
            {
            }

            public void RefreshDevices()
            {
            }

            public void SelectDevice(string deviceId)
            {
            }
        }

        private sealed class PointerSample : Sample, IPointerOption
        {
            public PointerSample(string id, CaptureSourceKind kind, CaptureState state, CaptureProblem problem, CaptureSourceInfo info, string selected)
                : base(id, kind, state, problem, info, selected)
            {
            }

            public IReadOnlyObservable<bool> DrawPointer { get; } = new ObservableValue<bool>(true);

            public void SetDrawPointer(bool draw)
            {
            }
        }
    }
}
