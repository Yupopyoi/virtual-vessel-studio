using System;
using System.Collections.Generic;
using System.IO;
using VirtualVessel.ProjectData.Config;

namespace VirtualVessel.VoiceLab.Scripts
{
    /// <summary>
    /// Where the training environment and its pieces are (Voice Lab detailed design 6.4, 8.5). Activation will
    /// build these and write this; until then a developer points it at their own.
    /// </summary>
    public sealed class VoiceLabEnvironment
    {
        /// <summary>The generator's window and returned frames, fixed with the runtime's 1 s window and 50 ms crossfade.</summary>
        public const int GeneratorFrames = 100;
        public const int GeneratorReturned = 21;

        public VoiceLabEnvironment(string python, string scripts, string rvc, string voxCpmModel, string template, string manifest)
        {
            Python = python;
            Scripts = scripts;
            Rvc = rvc;
            VoxCpmModel = voxCpmModel;
            Template = template;
            Manifest = manifest;
        }

        /// <summary>python.exe of the training environment.</summary>
        public string Python { get; }

        /// <summary>services/voice-lab: the entry scripts and the voicelab package.</summary>
        public string Scripts { get; }

        /// <summary>RVC's source folder at the pinned commit.</summary>
        public string Rvc { get; }

        /// <summary>The VoxCPM2 model's folder at the pinned revision.</summary>
        public string VoxCpmModel { get; }

        /// <summary>The generator template (.sentis) and its placement table (.json) for <see cref="GeneratorFrames"/>.</summary>
        public string Template { get; }

        public string Manifest { get; }

        /// <summary>The folders programs may run from: the environment's, which holds python.exe. Pure.</summary>
        public IReadOnlyList<string> AllowedFolders => new[] { EnvironmentFolder(Python) };

        /// <summary>The environment from its JSON, or null with a reason. Pure.</summary>
        public static VoiceLabEnvironment Parse(string json, out string problem)
        {
            problem = null;
            ConfigDocument document;
            try
            {
                document = ConfigDocument.Parse(json ?? string.Empty);
            }
            catch (FormatException exception)
            {
                problem = $"The environment file is not valid JSON: {exception.Message}";
                return null;
            }

            return new VoiceLabEnvironment(
                document.Get<string>("python"), document.Get<string>("scripts"), document.Get<string>("rvc"),
                document.Get<string>("voxCpmModel"), document.Get<string>("template"), document.Get<string>("manifest"));
        }

        /// <summary>What is missing, given how to check files and folders; empty when it can run. Pure apart from the checks passed in.</summary>
        public IReadOnlyList<string> Problems(Func<string, bool> fileExists, Func<string, bool> folderExists)
        {
            var problems = new List<string>();
            Check(problems, nameof(Python), Python, fileExists);
            Check(problems, nameof(Scripts), Scripts, folderExists);
            Check(problems, nameof(Rvc), Rvc, folderExists);
            Check(problems, nameof(VoxCpmModel), VoxCpmModel, folderExists);
            Check(problems, nameof(Template), Template, fileExists);
            Check(problems, nameof(Manifest), Manifest, fileExists);
            return problems;
        }

        /// <summary>
        /// The environment's root for a python.exe: a venv keeps it in Scripts\, so the venv folder is its parent.
        /// Pure.
        /// </summary>
        public static string EnvironmentFolder(string python)
        {
            string folder = Path.GetDirectoryName(python) ?? string.Empty;
            return string.Equals(Path.GetFileName(folder), "Scripts", StringComparison.OrdinalIgnoreCase)
                ? Path.GetDirectoryName(folder) ?? folder
                : folder;
        }

        private static void Check(List<string> problems, string name, string path, Func<string, bool> exists)
        {
            if (string.IsNullOrEmpty(path) || !Path.IsPathRooted(path))
            {
                problems.Add($"{name} needs a full path.");
            }
            else if (!exists(path))
            {
                problems.Add($"{name} does not exist: {path}");
            }
        }
    }
}
