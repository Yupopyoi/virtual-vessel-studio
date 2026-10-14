using System;
using System.Collections.Generic;
using UnityEngine.UIElements;
using VirtualVessel.UI.Binding;
using VirtualVessel.UI.Controls;
using VirtualVessel.UI.Icons;
using VirtualVessel.UI.Localization;

namespace VirtualVessel.UI.Notifications
{
    /// <summary>Shows notices (components.md 3.14).</summary>
    public interface INotificationService
    {
        void Show(NoticeKind kind, string titleKey, string bodyKey = null);
    }

    /// <summary>
    /// Stacks notices in a corner of the themed root. Placement and timing come from
    /// <see cref="NotificationOptions"/>; the corner is a USS class, so moving the stack needs no code
    /// change in screens. Notices that do not fit wait, highest priority first, and the stack shows how
    /// many are waiting; no notice is dropped.
    /// </summary>
    /// <remarks>Main thread only.</remarks>
    public sealed class NotificationService : INotificationService
    {
        public const string StackClass = "vv-notices";

        private readonly NotificationOptions _options;
        private readonly List<Notice> _visible = new List<Notice>();
        private readonly List<NoticeRequest> _waiting = new List<NoticeRequest>();
        private VisualElement _stack;
        private VisualElement _list;
        private WaitingCount _waitingCount;
        private long _sequence;

        public NotificationService(NotificationOptions options = null)
        {
            _options = options ?? new NotificationOptions();
        }

        public IReadOnlyList<Notice> Visible => _visible;

        public IReadOnlyList<NoticeRequest> Waiting => _waiting;

        public void Attach(VisualElement root)
        {
            if (root == null)
            {
                throw new ArgumentNullException(nameof(root));
            }

            _stack?.RemoveFromHierarchy();
            _stack = new VisualElement { pickingMode = PickingMode.Ignore };
            _stack.AddToClassList(StackClass);
            _stack.AddToClassList(NotificationPolicy.CornerClass(_options.Corner));
            _list = new VisualElement { pickingMode = PickingMode.Ignore };
            _waitingCount = new WaitingCount();
            _stack.Add(_list);

            // The waiting count sits nearest the corner, after the newest notice.
            _stack.Insert(NotificationPolicy.NewestFirst(_options.Corner) ? 0 : 1, _waitingCount);
            root.Add(_stack);
        }

        public void Show(NoticeKind kind, string titleKey, string bodyKey = null)
        {
            if (_stack == null)
            {
                throw new InvalidOperationException("The notification service is not attached to a root.");
            }

            var request = new NoticeRequest(kind, titleKey, bodyKey, _sequence++);
            if (!NotificationPolicy.HasRoom(_visible.Count, _options.MaxVisible))
            {
                int makeRoom = NotificationPolicy.ToMakeRoomFor(Requests(_visible), request);
                if (makeRoom < 0)
                {
                    _waiting.Add(request);
                    _waitingCount.Count = _waiting.Count;
                    return;
                }

                // Sent back, not dropped: it is shown again when its turn comes.
                Notice displaced = _visible[makeRoom];
                Remove(displaced, showNext: false);
                _waiting.Add(displaced.Request);
            }

            Display(request);
            _waitingCount.Count = _waiting.Count;
        }

        /// <summary>The failure text from a command (<c>UiServices.ShowFailure</c>) as an error notice.</summary>
        public void ShowFailure(string messageKey)
        {
            Show(NoticeKind.Error, CommonStrings.FailureTitle, messageKey);
        }

        private static List<NoticeRequest> Requests(List<Notice> notices)
        {
            var requests = new List<NoticeRequest>(notices.Count);
            foreach (Notice notice in notices)
            {
                requests.Add(notice.Request);
            }

            return requests;
        }

        private void Display(NoticeRequest request)
        {
            var notice = new Notice(request);
            notice.Closed += () => Remove(notice, showNext: true);
            _visible.Add(notice);
            if (NotificationPolicy.NewestFirst(_options.Corner))
            {
                _list.Insert(0, notice);
            }
            else
            {
                _list.Add(notice);
            }

            _stack.BringToFront();
            notice.style.transitionDuration = new List<TimeValue> { new TimeValue(TransitionMilliseconds(notice), TimeUnit.Millisecond) };

            // The entering style must be applied once before it is removed, or there is nothing to animate from.
            EventCallback<GeometryChangedEvent> onFirstLayout = null;
            onFirstLayout = _ =>
            {
                notice.UnregisterCallback(onFirstLayout);
                notice.RemoveFromClassList(Notice.EnteringClass);
            };
            notice.RegisterCallback(onFirstLayout);
            StartTimer(notice, NotificationPolicy.AutoHideAfter(request.Kind, _options));
        }

        private void StartTimer(Notice notice, TimeSpan? duration)
        {
            if (duration == null)
            {
                return;
            }

            long ms = (long)duration.Value.TotalMilliseconds;
            IVisualElementScheduledItem timer = notice.schedule.Execute(() => Remove(notice, showNext: true)).StartingIn(ms);
            if (_options.PauseOnHover)
            {
                notice.RegisterCallback<PointerEnterEvent>(_ => timer.Pause());
                notice.RegisterCallback<PointerLeaveEvent>(_ => timer.ExecuteLater(ms));
            }
        }

