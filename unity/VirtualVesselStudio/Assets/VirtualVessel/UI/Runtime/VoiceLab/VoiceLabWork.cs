using System;
using System.Collections.Generic;
using System.Linq;

namespace VirtualVessel.UI.VoiceLab
{
    /// <summary>A candidate of unfinished work, with its number on the screen and the number it was made near (0 for none).</summary>
    public sealed class VoiceLabWorkCandidate
    {
        public VoiceLabWorkCandidate(int number, int from, VoiceLabCandidate candidate)
        {
            Number = number;
            From = from;
            Candidate = candidate ?? throw new ArgumentNullException(nameof(candidate));
        }

        public int Number { get; }

        public int From { get; }

        public VoiceLabCandidate Candidate { get; }
    }

    /// <summary>
    /// STEP 1 and STEP 2 left unfinished, kept so they can be continued after the app was closed (the user's decision,
    /// 2026-11-15; Voice Lab detailed design 6.6): how the candidates were asked for, the candidates, the one picked,
    /// and the timbre with the timbres held.
    /// </summary>
    public sealed class VoiceLabWork
    {
        public VoiceLabWork(
            int step,
            VoiceLabRequest request,
            IReadOnlyList<VoiceLabWorkCandidate> candidates,
            int picked,
            VoiceLabTimbre timbre,
            IReadOnlyList<VoiceLabTimbre> held,
            DateTimeOffset savedAt = default)
        {
            Step = step;
            Request = request ?? new VoiceLabRequest(null, null);
            Candidates = candidates ?? Array.Empty<VoiceLabWorkCandidate>();
            Picked = picked;
            Timbre = timbre;
            Held = held ?? Array.Empty<VoiceLabTimbre>();
            SavedAt = savedAt;
        }

        /// <summary>The step that was open: STEP 1 or STEP 2 (<see cref="VoiceLabScreen.BaseStep"/>, <see cref="VoiceLabScreen.TimbreStep"/>).</summary>
        public int Step { get; }

        /// <summary>The words, the user's words or prompt, the line, and the next seed.</summary>
        public VoiceLabRequest Request { get; }

        public IReadOnlyList<VoiceLabWorkCandidate> Candidates { get; }

        /// <summary>The number of the candidate picked; 0 for none.</summary>
        public int Picked { get; }

        public VoiceLabTimbre Timbre { get; }

        public IReadOnlyList<VoiceLabTimbre> Held { get; }

        /// <summary>When it was kept; default for work not read from storage.</summary>
        public DateTimeOffset SavedAt { get; }

        /// <summary>The candidate picked, or null. Pure.</summary>
        public VoiceLabWorkCandidate PickedCandidate => Picked > 0 ? Candidates.FirstOrDefault(c => c.Number == Picked) : null;
    }
}
