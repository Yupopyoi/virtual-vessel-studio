using UnityEngine;

namespace VirtualVessel.UI.Theme
{
    /// <summary>
    /// The fonts the whole UI uses, as one unit (UI foundation detailed design 7.2). Changing the
    /// fonts of the application means changing or switching this set, never individual screens.
    /// </summary>
    [CreateAssetMenu(menuName = "Virtual Vessel/UI/Font Set", fileName = "FontSet")]
    public sealed class FontSet : ScriptableObject
    {
        [SerializeField]
        private string _id = "NotoSans";

        [SerializeField]
        private string _displayName = "Noto Sans";

        [SerializeField]
        [Tooltip("Body text, labels, captions.")]
        private Font _regular;

        [SerializeField]
        [Tooltip("Headings, buttons, and other bold text.")]
        private Font _bold;

        [SerializeField]
        [Tooltip("Numbers, logs, identifiers.")]
        private Font _mono;

        /// <summary>Stable id stored in ui-settings.json, such as "NotoSans".</summary>
        public string Id => _id;

        public string DisplayName => _displayName;

        public Font Regular => _regular;

        public Font Bold => _bold;

        public Font Mono => _mono;
    }
}