        private void Remove(Notice notice, bool showNext)
        {
            if (!_visible.Remove(notice))
            {
                return;
            }

            long delay = TransitionMilliseconds(notice);
            if (delay == 0)
            {
                notice.RemoveFromHierarchy();
            }
            else
            {
                notice.AddToClassList(Notice.LeavingClass);
                notice.schedule.Execute(notice.RemoveFromHierarchy).StartingIn(delay);
            }

            if (showNext)
            {
                ShowWaiting();
            }
        }

        private void ShowWaiting()
        {
            while (_waiting.Count > 0 && NotificationPolicy.HasRoom(_visible.Count, _options.MaxVisible))
            {
                int next = NotificationPolicy.NextToShow(_waiting);
                NoticeRequest request = _waiting[next];
                _waiting.RemoveAt(next);
                Display(request);
            }

            _waitingCount.Count = _waiting.Count;
        }

        /// <summary>The transition length is set from the options here, so USS does not repeat the number.</summary>
        private long TransitionMilliseconds(VisualElement notice)
        {
            return ReducedMotion.IsOn(notice) ? 0 : (long)_options.TransitionDuration.TotalMilliseconds;
        }
    }

    /// <summary>One notice: a status shape, a bold title, a body that says what to do next, and Close.</summary>
    public sealed class Notice : VisualElement
    {
        public const string UssClass = "vv-notice";

        public const string EnteringClass = UssClass + "--entering";

        public const string LeavingClass = UssClass + "--leaving";

        public Notice(NoticeRequest request)
        {
            Request = request;
            AddToClassList(UssClass);
            AddToClassList(UssClass + "--" + request.Kind.ToString().ToLowerInvariant());
            AddToClassList(EnteringClass);

            var icon = new Icon(NotificationPolicy.IconFor(request.Kind));
            icon.AddToClassList(UssClass + "__icon");
            Add(icon);

            var text = new VisualElement();
            text.AddToClassList(UssClass + "__text");
            Title = QuietText.Of(new Label());
            Title.AddToClassList(UssClass + "__title");
            Title.AddToClassList("vv-bold");
            Body = QuietText.Of(new Label());
            Body.AddToClassList(UssClass + "__body");
            Body.AddToClassList("vv-text-caption");
            text.Add(Title);
            text.Add(Body);
            Add(text);
            new KeyedText(this, value => Title.text = value) { Key = request.TitleKey };
            new KeyedText(this, value =>
            {
                Body.text = value;
                Body.style.display = string.IsNullOrEmpty(value) ? DisplayStyle.None : DisplayStyle.Flex;
            }) { Key = request.BodyKey };

            CloseButton = new IconButton(IconName.Close) { TooltipKey = CommonStrings.Close };
            CloseButton.AddToClassList(UssClass + "__close");
            CloseButton.clicked += () => Closed?.Invoke();
            Add(CloseButton);
        }

        public event Action Closed;

        public NoticeRequest Request { get; }

        public NoticeKind Kind => Request.Kind;

        public Label Title { get; }

        public Label Body { get; }

        public IconButton CloseButton { get; }
    }

    /// <summary>"N more" at the corner end of the stack, so that waiting notices are not hidden. Hidden at zero.</summary>
    public sealed class WaitingCount : VisualElement
    {
        public const string UssClass = "vv-notices__waiting";

        private readonly Label _label;
        private ILocalizer _localizer;
        private int _count;

        public WaitingCount()
        {
            pickingMode = PickingMode.Ignore;
            AddToClassList(UssClass);
            Add(new Icon(IconName.Plus) { pickingMode = PickingMode.Ignore });
            _label = QuietText.Of(new Label { pickingMode = PickingMode.Ignore });
            _label.AddToClassList("vv-text-caption");
            _label.AddToClassList("vv-bold");
            Add(_label);
            Refresh();
            PanelLifetime.WhileAttached(this, services =>
            {
                ILocalizer localizer = services?.Localizer;
                _localizer = localizer;
                Refresh();
                if (localizer == null)
                {
                    return DisposeAction.None;
                }

                Action<LocaleInfo> onChanged = _ => Refresh();
                localizer.LocaleChanged += onChanged;
                return new DisposeAction(() =>
                {
                    localizer.LocaleChanged -= onChanged;
                    _localizer = null;
                });
            });
        }

        public int Count
        {
            get => _count;
            set
            {
                _count = value;
                Refresh();
            }
        }

        public string Text => _label.text;

        /// <summary>The text for <paramref name="count"/> waiting notices; empty for none. Pure.</summary>
        public static string Format(ILocalizer localizer, int count)
        {
            if (count <= 0)
            {
                return string.Empty;
            }

            return localizer != null ? localizer.Plural(CommonStrings.MoreNotices, count) : "[" + CommonStrings.MoreNotices + "]";
        }

        private void Refresh()
        {
            _label.text = Format(_localizer, _count);
            style.display = _count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }
}
