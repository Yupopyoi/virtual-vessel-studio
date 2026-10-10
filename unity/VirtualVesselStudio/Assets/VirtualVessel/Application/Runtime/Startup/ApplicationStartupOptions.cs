using System;
using System.Collections.Generic;

namespace VirtualVessel.Application.Startup
{
    /// <summary>
    /// Developer-facing startup settings. None of these are shown in the normal user UI.
    /// </summary>
    public sealed class ApplicationStartupOptions
    {
        public const string DataRootArgument = "--data-root";

        /// <summary>Lets tests and CI redirect the Data Root without command-line access.</summary>
        public const string DataRootEnvironmentVariable = "VIRTUAL_VESSEL_DATA_ROOT";

        public static readonly TimeSpan DefaultShutdownTimeout = TimeSpan.FromSeconds(10);

        public ApplicationStartupOptions(string dataRootOverride, TimeSpan shutdownTimeout)
        {
            DataRootOverride = string.IsNullOrWhiteSpace(dataRootOverride) ? null : dataRootOverride;
            ShutdownTimeout = shutdownTimeout;
        }

        /// <summary>Null when the normal resolution order should be used.</summary>
        public string DataRootOverride { get; }

        public TimeSpan ShutdownTimeout { get; }

        /// <summary>
        /// Reads options from the command line, falling back to environment variables.
        /// The command line wins so that an explicit launch argument is never silently overridden.
        /// </summary>
        public static ApplicationStartupOptions FromEnvironment(IReadOnlyList<string> commandLineArguments, Func<string, string> getEnvironmentVariable)
        {
            string dataRoot = FindArgumentValue(commandLineArguments, DataRootArgument)
                ?? getEnvironmentVariable?.Invoke(DataRootEnvironmentVariable);

            return new ApplicationStartupOptions(dataRoot, DefaultShutdownTimeout);
        }

        private static string FindArgumentValue(IReadOnlyList<string> arguments, string name)
        {
            if (arguments == null)
            {
                return null;
            }

            for (int i = 0; i < arguments.Count - 1; i++)
            {
                if (string.Equals(arguments[i], name, StringComparison.OrdinalIgnoreCase))
                {
                    return arguments[i + 1];
                }
            }

            return null;
        }
    }
}
