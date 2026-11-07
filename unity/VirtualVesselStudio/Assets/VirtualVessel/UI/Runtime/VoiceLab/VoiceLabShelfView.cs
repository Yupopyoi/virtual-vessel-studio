using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using UnityEngine.UIElements;
using VirtualVessel.UI.Controls;
using VirtualVessel.UI.Icons;
using VirtualVessel.UI.Localization;

namespace VirtualVessel.UI.VoiceLab
{
    /// <summary>
    /// STEP 1's request (system design 14.14, Voice Lab detailed design 8.3.1): examples to start from, the
    /// shelf of words each with a sample, the request read back as one sentence, and the user's own words.
    /// Samples are made on this PC; until they are, the screen offers to make them and plays nothing.
    /// </summary>
    public sealed class VoiceLabShelfView : VisualElement
    {
        public const string WordsClass = VoiceLabScreen.UssClass + "__words";
        public const string WordClass = VoiceLabScreen.UssClass + "__word";
        public const string ExamplesClass = VoiceLabScreen.UssClass + "__examples";
        public const string AskClass = VoiceLabScreen.UssClass + "__ask";

        // The language samples are played in when the screen's has none (VoiceLabService.SampleLanguages).
        private const string SampleFallbackLanguage = "ja";

        private readonly IVoiceLab _lab;
        private readonly VoiceLabPlayer _player;
        private readonly VoiceLabShelf _shelf;
        private readonly Dictionary<string, ActionButton> _wordButtons = new Dictionary<string, ActionButton>(StringComparer.Ordinal);
        private readonly List<IconButton> _playButtons = new List<IconButton>();
        private readonly List<(VoiceLabExample Example, Label Words)> _exampleWords = new List<(VoiceLabExample, Label)>();
        private readonly VisualElement _samplesNotice;
        private readonly ActionButton _makeSamples;
        private readonly VoiceLabMakingBar _samplesBusy;
        private readonly StatusChip _samplesFailed;
        private readonly Label _sentence;
        private readonly TextInput _words;
        private CancellationTokenSource _samplesMaking;
        private ILocalizer _localizer;
        private IReadOnlyList<string> _picked;

        public VoiceLabShelfView(IVoiceLab lab, VoiceLabPlayer player)
        {
            _lab = lab ?? throw new ArgumentNullException(nameof(lab));
            _player = player ?? throw new ArgumentNullException(nameof(player));
            _shelf = lab.Shelf ?? new VoiceLabShelf(null, null);
            _picked = _shelf.Start();

            _samplesNotice = new VisualElement();
            _samplesNotice.Add(Ui.Text(VoiceLabStrings.SamplesMissing, VoiceLabScreen.NoteClass, "vv-text-body"));
            _makeSamples = Ui.Button(VoiceLabStrings.MakeSamples);
            _makeSamples.clicked += MakeSamples;
            _samplesBusy = new VoiceLabMakingBar(() => _lab.SamplesProgress, VoiceLabStrings.MakingSamples, () => _samplesMaking?.Cancel());
            _samplesFailed = Ui.Chip(StatusKind.Error, VoiceLabStrings.SamplesFailed);
            _samplesFailed.style.display = DisplayStyle.None;
            _samplesNotice.Add(VoiceLabScreen.Actions(_makeSamples));
            _samplesNotice.Add(_samplesBusy);
            _samplesNotice.Add(_samplesFailed);
            Add(_samplesNotice);

            Card examples = Ui.Card(VoiceLabStrings.Examples);
            var exampleList = new VisualElement();
            exampleList.AddToClassList(ExamplesClass);
            foreach (VoiceLabExample example in _shelf.Examples)
            {
                exampleList.Add(ExampleRow(example));
            }

            examples.Add(Ui.Text(VoiceLabStrings.ExamplesNote, VoiceLabScreen.NoteClass, "vv-text-body"));
            examples.Add(exampleList);
            Add(examples);

            Card words = Ui.Card(VoiceLabStrings.Shelf, alternate: true);
            words.Add(Ui.Text(VoiceLabStrings.ShelfNote, VoiceLabScreen.NoteClass, "vv-text-body"));
            Card more = Ui.Card(VoiceLabStrings.MoreMood, collapsible: true);
            more.Expanded = false;
            foreach (VoiceLabShelfGroup group in _shelf.Groups)
            {
                (group.Main ? words : more).Add(GroupRow(group));
            }

            words.Add(more);
            Add(words);

            var ask = new VisualElement();
            ask.AddToClassList(AskClass);
            ask.Add(Ui.Text(VoiceLabStrings.Ask, VoiceLabScreen.NoteClass, "vv-text-body"));
            _sentence = QuietText.Of(new Label());
            _sentence.AddToClassList("vv-text-body");
            ask.Add(_sentence);
            Add(ask);

            _words = Ui.TextField(VoiceLabStrings.Words);
            _words.PlaceholderKey = VoiceLabStrings.WordsHint;
            _words.MaxLength = 200;
            _words.RegisterValueChangedCallback(_ => Refresh());
            Add(_words);
            Add(Ui.Text(VoiceLabStrings.WordsNote, VoiceLabScreen.NoteClass, "vv-text-body"));

            RegisterCallback<AttachToPanelEvent>(OnAttach);
            RegisterCallback<DetachFromPanelEvent>(OnDetach);
            ShowPicks();
            ShowSamples();
        }

        /// <summary>The words picked, in the shelf's order.</summary>
        public IReadOnlyList<string> Picked => _picked;

        public string SentenceText => _sentence.text;

        public ActionButton MakeSamplesButton => _makeSamples;

