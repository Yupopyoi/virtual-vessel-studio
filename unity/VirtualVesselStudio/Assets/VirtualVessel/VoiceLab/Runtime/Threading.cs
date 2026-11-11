using System;

namespace VirtualVessel.VoiceLab
{
    /// <summary>
    /// Passes progress on at once, on the thread it arrives on. Progress&lt;T&gt; would post it to the caller's context, and a
    /// caller waiting on that context would see it only at the end.
    /// </summary>
    internal sealed class Forward<T> : IProgress<T>
    {
        private readonly Action<T> _report;

        public Forward(Action<T> report)
        {
            _report = report;
        }

        public void Report(T value)
        {
            _report(value);
        }
    }

    /// <summary>A value one thread writes and another reads, such as progress a script reports for the screen.</summary>
    internal sealed class Shared<T>
    {
        private readonly object _gate = new object();
        private T _value;

        public T Value
        {
            get
            {
                lock (_gate)
                {
                    return _value;
                }
            }

            set
            {
                lock (_gate)
                {
                    _value = value;
                }
            }
        }
    }
}
