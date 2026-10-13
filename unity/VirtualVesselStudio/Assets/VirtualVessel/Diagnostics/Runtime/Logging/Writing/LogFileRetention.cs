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

            List<LogFile> files = new DirectoryInfo(directory)
                .GetFiles("*" + JsonLinesFileSink.FileExtension)
                .Select(file => new LogFile(file.FullName, file.Name, file.LastWriteTimeUtc, file.Length))
                .ToList();

            foreach (LogFile file in SelectForDeletion(files, protectedPrefix, maxFiles, maxAge, maxTotalBytes, nowUtc))
            {
                try
                {
                    File.Delete(file.Path);
                    result.DeletedCount++;
                }
                catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
                {
                    result.Warnings.Add($"Failed to delete old log file {file.Path}: {exception.Message}");
                }
            }

            return result;
        }

        /// <summary>
        /// Returns the files to delete: newest first, files are kept until any limit is reached.
        /// Files whose name starts with <paramref name="protectedPrefix"/> are never selected.
        /// </summary>
        internal static List<LogFile> SelectForDeletion(
            IEnumerable<LogFile> files,
            string protectedPrefix,
            int maxFiles,
            TimeSpan maxAge,
            long maxTotalBytes,
            DateTimeOffset nowUtc)
        {
            var delete = new List<LogFile>();
            int kept = 0;
            long keptBytes = 0;
            IEnumerable<LogFile> candidates = files
                .Where(file => protectedPrefix == null || !file.Name.StartsWith(protectedPrefix, StringComparison.Ordinal))
                .OrderByDescending(file => file.LastWriteUtc);
            foreach (LogFile file in candidates)
            {
                bool tooOld = nowUtc.UtcDateTime - file.LastWriteUtc > maxAge;
                bool tooMany = kept >= maxFiles;
                bool tooLarge = keptBytes + file.Length > maxTotalBytes;
                if (tooOld || tooMany || tooLarge)
                {
                    delete.Add(file);
                    continue;
                }

                kept++;
                keptBytes += file.Length;
            }

            return delete;
        }
    }

    internal readonly struct LogFile
    {
        public LogFile(string path, string name, DateTime lastWriteUtc, long length)
        {
            Path = path;
            Name = name;
            LastWriteUtc = lastWriteUtc;
            Length = length;
        }

        public string Path { get; }

        public string Name { get; }

        public DateTime LastWriteUtc { get; }

        public long Length { get; }
    }

    internal sealed class RetentionResult
    {
        public int DeletedCount { get; set; }

        public List<string> Warnings { get; } = new List<string>();
    }
}
