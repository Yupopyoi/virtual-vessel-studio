using System;
using UnityEngine.UIElements;
using VirtualVessel.UI.Controls;

namespace VirtualVessel.UI.Binding
{
    /// <summary>
    /// Runs a setup while an element is on a panel and undoes it when the element leaves, so that
    /// subscriptions never outlive what they update (UI foundation detailed design 7.3).
    /// </summary>
    public static class PanelLifetime
    {
        /// <summary>
        /// Calls <paramref name="onAttach"/> every time <paramref name="element"/> joins a panel, with the
        /// services of its scope (null outside a scope), and disposes the result when it leaves.
        /// If the element is already on a panel, <paramref name="onAttach"/> runs now.
        /// </summary>
        /// <returns>Stops the tracking and disposes the current setup.</returns>
        public static IDisposable WhileAttached(VisualElement element, Func<UiServices, IDisposable> onAttach)
        {
            if (element == null)
            {
                throw new ArgumentNullException(nameof(element));
            }

            if (onAttach == null)
            {
                throw new ArgumentNullException(nameof(onAttach));
            }

            var tracker = new Tracker(element, onAttach);
            element.RegisterCallback<AttachToPanelEvent>(tracker.OnAttach);
            element.RegisterCallback<DetachFromPanelEvent>(tracker.OnDetach);
            if (element.panel != null)
            {
                tracker.Start();
            }

            return tracker;
        }

        private sealed class Tracker : IDisposable
        {
            private readonly VisualElement _element;
            private readonly Func<UiServices, IDisposable> _onAttach;
            private IDisposable _current;
            private bool _disposed;

            public Tracker(VisualElement element, Func<UiServices, IDisposable> onAttach)
            {
                _element = element;
                _onAttach = onAttach;
            }

            public void OnAttach(AttachToPanelEvent evt)
            {
                Start();
            }

            public void OnDetach(DetachFromPanelEvent evt)
            {
                Stop();
            }

            public void Start()
            {
                Stop();
                if (!_disposed)
                {
                    _current = _onAttach(UiServices.Find(_element));
                }
            }

            public void Dispose()
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                _element.UnregisterCallback<AttachToPanelEvent>(OnAttach);
                _element.UnregisterCallback<DetachFromPanelEvent>(OnDetach);
                Stop();
            }

            private void Stop()
            {
                IDisposable current = _current;
                _current = null;
                current?.Dispose();
            }
        }
    }

    /// <summary>A disposable that runs an action once.</summary>
    internal sealed class DisposeAction : IDisposable
    {
        public static readonly IDisposable None = new DisposeAction(null);

        private Action _action;

        public DisposeAction(Action action)
        {
            _action = action;
        }

        public void Dispose()
        {
            Action action = _action;
            _action = null;
            action?.Invoke();
        }
    }
}
