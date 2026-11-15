using System;
using System.Collections.Generic;
using System.Globalization;
using VirtualVessel.UI.Controls;
using VirtualVessel.UI.Localization;

namespace VirtualVessel.UI.VoiceLab
{
    /// <summary>
    /// What the preparation's cards say, from activation's state (screen spec voice-lab 2.1): the steps' chips, what to
    /// do for a failure, the meter's figures, the download's speed, and the lines of the build's record. Pure functions.
    /// </summary>
    public static class VoiceLabSetupText
    {
        /// <summary>The build's steps' names, in order of <see cref="VoiceLabSetupStep"/>.</summary>
        public static readonly string[] StepKeys =
        {
            VoiceLabStrings.SetupStepDownload,
            VoiceLabStrings.SetupStepUnpack,
            VoiceLabStrings.SetupStepInstall,
            VoiceLabStrings.SetupStepCheck,
        };

        /// <summary>A step's chip, from the step under way and the build's state. Pure.</summary>
        public static (StatusKind Status, string Key) StepStatusOf(int step, int current, VoiceLabSetupState state)
        {
            if (step < current)
            {
                return (StatusKind.Success, VoiceLabStrings.SetupStepDone);
            }

            if (step > current)
            {
                return (StatusKind.Idle, VoiceLabStrings.SetupStepLater);
            }

            switch (state)
            {
                case VoiceLabSetupState.Failed:
                    return (StatusKind.Error, VoiceLabStrings.SetupFailed);
                case VoiceLabSetupState.Paused:
                    return (StatusKind.Warning, VoiceLabStrings.SetupPaused);
                default:
                    // Live is for being on air only (design-system.md 3.4); the text tells running from not yet.
                    return (StatusKind.Idle, VoiceLabStrings.SetupRunning);
            }
        }

        /// <summary>What to tell the user for a failure: what happened, and what to do. Pure.</summary>
        public static (string Title, string Body) FailureKeys(VoiceLabSetupFailure failure)
        {
            switch (failure)
            {
                case VoiceLabSetupFailure.Network:
                    return (VoiceLabStrings.SetupFailedNetworkTitle, VoiceLabStrings.SetupFailedNetworkBody);
                case VoiceLabSetupFailure.Corrupt:
                    return (VoiceLabStrings.SetupFailedCorruptTitle, VoiceLabStrings.SetupFailedCorruptBody);
                case VoiceLabSetupFailure.DiskFull:
                    return (VoiceLabStrings.SetupFailedDiskTitle, VoiceLabStrings.SetupFailedDiskBody);
                case VoiceLabSetupFailure.Check:
                    return (VoiceLabStrings.SetupFailedCheckTitle, VoiceLabStrings.SetupFailedCheckBody);
                default:
                    // Setup, and a failure from before a restart whose kind is not kept.
                    return (VoiceLabStrings.SetupFailedSetupTitle, VoiceLabStrings.SetupFailedSetupBody);
            }
        }

        /// <summary>The space shown for the folder to build in, such as "C:\Users\me\AppData\Local (218.0 GB free)". Pure.</summary>
        public static string LocationText(ILocalizer localizer, string location, long? freeBytes)
        {
            return freeBytes == null || localizer == null
                ? location
                : localizer.Get(VoiceLabStrings.SetupLocationValue, ("path", location), ("free", VoiceLabLibraryView.SizeText(freeBytes.Value)));
        }

        /// <summary>The meter's detail: bytes while downloading, and how far the whole build is. Pure.</summary>
        public static string ProgressDetail(ILocalizer localizer, VoiceLabSetupProgress progress)
        {
            // The bar is the whole build; the bytes and their own share are the download's, so the two are named apart
            // (the user's review, 2026-11-11: "4.5 GB / 6.1 GB · 44%" read as a wrong share).
            string percent = VoiceLabView.PercentText(progress.Fraction);
            string overall = localizer?.Get(VoiceLabStrings.SetupOverall, ("percent", percent)) ?? percent;
            if (progress.Step != VoiceLabSetupStep.Download || progress.Total <= 0)
            {
                return overall;
            }

            string downloaded = VoiceLabView.PercentText((float)progress.Done / progress.Total);
            return string.Format(CultureInfo.InvariantCulture, "{0} / {1} ({2}) · {3}", VoiceLabLibraryView.SizeText(progress.Done), VoiceLabLibraryView.SizeText(progress.Total), downloaded, overall);
        }

