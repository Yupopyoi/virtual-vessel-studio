using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine.UIElements;
using VirtualVessel.UI.Controls;
using VirtualVessel.UI.Icons;

namespace VirtualVessel.UI.VoiceLab
{
    /// <summary>
    /// STEP 1, pick a base (system design 14.14): the mood, candidates made in it, and choosing one by ear
    /// and by its pitch in Hz.
    /// </summary>
    public sealed class VoiceLabBaseStep : VisualElement
    {
        public const string TilesClass = VoiceLabScreen.UssClass + "__tiles";
        public const string BusyClass = VoiceLabScreen.UssClass + "__busy";

        private readonly IVoiceLab _lab;
        private readonly VoiceLabPlayer _player;
        private readonly VoiceLabDraft _draft;
        private readonly IVoiceLabFlow _flow;
        private readonly Dictionary<string, ValueSlider> _mood = new Dictionary<string, ValueSlider>(StringComparer.Ordinal);
        private readonly List<(int Number, VoiceLabCandidate Candidate)> _candidates = new List<(int, VoiceLabCandidate)>();
        private readonly Dictionary<CandidateTile, VoiceLabCandidate> _shown = new Dictionary<CandidateTile, VoiceLabCandidate>();
        private readonly TextInput _words;
        private readonly ActionButton _make;
        private readonly ActionButton _makeMore;
        private readonly ActionButton _next;
        private readonly VisualElement _busy;
        private readonly StatusChip _failed;
        private readonly Card _candidatesCard;
        private readonly Segmented _order;
        private readonly VisualElement _tiles;
        private CancellationTokenSource _making;
        private CandidateTile _playing;
        private int _made;

        public VoiceLabBaseStep(IVoiceLab lab, VoiceLabPlayer player, VoiceLabDraft draft, IVoiceLabFlow flow)
        {
            _lab = lab ?? throw new ArgumentNullException(nameof(lab));
            _player = player ?? throw new ArgumentNullException(nameof(player));
            _draft = draft ?? throw new ArgumentNullException(nameof(draft));
            _flow = flow ?? throw new ArgumentNullException(nameof(flow));
            Add(VoiceLabScreen.Lead(VoiceLabStrings.BaseLead));

            Card mood = Ui.Card(VoiceLabStrings.Mood);
            foreach (MoodSlider slider in VoiceLabView.MainMood)
            {
                mood.Add(MoodRow(slider));
            }

            Card more = Ui.Card(VoiceLabStrings.MoreMood, alternate: true, collapsible: true);
            more.Expanded = false;
            foreach (MoodSlider slider in VoiceLabView.MoreMood)
            {
                more.Add(MoodRow(slider));
            }

            mood.Add(more);
            _words = Ui.TextField(VoiceLabStrings.Words);
            _words.PlaceholderKey = VoiceLabStrings.WordsHint;
            _words.MaxLength = 200;
            mood.Add(_words);

            _make = Ui.Button(VoiceLabStrings.Make, ButtonVariant.Primary);
            _make.clicked += () => Make(more: false);
            mood.Add(VoiceLabScreen.Actions(_make, Ui.Text(VoiceLabStrings.MakeNote, VoiceLabScreen.NoteClass, "vv-text-body")));
            Add(mood);

            _busy = new VisualElement();
            _busy.AddToClassList(BusyClass);
            _busy.Add(Ui.Text(VoiceLabStrings.Making, VoiceLabScreen.NoteClass, "vv-text-body"));
            ActionButton stop = Ui.Button(VoiceLabStrings.Stop);
            stop.clicked += () => _making?.Cancel();
            _busy.Add(stop);
            _busy.style.display = DisplayStyle.None;
            Add(_busy);

            _failed = Ui.Chip(StatusKind.Error, VoiceLabStrings.MakeFailed);
            _failed.style.display = DisplayStyle.None;
            Add(_failed);

            _candidatesCard = Ui.Card(VoiceLabStrings.Candidates);
            _order = Ui.Segmented(VoiceLabStrings.SortMade, VoiceLabStrings.SortLow, VoiceLabStrings.SortHigh);
            _order.TooltipKey = VoiceLabStrings.SortTip;
            _order.RegisterValueChangedCallback(_ => ShowTiles());
            _candidatesCard.HeaderEnd.Add(_order);
            _tiles = new VisualElement();
            _tiles.AddToClassList(TilesClass);
            _candidatesCard.Add(_tiles);
            _makeMore = Ui.Button(VoiceLabStrings.MakeMore);
            _makeMore.clicked += () => Make(more: true);
            _candidatesCard.Add(VoiceLabScreen.Actions(_makeMore, Ui.Text(VoiceLabStrings.CandidatesNote, VoiceLabScreen.NoteClass, "vv-text-body")));
            Add(_candidatesCard);

            _next = Ui.Button(VoiceLabStrings.ToTimbre, ButtonVariant.Primary);
            _next.clicked += () => _flow.GoTo(VoiceLabScreen.TimbreStep);
            Add(VoiceLabScreen.Actions(_next));
            ShowTiles();
        }

