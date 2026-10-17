using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace VirtualVessel.UI.Tests
{
    /// <summary>The screenshot combinations (UI foundation detailed design 7.10).</summary>
    public sealed class UiShotPlanTests
    {
        private static readonly DateTime s_now = new DateTime(2026, 10, 17, 1, 2, 3);

        [Test]
        public void Create_Default_CoversTheReviewChecklistForEveryScreen()
        {
            IReadOnlyList<UiShot> shots = UiShotPlan.Create(new UiShotOptions("out", Array.Empty<string>(), quick: false));

            Assert.That(shots.Select(shot => shot.Screen).Distinct(), Is.EqualTo(UiShotPlan.AllScreens));
            List<UiShot> setup = shots.Where(shot => shot.Screen == "setup").ToList();
            Assert.That(setup, Has.Count.EqualTo(7));
            Assert.That(setup.Count(shot => shot.Width == 1600 && shot.Scale == 1f && shot.Palette == "lavender"), Is.EqualTo(4), "Dark and light, Japanese and English.");
            Assert.That(setup.Any(shot => shot.Palette == "lemon" && shot.Background == "light"), Is.True, "A bright accent on light.");
            Assert.That(setup.Any(shot => shot.Width == 1024), Is.True, "A narrow window.");
            Assert.That(setup.Any(shot => shot.Scale == 1.5f), Is.True, "A larger UI size.");
            Assert.That(shots.Select(shot => shot.FileName).Distinct().Count(), Is.EqualTo(shots.Count), "Every file name is different.");
        }

        [Test]
        public void Create_QuickAndScreens_NarrowThePlan()
        {
            IReadOnlyList<UiShot> shots = UiShotPlan.Create(new UiShotOptions("out", new[] { "gallery", "unknown" }, quick: true));

            Assert.That(shots.Select(shot => shot.FileName), Is.EqualTo(new[]
            {
                "gallery-dark-ja-lavender-1600x900-100.png",
                "gallery-light-en-lavender-1600x900-100.png",
            }));
        }

        [Test]
        public void FromEnvironment_ReadsTheVariables_AndDefaultsUnderTheRepository()
        {
            var variables = new Dictionary<string, string>
            {
                [UiShotOptions.ScreensVariable] = "setup, gallery",
                [UiShotOptions.QuickVariable] = "true",
            };
            string project = @"E:\repo\unity\VirtualVesselStudio";

            UiShotOptions options = UiShotOptions.FromEnvironment(name => variables.TryGetValue(name, out string value) ? value : null, project, s_now);

            Assert.That(options.Screens, Is.EqualTo(new[] { "setup", "gallery" }));
            Assert.That(options.Quick, Is.True);
            Assert.That(options.Output, Is.EqualTo(@"E:\repo\screenshots\20261017-010203"));

            variables[UiShotOptions.OutputVariable] = @"D:\shots";
            Assert.That(UiShotOptions.FromEnvironment(name => variables.TryGetValue(name, out string value) ? value : null, project, s_now).Output, Is.EqualTo(@"D:\shots"));
        }

        [Test]
        public void IndexHtml_ListsEveryImageByScreen()
        {
            IReadOnlyList<UiShot> shots = UiShotPlan.Create(new UiShotOptions("out", new[] { "live", "setup" }, quick: true));

            string html = UiShotPlan.IndexHtml(shots);

            Assert.That(html, Does.Contain("<h2>live</h2>"));
            Assert.That(html, Does.Contain("<h2>setup</h2>"));
            foreach (UiShot shot in shots)
            {
                Assert.That(html, Does.Contain("src=\"" + shot.FileName + "\""));
            }
        }
    }
}
