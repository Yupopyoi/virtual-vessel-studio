using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using VirtualVessel.UI.Controls;
using VirtualVessel.UI.Icons;

namespace VirtualVessel.UI.Tests
{
    /// <summary>Pure parts of the step 5-2 components; behavior on a panel is in the PlayMode tests.</summary>
    public sealed class InputControlTests
    {
        [Test]
        public void TextInput_HidesItsExample_WhenFocused()
        {
            // The first characters typed were shown as the example, with the caret where it ended (the user's report).
            var input = new TextInput { PlaceholderKey = "diagnostics.recordings.description.placeholder" };
            Assert.That(input.Q<TextField>().textEdition.hidePlaceholderOnFocus, Is.True);
        }

        [Test]
        public void ImeCommit_LeavesTheCommittedWordInOrder()
        {
            // The order of the user's log of "こんにちは" (2026-10-31), which Unity left as "こにちはん".
            var ime = new ImeCommit();
            Assert.That(ime.Composition("ｋ", string.Empty, 0, 0), Is.False, "While composing, the text is Unity's.");
            ime.Composition("こ", "ｋ", 1, 1);
            ime.Composition("こんにちは", "こんにち", 4, 4);
            foreach (char character in "こんにちは")
            {
                ime.Text(character);
            }

            Assert.That(ime.Composition(string.Empty, "こんにちは", 5, 5), Is.True);
            Assert.That(new[] { ime.Changed(), ime.Changed(), ime.Changed(), ime.Changed(), ime.Changed() }, Is.EqualTo(new[] { false, false, false, false, true }), "Right on the last character, so nothing out of order is drawn.");
            Assert.That(ime.Result, Is.EqualTo(("こんにちは", 5)));
            Assert.That(Finished(ime), Is.EqualTo(("こんにちは", 5)));
            Assert.That(ime.Changed(), Is.False, "Then typing goes on as usual.");
        }

        [Test]
        public void ImeCommit_EndsWithTheComposition_EvenWithNoChangeToTheText()
        {
            // "ｍ" committed as "ｍ" brings no change; waiting for one kept it for the next word.
            var ime = new ImeCommit();
            ime.Composition("ｍ", "入力欄が", 4, 4);
            ime.Text('ｍ');
            Assert.That(ime.Composition(string.Empty, "入力欄がｍ", 5, 5), Is.True);
            Assert.That(Finished(ime), Is.EqualTo(("入力欄がｍ", 5)));

            ime.Text('\b');
            Assert.That(ime.Composition(string.Empty, "入力欄が", 4, 4), Is.False, "Nothing is left of it.");
        }

        [Test]
        public void ImeCommit_LeavesOutBackspaceAndEnter()
        {
            var ime = new ImeCommit();
            ime.Composition("あい", "x", 1, 1);
            ime.Text('\b');
            ime.Composition("あ", "xあい", 3, 3);
            ime.Text('あ');
            ime.Text('\r');
            ime.Composition(string.Empty, "xあ", 2, 2);

            Assert.That(Finished(ime), Is.EqualTo(("xあ", 2)));
        }

        [Test]
        public void ImeCommit_KeepsTheTextAroundTheComposition()
        {
            var ime = new ImeCommit();
            ime.Composition("か", "abcde", 2, 3);
            ime.Text('か');
            ime.Text('な');
            ime.Composition(string.Empty, "abかde", 3, 3);

            Assert.That(Finished(ime), Is.EqualTo(("abかなde", 4)), "A selection is replaced, and the caret is after the word.");
        }

        [Test]
        public void ImeCommit_PutsTheTextBack_WhenTheCompositionIsCanceled()
        {
            var ime = new ImeCommit();
            ime.Composition("あいう", "ab", 1, 1);

            Assert.That(ime.Composition(string.Empty, "aあいうb", 4, 4), Is.True);
            Assert.That(Finished(ime), Is.EqualTo(("ab", 1)));
        }

        [Test]
        public void ImeCommit_FollowsAPartialCommit()
        {
            var ime = new ImeCommit();
            ime.Composition("きょうは", string.Empty, 0, 0);
            ime.Text('今');
            ime.Text('日');
            ime.Composition("は", "今日は", 2, 2);
            ime.Text('は');
            ime.Composition(string.Empty, "今日は", 3, 3);

            Assert.That(Finished(ime), Is.EqualTo(("今日は", 3)));
        }

