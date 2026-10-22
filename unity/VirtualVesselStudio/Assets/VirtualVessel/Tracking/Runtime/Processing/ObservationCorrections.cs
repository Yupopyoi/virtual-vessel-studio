using System;
using System.Collections.Generic;

namespace VirtualVessel.Tracking.Processing
{
    /// <summary>
    /// A correction of raw observations for a kind of input (tracking detailed design 8.4): common
    /// processing, not a provider's own, applied before smoothing to every provider whose
    /// <see cref="ObservationTraits"/> include <see cref="Requires"/>. A new correction is a new
    /// implementation added to <see cref="ObservationCorrections.Catalog"/>; a new provider only states its
    /// traits.
    /// </summary>
    public interface IObservationCorrection
    {
        /// <summary>The traits an observation needs for this correction to apply.</summary>
        ObservationTraits Requires { get; }

        /// <summary>
        /// Corrects <paramref name="observation"/> in place. It is the processing's own copy of the raw
        /// observation, reused every frame, so nothing is allocated (CLAUDE.md 15.1, 23); the raw one, which
        /// recordings keep, is never changed.
        /// </summary>
        void Apply(TrackingFrame observation);
    }

    /// <summary>
    /// The corrections that suit one provider, chosen by its traits, and their use on each raw
    /// observation (tracking detailed design 8.4).
    /// </summary>
    public sealed class ObservationCorrections
    {
        /// <summary>Every correction this version knows, in the order they apply.</summary>
        public static readonly IReadOnlyList<IObservationCorrection> Catalog = new IObservationCorrection[]
        {
            new CameraAxisArmStraightening(),
        };

        /// <summary>No correction, as for a provider without traits that need any.</summary>
        public static readonly ObservationCorrections None = new ObservationCorrections(Array.Empty<IObservationCorrection>());

        private readonly IReadOnlyList<IObservationCorrection> _corrections;

        private ObservationCorrections(IReadOnlyList<IObservationCorrection> corrections)
        {
            _corrections = corrections;
        }

        /// <summary>Whether any correction applies; without one, the raw observation is used as it is.</summary>
        public bool Any => _corrections.Count > 0;

        public IReadOnlyList<IObservationCorrection> Corrections => _corrections;

        /// <summary>The corrections from <paramref name="catalog"/> whose requirements <paramref name="traits"/> meet, in order. Pure.</summary>
        public static ObservationCorrections For(ObservationTraits traits, IReadOnlyList<IObservationCorrection> catalog)
        {
            var chosen = new List<IObservationCorrection>();
            foreach (IObservationCorrection correction in catalog ?? Array.Empty<IObservationCorrection>())
            {
                if (correction.Requires != ObservationTraits.None && (traits & correction.Requires) == correction.Requires)
                {
                    chosen.Add(correction);
                }
            }

            return chosen.Count == 0 ? None : new ObservationCorrections(chosen);
        }

        /// <summary>Copies <paramref name="raw"/> into <paramref name="into"/> and corrects the copy; <paramref name="raw"/> is left as it is.</summary>
        public void Apply(TrackingFrame raw, TrackingFrame into)
        {
            into.CopyFrom(raw);
            foreach (IObservationCorrection correction in _corrections)
            {
                correction.Apply(into);
            }
        }
    }
}
