using System;
using VirtualVessel.UI.Streaming;
using VirtualVessel.Video.Recording;

namespace VirtualVessel.Streaming
{
    /// <summary>What each kind of destination takes, and the stream's settings as the user sees them (streaming detailed design 8.3). Pure.</summary>
    public static class StreamDestination
    {
        public const string YouTubeUrl = "rtmps://a.rtmps.youtube.com/live2";
        public const int MinVideoBitrate = 2_000_000;
        public const int MaxVideoBitrate = 12_000_000;

        private const string YouTubeSetting = "youtube";
        private const string CustomSetting = "custom";
        private const string Av1Setting = "av1";
        private const string H264Setting = "h264";

        public static StreamDestinationKind KindOf(string setting)
        {
            return setting == CustomSetting ? StreamDestinationKind.CustomRtmp : StreamDestinationKind.YouTube;
        }

        public static string DefaultUrl(StreamDestinationKind kind)
        {
            return kind == StreamDestinationKind.YouTube ? YouTubeUrl : string.Empty;
        }

        /// <summary>Many other services do not take AV1, so it is YouTube only.</summary>
        public static bool TakesAv1(StreamDestinationKind kind)
        {
            return kind == StreamDestinationKind.YouTube;
        }

        public static StreamingOptions OptionsOf(StreamSettings settings, bool keyRegistered, bool gpuHasAv1)
        {
            StreamDestinationKind kind = KindOf(settings.Destination);
            bool av1 = gpuHasAv1 && TakesAv1(kind);
            StreamingCodec codec = av1 && settings.VideoCodec != H264Setting ? StreamingCodec.Av1 : StreamingCodec.H264;
            return new StreamingOptions(kind, string.IsNullOrWhiteSpace(settings.ServerUrl) ? DefaultUrl(kind) : settings.ServerUrl, keyRegistered, codec, av1, ClampBitrate(settings.VideoBitrate));
        }

        /// <summary>
        /// A copy of <paramref name="settings"/> with the user's choices. A URL left at the destination's default stays
        /// null and an unchanged format stays unmade, so both follow the defaults.
        /// </summary>
        public static StreamSettings With(StreamSettings settings, StreamingOptions options, bool gpuHasAv1)
        {
            StreamingOptions before = OptionsOf(settings, options.KeyRegistered, gpuHasAv1);
            StreamSettings next = settings.Clone();
            next.Destination = options.Destination == StreamDestinationKind.CustomRtmp ? CustomSetting : YouTubeSetting;
            string url = options.ServerUrl?.Trim();
            bool destinationChanged = options.Destination != before.Destination;
            next.ServerUrl = destinationChanged || string.IsNullOrEmpty(url) || url == DefaultUrl(options.Destination) ? null : url;
            if (options.Codec != before.Codec)
            {
                next.VideoCodec = options.Codec == StreamingCodec.Av1 ? Av1Setting : H264Setting;
            }

            next.VideoBitrate = ClampBitrate(options.VideoBitrate);
            return next;
        }

        public static int ClampBitrate(int bitrate)
        {
            return Math.Min(Math.Max(bitrate, MinVideoBitrate), MaxVideoBitrate);
        }

        public static bool IsUsableUrl(string url)
        {
            return Uri.TryCreate(url, UriKind.Absolute, out Uri parsed) && (parsed.Scheme == "rtmp" || parsed.Scheme == "rtmps") && parsed.Host.Length > 0;
        }

        /// <summary>With rtmp:// the key travels unencrypted, and the setup screen says so.</summary>
        public static bool IsUnencrypted(string url)
        {
            return url != null && url.StartsWith("rtmp://", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Only the host is logged: some destinations put the key in the path.</summary>
        public static string HostOf(string url)
        {
            return Uri.TryCreate(url, UriKind.Absolute, out Uri parsed) ? parsed.Host : "(not a URL)";
        }

        /// <summary>Pasted keys often bring a line end or a space with them.</summary>
        public static string CleanKey(string key)
        {
            return key?.Trim() ?? string.Empty;
        }
    }
}
