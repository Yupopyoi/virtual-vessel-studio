using System;
using System.Collections.Generic;
using VirtualVessel.Avatar;
using VirtualVessel.Capture;
using VirtualVessel.Tracking;
using VirtualVessel.Tracking.Recording;
using VirtualVessel.UI.Credits;
using VirtualVessel.UI.Dialogs;
using VirtualVessel.UI.Frames;
using VirtualVessel.UI.Lighting;
using VirtualVessel.UI.Preview;
using VirtualVessel.UI.Recording;

namespace VirtualVessel.UI.Screens
{
    /// <summary>
    /// The application's services the live and settings screens show (docs/&lt;lang&gt;/ui/screens/), in
    /// one place so that a new one reaches its Card without a new parameter at every step on the way.
    /// The application composition fills it once; a service that is not there is null, and its part of
    /// the screens says so or is left out.
    /// </summary>
    public sealed class ScreenServices
    {
        /// <summary>The stream video for the live screen and the face picture (Video); null shows "no video".</summary>
        public IStreamPreviewSource StreamPreview { get; set; }

        /// <summary>The videos to place on the live screen (Stage).</summary>
        public IReadOnlyList<IFrameSource> FrameSources { get; set; } = Array.Empty<IFrameSource>();

        /// <summary>The capture sources (Capture); null says capture is unavailable.</summary>
        public ICaptureSources Captures { get; set; }

        /// <summary>Recording the stream (Video); null says recording is unavailable.</summary>
        public IRecordingService Recording { get; set; }

        /// <summary>Showing files and folders in Explorer; null leaves out "Open folder".</summary>
        public IFileExplorer Explorer { get; set; }

        /// <summary>Tracking (Tracking); null says it is unavailable.</summary>
        public ITrackingSource Tracking { get; set; }

        /// <summary>Developers' recordings of tracking (Tracking), for the Diagnostics screen; null leaves them out.</summary>
        public ITrackingRecordings TrackingRecordings { get; set; }

        /// <summary>Avatars (Avatar); null says they are unavailable.</summary>
        public IAvatarService Avatars { get; set; }

        /// <summary>The file-picking screen, such as for registering avatars; null disables picking files.</summary>
        public IFileDialog FileDialog { get; set; }

        /// <summary>The shipped license files for the credits; null leaves the credits out.</summary>
        public ICreditFiles Credits { get; set; }

        /// <summary>The stage's light (Stage); null leaves it out.</summary>
        public IStageLighting Lighting { get; set; }
    }
}
