using System;
using System.Globalization;
using System.Text;

namespace VirtualVessel.Diagnostics.Logging.Writing
{
    /// <summary>
    /// Minimal JSON writer for log lines. Kept in-house so logging adds no external dependency.
    /// </summary>
    internal static class JsonWriter
    {
        public static void WriteString(StringBuilder builder, string value)
        {
            if (value == null)
            {
                builder.Append("null");
                return;
            }

            builder.Append('"');
            foreach (char c in value)
            {
                switch (c)
                {
                    case '"':
                        builder.Append("\\\"");
                        break;
                    case '\\':
                        builder.Append("\\\\");
                        break;
                    case '\n':
                        builder.Append("\\n");
                        break;
                    case '\r':
                        builder.Append("\\r");
                        break;
                    case '\t':
                        builder.Append("\\t");
                        break;
                    default:
                        if (c < 0x20)
                        {
                            builder.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            builder.Append(c);
                        }

                        break;
                }
            }

            builder.Append('"');
        }

        public static void WriteValue(StringBuilder builder, object value)
        {
            switch (value)
            {
                case null:
                    builder.Append("null");
                    break;
                case string text:
                    WriteString(builder, text);
                    break;
                case bool flag:
                    builder.Append(flag ? "true" : "false");
                    break;
                case float number when float.IsNaN(number) || float.IsInfinity(number):
                    WriteString(builder, number.ToString(CultureInfo.InvariantCulture));
                    break;
                case double number when double.IsNaN(number) || double.IsInfinity(number):
                    WriteString(builder, number.ToString(CultureInfo.InvariantCulture));
                    break;
                case float number:
                    builder.Append(number.ToString("R", CultureInfo.InvariantCulture));
                    break;
                case double number:
                    builder.Append(number.ToString("R", CultureInfo.InvariantCulture));
                    break;
                case sbyte _:
                case byte _:
                case short _:
                case ushort _:
                case int _:
                case uint _:
                case long _:
                case ulong _:
                case decimal _:
                    builder.Append(((IFormattable)value).ToString(null, CultureInfo.InvariantCulture));
                    break;
                case DateTimeOffset dateTimeOffset:
                    WriteString(builder, dateTimeOffset.ToString("o", CultureInfo.InvariantCulture));
                    break;
                case DateTime dateTime:
                    WriteString(builder, dateTime.ToString("o", CultureInfo.InvariantCulture));
                    break;
                case TimeSpan timeSpan:
                    WriteString(builder, timeSpan.ToString("c", CultureInfo.InvariantCulture));
                    break;
                case IFormattable formattable:
                    WriteString(builder, formattable.ToString(null, CultureInfo.InvariantCulture));
                    break;
                default:
                    WriteString(builder, value.ToString());
                    break;
            }
        }
    }
}
