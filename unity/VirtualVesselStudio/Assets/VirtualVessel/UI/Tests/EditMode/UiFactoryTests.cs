using NUnit.Framework;
using UnityEngine.UIElements;
using VirtualVessel.UI.Commands;
using VirtualVessel.UI.Controls;
using VirtualVessel.UI.Icons;

namespace VirtualVessel.UI.Tests
{
    /// <summary>The C# factories build the same parts as UXML (UI foundation detailed design 6.3).</summary>
    public sealed class UiFactoryTests
    {
        [Test]
        public void Button_ChainsTooltipAndCommand()
        {
            var command = new UiCommand(() => { });

            ActionButton start = Ui.Button("live.start", ButtonVariant.Primary)
                .WithTooltip("tip.live.start", "Ctrl+Enter")
                .Bind(command);

            Assert.That(start.TextKey, Is.EqualTo("live.start"));
            Assert.That(start.Variant, Is.EqualTo(ButtonVariant.Primary));
            Assert.That(start.TooltipKey, Is.EqualTo("tip.live.start"));
            Assert.That(start.Shortcut, Is.EqualTo("Ctrl+Enter"));
            Assert.That(start.enabledSelf, Is.True);
        }

        [Test]
        public void Factories_SetTheirKeys()
        {
            Assert.That(Ui.IconButton(IconName.Mic, toggle: true).Toggle, Is.True);
            Assert.That(Ui.Toggle("voice.convert").TextKey, Is.EqualTo("voice.convert"));
            Assert.That(Ui.Segmented("a", "b").ItemKeys, Is.EqualTo("a,b"));
            Assert.That(Ui.Chip(StatusKind.Warning, "trk.weak").Status, Is.EqualTo(StatusKind.Warning));
            Assert.That(Ui.Select("audio.mic.device").LabelKey, Is.EqualTo("audio.mic.device"));
            Assert.That(Ui.TextField("stream.title").LabelKey, Is.EqualTo("stream.title"));
            Assert.That(Ui.NumberField("audio.sampleRate", "Hz").Unit, Is.EqualTo("Hz"));
            Assert.That(Ui.Progress("lab.training").LabelKey, Is.EqualTo("lab.training"));

            ValueSlider gain = Ui.Slider("audio.gain", -60, 0, "dB");
            Assert.That(gain.LabelKey, Is.EqualTo("audio.gain"));
            Assert.That(gain.LowValue, Is.EqualTo(-60));
            Assert.That(gain.HighValue, Is.EqualTo(0));
            Assert.That(gain.Unit, Is.EqualTo("dB"));
        }

        [Test]
        public void Text_IsAKeyedLabelWithClasses()
        {
            Label caption = Ui.Text("demo.note", "vv-text-caption", "dm-note");

            Assert.That(caption.text, Is.EqualTo("[demo.note]"), "Outside a scope the key is shown.");
            Assert.That(caption.ClassListContains("vv-text-caption"), Is.True);
            Assert.That(caption.ClassListContains("dm-note"), Is.True);
        }
    }
}