        [Test]
        public void ImeCommit_LeavesTypingWithoutACompositionAlone()
        {
            var ime = new ImeCommit();
            ime.Text('a');

            Assert.That(ime.Composition(string.Empty, "a", 1, 1), Is.False);
        }

        [Test]
        public void ImeCommit_ANextWordBeforeTheFinish_CarriesTheFirstOn()
        {
            // Typing on within moments of a commit: a finish due then must not empty the field.
            var ime = new ImeCommit();
            ime.Composition("あ", "x", 1, 1);
            ime.Text('あ');
            ime.Composition(string.Empty, "xあ", 2, 2);
            ime.Composition("い", "xあ", 2, 2);

            Assert.That(ime.TryFinish(out _), Is.False);

            ime.Text('い');
            ime.Composition(string.Empty, "xあい", 3, 3);
            Assert.That(Finished(ime), Is.EqualTo(("xあい", 3)));
            Assert.That(ime.TryFinish(out _), Is.False, "A second finish finds nothing to do.");
        }

        [Test]
        public void ImeCommit_ForgetsACompositionWhenCanceled()
        {
            // Clicking away in the middle of a word: its text must not come back with the next one.
            var ime = new ImeCommit();
            ime.Composition("あ", "old", 3, 3);
            ime.Cancel();

            ime.Composition("い", "new", 3, 3);
            ime.Text('い');
            ime.Composition(string.Empty, "newい", 4, 4);

            Assert.That(Finished(ime), Is.EqualTo(("newい", 4)));
        }

        private static (string Text, int Caret) Finished(ImeCommit ime)
        {
            Assert.That(ime.TryFinish(out (string Text, int Caret) result), Is.True);
            return result;
        }

        [Test]
        public void ValueSlider_SnapsAndMapsPositions()
        {
            Assert.That(ValueSlider.Snap(-12.3f, -60f, 0f, 1f), Is.EqualTo(-12f));
            Assert.That(ValueSlider.Snap(5f, -60f, 0f, 1f), Is.EqualTo(0f), "Clamped to the range.");
            Assert.That(ValueSlider.Snap(0.333f, 0f, 1f, 0f), Is.EqualTo(0.33f).Within(1e-5f), "Step 0 snaps to 1/100 of the range.");
            Assert.That(ValueSlider.ValueAt(50f, 200f, -60f, 0f, 1f), Is.EqualTo(-45f));
            Assert.That(ValueSlider.ValueAt(-10f, 200f, -60f, 0f, 1f), Is.EqualTo(-60f));
            Assert.That(ValueSlider.Fraction(-15f, -60f, 0f), Is.EqualTo(0.75f));
        }

        [Test]
        public void ValueSlider_FormatsLikeTheDesign()
        {
            Assert.That(ValueSlider.Format(-12f, 0, false, "dB"), Is.EqualTo("−12 dB"), "A true minus sign and the unit after a space.");
            Assert.That(ValueSlider.Format(4f, 0, true, null), Is.EqualTo("+4"));
            Assert.That(ValueSlider.Format(0f, 0, true, null), Is.EqualTo("0"), "Zero has no sign.");
            Assert.That(ValueSlider.Format(0.25f, 2, false, "x"), Is.EqualTo("0.25 x"));
        }

        [Test]
        public void SelectField_Helpers()
        {
            var choices = new[] { SelectChoice.FromKey("a", "x.a"), SelectChoice.FromText("mic-1", "USB Mic (Yeti)") };

            Assert.That(SelectField.IndexOf(choices, "mic-1"), Is.EqualTo(1));
            Assert.That(SelectField.IndexOf(choices, "none"), Is.EqualTo(-1));
            Assert.That(choices[1].Resolve(null), Is.EqualTo("USB Mic (Yeti)"), "Data text is shown as it is.");
            Assert.That(choices[0].Resolve(null), Is.EqualTo("[x.a]"));
            Assert.That(SelectField.MoveHighlight(-1, 1, 3), Is.EqualTo(0));
            Assert.That(SelectField.MoveHighlight(-1, -1, 3), Is.EqualTo(2));
            Assert.That(SelectField.MoveHighlight(2, 1, 3), Is.EqualTo(2), "Stops at the end.");
            Assert.That(SelectField.MoveHighlight(0, 1, 0), Is.EqualTo(-1));
        }

