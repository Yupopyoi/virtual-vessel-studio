using System;
using System.Collections.Generic;

namespace VirtualVessel.Capture
{
    /// <summary>
    /// What the capture sources start with, as written in capture-settings.json (capture detailed
    /// design 10). Property names become the camelCase JSON field names; flips are written as numbers
    /// (0 none, 1 left-right, 2 top-bottom, 3 both).
    /// </summary>
    public sealed class CaptureSettings
    {
        public const int SchemaVersion = 1;
        public const string FileName = "capture-settings.json";

        /// <summary>The capture board's camera name. Nothing is opened until it is set (capture detailed design 6.3).</summary>
        public string GameDeviceId { get; set; }

        public CaptureFlip GameFlip { get; set; }

        public int GameWidth { get; set; } = 1920;

        public int GameHeight { get; set; } = 1080;

        public int GameFrameRate { get; set; } = 60;

        /// <summary>Whether the game source was started; it starts again at the next start.</summary>
        public bool GameEnabled { get; set; }

        /// <summary>The monitor's device name; the first monitor that is not the main one when not set.</summary>
        public string SubScreenMonitorId { get; set; }

        public CaptureFlip SubScreenFlip { get; set; }

        /// <summary>Whether the mouse pointer is drawn into the sub-screen video, as the screen shows it.</summary>
        public bool SubScreenDrawPointer { get; set; } = true;

        public bool SubScreenEnabled { get; set; }

        public CaptureSettings Clone()
        {
            return (CaptureSettings)MemberwiseClone();
        }
    }

    /// <summary>
    /// What the user chose on the sources, compared every frame to find changes to save. A value
    /// type, so comparing allocates nothing.
    /// </summary>
    internal readonly struct CaptureChoices : IEquatable<CaptureChoices>
    {
        private CaptureChoices(string gameDevice, CaptureFlip gameFlip, bool gameEnabled, string monitor, CaptureFlip subFlip, bool drawPointer, bool subEnabled)
        {
            GameDevice = gameDevice;
            GameFlip = gameFlip;
            GameEnabled = gameEnabled;
            Monitor = monitor;
            SubFlip = subFlip;
            DrawPointer = drawPointer;
            SubEnabled = subEnabled;
        }

        public string GameDevice { get; }

        public CaptureFlip GameFlip { get; }

        public bool GameEnabled { get; }

        public string Monitor { get; }

        public CaptureFlip SubFlip { get; }

        public bool DrawPointer { get; }

        public bool SubEnabled { get; }

        public static CaptureChoices From(CaptureSettings settings)
        {
            return new CaptureChoices(settings.GameDeviceId, settings.GameFlip, settings.GameEnabled, settings.SubScreenMonitorId, settings.SubScreenFlip, settings.SubScreenDrawPointer, settings.SubScreenEnabled);
        }

        /// <summary>
        /// The choices as the sources show them now; a source that is missing keeps what
        /// <paramref name="current"/> had. A source counts as started unless it is stopped. Pure.
        /// </summary>
        public static CaptureChoices Of(CaptureChoices current, IReadOnlyList<CaptureSourceBase> sources)
        {
            CaptureChoices choices = current;
            for (int i = 0; i < sources.Count; i++)
            {
                CaptureSourceBase source = sources[i];
                bool enabled = source.State.Value != CaptureState.Stopped;
                if (source.Id == Game.GameCaptureSource.SourceId)
                {
                    choices = new CaptureChoices(source.SelectedDeviceId, source.Flip.Value, enabled, choices.Monitor, choices.SubFlip, choices.DrawPointer, choices.SubEnabled);
                }
                else if (source.Id == SubScreen.SubScreenCaptureSource.SourceId)
                {
                    bool drawPointer = source is IPointerOption pointer ? pointer.DrawPointer.Value : choices.DrawPointer;
                    choices = new CaptureChoices(choices.GameDevice, choices.GameFlip, choices.GameEnabled, source.SelectedDeviceId, source.Flip.Value, drawPointer, enabled);
                }
            }

            return choices;
        }

        /// <summary>A copy of <paramref name="settings"/> with these choices; other values, such as the requested size, are kept.</summary>
        public CaptureSettings ApplyTo(CaptureSettings settings)
        {
            CaptureSettings next = settings.Clone();
            next.GameDeviceId = GameDevice;
            next.GameFlip = GameFlip;
            next.GameEnabled = GameEnabled;
            next.SubScreenMonitorId = Monitor;
            next.SubScreenFlip = SubFlip;
            next.SubScreenDrawPointer = DrawPointer;
            next.SubScreenEnabled = SubEnabled;
            return next;
        }

        public bool Equals(CaptureChoices other)
        {
            return string.Equals(GameDevice, other.GameDevice, StringComparison.Ordinal)
                && GameFlip == other.GameFlip
                && GameEnabled == other.GameEnabled
                && string.Equals(Monitor, other.Monitor, StringComparison.Ordinal)
                && SubFlip == other.SubFlip
                && DrawPointer == other.DrawPointer
                && SubEnabled == other.SubEnabled;
        }

        public override bool Equals(object obj)
        {
            return obj is CaptureChoices other && Equals(other);
        }

        public override int GetHashCode()
        {
            return (GameDevice?.GetHashCode() ?? 0) ^ ((Monitor?.GetHashCode() ?? 0) * 31) ^ ((int)GameFlip << 2) ^ ((int)SubFlip << 4);
        }
    }
}
