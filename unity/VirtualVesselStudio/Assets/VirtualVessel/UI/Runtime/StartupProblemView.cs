using System;
using UnityEngine.UIElements;
using VirtualVessel.UI.Controls;
using VirtualVessel.UI.Icons;
using VirtualVessel.UI.Localization;

namespace VirtualVessel.UI
{
    /// <summary>Why the application cannot be used (UI foundation detailed design 7.9).</summary>
    public enum StartupProblemKind
    {
        /// <summary>The Data Root folder cannot be created or written.</summary>
        DataRootUnavailable,

        /// <summary>A required service or the UI itself failed to start.</summary>
        StartupFailed,
    }

    /// <summary>String table keys of the UI root: the startup problem screen and settings notices.</summary>
    [StringKeys]
    public static class UiRootStrings
    {
        public const string DataRootTitle = "startup.data-root.title";
        public const string DataRootBody = "startup.data-root.body";
        public const string FailedTitle = "startup.failed.title";
        public const string FailedBody = "startup.failed.body";
        public const string Quit = "startup.quit";

        /// <summary>The settings file could not be read and was moved aside.</summary>
        public const string SettingsRecoveredTitle = "settings.recovered.title";
        public const string SettingsRecoveredBody = "settings.recovered.body";

        public const string SaveFailedTitle = "settings.save-failed.title";
        public const string SaveFailedBody = "settings.save-failed.body";
    }

    /// <summary>
    /// The minimal screen shown when the application cannot start (UI foundation detailed design 7.9).
    /// </summary>
    /// <remarks>
    /// It states what the user can do and never shows exception text or other internals; those go to
    /// the developer log. The Data Root path is shown because it is the user's own folder to check.
    /// </remarks>
    public sealed class StartupProblemView : VisualElement
    {
        public const string UssClass = "vv-startup";
        public const string PanelClass = UssClass + "__panel";
        public const string IconClass = UssClass + "__icon";
        public const string TitleClass = UssClass + "__title";
        public const string BodyClass = UssClass + "__body";
        public const string PathClass = UssClass + "__path";
        public const string ButtonsClass = UssClass + "__buttons";

        /// <param name="path">The Data Root path for <see cref="StartupProblemKind.DataRootUnavailable"/>; otherwise ignored.</param>
        /// <param name="quit">Runs the normal shutdown.</param>
        public StartupProblemView(StartupProblemKind kind, string path, Action quit)
        {
            Kind = kind;
            AddToClassList(UssClass);

            var panel = new VisualElement();
            panel.AddToClassList(PanelClass);
            Add(panel);

            var heading = new VisualElement();
            heading.style.flexDirection = FlexDirection.Row;
            heading.style.alignItems = Align.Center;
            var icon = new Icon(IconName.Close);
            icon.AddToClassList(IconClass);
            heading.Add(icon);
            bool dataRoot = kind == StartupProblemKind.DataRootUnavailable;
            Title = Text(dataRoot ? UiRootStrings.DataRootTitle : UiRootStrings.FailedTitle, TitleClass, "vv-text-heading");
            heading.Add(Title);
            panel.Add(heading);

            Body = Text(dataRoot ? UiRootStrings.DataRootBody : UiRootStrings.FailedBody, BodyClass, "vv-text-body");
            panel.Add(Body);

            if (dataRoot && !string.IsNullOrEmpty(path))
            {
                // Selectable so the user can copy the path into Explorer.
                PathLabel = new Label(path) { selection = { isSelectable = true } };
                PathLabel.AddToClassList(PathClass);
                PathLabel.AddToClassList("vv-text-mono");
                panel.Add(PathLabel);
            }

            var buttons = new VisualElement();
            buttons.AddToClassList(ButtonsClass);
            QuitButton = new ActionButton(UiRootStrings.Quit, ButtonVariant.Primary);
            QuitButton.clicked += () => quit?.Invoke();
            buttons.Add(QuitButton);
            panel.Add(buttons);

            RegisterCallback<AttachToPanelEvent>(_ => schedule.Execute(() => QuitButton.Focus()));
        }

        public StartupProblemKind Kind { get; }

        public Label Title { get; }

        public Label Body { get; }

        /// <summary>Null unless the Data Root path is shown.</summary>
        public Label PathLabel { get; }

        public ActionButton QuitButton { get; }

        private static Label Text(string key, string className, string typography)
        {
            var label = new Label();
            label.AddToClassList(className);
            label.AddToClassList(typography);
            new KeyedText(label, text => label.text = text) { Key = key };
            return label;
        }
    }
}
