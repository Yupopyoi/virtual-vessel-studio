using UnityEngine;

namespace VirtualVessel.Tracking.Processing
{
    /// <summary>
    /// Drops a joint position that moved faster than a person can (tracking detailed design 8.3), such
    /// as an arm jumping for one frame. If it keeps happening, the movement is real, and the third such
    /// value in a row is accepted. Immutable: each value returns the next gate.
    /// </summary>
    public readonly struct OutlierGate
    {
        public const int AcceptAfter = 3;

        private readonly int _rejected;

        private OutlierGate(int rejected)
        {
            _rejected = rejected;
        }

        /// <summary>
        /// Whether <paramref name="candidate"/> may replace <paramref name="accepted"/>, which is
        /// <paramref name="elapsed"/> seconds older. <paramref name="next"/> is the gate for the next value.
        /// </summary>
        public bool Accept(Vector3 accepted, Vector3 candidate, double elapsed, float maxSpeed, out OutlierGate next)
        {
            bool plausible = elapsed <= 0
                ? candidate == accepted
                : Vector3.Distance(accepted, candidate) / elapsed <= maxSpeed;
            if (plausible || _rejected + 1 >= AcceptAfter)
            {
                next = default;
                return true;
            }

            next = new OutlierGate(_rejected + 1);
            return false;
        }
    }
}
