using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace VirtualVessel.Diagnostics.Logging.Writing
{
    /// <summary>
    /// Formats entries and file headers as single JSON Lines records.
    /// </summary>
    internal static class LogLineFormatter
    {
        public const int SchemaVersion = 1;

        public static string FormatEntry(LogEntry entry, StringBuilder builder)
        {
            builder.Clear();
            builder.Append("{\"ts\":");
            JsonWriter.WriteString(builder, entry.TimestampUtc.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture));
            builder.Append(",\"ms\":").Append(entry.ElapsedMilliseconds.ToString(CultureInfo.InvariantCulture));
            builder.Append(",\"lvl\":");
            JsonWriter.WriteString(builder, entry.Level.ToString());
            builder.Append(",\"mod\":");
            JsonWriter.WriteString(builder, entry.Module);

            if (entry.Category != null)
            {
                builder.Append(",\"cat\":");
                JsonWriter.WriteString(builder, entry.Category);
            }

            builder.Append(",\"msg\":");
            JsonWriter.WriteString(builder, entry.Message);

            if (entry.Context.Count > 0)
            {
                builder.Append(",\"ctx\":{");
                for (int i = 0; i < entry.Context.Count; i++)
                {
                    KeyValuePair<string, string> pair = entry.Context[i];
                    if (i > 0)
                    {
                        builder.Append(',');
                    }

                    JsonWriter.WriteString(builder, pair.Key);
                    builder.Append(':');
                    JsonWriter.WriteString(builder, pair.Value);
                }

                builder.Append('}');
            }

            if (entry.Properties.Count > 0)
            {
                builder.Append(",\"props\":{");
                for (int i = 0; i < entry.Properties.Count; i++)
                {
                    LogProperty property = entry.Properties[i];
                    if (i > 0)
                    {
                        builder.Append(',');
                    }

                    JsonWriter.WriteString(builder, property.Key ?? string.Empty);
                    builder.Append(':');
                    JsonWriter.WriteValue(builder, property.Value);
                }

                builder.Append('}');
            }

            if (entry.ExceptionType != null || entry.ExceptionText != null)
            {
                builder.Append(",\"ex\":{\"type\":");
                JsonWriter.WriteString(builder, entry.ExceptionType);
                builder.Append(",\"text\":");
                JsonWriter.WriteString(builder, entry.ExceptionText);
                builder.Append('}');
            }

            builder.Append(",\"thr\":").Append(entry.ThreadId.ToString(CultureInfo.InvariantCulture));

            if (entry.RepeatCount > 0)
            {
                builder.Append(",\"repeat\":").Append(entry.RepeatCount.ToString(CultureInfo.InvariantCulture));
            }

            builder.Append('}');
            return builder.ToString();
        }

        public static string FormatHeader(LogSessionHeader header, int part)
        {
            var builder = new StringBuilder(256);
            builder.Append("{\"header\":true,\"schemaVersion\":").Append(SchemaVersion.ToString(CultureInfo.InvariantCulture));
            builder.Append(",\"sessionId\":");
            JsonWriter.WriteString(builder, header.SessionId);
            builder.Append(",\"startedAt\":");
            JsonWriter.WriteString(builder, header.StartedAtUtc.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture));
            builder.Append(",\"part\":").Append(part.ToString(CultureInfo.InvariantCulture));
            builder.Append(",\"applicationVersion\":");
            JsonWriter.WriteString(builder, header.ApplicationVersion);
            builder.Append(",\"buildIdentifier\":");
            JsonWriter.WriteString(builder, header.BuildIdentifier);
            builder.Append(",\"unityVersion\":");
            JsonWriter.WriteString(builder, header.UnityVersion);
            builder.Append(",\"os\":");
            JsonWriter.WriteString(builder, header.OperatingSystem);
            builder.Append('}');
            return builder.ToString();
        }
    }
}