        /// <summary>The candidates' tiles, in the order shown.</summary>
        public IEnumerable<CandidateTile> Tiles => _tiles.Query<CandidateTile>().ToList();

        public ActionButton MakeButton => _make;

        public ActionButton NextButton => _next;

        /// <summary>The mood as the sliders and words set it.</summary>
        public VoiceLabMood Mood()
        {
            var sliders = new Dictionary<string, float>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, ValueSlider> slider in _mood)
            {
                sliders[slider.Key] = VoiceLabView.MoodValue(slider.Value.value);
            }

            return new VoiceLabMood(sliders, _words.value?.Trim());
        }

        private VisualElement MoodRow(MoodSlider mood)
        {
            ValueSlider slider = Ui.Slider(mood.NameKey, 0f, 100f);
            slider.Step = 1f;
            slider.SetValueWithoutNotify(mood.Start * 100f);
            _mood.Add(mood.Key, slider);
            return VoiceLabTimbreStep.WithEnds(slider, mood.LowKey, mood.HighKey);
        }

        private async void Make(bool more)
        {
            if (_making != null)
            {
                return;
            }

            _making = new CancellationTokenSource();
            SetBusy(true);
            try
            {
                IReadOnlyList<VoiceLabCandidate> made = await _lab.MakeCandidatesAsync(Mood(), VoiceLabView.CandidatesPerRound, _making.Token);
                if (!more)
                {
                    // A new mood starts a new set: the earlier candidates were for another one.
                    _candidates.Clear();
                    _made = 0;
                    if (_draft.Candidate != null)
                    {
                        _draft.Candidate = null;
                        _flow.Rewind(VoiceLabScreen.BaseStep);
                    }
                }

                foreach (VoiceLabCandidate candidate in made)
                {
                    _candidates.Add((++_made, candidate));
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (VoiceLabFailedException)
            {
                // The service has logged why; the user is told it did not work.
                _failed.style.display = DisplayStyle.Flex;
            }
            finally
            {
                _making.Dispose();
                _making = null;
                SetBusy(false);
                ShowTiles();
            }
        }

        private void SetBusy(bool busy)
        {
            if (busy)
            {
                _failed.style.display = DisplayStyle.None;
            }

            _busy.style.display = busy ? DisplayStyle.Flex : DisplayStyle.None;
            _make.SetEnabled(!busy);
            _makeMore.SetEnabled(!busy);
        }

        private void ShowTiles()
        {
            _player.Stop();
            _tiles.Clear();
            _shown.Clear();
            _candidatesCard.style.display = _candidates.Count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            var medians = new int[_candidates.Count];
            for (int i = 0; i < medians.Length; i++)
            {
                medians[i] = _candidates[i].Candidate.MedianHz;
            }

            foreach (int index in VoiceLabView.Order(medians, (CandidateOrder)_order.value))
            {
                (int number, VoiceLabCandidate candidate) = _candidates[index];
                var tile = new CandidateTile(number, candidate.MedianHz, candidate.LowHz, candidate.HighHz)
                {
                    Picked = ReferenceEquals(candidate, _draft.Candidate),
                };
                tile.PlayButton.style.display = _player.Available ? DisplayStyle.Flex : DisplayStyle.None;
                tile.PlayButton.clicked += () => Play(tile, candidate);
                tile.PickButton.clicked += () => Pick(number, candidate);
                _tiles.Add(tile);
                _shown.Add(tile, candidate);
            }

            _next.SetEnabled(_draft.Candidate != null);
        }

        private async void Play(CandidateTile tile, VoiceLabCandidate candidate)
        {
            if (_playing == tile)
            {
                _player.Stop();
                return;
            }

            _playing = tile;
            tile.PlayButton.IconName = IconName.Stop;
            await _player.PlayAsync(candidate.File, () =>
            {
                tile.PlayButton.IconName = IconName.Play;
                if (_playing == tile)
                {
                    _playing = null;
                }
            });
        }

        private void Pick(int number, VoiceLabCandidate candidate)
        {
            if (ReferenceEquals(candidate, _draft.Candidate))
            {
                return;
            }

            _draft.Candidate = candidate;
            _draft.CandidateNumber = number;
            _flow.Rewind(VoiceLabScreen.BaseStep);

            // Only the marks change, so a candidate being listened to goes on playing.
            foreach (KeyValuePair<CandidateTile, VoiceLabCandidate> shown in _shown)
            {
                shown.Key.Picked = ReferenceEquals(shown.Value, candidate);
            }

            _next.SetEnabled(true);
        }
    }
}
