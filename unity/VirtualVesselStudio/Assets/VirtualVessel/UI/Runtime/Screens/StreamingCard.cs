using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine.UIElements;
using VirtualVessel.UI.Binding;
using VirtualVessel.UI.Commands;
using VirtualVessel.UI.Controls;
using VirtualVessel.UI.Localization;
using VirtualVessel.UI.Streaming;

namespace VirtualVessel.UI.Screens
{
    /// <summary>String table keys of the streaming Card (docs/&lt;lang&gt;/ui/screens/setup.md).</summary>
    [StringKeys]
    public static class StreamingCardStrings
    {
        public const string Title = "setup.streaming.title";
        public const string Unavailable = "setup.streaming.unavailable";
        public const string Locked = "setup.streaming.locked";
        public const string Destination = "setup.streaming.destination";
        public const string DestinationYouTube = "setup.streaming.destination.youtube";
        public const string DestinationCustom = "setup.streaming.destination.custom";
        public const string Url = "setup.streaming.url";
        public const string UrlDescription = "setup.streaming.url.description";
        public const string UrlPlaceholder = "setup.streaming.url.placeholder";
        public const string UrlInvalid = "setup.streaming.url.invalid";
        public const string UrlUnencrypted = "setup.streaming.url.unencrypted";
        public const string Key = "setup.streaming.key";
        public const string KeyDescription = "setup.streaming.key.description";
        public const string KeyRegistered = "setup.streaming.key.registered";
        public const string KeyNotRegistered = "setup.streaming.key.not-registered";
        public const string KeyRegister = "setup.streaming.key.register";
        public const string KeyRemove = "setup.streaming.key.remove";
        public const string KeyDialogTitle = "setup.streaming.key.dialog.title";
        public const string KeyDialogBody = "setup.streaming.key.dialog.body";
        public const string KeyDialogField = "setup.streaming.key.dialog.field";
        public const string KeyDialogConfirm = "setup.streaming.key.dialog.confirm";
        public const string KeyFailed = "setup.streaming.key.failed";
        public const string KeyRemoveTitle = "setup.streaming.key.remove.title";
        public const string KeyRemoveBody = "setup.streaming.key.remove.body";
        public const string KeyRemoveConfirm = "setup.streaming.key.remove.confirm";
        public const string Codec = "setup.streaming.codec";
        public const string CodecDescription = "setup.streaming.codec.description";
        public const string CodecNoAv1 = "setup.streaming.codec.no-av1";
        public const string CodecAv1 = "setup.streaming.codec.av1";
        public const string CodecH264 = "setup.streaming.codec.h264";
        public const string Bitrate = "setup.streaming.bitrate";
        public const string BitrateDescription = "setup.streaming.bitrate.description";
        public const string Test = "setup.streaming.test";
        public const string TestDescription = "setup.streaming.test.description";
        public const string TestRun = "setup.streaming.test.run";
        public const string TestOk = "setup.streaming.test.ok";
    }

    /// <summary>
    /// Streaming on the setup screen (streaming detailed design 18.2): where the stream goes, the key, the
    /// format, the bitrate, and a connection test. The key's value is never shown or kept here: the dialog
    /// hands it to the service, and only whether one is registered comes back. While streaming nothing can change.
    /// </summary>
    public sealed class StreamingCard : VisualElement
    {
        public const string UssClass = "vv-streaming-card";
        public const string ValueClass = UssClass + "__value";
        public const string NoteClass = UssClass + "__note";
        public const string ButtonsClass = UssClass + "__buttons";

        private const float MegabitsToBits = 1_000_000f;
        private const float MinMegabits = 2f;
        private const float MaxMegabits = 12f;
        private const float MegabitStep = 0.5f;

        private readonly IStreamingService _streaming;
        private readonly KeyedText _keyText;
        private readonly KeyedText _urlNoteText;
        private readonly KeyedText _testText;
        private readonly KeyedText _lockedText;
        private UiServices _services;

