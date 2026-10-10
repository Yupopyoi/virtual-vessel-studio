namespace VirtualVessel.Diagnostics.Logging
{
    /// <summary>
    /// Creates logs. Passed to services by the composition root; there is no static access.
    /// </summary>
    public interface ILogProvider
    {
        /// <param name="module">A module name from system design 2.4, such as "Avatar" or "VoiceLab".</param>
        /// <param name="category">An optional processing category within the module, such as "RvcTraining".</param>
        ILog GetLog(string module, string category = null);
    }
}
