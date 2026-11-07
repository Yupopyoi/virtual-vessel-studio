using System;
using System.Collections.Generic;
using System.Linq;

namespace VirtualVessel.UI.VoiceLab
{
    /// <summary>
    /// STEP 1's shelf of words (Voice Lab detailed design 8.3.1), as services/voice-lab/voicelab/shelf.json
    /// defines it. Each word fills one slot of the instruction; a slot takes one word. The words' texts are
    /// "lab.word.&lt;id&gt;" (the label) and "lab.word.&lt;id&gt;.phrase" (its part of the read-back sentence).
    /// </summary>
    public sealed class VoiceLabShelf
    {
        public VoiceLabShelf(IReadOnlyList<VoiceLabShelfGroup> groups, IReadOnlyList<VoiceLabExample> examples)
        {
            Groups = groups ?? Array.Empty<VoiceLabShelfGroup>();
            Examples = examples ?? Array.Empty<VoiceLabExample>();
        }

        public IReadOnlyList<VoiceLabShelfGroup> Groups { get; }

        public IReadOnlyList<VoiceLabExample> Examples { get; }

        public IEnumerable<VoiceLabShelfWord> Words => Groups.SelectMany(g => g.Words);

        public VoiceLabShelfGroup GroupOf(string wordId)
        {
            return Groups.FirstOrDefault(g => g.Words.Any(w => w.Id == wordId));
        }

        public VoiceLabShelfWord Word(string wordId)
        {
            return Words.FirstOrDefault(w => w.Id == wordId);
        }

        /// <summary>
        /// The words picked at the start: the default of each group that takes one word, so every such group
        /// shows a choice. Groups that take any start empty; their slots keep their defaults unpicked. Pure.
        /// </summary>
        public IReadOnlyList<string> Start()
        {
            return Groups.Where(g => g.PickOne).SelectMany(g => g.Words.Where(w => w.IsDefault)).Select(w => w.Id).ToArray();
        }

        /// <summary>An example's words, with the start's words for the slots it leaves open. Pure.</summary>
        public IReadOnlyList<string> FromExample(VoiceLabExample example)
        {
            var picked = new List<string>(example.Words.Where(id => Word(id) != null));
            foreach (string id in Start())
            {
                if (!picked.Any(p => Word(p).Slot == Word(id).Slot))
                {
                    picked.Add(id);
                }
            }

            return InShelfOrder(picked);
        }

        /// <summary>
        /// The picks after pressing a word. A word replaces the one in its slot; pressing a picked word takes it
        /// off, except in a group that takes one word, which always keeps one. Pure.
        /// </summary>
        public IReadOnlyList<string> Toggle(IReadOnlyList<string> picked, string wordId)
        {
            VoiceLabShelfWord word = Word(wordId);
            if (word == null)
            {
                return picked;
            }

            var next = new List<string>(picked);
            if (next.Contains(wordId))
            {
                if (!GroupOf(wordId).PickOne)
                {
                    next.Remove(wordId);
                }

                return InShelfOrder(next);
            }

            next.RemoveAll(id => Word(id)?.Slot == word.Slot);
            next.Add(wordId);
            return InShelfOrder(next);
        }

        /// <summary>The sample a word plays: the base for a default word, which adds nothing to it. Pure.</summary>
        public static string WordSampleName(VoiceLabShelfWord word)
        {
            return word.IsDefault ? "base.wav" : "word_" + word.Id + ".wav";
        }

        public static string ExampleSampleName(VoiceLabExample example)
        {
            return "example_" + example.Id + ".wav";
        }

        private IReadOnlyList<string> InShelfOrder(IEnumerable<string> picked)
        {
            var set = new HashSet<string>(picked, StringComparer.Ordinal);
            return Words.Where(w => set.Contains(w.Id)).Select(w => w.Id).ToArray();
        }
    }

    public sealed class VoiceLabShelfGroup
    {
        public VoiceLabShelfGroup(string id, bool main, IReadOnlyList<VoiceLabShelfWord> words)
        {
            Id = id;
            Main = main;
            Words = words ?? Array.Empty<VoiceLabShelfWord>();
        }

        public string Id { get; }

        /// <summary>Shown at first; the others are folded under More detail.</summary>
        public bool Main { get; }

        public IReadOnlyList<VoiceLabShelfWord> Words { get; }

        /// <summary>One slot: the group takes one word, like a choice of one.</summary>
        public bool PickOne => Words.Select(w => w.Slot).Distinct().Count() == 1;

        public string NameKey => "lab.group." + Id;
    }

    public sealed class VoiceLabShelfWord
    {
        public VoiceLabShelfWord(string id, string slot, bool isDefault)
        {
            Id = id;
            Slot = slot;
            IsDefault = isDefault;
        }

        public string Id { get; }

        public string Slot { get; }

        /// <summary>Its slot's default: picking it changes nothing in the instruction.</summary>
        public bool IsDefault { get; }

        public string LabelKey => "lab.word." + Id;

        public string PhraseKey => "lab.word." + Id + ".phrase";
    }

    public sealed class VoiceLabExample
    {
        public VoiceLabExample(string id, IReadOnlyList<string> words)
        {
            Id = id;
            Words = words ?? Array.Empty<string>();
        }

        public string Id { get; }

        public IReadOnlyList<string> Words { get; }

        public string NameKey => "lab.example." + Id;
    }

    /// <summary>What candidates are asked for: the shelf's word ids and the user's own words.</summary>
    public sealed class VoiceLabRequest
    {
        public VoiceLabRequest(IReadOnlyList<string> words, string freeWords)
        {
            Words = words ?? Array.Empty<string>();
            FreeWords = freeWords ?? string.Empty;
        }

        public IReadOnlyList<string> Words { get; }

        public string FreeWords { get; }
    }
}
