using System;
using System.Collections.Generic;
using UnityEngine.UIElements;
using VirtualVessel.UI.Localization;

namespace VirtualVessel.UI.Controls
{
    /// <summary>How a step stands against where the user is.</summary>
    public enum StepStatus
    {
        Done,
        Current,
        Ahead,
    }

    /// <summary>
    /// Where the user is in work split into steps, "STEP 1" to "STEP n" (components.md 3.26). Steps already
    /// reached can be pressed to go back to; the ones ahead cannot.
    /// </summary>
    [StringKeys]
    public static class StepIndicatorStrings
    {
        /// <summary>"STEP {n}".</summary>
        public const string Number = "common.step";
    }

    [UxmlElement]
    public partial class StepIndicator : VisualElement
    {
        public const string UssClass = "vv-steps";
        public const string StepClass = "vv-step";
        public const string CurrentClass = StepClass + "--current";
        public const string DoneClass = StepClass + "--done";

        private readonly List<Button> _steps = new List<Button>();
        private readonly List<(KeyedText Name, Label Number)> _texts = new List<(KeyedText, Label)>();
        private int _current;
        private int _reached;

        public StepIndicator()
        {
            AddToClassList(UssClass);
            RegisterCallback<AttachToPanelEvent>(_ => RefreshNumbers());
        }

        /// <summary>Raised with the step's index when the user presses a step they have reached.</summary>
        public event Action<int> StepChosen;

        public int Current => _current;

        /// <summary>The furthest step reached; steps up to it can be revisited.</summary>
        public int Reached => _reached;

        public void SetSteps(params string[] nameKeys)
        {
            Clear();
            _steps.Clear();
            _texts.Clear();
            for (int i = 0; i < nameKeys.Length; i++)
            {
                int index = i;
                var step = new Button(() => Choose(index));
                step.AddToClassList(StepClass);
                var number = new Label();
                number.AddToClassList(StepClass + "__number");
                number.AddToClassList("vv-mono");
                var name = new Label();
                name.AddToClassList(StepClass + "__name");
                step.Add(number);
                step.Add(name);
                Add(step);
                _steps.Add(step);
                _texts.Add((new KeyedText(name, value => name.text = value) { Key = nameKeys[i] }, number));
            }

            RefreshNumbers();
            Show(0, 0);
        }

        /// <summary>Shows <paramref name="current"/> as the step the user is on, with steps up to <paramref name="reached"/> open.</summary>
        public void Show(int current, int reached)
        {
            _current = current;
            _reached = Math.Max(reached, current);
            for (int i = 0; i < _steps.Count; i++)
            {
                StepStatus status = StatusOf(i, _current);
                _steps[i].EnableInClassList(CurrentClass, status == StepStatus.Current);
                _steps[i].EnableInClassList(DoneClass, status == StepStatus.Done);
                _steps[i].SetEnabled(i <= _reached);
            }
        }

        /// <summary>A step's status against the current one. Pure.</summary>
        public static StepStatus StatusOf(int step, int current)
        {
            return step < current ? StepStatus.Done : step == current ? StepStatus.Current : StepStatus.Ahead;
        }

        /// <summary>"STEP n" in the language, or as it is without a string table. Pure.</summary>
        public static string NumberText(ILocalizer localizer, int index)
        {
            string number = (index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
            return localizer?.Get(StepIndicatorStrings.Number, ("n", number)) ?? "STEP " + number;
        }

        private void Choose(int index)
        {
            if (index <= _reached && index != _current)
            {
                StepChosen?.Invoke(index);
            }
        }

        private void RefreshNumbers()
        {
            ILocalizer localizer = UiServices.Find(this)?.Localizer;
            for (int i = 0; i < _texts.Count; i++)
            {
                _texts[i].Number.text = NumberText(localizer, i);
            }
        }
    }
}
