using System;
using System.Collections.Generic;
using VirtualVessel.Capture.SubScreen;
using VirtualVessel.Core.Time;
using VirtualVessel.Diagnostics.Logging;
using VirtualVessel.Diagnostics.Performance;

namespace VirtualVessel.Capture.Game
{
    /// <summary>
    /// The game screen (<c>game</c>): a capture board through WebCamTexture, or a game's window on this PC through
    /// the native plugin, chosen among the same devices (capture detailed design 8.6). The window is the same game
    /// screen on the stream, so its placement, flips, and sound row stay as they are.
    /// </summary>
    internal sealed class GameCaptureSource : WebCamCaptureSource
    {
        private readonly IWindowCapture _windows;
        private readonly NativeFrames _frames;
        private bool _windowsFailed;

        /// <param name="windows">The window capture; null to offer capture boards only.</param>
        public GameCaptureSource(ICameraDevices devices, IWindowCapture windows, WebCamRequest request, string deviceId, CaptureFlip flip, Func<string, bool> inUseElsewhere, IMonotonicClock clock, IPerformanceMetrics metrics, ILog log)
            : base(GameSourceId, CaptureSourceKind.GameCapture, devices, request, deviceId, flip, inUseElsewhere, clock, metrics, log)
        {
            _windows = windows;
            _frames = new NativeFrames(GameSourceId, "GameCapture", metrics, log);
        }

        /// <summary>The window's name in device lists: its title and program, such as "My Game (game.exe)". Pure.</summary>
        internal static string DisplayNameOf(WindowDescription window)
        {
            return string.IsNullOrEmpty(window.Title) ? window.Executable : $"{window.Title} ({window.Executable})";
        }

        /// <summary>The capture boards, then the windows on this PC. Pure.</summary>
        internal static IReadOnlyList<CaptureDevice> Combine(IReadOnlyList<CaptureDevice> boards, IReadOnlyList<WindowDescription> windows)
        {
            var devices = new List<CaptureDevice>(boards.Count + windows.Count);
            devices.AddRange(boards);
            foreach (WindowDescription window in windows)
            {
                devices.Add(new CaptureDevice(CaptureWindow.IdOf(window.Executable), DisplayNameOf(window)));
            }

            return devices;
        }

        protected override IReadOnlyList<CaptureDevice> ListDevices()
        {
            return Combine(base.ListDevices(), ListWindows());
        }

        protected override void Open(CaptureDevice device)
        {
            string program = CaptureWindow.ProgramOf(device.Id);
            if (program == null)
            {
                base.Open(device);
                return;
            }

            if (_windows == null)
            {
                throw new InvalidOperationException($"No window capture for '{device.DisplayName}'.");
            }

            // The cursor is left out: a game draws its own, and Windows' would show twice (the user's choice, 2026-11-20).
            // The plugin finds the window and starts capturing on its own thread; OnTick reports the outcome.
            _frames.Begin(_windows.OpenWindow(program, drawPointer: false), program);
            SetInfo(new CaptureSourceInfo(0, 0, 0, device.DisplayName));
        }

        protected override void Close()
        {
            try
            {
                base.Close();
            }
            finally
            {
                _frames.Close();
            }
        }

        protected override void OnTick(long now)
        {
            if (_frames.IsOpen)
            {
                TickNative(_frames, now);
                return;
            }

            base.OnTick(now);
        }

        /// <summary>The windows on this PC; none, with one warning, when the plugin cannot list them.</summary>
        private IReadOnlyList<WindowDescription> ListWindows()
        {
            if (_windows == null)
            {
                return Array.Empty<WindowDescription>();
            }

            try
            {
                IReadOnlyList<WindowDescription> windows = _windows.ListWindows();
                _windowsFailed = false;
                return windows;
            }
            catch (Exception exception)
            {
                // Capture boards stay usable without the plugin; the warning is not repeated every check.
                if (!_windowsFailed)
                {
                    Log?.Warning($"Capture source '{Id}' could not list the windows on this PC.", exception);
                }

                _windowsFailed = true;
                return Array.Empty<WindowDescription>();
            }
        }
    }
}
