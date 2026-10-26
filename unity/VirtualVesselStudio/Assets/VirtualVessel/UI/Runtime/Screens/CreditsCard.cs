using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.UIElements;
using VirtualVessel.UI.Controls;
using VirtualVessel.UI.Credits;
using VirtualVessel.UI.Localization;

namespace VirtualVessel.UI.Screens
{
    /// <summary>String table keys of the credits Card (docs/&lt;lang&gt;/ui/screens/setup.md 8).</summary>
    [StringKeys]
    public static class CreditStrings
    {
        public const string Title = "setup.credits.title";
        public const string Intro = "setup.credits.intro";
        public const string Unavailable = "setup.credits.unavailable";
        public const string Show = "setup.credits.show";
        public const string Hide = "setup.credits.hide";
        public const string Missing = "setup.credits.missing";
    }

    /// <summary>
    /// The software and assets the application uses, from the SBOM shipped with it, and the full
    /// license texts shipped next to it (docs/&lt;lang&gt;/ui/screens/setup.md 8).
    /// </summary>
    /// <remarks>
    /// The SBOM is read when the Card is built; a license text when it is first shown. Every text
    /// from those files is shown as written, never parsed as markup.
    /// </remarks>
    public sealed class CreditsCard : VisualElement
    {
        public const string UssClass = "vv-credits-card";
        public const string IntroClass = UssClass + "__intro";
        public const string EntryClass = UssClass + "__entry";
        public const string NestedClass = EntryClass + "--nested";
        public const string TextClass = UssClass + "__text";
        public const string FileClass = UssClass + "__file";
        public const string PartClass = UssClass + "__part";

        private readonly ICreditFiles _files;
        private readonly List<CreditRow> _rows = new List<CreditRow>();

        public CreditsCard(ICreditFiles files)
        {
            _files = files ?? throw new ArgumentNullException(nameof(files));
            AddToClassList(UssClass);
            AddToClassList(CaptureSourceCard.UssClass);
            Card card = Ui.Card(CreditStrings.Title);
            Add(card);

            Catalog = Load(files);
            if (Catalog == null)
            {
                card.Add(Ui.Text(CreditStrings.Unavailable, CaptureSourceCard.ProblemClass, "vv-text-body"));
                return;
            }

            card.Add(Ui.Text(CreditStrings.Intro, IntroClass, "vv-text-body"));
            IEnumerable<CreditEntry> entries = Catalog.Application == null ? Catalog.Components : Catalog.Components.Prepend(Catalog.Application);
            foreach (CreditEntry entry in entries)
            {
                var row = new CreditRow(entry, this);
                _rows.Add(row);
                card.Add(row.Row);
                if (row.Text != null)
                {
                    card.Add(row.Text);
                }
            }
        }

        /// <summary>The credits shown; null when the SBOM could not be read.</summary>
        public CreditCatalog Catalog { get; }

        /// <summary>The rows in order, for tests.</summary>
        public IReadOnlyList<CreditRow> Rows => _rows;

        /// <summary>"0.16.3 · homuler · MIT": the parts the SBOM gives. Data only, so the same in every language. Pure.</summary>
        public static string Details(CreditEntry entry)
        {
            return string.Join(" · ", new[] { entry.Version, entry.Supplier, entry.License }.Where(part => !string.IsNullOrEmpty(part)));
        }

        private static CreditCatalog Load(ICreditFiles files)
        {
            string sbom = files.ReadText(CreditFiles.SbomPath);
            if (sbom == null)
            {
                return null;
            }

            try
            {
                return CreditCatalog.Parse(sbom);
            }
            catch (FormatException)
            {
                // The SBOM ships with the application, so this is a broken install; the screen says so.
                return null;
            }
        }

        private static Label PlainLabel(string text, params string[] classes)
        {
            var label = new Label(text) { enableRichText = false };
            foreach (string ussClass in classes)
            {
                label.AddToClassList(ussClass);
            }

            return label;
        }

        /// <summary>One credited component, and its license text once shown.</summary>
        public sealed class CreditRow
        {
            private readonly CreditsCard _owner;
            private bool _filled;

            internal CreditRow(CreditEntry entry, CreditsCard owner)
            {
                Entry = entry;
                _owner = owner;
                Row = new VisualElement();
                Row.AddToClassList(SettingRow.UssClass);
                Row.AddToClassList(EntryClass);
                Row.EnableInClassList(NestedClass, entry.Depth > 0);

                var text = new VisualElement();
                text.AddToClassList(SettingRow.UssClass + "__text");
                text.Add(PlainLabel(entry.Name, SettingRow.UssClass + "__name", "vv-text-body"));
                string details = Details(entry);
                if (details.Length > 0)
                {
                    text.Add(PlainLabel(details, SettingRow.UssClass + "__description", "vv-text-caption"));
                }

                Row.Add(text);
                if (entry.LicenseFiles.Count == 0)
                {
                    return;
                }

                var control = new VisualElement();
                control.AddToClassList(SettingRow.UssClass + "__control");
                Toggle = Ui.Button(CreditStrings.Show);
                Toggle.clicked += () => Shown = !Shown;
                control.Add(Toggle);
                Row.Add(control);

                Text = new ScrollView(ScrollViewMode.Vertical);
                Text.AddToClassList(TextClass);
                Text.style.display = DisplayStyle.None;
            }

            public CreditEntry Entry { get; }

            public VisualElement Row { get; }

            /// <summary>"Show full text"; null when the SBOM names no license file.</summary>
            public ActionButton Toggle { get; }

            /// <summary>The license text; null when the SBOM names no license file.</summary>
            public ScrollView Text { get; }

            public bool Shown
            {
                get => Text != null && Text.style.display == DisplayStyle.Flex;
                set
                {
                    if (Text == null)
                    {
                        return;
                    }

                    if (value && !_filled)
                    {
                        Fill();
                    }

                    Text.style.display = value ? DisplayStyle.Flex : DisplayStyle.None;
                    Toggle.TextKey = value ? CreditStrings.Hide : CreditStrings.Show;
                }
            }

            private void Fill()
            {
                _filled = true;
                bool named = Entry.LicenseFiles.Count > 1;
                foreach (string file in Entry.LicenseFiles)
                {
                    // The file name tells several texts apart, such as a license and its notices.
                    if (named)
                    {
                        Text.Add(PlainLabel(file, FileClass, "vv-text-mono"));
                    }

                    string content = _owner._files.ReadText(file);
                    if (content == null)
                    {
                        Text.Add(Ui.Text(CreditStrings.Missing, PartClass, "vv-text-caption"));
                        continue;
                    }

                    foreach (string part in CreditText.Split(content))
                    {
                        Text.Add(PlainLabel(part, PartClass, "vv-text-caption"));
                    }
                }
            }
        }
    }
}
