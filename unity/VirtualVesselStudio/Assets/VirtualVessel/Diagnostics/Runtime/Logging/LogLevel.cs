namespace VirtualVessel.Diagnostics.Logging
{
    /// <summary>
    /// Log levels (system design 5.3). Higher values are more severe.
    /// </summary>
    public enum LogLevel
    {
        /// <summary>Very detailed processing traces.</summary>
        Trace = 0,

        /// <summary>Internal state needed for development and debugging.</summary>
        Debug = 1,

        /// <summary>Normal major processing.</summary>
        Information = 2,

        /// <summary>Processing can continue but needs attention.</summary>
        Warning = 3,

        /// <summary>A specific operation or function failed.</summary>
        Error = 4,

        /// <summary>A serious impact on the whole application.</summary>
        Critical = 5,
    }
}
