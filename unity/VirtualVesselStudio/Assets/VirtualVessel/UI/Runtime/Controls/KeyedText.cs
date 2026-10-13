using System;
using UnityEngine.UIElements;
using VirtualVessel.UI.Binding;
using VirtualVessel.UI.Localization;

namespace VirtualVessel.UI.Controls
{
    /// <summary>
    /// Text given by a string table key (the <c>*-key</c> attributes, UI foundation detailed design 6.2).
    /// It is looked up when the owner joins a panel and again whenever the language changes.
    /// </summary>
    public sealed class KeyedText
    {
        private readonly Action<string> _apply;
        private ILocalizer _localizer;
        private string _key;

        public KeyedText(VisualElement owner, Action<string> apply)
        {
            _apply = apply ?? throw new ArgumentNullException(nameof(apply));
            PanelLifetime.WhileAttached(owner, services =>
            {
                ILocalizer localizer = services?.Localizer;
                _localizer = localizer;
                Apply();
                if (localizer == null)
                {
                    return DisposeAction.None;
                }

                Action<LocaleInfo> onChanged = _ => Apply();
                localizer.LocaleChanged += onChanged;
                return new DisposeAction(() =>
                {
                    localizer.LocaleChanged -= onChanged;
                    _localizer = null;
                });
            });
        }

        public string Key
        {
            get => _key;
            set
            {
                if (!string.Equals(_key, value, StringComparison.Ordinal))
                {
                    _key = value;
                    Apply();
                }
            }
        }

        /// <summary>
        /// The text for <paramref name="key"/>: empty without a key, and <c>[key]</c> outside a scope so
        /// that layouts in the UI Builder still show which text goes where.
        /// </summary>
        public static string Resolve(ILocalizer localizer, string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return string.Empty;
            }

            return localizer != null ? localizer.Get(key) : "[" + key + "]";
        }

        private void Apply()
        {
            _apply(Resolve(_localizer, _key));
        }
    }
}
