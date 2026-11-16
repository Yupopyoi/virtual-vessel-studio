using System;
using VirtualVessel.ProjectData.Config;

namespace VirtualVessel.VoiceLab.Scripts
{
    /// <summary>The kind of one line a script writes (Voice Lab detailed design 8.5).</summary>
    public enum ScriptLineKind
    {
        /// <summary>Not a JSON Lines record; scripts keep standard output for these, so it is unexpected.</summary>
        Other,
        Progress,
        Result,
        Error,
    }

    /// <summary>One line of a script's standard output, read.</summary>
    public sealed class ScriptLine
    {
        private ScriptLine(ScriptLineKind kind, ConfigDocument fields)
        {
            Kind = kind;
            Fields = fields ?? ConfigDocument.Create();
        }

        public ScriptLineKind Kind { get; }

        /// <summary>The record as it came, for the result's own fields.</summary>
        public ConfigDocument Fields { get; }

        public string Stage => Fields.Get<string>("stage");

        public int Done => Fields.Get("done", 0);

        public int Total => Fields.Get("total", 0);

        public string ErrorCode => Fields.Get<string>("code");

        public string ErrorMessage => Fields.Get<string>("message");

        /// <summary>A line read as a record. Pure.</summary>
        public static ScriptLine Parse(string line)
        {
            if (string.IsNullOrWhiteSpace(line) || line.TrimStart()[0] != '{')
            {
                return new ScriptLine(ScriptLineKind.Other, null);
            }

            ConfigDocument fields;
            try
            {
                fields = ConfigDocument.Parse(line);
            }
            catch (FormatException)
            {
                return new ScriptLine(ScriptLineKind.Other, null);
            }

            switch (fields.Get<string>("type"))
            {
                case "progress":
                    return new ScriptLine(ScriptLineKind.Progress, fields);
                case "result":
                    return new ScriptLine(ScriptLineKind.Result, fields);
                case "error":
                    return new ScriptLine(ScriptLineKind.Error, fields);
                default:
                    return new ScriptLine(ScriptLineKind.Other, fields);
            }
        }
    }
}
