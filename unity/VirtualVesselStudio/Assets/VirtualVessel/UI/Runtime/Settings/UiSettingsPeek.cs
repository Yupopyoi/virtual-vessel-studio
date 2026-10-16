using System;
using System.IO;
using VirtualVessel.ProjectData.Config;

namespace VirtualVessel.UI.Settings
{
    /// <summary>
    /// Reads single values from ui-settings.json without changing anything, for the startup problem
    /// screen (UI foundation detailed design 7.9).
    /// </summary>
    /// <remarks>
    /// Unlike <see cref="UiSettingsService.Load"/>, a broken file is neither moved aside nor reported:
    /// the screen shows while something else has already failed, and must not touch the user's files.
    /// </remarks>
    public static class UiSettingsPeek
    {
        /// <summary>The saved language, or null when there is none or the file cannot be read.</summary>
        public static string ReadLanguage(string configDirectory)
        {
            if (string.IsNullOrEmpty(configDirectory))
            {
                return null;
            }

            try
            {
                var file = new FileInfo(Path.Combine(configDirectory, UiSettings.FileName));
                if (!file.Exists || file.Length > ConfigFileStore<UiSettings>.MaxFileBytes)
                {
                    return null;
                }

                return ConfigDocument.Parse(File.ReadAllText(file.FullName)).Get<string>("language");
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is FormatException || exception is ArgumentException || exception is NotSupportedException)
            {
                return null;
            }
        }
    }
}
