using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine.UIElements;
using VirtualVessel.UI.Controls;

namespace VirtualVessel.UI.VoiceLab
{
    /// <summary>
    /// STEP 1, pick a base (system design 14.14): the mood, candidates made in it, and choosing one by ear
    /// and by its pitch in Hz.
    /// </summary>
    public sealed class VoiceLabBaseStep : VisualElement
    {
        public const string TilesClass = VoiceLabScreen.UssClass + "__tiles";

        private readonly IVoiceLab _lab;
        private readonly VoiceLabPlayer _player;
        private readonly VoiceLabDraft _draft;
        private readonly IVoiceLabFlow _flow;
        private readonly VoiceLabShelfView _request;
        private readonly List<(int Number, int From, VoiceLabCandidate Candidate)> _candidates = new List<(int, int, VoiceLabCandidate)>();
        private readonly Dictionary<CandidateTile, VoiceLabCandidate> _shown = new Dictionary<CandidateTile, VoiceLabCandidate>();
        private readonly ActionButton _make;
        private readonly ActionButton _makeMore;
        private readonly ActionButton _makeNear;
        private readonly ActionButton _next;
        private readonly VoiceLabMakingBar _busy;
        private readonly Segmented _count;
        private readonly Label _instruction;
        private readonly StatusChip _failed;
        private readonly Card _candidatesCard;
        private readonly Segmented _order;
        private readonly VisualElement _tiles;
        private CancellationTokenSource _making;
        private int _made;

        public VoiceLabBaseStep(IVoiceLab lab, VoiceLabPlayer player, VoiceLabDraft draft, IVoiceLabFlow flow)
        {
            _lab = lab ?? throw new ArgumentNullException(nameof(lab));
            _player = player ?? throw new ArgumentNullException(nameof(player));
            _draft = draft ?? throw new ArgumentNullException(nameof(draft));
            _flow = flow ?? throw new ArgumentNullException(nameof(flow));
            Add(VoiceLabScreen.Lead(VoiceLabStrings.BaseLead));

            // Examples, the word shelf, the read-back sentence, and the user's words (Voice Lab detailed design 8.3.1).
            _request = new VoiceLabShelfView(lab, player);
            Add(_request);

            Card mood = Ui.Card(VoiceLabStrings.Mood);
            _count = Ui.Segmented(CountKeys());
            _count.TooltipKey = VoiceLabStrings.CountTip;
            _count.SetValueWithoutNotify(VoiceLabView.DefaultCountIndex);
            var countRow = Ui.SettingRow(VoiceLabStrings.Count);
            countRow.Add(_count);
            mood.Add(countRow);

            _make = Ui.Button(VoiceLabStrings.Make, ButtonVariant.Primary);
            _make.clicked += () => Make(MakeKind.New);
            mood.Add(VoiceLabScreen.Actions(_make, Ui.Text(VoiceLabStrings.MakeNote, VoiceLabScreen.NoteClass, "vv-text-body")));
            Add(mood);

            _busy = new VoiceLabMakingBar(() => _lab.CandidatesProgress, VoiceLabStrings.Making, () => _making?.Cancel());
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
            _makeMore.clicked += () => Make(MakeKind.More);
            _makeNear = Ui.Button(VoiceLabStrings.MakeNear);
            _makeNear.TooltipKey = VoiceLabStrings.MakeNearTip;
            _makeNear.clicked += () => Make(MakeKind.Near);
            _candidatesCard.Add(VoiceLabScreen.Actions(_makeNear, _makeMore, Ui.Text(VoiceLabStrings.CandidatesNote, VoiceLabScreen.NoteClass, "vv-text-body")));

            // The English instruction the candidates were made from, for whoever wants to read it.
            Card instruction = Ui.Card(VoiceLabStrings.Instruction, collapsible: true);
            instruction.Expanded = false;
            _instruction = QuietText.Of(new Label());
            _instruction.AddToClassList("vv-text-body");
            _instruction.AddToClassList(VoiceLabScreen.NoteClass);
            instruction.Add(_instruction);
            _candidatesCard.Add(instruction);
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

        /// <summary>How many candidates to make.</summary>
        public Segmented CountChooser => _count;

        /// <summary>Examples, words, and the user's own words.</summary>
        public VoiceLabShelfView RequestView => _request;

        public string InstructionText => _instruction.text;

        private enum MakeKind
        {
            New,
            More,
            Near,
        }

        public ActionButton MakeNearButton => _makeNear;

        private async void Make(MakeKind kind)
        {
            if (_making != null)
            {
                return;
            }

            VoiceLabCandidate near = kind == MakeKind.Near ? _draft.Candidate : null;
            int nearNumber = kind == MakeKind.Near ? _draft.CandidateNumber : 0;
            if (kind == MakeKind.Near && near == null)
            {
                return;
            }

            _making = new CancellationTokenSource();
            SetBusy(true);
            try
            {
                IReadOnlyList<VoiceLabCandidate> made = await _lab.MakeCandidatesAsync(_request.Request(), VoiceLabView.CandidateCounts[_count.value], near, _making.Token);
                if (kind == MakeKind.New)
                {
                    // A new request starts a new set: the earlier candidates were for another one.
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
                    _candidates.Add((++_made, nearNumber, candidate));
                }

                _instruction.text = made.Count > 0 ? made[made.Count - 1].Instruction : _instruction.text;
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

            _busy.SetRunning(busy);

            _count.SetEnabled(!busy);
            _make.SetEnabled(!busy);
            _makeMore.SetEnabled(!busy);
            _makeNear.SetEnabled(!busy && _draft.Candidate != null);
        }

        private static string[] CountKeys()
        {
            var keys = new string[VoiceLabView.CandidateCounts.Count];
            for (int i = 0; i < keys.Length; i++)
            {
                keys[i] = VoiceLabStrings.Count + "." + VoiceLabView.CandidateCounts[i].ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

            return keys;
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
                (int number, int from, VoiceLabCandidate candidate) = _candidates[index];
                var tile = new CandidateTile(number, candidate.MedianHz, candidate.LowHz, candidate.HighHz)
                {
                    Picked = ReferenceEquals(candidate, _draft.Candidate),
                    FromNumber = from,
                };
                tile.PlayButton.style.display = _player.Available ? DisplayStyle.Flex : DisplayStyle.None;
                tile.PlayButton.clicked += () => _player.Toggle(tile.PlayButton, candidate.File);
                tile.PickButton.clicked += () => Pick(number, candidate);
                _tiles.Add(tile);
                _shown.Add(tile, candidate);
            }

            _next.SetEnabled(_draft.Candidate != null);
            _makeNear.SetEnabled(_making == null && _draft.Candidate != null);
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
            _makeNear.SetEnabled(_making == null);
        }
    }
}
