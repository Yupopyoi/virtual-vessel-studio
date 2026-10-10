using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace VirtualVessel.Diagnostics.Logging.Writing
{
    /// <summary>
    /// Writes entries to JSON Lines files, one series of files per session.
    /// </summary>
    /// <remarks>
    /// When a file reaches the size limit, writing continues in the next part of the same session,
    /// each part starting with its own header so it can be read on its own.
    /// </remarks>
    internal sealed class JsonLinesFileSink : ILogSink
    {
        public const string FileExtension = ".jsonl";

        private static readonly Encoding s_encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        private readonly string _directory;
        private readonly LogSessionHeader _header;
        private readonly long _maxFileSizeBytes;
        private readonly StringBuilder _lineBuilder = new StringBuilder(512);

        private StreamWriter _writer;
        private long _currentSize;
        private int _part;

        public JsonLinesFileSink(string directory, LogSessionHeader header, long maxFileSizeBytes)
        {
            _directory = directory ?? throw new ArgumentNullException(nameof(directory));
            _header = header ?? throw new ArgumentNullException(nameof(header));
            _maxFileSizeBytes = maxFileSizeBytes;
        }

        public string Name => "File";

        public string CurrentFilePath { get; private set; }

        /// <summary>The prefix shared by every part of this session, used to protect them from retention.</summary>
        public string SessionFilePrefix => BuildPrefix(_header);

        /// <summary>
        /// Creates the directory and the first file. Throws if the file cannot be created, which fails
        /// the required logging service.
        /// </summary>
        public void Open()
        {
            Directory.CreateDirectory(_directory);
            OpenNextPart();
        }

        public void Write(LogEntry entry)
        {
            string line = LogLineFormatter.FormatEntry(entry, _lineBuilder);
            WriteLine(line);

            // Flush errors immediately so that the lines just before a crash are on disk.
            if (entry.Level >= LogLevel.Error)
            {
                _writer.Flush();
            }
        }

        public void Flush()
        {
            _writer?.Flush();
        }

        public void Dispose()
        {
            _writer?.Dispose();
            _writer = null;
        }

        internal static string BuildPrefix(LogSessionHeader header)
        {
            string shortId = header.SessionId.Length > 8 ? header.SessionId.Substring(0, 8) : header.SessionId;
            return header.StartedAtUtc.UtcDateTime.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "_" + shortId + "_";
        }

        private void WriteLine(string line)
        {
            long lineBytes = s_encoding.GetByteCount(line) + 1;
            if (_currentSize > 0 && _currentSize + lineBytes > _maxFileSizeBytes)
            {
                OpenNextPart();
            }

            _writer.Write(line);
            _writer.Write('\n');
            _currentSize += lineBytes;
        }

        private void OpenNextPart()
        {
            _writer?.Dispose();
            _part++;

            string fileName = SessionFilePrefix + _part.ToString("000", CultureInfo.InvariantCulture) + FileExtension;
            CurrentFilePath = Path.Combine(_directory, fileName);

            // FileShare.Read lets developers open the current log in an editor while the app runs.
            var stream = new FileStream(CurrentFilePath, FileMode.Create, FileAccess.Write, FileShare.Read);
            _writer = new StreamWriter(stream, s_encoding, bufferSize: 16 * 1024);
            _currentSize = 0;

            string header = LogLineFormatter.FormatHeader(_header, _part);
            _writer.Write(header);
            _writer.Write('\n');
            _currentSize += s_encoding.GetByteCount(header) + 1;
            _writer.Flush();
        }
    }
}
