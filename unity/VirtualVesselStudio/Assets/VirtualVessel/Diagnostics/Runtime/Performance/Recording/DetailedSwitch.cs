namespace VirtualVessel.Diagnostics.Performance.Recording
{
    /// <summary>Shared on/off switch for detailed recording, read on every measuring call.</summary>
    internal sealed class DetailedSwitch
    {
        private volatile bool _enabled;

        public DetailedSwitch(bool enabled)
        {
            _enabled = enabled;
        }

        public bool IsEnabled
        {
            get => _enabled;
            set => _enabled = value;
        }
    }
}
