using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using VirtualVessel.Diagnostics.Logging;
using VirtualVessel.ProjectData.Config;

namespace VirtualVessel.UI.Credits
{
    /// <summary>
    /// The license files shipped with the application, read by their path in the release package
    /// (development workflow 18.1.2), such as <c>licenses/UniGLTF-LICENSE.md</c>.
    /// </summary>
    public interface ICreditFiles
    {
        /// <summary>The file's text; null when it is missing or cannot be read.</summary>
        string ReadText(string packagePath);
    }

    /// <summary>
    /// Reads the shipped license files: next to the built application, or, in the Editor, from the
    /// sources that tools/release/license-files.json names, so both show the same credits.
    /// </summary>
    /// <remarks>
    /// Paths come from the SBOM, a file shipped with the application. They are still kept inside the
    /// application or repository folder, so a changed SBOM cannot make the screen read other files.
    /// Files are read when shown, on the main thread; the largest is about 140 KB.
    /// </remarks>
    public sealed class CreditFiles : ICreditFiles
    {
        /// <summary>The SBOM's path in the release package.</summary>
        public const string SbomPath = "licenses/virtual-vessel-studio.cdx.json";

        /// <summary>The list of shipped files, relative to the repository root.</summary>
        public const string LicenseFileListPath = "tools/release/license-files.json";

        private readonly Func<string, string> _locate;
        private readonly ILog _log;

        private CreditFiles(Func<string, string> locate, ILog log)
        {
            _locate = locate;
            _log = log;
        }

        /// <summary>Files in the release package, next to the application's executable.</summary>
        public static CreditFiles ForPackage(string applicationFolder, ILog log)
        {
            if (string.IsNullOrWhiteSpace(applicationFolder))
            {
                throw new ArgumentException("The application folder must not be empty.", nameof(applicationFolder));
            }

            return new CreditFiles(path => Path.Combine(applicationFolder, path), log);
        }

        /// <summary>Files at their sources in the repository, for Play Mode in the Editor.</summary>
        public static CreditFiles ForRepository(string repositoryRoot, ILog log)
        {
            if (string.IsNullOrWhiteSpace(repositoryRoot))
            {
                throw new ArgumentException("The repository folder must not be empty.", nameof(repositoryRoot));
            }

            // Read on first use, so that starting the application reads nothing.
            var sources = new Lazy<IReadOnlyDictionary<string, string>>(() => ReadSources(repositoryRoot, log));
            return new CreditFiles(path => sources.Value.TryGetValue(path, out string source) ? FindSource(repositoryRoot, source) : null, log);
        }

        public string ReadText(string packagePath)
        {
            if (!IsSafeRelativePath(packagePath))
            {
                _log?.Warning($"Credits: ignored the license file path '{packagePath}'; it must stay inside the application folder.");
                return null;
            }

            string file = _locate(packagePath);
            if (file == null || !File.Exists(file))
            {
                _log?.Warning($"Credits: the license file {packagePath} is missing{(file == null ? string.Empty : $" ({file})")}.");
                return null;
            }

            try
            {
                return File.ReadAllText(file);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                _log?.Warning($"Credits: could not read the license file {file}.", exception);
                return null;
            }
        }

        /// <summary>
        /// A path inside its root: relative, with forward slashes, and no empty, '.', or '..' parts.
        /// The same rule as the repository checks. Pure.
        /// </summary>
        public static bool IsSafeRelativePath(string path)
        {
            if (string.IsNullOrEmpty(path) || path.StartsWith("/", StringComparison.Ordinal) || path.Contains('\\') || path.Contains(':'))
            {
                return false;
            }

            return path.Split('/').All(part => part.Length > 0 && part != "." && part != "..");
        }

        /// <summary>The package path to source path pairs of license-files.json. Pure.</summary>
        /// <exception cref="FormatException">The text is not such a list.</exception>
        public static IReadOnlyDictionary<string, string> ParseSources(string json)
        {
            Dictionary<string, string> files = ConfigDocument.Parse(json).Get<Dictionary<string, string>>("files");
            if (files == null)
            {
                throw new FormatException("The license file list has no 'files' object of text values.");
            }

            return new Dictionary<string, string>(files, StringComparer.Ordinal);
        }

        private static IReadOnlyDictionary<string, string> ReadSources(string repositoryRoot, ILog log)
        {
            string file = Path.Combine(repositoryRoot, LicenseFileListPath);
            try
            {
                return ParseSources(File.ReadAllText(file));
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is FormatException)
            {
                log?.Warning($"Credits: could not read {file}.", exception);
                return new Dictionary<string, string>();
            }
        }

        // A source may write one folder name with '*', such as the hash of a package in
        // Library/PackageCache; it must match exactly one folder.
        private static string FindSource(string repositoryRoot, string source)
        {
            if (!IsSafeRelativePath(source))
            {
                return null;
            }

            string current = repositoryRoot;
            foreach (string part in source.Split('/'))
            {
                if (part.Contains('*'))
                {
                    string[] matches = Directory.Exists(current) ? Directory.GetDirectories(current, part) : Array.Empty<string>();
                    if (matches.Length != 1)
                    {
                        return null;
                    }

                    current = matches[0];
                }
                else
                {
                    current = Path.Combine(current, part);
                }
            }

            return current;
        }
    }
}