        public StreamingCard(IStreamingService streaming)
        {
            _streaming = streaming ?? throw new ArgumentNullException(nameof(streaming));
            AddToClassList(UssClass);
            Card card = Ui.Card(StreamingCardStrings.Title);
            Add(card);

            Locked = Note(card);
            _lockedText = new KeyedText(Locked, text => Locked.text = text);

            Destination = Ui.Segmented(StreamingCardStrings.DestinationYouTube, StreamingCardStrings.DestinationCustom);
            Destination.RegisterValueChangedCallback(evt => _streaming.SetOptions(_streaming.Options.Value.WithDestination((StreamDestinationKind)evt.newValue)));
            Rows.Add(AddRow(card, StreamingCardStrings.Destination, null, Destination));

            Url = Ui.TextField(null);
            Url.PlaceholderKey = StreamingCardStrings.UrlPlaceholder;
            Url.MaxLength = 512;

            // On leaving the field, not on each key: the service tidies the URL, and rewriting the text under the caret would fight the typing.
            Url.RegisterCallback<FocusOutEvent>(_ => ApplyUrl());
            SettingRow url = Ui.SettingRow(StreamingCardStrings.Url, StreamingCardStrings.UrlDescription);
            url.TextArea.Add(Url);
            UrlNote = Note(url.TextArea);
            _urlNoteText = new KeyedText(UrlNote, text => UrlNote.text = text);
            card.Add(url);
            Rows.Add(url);

            KeyText = new Label();
            KeyText.AddToClassList(ValueClass);
            KeyText.AddToClassList("vv-text-body");
            _keyText = new KeyedText(KeyText, text => KeyText.text = text);
            RegisterButton = Ui.Button(StreamingCardStrings.KeyRegister).Bind(new UiCommand(RegisterAsync));
            RemoveButton = Ui.Button(StreamingCardStrings.KeyRemove).Bind(new DangerCommand(
                new UiCommand(() => _streaming.SetStreamKey(string.Empty)),
                new ConfirmationRequest(StreamingCardStrings.KeyRemoveTitle, StreamingCardStrings.KeyRemoveBody, StreamingCardStrings.KeyRemoveConfirm)));
            var keyButtons = new VisualElement();
            keyButtons.AddToClassList(ButtonsClass);
            keyButtons.Add(RegisterButton);
            keyButtons.Add(RemoveButton);
            SettingRow key = Ui.SettingRow(StreamingCardStrings.Key, StreamingCardStrings.KeyDescription);
            key.TextArea.Add(KeyText);
            key.Add(keyButtons);
            card.Add(key);
            Rows.Add(key);

            Codec = Ui.Segmented(StreamingCardStrings.CodecAv1, StreamingCardStrings.CodecH264);
            Codec.RegisterValueChangedCallback(evt => _streaming.SetOptions(_streaming.Options.Value.WithCodec((StreamingCodec)evt.newValue)));
            CodecRow = AddRow(card, StreamingCardStrings.Codec, StreamingCardStrings.CodecDescription, Codec);
            Rows.Add(CodecRow);

            Bitrate = Ui.Slider(null, MinMegabits, MaxMegabits, "Mbps");
            Bitrate.Step = MegabitStep;
            Bitrate.Decimals = 1;
            Bitrate.RegisterValueChangedCallback(evt => _streaming.SetOptions(_streaming.Options.Value.WithVideoBitrate(Bits(evt.newValue))));
            Rows.Add(AddRow(card, StreamingCardStrings.Bitrate, StreamingCardStrings.BitrateDescription, Bitrate));

            TestButton = Ui.Button(StreamingCardStrings.TestRun).Bind(new UiCommand(TestAsync));
            SettingRow test = Ui.SettingRow(StreamingCardStrings.Test, StreamingCardStrings.TestDescription);
            test.Add(TestButton);
            TestResult = Note(test.TextArea);
            _testText = new KeyedText(TestResult, text => TestResult.text = text);
            card.Add(test);
            Rows.Add(test);

            PanelLifetime.WhileAttached(this, Attach);
        }

        public Label Locked { get; }

        public Segmented Destination { get; }

        public TextInput Url { get; }

        /// <summary>The warning that rtmp:// is not encrypted.</summary>
        public Label UrlNote { get; }

        public Label KeyText { get; }

        public ActionButton RegisterButton { get; }

        public ActionButton RemoveButton { get; }

        public Segmented Codec { get; }

        public SettingRow CodecRow { get; }

        public ValueSlider Bitrate { get; }

        public ActionButton TestButton { get; }

        public Label TestResult { get; }

        private List<VisualElement> Rows { get; } = new List<VisualElement>();

