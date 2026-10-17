using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;

namespace VirtualVessel.UI.Tests
{
    /// <summary>One screenshot: which screen, in which look, at which size (UI foundation detailed design 7.10).</summary>
    public sealed class UiShot
    {
        public UiShot(string screen, string background, string language, string palette, int width, int height, float scale)
        {
            Screen = screen;
            Background = background;
            Language = language;
            Palette = palette;
            Width = width;
            Height = height;
            Scale = scale;
        }

        public string Screen { get; }

        /// <summary>"dark" or "light".</summary>
        public string Background { get; }

        public string Language { get; }

        public string Palette { get; }

        public int Width { get; }

        public int Height { get; }

        /// <summary>The UI size step, such as 1.5.</summary>
        public float Scale { get; }

        /// <summary>"setup-dark-ja-lavender-1600x900-100.png".</summary>
        public string FileName => string.Format(
            CultureInfo.InvariantCulture,
            "{0}-{1}-{2}-{3}-{4}x{5}-{6}.png",
            Screen,
            Background,
            Language,
            Palette,
            Width,
            Height,
            (int)Math.Round(Scale * 100f));
    }

    /// <summary>What to write and where, from the environment the tool or the script sets.</summary>
    public sealed class UiShotOptions
    {
        public const string OutputVariable = "VV_UI_CAPTURE";
        public const string ScreensVariable = "VV_UI_CAPTURE_SCREENS";
        public const string QuickVariable = "VV_UI_CAPTURE_QUICK";

        public UiShotOptions(string output, IReadOnlyList<string> screens, bool quick)
        {
            Output = output;
            Screens = screens;
            Quick = quick;
        }

        public string Output { get; }

        /// <summary>The screens to write; empty for all.</summary>
        public IReadOnlyList<string> Screens { get; }

        /// <summary>Only dark Japanese and light English.</summary>
        public bool Quick { get; }

        /// <summary>
        /// Reads the options. Without an output folder, the default is screenshots/&lt;timestamp&gt; at the
        /// repository root, two levels above the Unity project. Pure: the environment comes in as a function.
        /// </summary>
        public static UiShotOptions FromEnvironment(Func<string, string> getVariable, string projectPath, DateTime now)
        {
            string output = getVariable(OutputVariable);
            if (string.IsNullOrWhiteSpace(output))
            {
                output = UiShotPlan.DefaultOutput(projectPath, now);
            }

            string screens = getVariable(ScreensVariable) ?? string.Empty;
            string quick = getVariable(QuickVariable) ?? string.Empty;
            return new UiShotOptions(
                output,
                screens.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries),
                quick == "1" || quick.Equals("true", StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>
    /// The combinations to write (UI foundation detailed design 7.10, ui-guidelines.md 5). Pure.
    /// </summary>
    public static class UiShotPlan
    {
        public const string DefaultPalette = "lavender";

        /// <summary>A palette with a bright accent, to check contrast on the light background.</summary>
        public const string BrightPalette = "lemon";

        public static readonly string[] AllScreens = { "live", "setup", "lab", "diagnostics", "gallery" };

        public static IReadOnlyList<UiShot> Create(UiShotOptions options)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            IEnumerable<string> screens = options.Screens.Count == 0
                ? AllScreens
                : AllScreens.Where(screen => options.Screens.Contains(screen, StringComparer.Ordinal));
            var shots = new List<UiShot>();
            foreach (string screen in screens)
            {
                if (options.Quick)
                {
                    shots.Add(new UiShot(screen, "dark", "ja", DefaultPalette, 1600, 900, 1f));
                    shots.Add(new UiShot(screen, "light", "en", DefaultPalette, 1600, 900, 1f));
                    continue;
                }

                foreach (string language in new[] { "ja", "en" })
                {
                    foreach (string background in new[] { "dark", "light" })
                    {
                        shots.Add(new UiShot(screen, background, language, DefaultPalette, 1600, 900, 1f));
                    }
                }

                shots.Add(new UiShot(screen, "light", "ja", BrightPalette, 1600, 900, 1f));
                shots.Add(new UiShot(screen, "dark", "ja", DefaultPalette, 1024, 768, 1f));
                shots.Add(new UiShot(screen, "dark", "ja", DefaultPalette, 1600, 900, 1.5f));
            }

            return shots;
        }

        /// <summary>screenshots/yyyyMMdd-HHmmss at the repository root, which Git ignores.</summary>
        public static string DefaultOutput(string projectPath, DateTime now)
        {
            string repository = Path.GetFullPath(Path.Combine(projectPath, "..", ".."));
            return Path.Combine(repository, "screenshots", now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture));
        }

        /// <summary>A page that shows every image with what it is, grouped by screen.</summary>
        public static string IndexHtml(IReadOnlyList<UiShot> shots)
        {
            var html = new StringBuilder();
            html.AppendLine("<!doctype html>");
            html.AppendLine("<html lang=\"en\"><head><meta charset=\"utf-8\"><title>UI screenshots</title>");
            html.AppendLine("<style>body{margin:24px;font:14px sans-serif;background:#1e1e22;color:#e8e6ef}h2{margin:32px 0 8px}figure{margin:0 0 24px}figcaption{margin:4px 0;color:#b9b5c8}img{max-width:100%;border:1px solid #444}</style>");
            html.AppendLine("</head><body>");
            foreach (IGrouping<string, UiShot> screen in shots.GroupBy(shot => shot.Screen))
            {
                html.AppendLine("<h2>" + WebUtility.HtmlEncode(screen.Key) + "</h2>");
                foreach (UiShot shot in screen)
                {
                    string name = WebUtility.HtmlEncode(shot.FileName);
                    html.AppendLine("<figure><figcaption>" + name + "</figcaption><img src=\"" + name + "\" alt=\"" + name + "\" loading=\"lazy\"></figure>");
                }
            }

            html.AppendLine("</body></html>");
            return html.ToString();
        }
    }
}
