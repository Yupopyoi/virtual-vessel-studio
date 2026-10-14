using UnityEngine.UIElements;

namespace VirtualVessel.UI.Controls
{
    /// <summary>
    /// Text elements send a <c>ChangeEvent&lt;string&gt;</c> when their text changes, and it bubbles. In a
    /// component whose own value is a string, that would look like a value change to its listeners, so
    /// the inner labels keep those events to themselves.
    /// </summary>
    internal static class QuietText
    {
        public static T Of<T>(T element)
            where T : TextElement
        {
            element.RegisterCallback<ChangeEvent<string>>(evt => evt.StopPropagation());
            return element;
        }
    }
}
