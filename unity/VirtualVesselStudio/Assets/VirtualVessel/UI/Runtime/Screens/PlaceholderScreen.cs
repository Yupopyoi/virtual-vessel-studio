using UnityEngine.UIElements;
using VirtualVessel.UI.Controls;

namespace VirtualVessel.UI.Screens
{
    /// <summary>
    /// A screen whose content is not built yet: it says so (UI foundation detailed design 7.7). Replaced
    /// when the screen gets its specification.
    /// </summary>
    public sealed class PlaceholderScreen : VisualElement
    {
        public const string NoteClass = "vv-screen__note";

        /// <param name="content">Optional content shown above the note, such as the App settings card.</param>
        public PlaceholderScreen(VisualElement content = null)
        {
            if (content != null)
            {
                Add(content);
            }

            Add(Ui.Text(ScreenStrings.Placeholder, NoteClass, "vv-text-body"));
        }
    }
}