        [TestCase(100f, 200f, 1000f, 100f)]
        [TestCase(800f, 300f, 1000f, 600f)]
        [TestCase(900f, 300f, 1000f, 692f)]
        [TestCase(10f, 2000f, 1000f, 8f)]
        public void SelectField_MenuLeft_StaysInsideTheWindow(float fieldLeft, float menuWidth, float hostWidth, float expected)
        {
            // A 100px wide field: the list opens at it, right-aligned with it near the right edge, never off screen.
            var field = new Rect(fieldLeft, 50f, 100f, 32f);

            Assert.That(SelectField.MenuLeft(field, menuWidth, hostWidth, 8f), Is.EqualTo(expected));
        }

        [TestCase("48000", 48000.0)]
        [TestCase(" -3.5 ", -3.5)]
        [TestCase("−5", -5.0)]
        [TestCase("４８０００", 48000.0)]
        [TestCase("1e3", 1000.0)]
        public void NumberInput_TryParse_Accepts(string text, double expected)
        {
            Assert.That(NumberInput.TryParse(text, out double number), Is.True);
            Assert.That(number, Is.EqualTo(expected));
        }

        [TestCase("")]
        [TestCase("abc")]
        [TestCase("1,5")]
        [TestCase("NaN")]
        public void NumberInput_TryParse_Rejects(string text)
        {
            Assert.That(NumberInput.TryParse(text, out _), Is.False);
        }

        [Test]
        public void NumberInput_NormalizeAndFormat()
        {
            Assert.That(NumberInput.Normalize(96000.4, 8000, 48000, 0), Is.EqualTo(48000));
            Assert.That(NumberInput.Normalize(1.25, 0, 10, 1), Is.EqualTo(1.3));
            Assert.That(NumberInput.Format(48000, 0), Is.EqualTo("48000"));
            Assert.That(NumberInput.Format(-0.5, 2), Is.EqualTo("-0.50"));
        }

        [Test]
        public void LevelMeter_SegmentsAndWarningRange()
        {
            Assert.That(LevelMeter.LitSegments(0f, 24), Is.EqualTo(0));
            Assert.That(LevelMeter.LitSegments(0.5f, 24), Is.EqualTo(12));
            Assert.That(LevelMeter.LitSegments(2f, 24), Is.EqualTo(24));
            Assert.That(LevelMeter.KindOf(17, 24), Is.EqualTo(MeterSegment.Normal));
            Assert.That(LevelMeter.KindOf(18, 24), Is.EqualTo(MeterSegment.Warning), "The top quarter (6 of 24) starts at 18.");
            Assert.That(LevelMeter.KindOf(21, 24), Is.EqualTo(MeterSegment.Warning));
            Assert.That(LevelMeter.KindOf(22, 24), Is.EqualTo(MeterSegment.Peak), "The top 2 of 24 are red.");
            Assert.That(LevelMeter.KindOf(23, 24), Is.EqualTo(MeterSegment.Peak));
            Assert.That(LevelMeter.KindOf(5, 6), Is.EqualTo(MeterSegment.Peak), "A short meter still has a peak segment.");
        }

        [Test]
        public void ProgressMeter_EndColorFollowsProgress()
        {
            Color end = ProgressMeter.EndColor(Color.black, Color.white, 0.5f);

            Assert.That(end.r, NUnit.Framework.Is.EqualTo(0.5f).Within(1e-5f));
            Assert.That(ProgressMeter.EndColor(Color.black, Color.white, 2f), NUnit.Framework.Is.EqualTo(Color.white));
        }

        [Test]
        public void SettingRow_ChildrenGoToTheControlSide()
        {
            var row = new SettingRow("audio.noise", "audio.noise.desc");
            var toggle = new SwitchToggle();
            row.Add(toggle);

            Assert.That(toggle.hierarchy.parent.ClassListContains("vv-setting-row__control"), NUnit.Framework.Is.True);
        }

        [Test]
        public void NavItem_ShowsSelectionAndText()
        {
            var item = new NavItem(IconName.Live, "nav.live") { Selected = true };

            Assert.That(item.ClassListContains(NavItem.SelectedClass), NUnit.Framework.Is.True);
            Assert.That(item.ClassListContains(Button.ussClassName), NUnit.Framework.Is.False);
            Assert.That(item.DisplayedText, NUnit.Framework.Is.EqualTo("[nav.live]"));
        }
    }
}
