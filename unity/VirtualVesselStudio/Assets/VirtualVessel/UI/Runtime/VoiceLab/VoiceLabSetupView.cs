using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using UnityEngine.UIElements;
using VirtualVessel.ProjectData.Records;
using VirtualVessel.UI.Controls;
using VirtualVessel.UI.Dialogs;
using VirtualVessel.UI.Localization;
using VirtualVessel.UI.Binding;

namespace VirtualVessel.UI.VoiceLab
{
    /// <summary>What the setup view shows (screen spec voice-lab 2.1).</summary>
    public enum VoiceLabSetupPage
    {
        /// <summary>Nothing: Voice Lab is ready and has no update.</summary>
        None,

        /// <summary>The check of the PC and where to build, before the terms.</summary>
        Check,

        Terms,

        /// <summary>A build running, stopped, or failed.</summary>
        Progress,

        /// <summary>A first build just completed.</summary>
        Done,

        /// <summary>Ready, with a newer environment to build.</summary>
        Update,
    }

    /// <summary>A part of the terms' Markdown as the screen shows it.</summary>
    public enum VoiceLabTermsBlockKind
    {
        Title,
        Heading,
        Paragraph,

        /// <summary>A numbered item; nested ones are indented.</summary>
        Item,

        /// <summary>A row of a table, its cells joined.</summary>
        Row,
    }

    /// <summary>
    /// Preparing Voice Lab on the screen (screen spec voice-lab 2.1; Voice Lab detailed design 8.1, 8.2): the check of
    /// the PC with the folder to build in, the terms to agree to, the build's steps and how far it is, stopping and
    /// continuing, what to do when it failed, and an update once ready. The terms are read from the files the app ships,
    /// so they are kept in one place (the user's review, 2026-11-11).
    /// </summary>
    public sealed class VoiceLabSetupView : VisualElement
    {
        public const string UssClass = "vv-lab-setup";
        public const string TermsClass = UssClass + "__terms";
        public const string TermsHeadingClass = UssClass + "__terms-heading";
        public const string TermsItemClass = UssClass + "__terms-item";
        public const string StepClass = UssClass + "__step";
        public const string ProblemClass = UssClass + "__problem";
        public const string ControlsClass = UssClass + "__controls";
        public const string LogClass = UssClass + "__log";

        // The speed is the average over this window, so it does not jump with each chunk.
        private const double RateWindowSeconds = 5d;

        private const int RefreshMs = 250;

        private static readonly Regex s_escape = new Regex(@"\\([\\`*_{}\[\]()#+\-.!|])", RegexOptions.CultureInvariant);

        private static readonly string[] s_stepKeys =
        {
            VoiceLabStrings.SetupStepDownload,
            VoiceLabStrings.SetupStepUnpack,
            VoiceLabStrings.SetupStepInstall,
            VoiceLabStrings.SetupStepCheck,
        };

        private readonly IVoiceLabSetup _setup;
        private readonly IFileDialog _files;
        private readonly Action _toMake;
        private readonly Label _gpuValue;
        private readonly Label _driverValue;
        private readonly Label _locationValue;
        private readonly Label _onlineValue;
        private readonly StatusChip[] _checkChips = new StatusChip[4];
        private readonly Label[] _problems = new Label[4];
        private readonly KeyedText[] _problemTexts = new KeyedText[4];
        private readonly StatusChip _locationFailed;
        private readonly Label _termsVersion;
        private readonly VisualElement _termsBody;
        private readonly StatusChip _progressChip;
        private readonly StatusChip _failureTitle;
        private readonly Label _failureBody;
        private readonly KeyedText _failureText;
        private readonly StatusChip[] _stepChips = new StatusChip[4];
        private readonly ProgressMeter _meter;
        private readonly KeyedText _progressNote;
        private readonly StatusChip _updateFailed;
        private readonly Label _updateSize;
        private CancellationTokenSource _run;
        private CancellationTokenSource _checking;
        private IVisualElementScheduledItem _refresh;
        private readonly ScrollView _log;
        private readonly List<(double Seconds, long Done)> _samples = new List<(double Seconds, long Done)>();
        private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();
        private int _shownEntries;
        private bool _wantsTerms;
        private bool _justFinished;
        private string _termsLanguage;

