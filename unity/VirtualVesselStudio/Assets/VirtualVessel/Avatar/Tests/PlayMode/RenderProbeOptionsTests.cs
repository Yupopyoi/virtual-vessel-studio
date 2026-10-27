using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using VirtualVessel.Tracking;

namespace VirtualVessel.Avatar.Tests
{
    /// <summary>The render probe's options (avatar detailed design 14.1); pure, so they run with the other tests.</summary>
    public sealed class RenderProbeOptionsTests
    {
        [Test]
        public void TheOptions_ComeFromTheVariables_WithDefaults()
        {
            var variables = new Dictionary<string, string>
            {
                [RenderProbeOptions.RecordingVariable] = "20261025-122051.vvtrack",
                [RenderProbeOptions.TimesVariable] = "16.5, 8",
                [RenderProbeOptions.DepthVariable] = "2D",
                [RenderProbeOptions.LevelVariable] = "3",
            };
            RenderProbeOptions options = RenderProbeOptions.From(name => variables.TryGetValue(name, out string value) ? value : null, "recordings", "out");

            Assert.That(options.Recording, Is.EqualTo(Path.Combine("recordings", "20261025-122051.vvtrack")));
            Assert.That(options.Times, Is.EqualTo(new[] { 8.0, 16.5 }), "In order.");
            Assert.That(options.Flat, Is.True);
            Assert.That(options.Smoothing, Is.EqualTo(3));
            Assert.That(options.Sample, Is.EqualTo(RenderProbeOptions.DefaultSample));
            Assert.That(options.Output, Is.EqualTo("out"));
            Assert.That(options.TimesFor(10.0), Is.EqualTo(new[] { 8.0 }), "Past the end is left out.");

            RenderProbeOptions plain = RenderProbeOptions.From(_ => null, "recordings", "out");
            Assert.That(plain.Recording, Is.Null);
            Assert.That(plain.Smoothing, Is.EqualTo(SmoothingLevel.Standard));
            Assert.That(plain.Flat, Is.False);
            Assert.That(plain.TimesFor(5.0), Is.EqualTo(new[] { 2.0, 4.0 }), "Every two seconds.");
        }
    }
}
