using System;
using System.Collections.Generic;
using System.Linq;
using VirtualVessel.ProjectData.Config;

namespace VirtualVessel.UI.Credits
{
    /// <summary>One credited component: what it is, under which license, and the files with the full text.</summary>
    public sealed class CreditEntry
    {
        public CreditEntry(string name, string version, string supplier, string license, IReadOnlyList<string> licenseFiles, int depth)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Version = version;
            Supplier = supplier;
            License = license;
            LicenseFiles = licenseFiles ?? Array.Empty<string>();
            Depth = depth;
        }

        public string Name { get; }

        /// <summary>Null when the SBOM gives none.</summary>
        public string Version { get; }

        /// <summary>Null when the SBOM gives none.</summary>
        public string Supplier { get; }

        /// <summary>The license names joined with ", "; null when the SBOM gives none.</summary>
        public string License { get; }

        /// <summary>Paths in the release package, such as <c>licenses/UniGLTF-LICENSE.md</c>.</summary>
        public IReadOnlyList<string> LicenseFiles { get; }

        /// <summary>0 for a top-level component; 1 for one built into it, and so on.</summary>
        public int Depth { get; }
    }

    /// <summary>
    /// The credits read from the CycloneDX SBOM (development workflow 18.1.2): the application itself,
    /// every component shipped with it, and every component fetched to the user's PC when a feature is
    /// activated, such as Voice Lab's Python environment; each followed by the components built into it.
    /// </summary>
    /// <remarks>
    /// The groups follow a top-level component's scope (<c>required</c>, which CycloneDX assumes when
    /// none is given, and <c>optional</c>), and both keep the SBOM's order. Development-only components
    /// (scope <c>excluded</c>) and everything inside them are left out. The SBOM's own types stay inside
    /// this class; screens see <see cref="CreditEntry"/> only.
    /// </remarks>
    public sealed class CreditCatalog
    {
        public const string LicenseFileProperty = "virtual-vessel-studio:license-file";

        public CreditCatalog(CreditEntry application, IReadOnlyList<CreditEntry> components, IReadOnlyList<CreditEntry> onDemand)
        {
            Application = application;
            Components = components ?? throw new ArgumentNullException(nameof(components));
            OnDemand = onDemand ?? throw new ArgumentNullException(nameof(onDemand));
        }

        /// <summary>The application itself; null when the SBOM does not describe it.</summary>
        public CreditEntry Application { get; }

        /// <summary>The components shipped with the application (scope <c>required</c>).</summary>
        public IReadOnlyList<CreditEntry> Components { get; }

        /// <summary>
        /// The components not shipped but fetched to the user's PC when a feature is activated (scope
        /// <c>optional</c>), such as Voice Lab's Python environment.
        /// </summary>
        public IReadOnlyList<CreditEntry> OnDemand { get; }

        /// <summary>Reads an SBOM. Pure.</summary>
        /// <exception cref="FormatException">The text is not a CycloneDX SBOM this version can read.</exception>
        public static CreditCatalog Parse(string json)
        {
            ConfigDocument document = ConfigDocument.Parse(json ?? throw new ArgumentNullException(nameof(json)));
            if (document.Get<string>("bomFormat") != "CycloneDX")
            {
                throw new FormatException("Not a CycloneDX SBOM.");
            }

            List<SbomComponent> components = document.Get<List<SbomComponent>>("components");
            if (components == null && document.Contains("components"))
            {
                throw new FormatException("The SBOM's components are not a list of components.");
            }

            SbomComponent application = document.Get<SbomComponent>("metadata.component");
            var shipped = new List<CreditEntry>();
            var onDemand = new List<CreditEntry>();
            foreach (SbomComponent component in components ?? Enumerable.Empty<SbomComponent>())
            {
                if (component?.Name == null || IsExcluded(component))
                {
                    continue;
                }

                AddWithParts(component, 0, string.Equals(component.Scope, "optional", StringComparison.Ordinal) ? onDemand : shipped);
            }

            return new CreditCatalog(application?.Name == null ? null : ToEntry(application, 0), shipped, onDemand);
        }

        // CycloneDX: a missing scope means required.
        private static bool IsExcluded(SbomComponent component)
        {
            return string.Equals(component.Scope, "excluded", StringComparison.Ordinal);
        }

        private static void AddWithParts(SbomComponent component, int depth, List<CreditEntry> into)
        {
            into.Add(ToEntry(component, depth));
            foreach (SbomComponent part in component.Components ?? Enumerable.Empty<SbomComponent>())
            {
                if (part?.Name != null && !IsExcluded(part))
                {
                    AddWithParts(part, depth + 1, into);
                }
            }
        }

        private static CreditEntry ToEntry(SbomComponent component, int depth)
        {
            string[] licenses = (component.Licenses ?? new List<SbomLicenseChoice>())
                .Select(choice => choice?.License?.Id ?? choice?.License?.Name ?? choice?.Expression)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .ToArray();
            string[] files = (component.Properties ?? new List<SbomProperty>())
                .Where(property => property != null && property.Name == LicenseFileProperty && !string.IsNullOrWhiteSpace(property.Value))
                .Select(property => property.Value)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            return new CreditEntry(
                component.Name,
                NullIfBlank(component.Version),
                NullIfBlank(component.Supplier?.Name),
                licenses.Length == 0 ? null : string.Join(", ", licenses),
                files,
                depth);
        }

        private static string NullIfBlank(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }

        // The part of CycloneDX 1.6 that the credits use; JSON names match case-insensitively. Only
        // read by reflection, which the Mono backend's default stripping keeps, as for UiSettings.
        private sealed class SbomComponent
        {
            public string Name { get; set; }

            public string Version { get; set; }

            public string Scope { get; set; }

            public SbomSupplier Supplier { get; set; }

            public List<SbomLicenseChoice> Licenses { get; set; }

            public List<SbomProperty> Properties { get; set; }

            public List<SbomComponent> Components { get; set; }
        }

        private sealed class SbomSupplier
        {
            public string Name { get; set; }
        }

        private sealed class SbomLicenseChoice
        {
            public SbomLicense License { get; set; }

            public string Expression { get; set; }
        }

        private sealed class SbomLicense
        {
            public string Id { get; set; }

            public string Name { get; set; }
        }

        private sealed class SbomProperty
        {
            public string Name { get; set; }

            public string Value { get; set; }
        }
    }
}