        /// <summary>
        /// The download's speed in bytes a second over the samples (seconds, bytes done), and the seconds left at that
        /// speed; zero and null until there are two samples apart in time. Pure.
        /// </summary>
        public static (double BytesPerSecond, double? SecondsLeft) Rate(IReadOnlyList<(double Seconds, long Done)> samples, long total)
        {
            if (samples == null || samples.Count < 2)
            {
                return (0d, null);
            }

            (double Seconds, long Done) first = samples[0];
            (double Seconds, long Done) last = samples[samples.Count - 1];
            double elapsed = last.Seconds - first.Seconds;
            if (elapsed <= 0d)
            {
                return (0d, null);
            }

            double rate = Math.Max(0d, (last.Done - first.Done) / elapsed);
            return (rate, rate > 0d ? Math.Max(0d, total - last.Done) / rate : (double?)null);
        }

        /// <summary>"12.3 MB/s · about 8 min left". Pure.</summary>
        public static string RateText(ILocalizer localizer, double bytesPerSecond, double? secondsLeft)
        {
            string speed = (bytesPerSecond / (1024d * 1024d)).ToString("0.0", CultureInfo.InvariantCulture) + " MB/s";
            if (secondsLeft == null || localizer == null)
            {
                return speed;
            }

            int minutes = (int)Math.Ceiling(secondsLeft.Value / 60d);
            string left = minutes <= 1
                ? localizer.Get(VoiceLabStrings.SetupLeftUnderMinute)
                : localizer.Get(VoiceLabStrings.SetupLeft, ("minutes", minutes.ToString(CultureInfo.InvariantCulture)));
            return speed + " · " + left;
        }

        /// <summary>
        /// A component's name in the record: the product's own name where it has one, else a phrase in the language, else
        /// the id. Pure.
        /// </summary>
        public static string ItemName(ILocalizer localizer, string id)
        {
            string Get(string key, string fallback) => localizer?.Get(key) ?? fallback;
            id ??= string.Empty;
            if (id == "uv")
            {
                return "uv";
            }

            if (id == "rvc")
            {
                return "RVC";
            }

            if (id.StartsWith("hubert", StringComparison.Ordinal))
            {
                return "HuBERT";
            }

            if (id == "rmvpe")
            {
                return "RMVPE";
            }

            if (id.StartsWith("voxcpm2", StringComparison.Ordinal))
            {
                return "VoxCPM2";
            }

            if (id.StartsWith("pretrained", StringComparison.Ordinal))
            {
                return Get(VoiceLabStrings.SetupItemPretrained, id);
            }

            if (id == "mute")
            {
                return Get(VoiceLabStrings.SetupItemMute, id);
            }

            if (id.StartsWith("template", StringComparison.Ordinal))
            {
                return Get(VoiceLabStrings.SetupItemTemplate, id);
            }

            if (id.StartsWith("shared-", StringComparison.Ordinal))
            {
                string model = ItemName(null, id.Substring("shared-".Length));
                return localizer?.Get(VoiceLabStrings.SetupItemShared, ("name", model)) ?? model;
            }

            return id;
        }

        /// <summary>"22:14:05  Downloading: VoxCPM2 (4.3 GB)": one line of the record. Pure.</summary>
        public static string EntryText(ILocalizer localizer, VoiceLabSetupEntry entry)
        {
            string time = entry.At.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
            string text;
            switch (entry.Kind)
            {
                case VoiceLabSetupEntryKind.Download:
                    text = localizer?.Get(VoiceLabStrings.SetupLogDownload, ("name", ItemName(localizer, entry.Item)), ("size", VoiceLabLibraryView.SizeText(entry.Size))) ?? entry.Item;
                    break;
                default:
                    text = localizer?.Get(LogKey(entry.Kind)) ?? entry.Kind.ToString();
                    break;
            }

            return time + "  " + text;
        }

        private static string LogKey(VoiceLabSetupEntryKind kind)
        {
            switch (kind)
            {
                case VoiceLabSetupEntryKind.Unpack:
                    return VoiceLabStrings.SetupLogUnpack;
                case VoiceLabSetupEntryKind.Runtime:
                    return VoiceLabStrings.SetupLogRuntime;
                case VoiceLabSetupEntryKind.Packages:
                    return VoiceLabStrings.SetupLogPackages;
                case VoiceLabSetupEntryKind.Check:
                    return VoiceLabStrings.SetupLogCheck;
                case VoiceLabSetupEntryKind.Finish:
                    return VoiceLabStrings.SetupLogFinish;
                case VoiceLabSetupEntryKind.Done:
                    return VoiceLabStrings.SetupLogDone;
                case VoiceLabSetupEntryKind.Stopped:
                    return VoiceLabStrings.SetupLogStopped;
                default:
                    return VoiceLabStrings.SetupLogFailed;
            }
        }
    }
}