        public ActionButton WordButton(string wordId)
        {
            return _wordButtons.TryGetValue(wordId, out ActionButton button) ? button : null;
        }

        /// <summary>What candidates are asked for now.</summary>
        public VoiceLabRequest Request()
        {
            return new VoiceLabRequest(_picked, _words.value?.Trim());
        }

        /// <summary>Starts from an example: its words go on the shelf.</summary>
        public void PickExample(VoiceLabExample example)
        {
            _picked = _shelf.FromExample(example);
            ShowPicks();
        }

        public void Press(string wordId)
        {
            _picked = _shelf.Toggle(_picked, wordId);
            ShowPicks();
        }

        private VisualElement ExampleRow(VoiceLabExample example)
        {
            var row = new VisualElement();
            row.AddToClassList(VoiceLabScreen.UssClass + "__example");
            IconButton play = PlayButton(VoiceLabShelf.ExampleSampleName(example), example.NameKey);
            ActionButton pick = Ui.Button(example.NameKey);
            pick.clicked += () => PickExample(example);
            var words = QuietText.Of(new Label());
            words.AddToClassList(VoiceLabScreen.NoteClass);
            _exampleWords.Add((example, words));
            row.Add(play);
            row.Add(pick);
            row.Add(words);
            return row;
        }

        private VisualElement GroupRow(VoiceLabShelfGroup group)
        {
            SettingRow row = Ui.SettingRow(group.NameKey, group.PickOne ? VoiceLabStrings.PickOne : VoiceLabStrings.PickAny);
            var words = new VisualElement();
            words.AddToClassList(WordsClass);
            foreach (VoiceLabShelfWord word in group.Words)
            {
                var chip = new VisualElement();
                chip.AddToClassList(WordClass);
                ActionButton button = Ui.Button(word.LabelKey);
                button.clicked += () => Press(word.Id);
                _wordButtons.Add(word.Id, button);
                chip.Add(button);
                chip.Add(PlayButton(VoiceLabShelf.WordSampleName(word), word.LabelKey));
                words.Add(chip);
            }

            row.Add(words);
            return row;
        }

        private IconButton PlayButton(string sampleName, string nameKey)
        {
            IconButton play = Ui.IconButton(IconName.Play).WithTooltip(VoiceLabStrings.PlaySample);
            play.userData = sampleName;
            play.clicked += () => _player.Toggle(play, SampleFile(sampleName));
            _playButtons.Add(play);
            return play;
        }

        private string SampleFile(string name)
        {
            string language = _localizer?.Locale.Code;
            return (language != null ? _lab.SampleFile(language, name) : null) ?? _lab.SampleFile(SampleFallbackLanguage, name);
        }

        private async void MakeSamples()
        {
            if (_samplesMaking != null)
            {
                return;
            }

            _samplesMaking = new CancellationTokenSource();
            _samplesFailed.style.display = DisplayStyle.None;
            SetSamplesBusy(true);
            try
            {
                await _lab.MakeSamplesAsync(_samplesMaking.Token);
            }
            catch (OperationCanceledException)
            {
            }
            catch (VoiceLabFailedException)
            {
                // The service has logged why.
                _samplesFailed.style.display = DisplayStyle.Flex;
            }
            finally
            {
                _samplesMaking.Dispose();
                _samplesMaking = null;
                SetSamplesBusy(false);
                ShowSamples();
            }
        }

        private void SetSamplesBusy(bool busy)
        {
            _makeSamples.SetEnabled(!busy);
            _samplesBusy.SetRunning(busy);
        }

        /// <summary>
        /// Play buttons are always shown, so the user sees where samples will be; one without its sample is
        /// disabled and says how to get it (the user's review, 2026-11-07). The offer to make them shows while
        /// the base is missing.
        /// </summary>
        private void ShowSamples()
        {
            bool canPlay = _player.Available;
            foreach (IconButton play in _playButtons)
            {
                bool made = SampleFile((string)play.userData) != null;
                play.style.display = canPlay ? DisplayStyle.Flex : DisplayStyle.None;
                play.SetEnabled(made);
                play.TooltipKey = made ? VoiceLabStrings.PlaySample : VoiceLabStrings.SampleNotMade;
            }

            _samplesNotice.style.display = SampleFile("base.wav") == null ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void ShowPicks()
        {
            foreach (KeyValuePair<string, ActionButton> item in _wordButtons)
            {
                // Picked words are filled, so the state shows by shape and weight as well as color.
                item.Value.Variant = _picked.Contains(item.Key) ? ButtonVariant.Primary : ButtonVariant.Secondary;
            }

            Refresh();
        }

        private void Refresh()
        {
            Func<string, string> text = key => KeyedText.Resolve(_localizer, key);
            _sentence.text = VoiceLabView.AskSentence(_shelf, _picked, _words?.value, text);
            foreach ((VoiceLabExample example, Label words) in _exampleWords)
            {
                words.text = string.Join(" · ", example.Words.Select(id => _shelf.Word(id)).Where(w => w != null).Select(w => text(w.LabelKey)));
            }
        }

        private void OnAttach(AttachToPanelEvent evt)
        {
            _localizer = UiServices.Find(this)?.Localizer;
            if (_localizer != null)
            {
                _localizer.LocaleChanged += OnLocaleChanged;
            }

            Refresh();
            ShowSamples();
        }

        private void OnDetach(DetachFromPanelEvent evt)
        {
            if (_localizer != null)
            {
                _localizer.LocaleChanged -= OnLocaleChanged;
            }

            _player.Stop();
        }

        private void OnLocaleChanged(LocaleInfo locale)
        {
            Refresh();
            ShowSamples();
        }
    }
}
