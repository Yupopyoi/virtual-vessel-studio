using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace VirtualVessel.Diagnostics.Logging.Writing
{
    /// <summary>
    /// Deletes old log files so that logs cannot fill the disk (system design 5.25).
    /// </summary>
    /// <remarks>
    /// Files are kept newest first until any limit (count, age, or total size) is reached; everything
    /// older is deleted. Files of the current session are never deleted.
    /// </remarks>
    internal static class LogFileRetention
    {
        public static RetentionResult Apply(
            string directory,
            string protectedPrefix,
            int maxFiles,
            TimeSpan maxAge,
            long maxTotalBytes,
            DateTimeOffset nowUtc)
        {
            var result = new RetentionResult();
            if (!Directory.Exists(directory))
            {
                return result;
            }

            List<FileInfo> files = new DirectoryInfo(directory)
                .GetFiles("*" + JsonLinesFileSink.FileExtension)
                .Where(file => protectedPrefix == null || !file.Name.StartsWith(protectedPrefix, StringComparison.Ordinal))
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .ToList();

            int kept = 0;
            long keptBytes = 0;
            foreach (FileInfo file in files)
            {
                bool tooOld = nowUtc.UtcDateTime - file.LastWriteTimeUtc > maxAge;
                bool tooMany = kept >= maxFiles;
                bool tooLarge = keptBytes + file.Length > maxTotalBytes;

                if (!tooOld && !tooMany && !tooLarge)
                {
                    kept++;
                    keptBytes += file.Length;
                    continue;
                }

                try
                {
                    file.Delete();
                    result.DeletedCount++;
                }
                catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
                {
                    result.Warnings.Add($"Failed to delete old log file {file.FullName}: {exception.Message}");
                }
            }

            return result;
        }
    }

    internal sealed class RetentionResult
    {
        public int DeletedCount { get; set; }

        public List<string> Warnings { get; } = new List<string>();
    }
}
