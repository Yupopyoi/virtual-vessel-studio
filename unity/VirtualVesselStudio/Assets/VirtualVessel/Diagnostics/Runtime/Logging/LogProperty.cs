namespace VirtualVessel.Diagnostics.Logging
{
    /// <summary>
    /// A key / value pair attached to a structured log entry (system design 5.4).
    /// </summary>
    public readonly struct LogProperty
    {
        public LogProperty(string key, object value)
        {
            Key = key;
            Value = value;
        }

        public string Key { get; }

        public object Value { get; }
    }
}
