using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using VirtualVessel.ProjectData.Config;

namespace VirtualVessel.VoiceLab.Activation
{
    /// <summary>Where a downloaded component goes.</summary>
    public enum VoiceComponentPlace
    {
        /// <summary>Into the training environment's version folder, deleted with it.</summary>
        Environment,

        /// <summary>Into the Data Root's VoiceModels/Shared/1, which streaming uses; it stays when the environment goes.</summary>
        SharedModels,
    }

    /// <summary>One file to download, with where it comes from, what it must hash to, and where it goes.</summary>
    public sealed class VoiceComponent
    {
        public VoiceComponent(string id, string url, string sha256, long size, string license, VoiceComponentPlace place, string target, bool extract, int strip)
        {
            Id = id;
            Url = url;
            Sha256 = sha256;
            Size = size;
            License = license;
            Place = place;
            Target = target;
            Extract = extract;
            Strip = strip;
        }

        public string Id { get; }

        /// <summary>The source it is published at.</summary>
        public string Url { get; }

        /// <summary>Lowercase hex; a file that does not match is never used (system design 13.4).</summary>
        public string Sha256 { get; }

        public long Size { get; }

        public string License { get; }

        public VoiceComponentPlace Place { get; }

        /// <summary>A relative path under the place: the file's, or the folder a zip is extracted into.</summary>
        public string Target { get; }

        /// <summary>Whether it is a zip to extract into <see cref="Target"/>.</summary>
        public bool Extract { get; }

        /// <summary>Leading folders to drop from the zip's paths, such as the one GitHub's archives have.</summary>
        public int Strip { get; }
    }

    /// <summary>
    /// What activation downloads (Voice Lab detailed design 6.2): voice-components.json, bundled with the app and fixed
    /// for its version (system design 13.5), built by tools/voice/voice_components.py from the files that were tested.
    /// </summary>
    public sealed class VoiceComponentManifest
    {
        public const int SchemaVersion = 1;
        public const string FileName = "voice-components.json";

        private static readonly Regex s_sha256 = new Regex("^[0-9a-f]{64}$");
        private static readonly Regex s_python = new Regex(@"^\d+\.\d+\.\d+$");
        private static readonly Regex s_version = new Regex(@"^[0-9A-Za-z][0-9A-Za-z._-]*$");

        private VoiceComponentManifest(string version, string python, string lockFile, IReadOnlyList<VoiceComponent> components)
        {
            Version = version;
            Python = python;
            LockFile = lockFile;
            Components = components;
        }

        /// <summary>The environment's version, also its folder's name.</summary>
        public string Version { get; }

        /// <summary>The Python version uv installs.</summary>
        public string Python { get; }

        /// <summary>The lock file beside the scripts, which pins every package with its hashes.</summary>
        public string LockFile { get; }

        public IReadOnlyList<VoiceComponent> Components { get; }

        /// <summary>Bytes to download in all.</summary>
        public long TotalSize => Components.Sum(c => c.Size);

        /// <summary>The list from its JSON, or null with every problem found. Pure.</summary>
        public static VoiceComponentManifest Parse(string json, out IReadOnlyList<string> problems)
        {
            var found = new List<string>();
            problems = found;
            ConfigDocument document;
            try
            {
                document = ConfigDocument.Parse(json ?? string.Empty);
            }
            catch (FormatException exception)
            {
                found.Add($"The component list is not valid JSON: {exception.Message}");
                return null;
            }

            int schema = document.Get("schemaVersion", 0);
            if (schema != SchemaVersion)
            {
                found.Add($"The component list has schema version {schema}; this app reads {SchemaVersion}.");
                return null;
            }

            string version = document.Get<string>("version");
            string python = document.Get<string>("python");
            string lockFile = document.Get<string>("lock");
            Check(found, version != null && s_version.IsMatch(version), "'version' must be a plain name, as it names a folder.");
            Check(found, python != null && s_python.IsMatch(python), "'python' must be a version such as 3.12.2.");
            Check(found, IsRelativeInside(lockFile), "'lock' must be a file beside the scripts.");

            List<Entry> entries = document.Get<List<Entry>>("components") ?? new List<Entry>();
            Check(found, entries.Count > 0, "There are no components.");
            var components = new List<VoiceComponent>();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (Entry entry in entries)
            {
                string name = entry?.Id ?? "(no id)";
                if (entry == null || string.IsNullOrEmpty(entry.Id) || !ids.Add(entry.Id))
                {
                    found.Add($"Component {name} has no id or one already used.");
                    continue;
                }

                VoiceComponentPlace? place = entry.Place == "environment" ? VoiceComponentPlace.Environment : entry.Place == "sharedModels" ? VoiceComponentPlace.SharedModels : (VoiceComponentPlace?)null;
                bool extract = entry.Extract == "zip";
                int before = found.Count;
                Check(found, IsHttps(entry.Url), $"{name}: 'url' must be https.");
                Check(found, entry.Sha256 != null && s_sha256.IsMatch(entry.Sha256), $"{name}: 'sha256' must be 64 lowercase hex digits.");
                Check(found, entry.Size > 0, $"{name}: 'size' must be above 0.");
                Check(found, !string.IsNullOrWhiteSpace(entry.License), $"{name}: 'license' is missing.");
                Check(found, place != null, $"{name}: 'place' must be environment or sharedModels.");
                Check(found, IsRelativeInside(entry.Target), $"{name}: 'target' must be a relative path that stays inside its place.");
                Check(found, entry.Extract == null || extract, $"{name}: 'extract' can only be zip.");
                Check(found, entry.Strip >= 0 && (extract || entry.Strip == 0), $"{name}: 'strip' is for zips, and cannot be negative.");
                if (found.Count == before)
                {
                    components.Add(new VoiceComponent(entry.Id, entry.Url, entry.Sha256, entry.Size, entry.License, place.Value, entry.Target, extract, entry.Strip));
                }
            }

            return found.Count == 0 ? new VoiceComponentManifest(version, python, lockFile, components) : null;
        }

        /// <summary>
        /// Whether a path from the list stays inside the folder it is joined to: relative, no drive, no "..". It is
        /// checked here, before any file is written, as the list could otherwise put a file anywhere. Pure.
        /// </summary>
        public static bool IsRelativeInside(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path) || path.Contains(':'))
            {
                return false;
            }

            string[] parts = path.Split('/', '\\');
            return parts.All(p => p.Length > 0 && p != "." && p != "..");
        }

        private static bool IsHttps(string url)
        {
            return Uri.TryCreate(url, UriKind.Absolute, out Uri uri) && uri.Scheme == Uri.UriSchemeHttps;
        }

        private static void Check(List<string> problems, bool ok, string problem)
        {
            if (!ok)
            {
                problems.Add(problem);
            }
        }

        // The JSON's shape; read through ConfigDocument, as JSON is only read by ProjectData's parts.
        private sealed class Entry
        {
            public string Id { get; set; }

            public string Url { get; set; }


            public string Sha256 { get; set; }

            public long Size { get; set; }

            public string License { get; set; }

            public string Place { get; set; }

            public string Target { get; set; }

            public string Extract { get; set; }

            public int Strip { get; set; }
        }
    }
}
