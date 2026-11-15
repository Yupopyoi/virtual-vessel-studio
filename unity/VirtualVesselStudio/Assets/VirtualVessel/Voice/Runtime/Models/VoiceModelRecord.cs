using System.Collections.Generic;

namespace VirtualVessel.Voice.Models
{
    /// <summary>model.json: what a registered voice is called and where it came from (Voice detailed design 6.1).</summary>
    public sealed class VoiceModelRecord
    {
        public const int SchemaVersion = 1;

        public string Id { get; set; }

        public string Name { get; set; }

        /// <summary>The Training Run it was made from, for Voice Lab; Voice does not read it.</summary>
        public string RunId { get; set; }

        public string RegisteredAtUtc { get; set; }

        /// <summary>For matching the user's pitch (Voice Lab detailed design 8.3.4); 0 when unknown.</summary>
        public int MedianPitchHz { get; set; }

        public Dictionary<string, string> Versions { get; set; } = new Dictionary<string, string>();
    }

    /// <summary>A voice to register: its files in a folder, copied in as they are (system design 13.21).</summary>
    public sealed class NewVoiceModel
    {
        public NewVoiceModel(string id, string name, string runId, string sourceFolder, IReadOnlyList<string> files, IReadOnlyDictionary<string, string> versions)
        {
            Id = id;
            Name = name;
            RunId = runId;
            SourceFolder = sourceFolder;
            Files = files ?? new string[0];
            Versions = versions ?? new Dictionary<string, string>();
        }

        public string Id { get; }

        public string Name { get; }

        public string RunId { get; }

        public string SourceFolder { get; }

        /// <summary>
        /// File names in <see cref="SourceFolder"/>: at least <see cref="VoiceModelStore.RuntimeFiles"/>. Others, such
        /// as Voice Lab's design.json, are copied without being read.
        /// </summary>
        public IReadOnlyList<string> Files { get; }

        public IReadOnlyDictionary<string, string> Versions { get; }
    }
}
