using System;
using System.Threading.Tasks;
using UnityEngine.UIElements;
using VirtualVessel.Core.State;
using VirtualVessel.UI.Commands;
using VirtualVessel.UI.Controls;

namespace VirtualVessel.UI.Binding
{
    /// <summary>Moves UI updates to the main thread when they come from another thread.</summary>
    internal static class MainThread
    {
        public static void Run(UiServices services, Action action)
        {
            if (services?.Dispatcher == null || services.Dispatcher.IsMainThread)
            {
                action();
            }
            else
            {
                services.Dispatcher.Post(action);
            }
        }
    }

    /// <summary>
    /// Connects runtime state to components (UI foundation detailed design 7.3). Subscriptions start when
    /// the element joins a panel and end when it leaves; nothing has to be unsubscribed by hand.
    /// </summary>
    public static class ObservableBinding
    {
        /// <summary>Calls <paramref name="apply"/> with the current value now and after every change, on the main thread.</summary>
        public static IDisposable Bind<T>(this VisualElement element, IReadOnlyObservable<T> source, Action<T> apply)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            if (apply == null)
            {
                throw new ArgumentNullException(nameof(apply));
            }

            return PanelLifetime.WhileAttached(element, services =>
            {
                apply(source.Value);
                return source.Subscribe(_ => MainThread.Run(services, () =>
                {
                    // Read the latest value: posted updates can arrive out of order across threads.
                    if (element.panel != null)
                    {
                        apply(source.Value);
                    }
                }));
            });
        }

        /// <summary>Shows the value as text, formatted by <paramref name="format"/>.</summary>
        public static IDisposable BindText<T>(this TextElement element, IReadOnlyObservable<T> source, Func<T, string> format)
        {
            if (format == null)
            {
                throw new ArgumentNullException(nameof(format));
            }

            return element.Bind(source, value => element.text = format(value));
        }

        /// <summary>
        /// Shows the runtime value with <c>SetValueWithoutNotify</c> and passes only the user's changes
        /// to <paramref name="onUserChange"/>. When that finishes, the field shows the runtime value
        /// again, so a rejected or failed change does not stay on screen.
        /// </summary>
        public static IDisposable BindValue<TField, T>(this TField field, IReadOnlyObservable<T> source, Func<T, Task> onUserChange)
            where TField : VisualElement, INotifyValueChanged<T>
        {
            if (onUserChange == null)
            {
                throw new ArgumentNullException(nameof(onUserChange));
            }

            EventCallback<ChangeEvent<T>> onChange = async evt =>
            {
                if (evt.target != field)
                {
                    return;
                }

                await CommandRunner.RunAsync(() => onUserChange(evt.newValue), UiServices.Find(field));
                field.SetValueWithoutNotify(source.Value);
            };
            field.RegisterCallback(onChange);
            IDisposable binding = field.Bind(source, field.SetValueWithoutNotify);
            return new DisposeAction(() =>
            {
                field.UnregisterCallback(onChange);
                binding.Dispose();
            });
        }

        /// <summary><see cref="BindValue{TField,T}"/> for a synchronous handler.</summary>
        public static IDisposable BindValue<TField, T>(this TField field, IReadOnlyObservable<T> source, Action<T> onUserChange)
            where TField : VisualElement, INotifyValueChanged<T>
        {
            if (onUserChange == null)
            {
                throw new ArgumentNullException(nameof(onUserChange));
            }

            return field.BindValue<TField, T>(source, value =>
            {
                onUserChange(value);
                return Task.CompletedTask;
            });
        }
    }
}