        public static int Bits(float megabits)
        {
            return (int)(megabits * MegabitsToBits);
        }


        /// <summary>The warning under the URL; null for none. Pure.</summary>
        public static string UrlNoteKey(string url)
        {
            return StreamingOptions.IsUnencrypted(url) ? StreamingCardStrings.UrlUnencrypted : null;
        }

        /// <summary>Whether settings can change. Pure.</summary>
        public static bool Editable(StreamingState state)
        {
            return state == StreamingState.Idle || state == StreamingState.Failed;
        }

        private static Label Note(VisualElement parent)
        {
            var note = new Label();
            note.AddToClassList(NoteClass);
            note.AddToClassList("vv-text-caption");
            note.style.display = DisplayStyle.None;
            parent.Add(note);
            return note;
        }

        private static SettingRow AddRow(Card card, string nameKey, string descriptionKey, VisualElement control)
        {
            SettingRow row = Ui.SettingRow(nameKey, descriptionKey);
            row.Add(control);
            card.Add(row);
            return row;
        }

        private IDisposable Attach(UiServices services)
        {
            _services = services;
            var subscriptions = new List<IDisposable>
            {
                _streaming.Options.Subscribe(_ => Show()),
                _streaming.State.Subscribe(_ => Show()),
            };
            Show();
            return new DisposeAll(subscriptions);
        }

        private void ApplyUrl()
        {
            StreamingOptions options = _streaming.Options.Value;
            if (!string.Equals(Url.value, options.ServerUrl, StringComparison.Ordinal))
            {
                _streaming.SetOptions(options.WithServerUrl(Url.value));
            }
        }

        private async Task RegisterAsync(CancellationToken cancellationToken)
        {
            if (_services?.Dialogs == null)
            {
                return;
            }

            string key = await _services.Dialogs.AskSecretAsync(
                StreamingCardStrings.KeyDialogTitle,
                StreamingCardStrings.KeyDialogBody,
                StreamingCardStrings.KeyDialogField,
                StreamingCardStrings.KeyDialogConfirm,
                cancellationToken);
            if (string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            bool registered = _streaming.SetStreamKey(key);
            ShowKey(registered ? null : StreamingCardStrings.KeyFailed);
        }

        private async Task TestAsync(CancellationToken cancellationToken)
        {
            ShowTest(null);
            StreamingProblem problem = await _streaming.TestConnectionAsync();
            ShowTest(problem == StreamingProblem.None ? StreamingCardStrings.TestOk : StreamControl.ProblemKey(problem));
        }

        private void ShowTest(string key)
        {
            _testText.Key = key;
            TestResult.style.display = key != null ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void ShowKey(string failureKey)
        {
            bool registered = _streaming.Options.Value.KeyRegistered;
            _keyText.Key = failureKey ?? (registered ? StreamingCardStrings.KeyRegistered : StreamingCardStrings.KeyNotRegistered);
            RemoveButton.style.display = registered ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void Show()
        {
            StreamingOptions options = _streaming.Options.Value;
            bool editable = Editable(_streaming.State.Value);
            foreach (VisualElement row in Rows)
            {
                row.SetEnabled(editable);
            }

            _lockedText.Key = editable ? null : StreamingCardStrings.Locked;
            Locked.style.display = editable ? DisplayStyle.None : DisplayStyle.Flex;

            Destination.SetValueWithoutNotify((int)options.Destination);
            Url.SetValueWithoutNotify(options.ServerUrl ?? string.Empty);
            bool empty = string.IsNullOrEmpty(options.ServerUrl);
            Url.ErrorKey = empty || StreamingOptions.IsRtmpUrl(options.ServerUrl) ? null : StreamingCardStrings.UrlInvalid;
            string note = UrlNoteKey(options.ServerUrl);
            _urlNoteText.Key = note;
            UrlNote.style.display = note != null ? DisplayStyle.Flex : DisplayStyle.None;

            ShowKey(null);
            Codec.SetItemEnabled((int)StreamingCodec.Av1, options.Av1Available);
            Codec.SetValueWithoutNotify((int)options.Codec);
            CodecRow.DescriptionKey = options.Av1Available ? StreamingCardStrings.CodecDescription : StreamingCardStrings.CodecNoAv1;
            Bitrate.SetValueWithoutNotify(options.VideoBitrate / MegabitsToBits);
        }
    }
}