        /// <param name="setup">Activation as the screen sees it.</param>
        /// <param name="files">Picks the folder to build in; null keeps the default.</param>
        /// <param name="toMake">Opens making a voice once the first build completed.</param>
        public VoiceLabSetupView(IVoiceLabSetup setup, IFileDialog files, Action toMake)
        {
            _setup = setup ?? throw new ArgumentNullException(nameof(setup));
            _files = files;
            _toMake = toMake;
            AddToClassList(UssClass);

            CheckCard = Ui.Card(VoiceLabStrings.SetupTitle);
            CheckCard.Add(VoiceLabScreen.Lead(VoiceLabStrings.SetupLead));
            _gpuValue = CheckRow(0, VoiceLabStrings.SetupGpu, VoiceLabStrings.SetupGpuHelp, null);
            _driverValue = CheckRow(1, VoiceLabStrings.SetupDriver, VoiceLabStrings.SetupDriverHelp, null);
            ChangeButton = Ui.Button(VoiceLabStrings.SetupChange);
            ChangeButton.clicked += ChangeLocation;
            ChangeButton.SetEnabled(files != null);
            _locationValue = CheckRow(2, VoiceLabStrings.SetupLocation, VoiceLabStrings.SetupLocationHelp, ChangeButton);
            _locationFailed = Ui.Chip(StatusKind.Error, VoiceLabStrings.SetupLocationFailed);
            _locationFailed.AddToClassList(VoiceLabScreen.GapClass);
            _locationFailed.style.display = DisplayStyle.None;
            CheckCard.Add(_locationFailed);
            _onlineValue = CheckRow(3, VoiceLabStrings.SetupOnline, VoiceLabStrings.SetupOnlineHelp, null);
            ToTermsButton = Ui.Button(VoiceLabStrings.SetupToTerms, ButtonVariant.Primary);
            ToTermsButton.clicked += () => ShowTerms(true);
            ToTermsButton.SetEnabled(false);
            CheckCard.Add(VoiceLabScreen.Actions(ToTermsButton));
            CheckCard.Add(Ui.Text(VoiceLabStrings.SetupAgreeNote, VoiceLabScreen.NoteClass, "vv-text-body"));
            CheckCard.Add(Ui.Text(VoiceLabStrings.SetupTimeNote, VoiceLabScreen.NoteClass, "vv-text-body"));
            Add(CheckCard);

            TermsCard = Ui.Card(VoiceLabStrings.SetupTermsTitle);
            _termsVersion = QuietText.Of(new Label());
            _termsVersion.AddToClassList(VoiceLabScreen.NoteClass);
            _termsVersion.AddToClassList("vv-text-caption");
            TermsCard.Add(_termsVersion);
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList(TermsClass);
            _termsBody = scroll.contentContainer;
            TermsCard.Add(scroll);
            AgreeToggle = Ui.Toggle(VoiceLabStrings.SetupAgree);
            AgreeToggle.AddToClassList(VoiceLabScreen.GapClass);
            AgreeToggle.RegisterValueChangedCallback(e => StartButton.SetEnabled(e.newValue));
            TermsCard.Add(AgreeToggle);
            BackButton = Ui.Button(VoiceLabStrings.SetupBack);
            ActionButton back = BackButton;
            back.clicked += () => ShowTerms(false);
            StartButton = Ui.Button(VoiceLabStrings.SetupStart, ButtonVariant.Primary);
            StartButton.clicked += AgreeAndRun;
            StartButton.SetEnabled(false);
            TermsCard.Add(VoiceLabScreen.Actions(back, StartButton));
            Add(TermsCard);

            ProgressCard = Ui.Card(VoiceLabStrings.SetupTitle);
            _progressChip = Ui.Chip(StatusKind.Idle, VoiceLabStrings.SetupRunning);
            _progressChip.AddToClassList(VoiceLabScreen.GapClass);
            ProgressCard.Add(_progressChip);
            _failureTitle = Ui.Chip(StatusKind.Error, VoiceLabStrings.SetupFailedSetupTitle);
            ProgressCard.Add(_failureTitle);
            _failureBody = TextLabel(VoiceLabScreen.NoteClass);
            _failureText = new KeyedText(_failureBody, value => _failureBody.text = value);
            _failureBody.AddToClassList(VoiceLabScreen.GapClass);
            ProgressCard.Add(_failureBody);
            for (int i = 0; i < s_stepKeys.Length; i++)
            {
                var row = new VisualElement();
                row.AddToClassList(StepClass);
                _stepChips[i] = Ui.Chip(StatusKind.Idle, VoiceLabStrings.SetupStepLater);
                row.Add(_stepChips[i]);
                row.Add(Ui.Text(s_stepKeys[i], "vv-text-body"));
                ProgressCard.Add(row);
            }

            _meter = Ui.Progress(s_stepKeys[0]);
            _meter.AddToClassList(VoiceLabScreen.GapClass);
            ProgressCard.Add(_meter);

            // What is being done, line by line in the app's words (the user's request, 2026-11-11).
            _log = new ScrollView(ScrollViewMode.Vertical);
            _log.AddToClassList(LogClass);
            _log.AddToClassList(VoiceLabScreen.GapClass);
            ProgressCard.Add(_log);
            StopButton = Ui.Button(VoiceLabStrings.Stop);
            StopButton.clicked += () => _run?.Cancel();
            ResumeButton = Ui.Button(VoiceLabStrings.SetupResume, ButtonVariant.Primary);
            ResumeButton.clicked += RunOrAsk;
            ProgressCard.Add(VoiceLabScreen.Actions(StopButton, ResumeButton));
            Label progressNote = TextLabel(VoiceLabScreen.NoteClass);
            _progressNote = new KeyedText(progressNote, value => progressNote.text = value);
            ProgressCard.Add(progressNote);
            Add(ProgressCard);

            DoneCard = Ui.Card(VoiceLabStrings.SetupDoneTitle);
            DoneCard.Add(Ui.Text(VoiceLabStrings.SetupDoneBody, VoiceLabScreen.NoteClass, "vv-text-body"));
            ToMakeButton = Ui.Button(VoiceLabStrings.SetupToMake, ButtonVariant.Primary);
            ToMakeButton.clicked += () =>
            {
                _justFinished = false;
                Refresh();
                _toMake?.Invoke();
            };
            DoneCard.Add(VoiceLabScreen.Actions(ToMakeButton));
            Add(DoneCard);

            UpdateCard = Ui.Card(VoiceLabStrings.SetupUpdateTitle);
            UpdateCard.Add(Ui.Text(VoiceLabStrings.SetupUpdateBody, VoiceLabScreen.NoteClass, "vv-text-body"));
            _updateFailed = Ui.Chip(StatusKind.Error, VoiceLabStrings.SetupUpdateFailed);
            _updateFailed.AddToClassList(VoiceLabScreen.GapClass);
            UpdateCard.Add(_updateFailed);
            UpdateButton = Ui.Button(VoiceLabStrings.SetupUpdateStart, ButtonVariant.Primary);
            UpdateButton.clicked += RunOrAsk;
            _updateSize = TextLabel(VoiceLabScreen.NoteClass);
            UpdateCard.Add(VoiceLabScreen.Actions(UpdateButton, _updateSize));
            Add(UpdateCard);

            RegisterCallback<AttachToPanelEvent>(_ => Refresh());

            // The check's values and the terms are built in the language shown; they follow it when it changes.
            PanelLifetime.OnLocaleChanged(this, () =>
            {
                ShowCheck(LastCheck);
                ShowEntries(rewrite: true);
                Refresh();
            });
            RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                _checking?.Cancel();
                _checking = null;
            });
            Refresh();
        }

        public Card CheckCard { get; }

        public Card TermsCard { get; }

        public Card ProgressCard { get; }

        public Card DoneCard { get; }

        public Card UpdateCard { get; }

        public ActionButton ChangeButton { get; }

        public ActionButton ToTermsButton { get; }

        public SwitchToggle AgreeToggle { get; }

        public ActionButton StartButton { get; }

        public ActionButton StopButton { get; }

        public ActionButton ResumeButton { get; }

        public ActionButton ToMakeButton { get; }

        public ActionButton UpdateButton { get; }

        /// <summary>The page shown now.</summary>
        public VoiceLabSetupPage Page { get; private set; }

        public ActionButton BackButton { get; }

        /// <summary>
        /// Voice Lab is built and only the terms are to agree to again: agreeing opens the steps, with nothing to build,
        /// and there is nowhere to go back to. Pure.
        /// </summary>
        public static bool OnlyToAgree(VoiceLabSetupState state, bool updateAvailable)
        {
            return state == VoiceLabSetupState.Ready && !updateAvailable;
        }

        /// <summary>The last check of the PC; null until one completed.</summary>
        public VoiceLabPcCheck LastCheck { get; private set; }

        /// <summary>
        /// The page for activation's state: a build running shows its progress; the terms when asked for; one stopped or
        /// failed its progress, to continue; once ready, the first build's completion, then terms not agreed to (changed
        /// since, Voice Lab detailed design 8.2), then an update when there is one. Pure.
        /// </summary>
        public static VoiceLabSetupPage PageOf(VoiceLabSetupState state, bool updateAvailable, bool wantsTerms, bool justFinished, bool termsAgreed = true)
        {
            if (state == VoiceLabSetupState.Preparing)
            {
                return VoiceLabSetupPage.Progress;
            }

            if (wantsTerms)
            {
                return VoiceLabSetupPage.Terms;
            }

            switch (state)
            {
                case VoiceLabSetupState.Paused:
                case VoiceLabSetupState.Failed:
                    return VoiceLabSetupPage.Progress;
                case VoiceLabSetupState.Ready:
                    if (justFinished)
                    {
                        return VoiceLabSetupPage.Done;
                    }

                    return !termsAgreed ? VoiceLabSetupPage.Terms : updateAvailable ? VoiceLabSetupPage.Update : VoiceLabSetupPage.None;
                default:
                    return VoiceLabSetupPage.Check;
            }
        }

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

        /// <summary>
        /// The terms' Markdown as plain blocks: HTML comments and table rules left out, numbered items with their numbers,
        /// escapes and bold marks removed, and the lines of a paragraph joined. Pure.
        /// </summary>
        public static List<(VoiceLabTermsBlockKind Kind, int Indent, string Text)> TermsBlocks(string markdown)
        {
            var blocks = new List<(VoiceLabTermsBlockKind, int, string)>();
            var paragraph = new StringBuilder();

            void EndParagraph()
            {
                if (paragraph.Length > 0)
                {
                    blocks.Add((VoiceLabTermsBlockKind.Paragraph, 0, paragraph.ToString()));
                    paragraph.Clear();
                }
            }

            foreach (string raw in (markdown ?? string.Empty).Replace("\r\n", "\n").Split('\n'))
            {
                string trimmed = raw.Trim();
                int indent = (raw.Length - raw.TrimStart(' ').Length) / 3;
                if (trimmed.Length == 0 || trimmed.StartsWith("<!--", StringComparison.Ordinal))
                {
                    EndParagraph();
                    continue;
                }

                if (trimmed.StartsWith("# ", StringComparison.Ordinal))
                {
                    EndParagraph();
                    blocks.Add((VoiceLabTermsBlockKind.Title, 0, Plain(trimmed.Substring(2))));
                }
                else if (trimmed.StartsWith("#", StringComparison.Ordinal))
                {
                    EndParagraph();
                    blocks.Add((VoiceLabTermsBlockKind.Heading, 0, Plain(trimmed.TrimStart('#').Trim())));
                }
                else if (trimmed.StartsWith("|", StringComparison.Ordinal))
                {
                    EndParagraph();
                    string[] cells = trimmed.Trim('|').Split('|');
                    if (Array.TrueForAll(cells, c => c.Trim().Trim('-', ':').Length == 0))
                    {
                        continue;
                    }

                    blocks.Add((VoiceLabTermsBlockKind.Row, 0, Plain(string.Join(" ／ ", Array.ConvertAll(cells, c => c.Trim())))));
                }
                else if (IsNumberedItem(trimmed) || trimmed.StartsWith("- ", StringComparison.Ordinal))
                {
                    EndParagraph();
                    string text = trimmed.StartsWith("- ", StringComparison.Ordinal) ? "・" + trimmed.Substring(2) : trimmed;
                    blocks.Add((VoiceLabTermsBlockKind.Item, indent, Plain(text)));
                }
                else
                {
                    paragraph.Append(Plain(trimmed));
                }
            }

            EndParagraph();
            return blocks;
        }

        // "1. text", not the escaped "1\." that starts a paragraph.
        private static bool IsNumberedItem(string line)
        {
            int digits = 0;
            while (digits < line.Length && char.IsDigit(line[digits]))
            {
                digits++;
            }

            return digits > 0 && digits + 1 < line.Length && line[digits] == '.' && line[digits + 1] == ' ';
        }

        private static string Plain(string text)
        {
            // Markdown's escapes, such as "1\." for a number that is not a list; other backslashes, as in a path, stay.
            return s_escape.Replace(text.Replace("**", string.Empty), "$1");
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

        /// <summary>Shows activation's state again, such as when the screen opens.</summary>
        public void Refresh()
        {
            VoiceLabSetupState state = _setup.State;
            VoiceLabSetupPage page = PageOf(state, _setup.UpdateAvailable, _wantsTerms, _justFinished, _setup.TermsAgreed);
            VoiceLabSetupPage before = Page;
            Page = page;

            // Looked at on coming to it: on opening the screen, and after the environment was deleted.
            if (page == VoiceLabSetupPage.Check && panel != null && _checking == null && (before != page || LastCheck == null))
            {
                CheckPc();
            }

            style.display = page == VoiceLabSetupPage.None ? DisplayStyle.None : DisplayStyle.Flex;
            CheckCard.style.display = Show(page == VoiceLabSetupPage.Check);
            TermsCard.style.display = Show(page == VoiceLabSetupPage.Terms);
            ProgressCard.style.display = Show(page == VoiceLabSetupPage.Progress);
            DoneCard.style.display = Show(page == VoiceLabSetupPage.Done);
            UpdateCard.style.display = Show(page == VoiceLabSetupPage.Update);
            ILocalizer localizer = UiServices.Find(this)?.Localizer;
            if (page == VoiceLabSetupPage.Terms)
            {
                bool onlyToAgree = OnlyToAgree(state, _setup.UpdateAvailable);
                StartButton.TextKey = onlyToAgree ? VoiceLabStrings.SetupAgreeOnly : VoiceLabStrings.SetupStart;
                BackButton.style.display = onlyToAgree ? DisplayStyle.None : DisplayStyle.Flex;
                ShowTermsText(localizer);
            }
            else if (page == VoiceLabSetupPage.Progress)
            {
                ShowProgress(state);
            }
            else if (page == VoiceLabSetupPage.Update)
            {
                _updateFailed.style.display = Show(_setup.LastFailure != VoiceLabSetupFailure.None);
                _updateSize.text = localizer?.Get(VoiceLabStrings.SetupUpdateSize, ("size", VoiceLabLibraryView.SizeText(_setup.DownloadBytes))) ?? string.Empty;
            }
        }

        private static StyleEnum<DisplayStyle> Show(bool shown)
        {
            return shown ? DisplayStyle.Flex : DisplayStyle.None;
        }

        // Text set by the view, such as a GPU's name or a key it changes.
        private static Label TextLabel(string ussClass)
        {
            Label label = QuietText.Of(new Label());
            label.AddToClassList(ussClass);
            label.AddToClassList("vv-text-body");
            return label;
        }

        private Label CheckRow(int index, string nameKey, string helpKey, VisualElement control)
        {
            SettingRow row = Ui.SettingRow(nameKey);
            row.HelpKey = helpKey;
            Label value = TextLabel(VoiceLabScreen.NoteClass);
            row.TextArea.Add(value);
            var controls = new VisualElement();
            controls.AddToClassList(ControlsClass);
            _checkChips[index] = Ui.Chip(StatusKind.Idle, VoiceLabStrings.SetupChecking);
            controls.Add(_checkChips[index]);
            if (control != null)
            {
                controls.Add(control);
            }

            row.Add(controls);

            CheckCard.Add(row);
            Label problem = _problems[index] = TextLabel(ProblemClass);
            _problemTexts[index] = new KeyedText(problem, text => problem.text = text);
            problem.style.display = DisplayStyle.None;
            CheckCard.Add(_problems[index]);
            return value;
        }

        private async void CheckPc()
        {
            _checking?.Cancel();
            var checking = _checking = new CancellationTokenSource();
            ToTermsButton.SetEnabled(false);
            foreach (StatusChip chip in _checkChips)
            {
                chip.Status = StatusKind.Idle;
                chip.TextKey = VoiceLabStrings.SetupChecking;
            }

            ShowCheck(null);
            VoiceLabPcCheck check;
            try
            {
                check = await _setup.CheckPcAsync(checking.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            finally
            {
                if (_checking == checking)
                {
                    _checking = null;
                }

                checking.Dispose();
            }

            // A newer check, such as for another folder, replaced this one.
            if (checking.IsCancellationRequested)
            {
                return;
            }

            LastCheck = check;
            ShowCheck(check);
        }

        private void ShowCheck(VoiceLabPcCheck check)
        {
            ILocalizer localizer = UiServices.Find(this)?.Localizer;
            string unknown = localizer?.Get(VoiceLabStrings.SetupUnknown) ?? string.Empty;
            _gpuValue.text = check == null ? string.Empty : check.Gpu ?? unknown;
            _driverValue.text = check == null ? string.Empty : check.Driver ?? unknown;
            _locationValue.text = LocationText(localizer, check?.Location ?? _setup.Location, check?.FreeBytes);
            _onlineValue.text = check == null ? string.Empty : localizer?.Get(check.Online ? VoiceLabStrings.SetupOnlineOk : VoiceLabStrings.SetupOnlineNo) ?? string.Empty;
            if (check == null)
            {
                foreach (Label problem in _problems)
                {
                    problem.style.display = DisplayStyle.None;
                }

                return;
            }

            ShowCheckRow(0, check.GpuOk, check.Gpu == null ? VoiceLabStrings.SetupGpuMissing : VoiceLabStrings.SetupGpuOld);
            ShowCheckRow(1, check.DriverOk, VoiceLabStrings.SetupDriverOld);
            ShowCheckRow(2, check.SpaceOk, VoiceLabStrings.SetupLocationLow);
            ShowCheckRow(3, check.Online, VoiceLabStrings.SetupOnlineProblem);
            ToTermsButton.SetEnabled(check.AllOk);
        }

        private void ShowCheckRow(int index, bool ok, string problemKey)
        {
            _checkChips[index].Status = ok ? StatusKind.Success : StatusKind.Error;
            _checkChips[index].TextKey = ok ? VoiceLabStrings.SetupCheckOk : VoiceLabStrings.SetupCheckNg;
            _problemTexts[index].Key = problemKey;
            _problems[index].style.display = Show(!ok);
        }

        private async void ChangeLocation()
        {
            if (_files == null)
            {
                return;
            }

            string picked = await _files.PickFolderAsync(UiServices.Find(this)?.Localizer?.Get(VoiceLabStrings.SetupLocationPickTitle));
            if (string.IsNullOrEmpty(picked))
            {
                return;
            }

            CrudResult set = _setup.SetLocation(picked);
            _locationFailed.style.display = DisplayStyle.None;
            if (!set.Ok)
            {
                VoiceLabScreen.ShowFailure(this, _locationFailed, "Choosing where to build Voice Lab", set.Error, set.Detail);
                _locationFailed.TextKey = VoiceLabStrings.SetupLocationFailed;
            }

            CheckPc();
        }

        private void ShowTerms(bool shown)
        {
            _wantsTerms = shown;
            AgreeToggle.SetValueWithoutNotify(false);
            StartButton.SetEnabled(false);
            Refresh();
        }

        private void ShowTermsText(ILocalizer localizer)
        {
            string language = localizer?.Locale?.Code ?? "en";
            _termsVersion.text = localizer?.Get(VoiceLabStrings.SetupTermsVersion, ("version", _setup.TermsVersion ?? string.Empty)) ?? string.Empty;
            if (language == _termsLanguage)
            {
                return;
            }

            _termsLanguage = language;
            _termsBody.Clear();
            string text = _setup.TermsText(language);
            if (text == null)
            {
                // The terms cannot be agreed to unread.
                _termsBody.Add(Ui.Text(VoiceLabStrings.SetupTermsMissing, ProblemClass, "vv-text-body"));
                AgreeToggle.SetEnabled(false);
                return;
            }

            AgreeToggle.SetEnabled(true);
            foreach ((VoiceLabTermsBlockKind kind, int indent, string block) in TermsBlocks(text))
            {
                Label label = QuietText.Of(new Label(block));
                label.AddToClassList(kind == VoiceLabTermsBlockKind.Title ? "vv-text-subhead" : "vv-text-body");
                label.AddToClassList(kind == VoiceLabTermsBlockKind.Title || kind == VoiceLabTermsBlockKind.Heading ? TermsHeadingClass : TermsItemClass);
                label.style.marginLeft = indent * 16;
                _termsBody.Add(label);
            }
        }

        private void ShowProgress(VoiceLabSetupState state)
        {
            bool running = state == VoiceLabSetupState.Preparing;
            VoiceLabSetupProgress progress = _setup.Progress;
            int current = (int)progress.Step;
            (StatusKind status, string key) = StepStatusOf(current, current, state);
            _progressChip.Status = status;
            _progressChip.TextKey = key;
            bool failed = state == VoiceLabSetupState.Failed;
            (string title, string body) = FailureKeys(_setup.LastFailure);
            _failureTitle.TextKey = title;
            _failureText.Key = body;
            _failureTitle.style.display = Show(failed);
            _failureBody.style.display = Show(failed);
            for (int i = 0; i < _stepChips.Length; i++)
            {
                (StatusKind stepStatus, string stepKey) = StepStatusOf(i, current, state);
                _stepChips[i].Status = stepStatus;
                _stepChips[i].TextKey = stepKey;
            }

            // Only while it runs: a stopped build's last figures would be stale, and after a restart there are none.
            _meter.style.display = Show(running);
            _meter.LabelKey = s_stepKeys[current];
            _meter.Progress = progress.Fraction;
            _meter.Detail = ProgressDetail(UiServices.Find(this)?.Localizer, progress) + DownloadRate(progress, running);
            ShowEntries();
            StopButton.style.display = Show(running);
            StopButton.SetEnabled(_run != null);
            ResumeButton.style.display = Show(!running);
            _progressNote.Key = running ? VoiceLabStrings.SetupRunningNote
                : _setup.PausedForLive ? VoiceLabStrings.SetupLiveNote
                : VoiceLabStrings.SetupResumeNote;
        }

        // Samples the bytes done while downloading; the speed is read from the last few seconds.
        private string DownloadRate(VoiceLabSetupProgress progress, bool running)
        {
            if (!running || progress.Step != VoiceLabSetupStep.Download || progress.Total <= 0)
            {
                _samples.Clear();
                return string.Empty;
            }

            double now = _clock.Elapsed.TotalSeconds;
            if (_samples.Count > 0 && progress.Done < _samples[_samples.Count - 1].Done)
            {
                _samples.Clear();
            }

            _samples.Add((now, progress.Done));
            _samples.RemoveAll(sample => now - sample.Seconds > RateWindowSeconds);
            (double rate, double? left) = Rate(_samples, progress.Total);
            return rate > 0d ? " · " + RateText(UiServices.Find(this)?.Localizer, rate, left) : string.Empty;
        }

        // Adds the record's new lines and keeps the newest in view; a language change writes them all again.
        private void ShowEntries(bool rewrite = false)
        {
            IReadOnlyList<VoiceLabSetupEntry> entries = _setup.Entries;
            if (rewrite || entries.Count < _shownEntries)
            {
                _log.Clear();
                _shownEntries = 0;
            }

            if (entries.Count == _shownEntries)
            {
                return;
            }

            ILocalizer localizer = UiServices.Find(this)?.Localizer;
            for (int i = _shownEntries; i < entries.Count; i++)
            {
                Label line = QuietText.Of(new Label(EntryText(localizer, entries[i])));
                line.AddToClassList("vv-mono");
                _log.Add(line);
            }

            _shownEntries = entries.Count;
            _log.style.display = entries.Count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            // After layout, so the newest line is measured before it is scrolled to.
            Label newest = (Label)_log[_log.childCount - 1];
            _log.schedule.Execute(() => _log.ScrollTo(newest)).StartingIn(50);
        }

        private void AgreeAndRun()
        {
            if (!AgreeToggle.value)
            {
                return;
            }

            _setup.AgreeToTerms();
            _wantsTerms = false;
            if (OnlyToAgree(_setup.State, _setup.UpdateAvailable))
            {
                Refresh();
                _toMake?.Invoke();
                return;
            }

            Run();
        }

        // The terms changed since they were agreed to: they come first again.
        private void RunOrAsk()
        {
            if (!_setup.TermsAgreed)
            {
                ShowTerms(true);
                return;
            }

            Run();
        }

        private async void Run()
        {
            if (_run != null)
            {
                return;
            }

            bool wasReady = _setup.State == VoiceLabSetupState.Ready;
            _run = new CancellationTokenSource();
            _refresh = schedule.Execute(Refresh).Every(RefreshMs);
            var task = _setup.RunAsync(_run.Token);
            Refresh();
            try
            {
                await task;
            }
            catch (OperationCanceledException)
            {
                // Stopped: the downloads are kept, and continuing picks up from them.
            }
            finally
            {
                _refresh.Pause();
                _refresh = null;
                _run.Dispose();
                _run = null;
            }

            _justFinished = !wasReady && _setup.State == VoiceLabSetupState.Ready;
            Refresh();
        }
    }
}
