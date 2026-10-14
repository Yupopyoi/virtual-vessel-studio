using System;
using System.Collections.Generic;
using VirtualVessel.UI.Icons;

namespace VirtualVessel.UI.Notifications
{
    public enum NoticeKind
    {
        Info,
        Success,
        Warning,
        Error,
    }

    /// <summary>Screen corner where notices stack.</summary>
    public enum NoticeCorner
    {
        BottomRight,
        BottomLeft,
        TopRight,
        TopLeft,
    }

    /// <summary>A notice to show, in the order it was asked for.</summary>
    public readonly struct NoticeRequest
    {
        public NoticeRequest(NoticeKind kind, string titleKey, string bodyKey, long sequence)
        {
            Kind = kind;
            TitleKey = titleKey;
            BodyKey = bodyKey;
            Sequence = sequence;
        }

        public NoticeKind Kind { get; }

        public string TitleKey { get; }

        public string BodyKey { get; }

        /// <summary>Arrival order; earlier requests of the same priority go first.</summary>
        public long Sequence { get; }
    }

    /// <summary>
    /// Where notices appear and how long they stay. These are defaults that may change; everything that
    /// depends on them reads them from here (UI foundation detailed design 7.8).
    /// </summary>
    public sealed class NotificationOptions
    {
        public NoticeCorner Corner { get; set; } = NoticeCorner.BottomRight;

        /// <summary>Time on screen per kind; null keeps the notice until it is closed.</summary>
        public TimeSpan? InfoDuration { get; set; } = TimeSpan.FromSeconds(5);

        public TimeSpan? SuccessDuration { get; set; } = TimeSpan.FromSeconds(5);

        public TimeSpan? WarningDuration { get; set; } = TimeSpan.FromSeconds(8);

        /// <summary>Errors stay by default, so that they are not missed.</summary>
        public TimeSpan? ErrorDuration { get; set; }

        /// <summary>
        /// At most this many notices are shown. Later ones wait and appear in order as shown ones leave, so
        /// no notice is lost and an error that stays is never pushed out.
        /// </summary>
        public int MaxVisible { get; set; } = 3;

        /// <summary>Holds the timer while the pointer is on a notice; it starts again when the pointer leaves.</summary>
        public bool PauseOnHover { get; set; } = true;

        /// <summary>Length of the enter and leave transitions (design-system.md 7); none with reduced motion.</summary>
        public TimeSpan TransitionDuration { get; set; } = TimeSpan.FromMilliseconds(150);
    }

    /// <summary>Decisions about notices. Pure.</summary>
    public static class NotificationPolicy
    {
        public static TimeSpan? AutoHideAfter(NoticeKind kind, NotificationOptions options)
        {
            return kind switch
            {
                NoticeKind.Success => options.SuccessDuration,
                NoticeKind.Warning => options.WarningDuration,
                NoticeKind.Error => options.ErrorDuration,
                _ => options.InfoDuration,
            };
        }

        /// <summary>Whether another notice can be shown now, or must wait. At least one always fits.</summary>
        public static bool HasRoom(int visible, int maxVisible)
        {
            return visible < Math.Max(1, maxVisible);
        }

        /// <summary>Which notices go first when not all fit: Error, then Warning, Success, and Info.</summary>
        public static int Priority(NoticeKind kind)
        {
            return kind switch
            {
                NoticeKind.Error => 3,
                NoticeKind.Warning => 2,
                NoticeKind.Success => 1,
                _ => 0,
            };
        }

        /// <summary>The waiting notice to show next: the highest priority, then the earliest. -1 when none wait.</summary>
        public static int NextToShow(IReadOnlyList<NoticeRequest> waiting)
        {
            int best = -1;
            for (int i = 0; i < waiting.Count; i++)
            {
                if (best < 0 || GoesBefore(waiting[i], waiting[best]))
                {
                    best = i;
                }
            }

            return best;
        }

        /// <summary>
        /// The shown notice to send back to the waiting list so that <paramref name="incoming"/> can be shown
        /// now: the lowest-priority one, the oldest of those, and only if its priority is lower than the
        /// incoming one's. -1 when the incoming notice must wait.
        /// </summary>
        public static int ToMakeRoomFor(IReadOnlyList<NoticeRequest> shown, NoticeRequest incoming)
        {
            int lowest = -1;
            for (int i = 0; i < shown.Count; i++)
            {
                if (lowest < 0 || Priority(shown[i].Kind) < Priority(shown[lowest].Kind)
                    || (Priority(shown[i].Kind) == Priority(shown[lowest].Kind) && shown[i].Sequence < shown[lowest].Sequence))
                {
                    lowest = i;
                }
            }

            return lowest >= 0 && Priority(shown[lowest].Kind) < Priority(incoming.Kind) ? lowest : -1;
        }

        /// <summary>The USS class that places the stack, such as <c>vv-notices--bottom-right</c>.</summary>
        public static string CornerClass(NoticeCorner corner)
        {
            return corner switch
            {
                NoticeCorner.BottomLeft => "vv-notices--bottom-left",
                NoticeCorner.TopRight => "vv-notices--top-right",
                NoticeCorner.TopLeft => "vv-notices--top-left",
                _ => "vv-notices--bottom-right",
            };
        }

        /// <summary>
        /// Newest notices, and the count of waiting ones, sit nearest the corner: at the bottom of a bottom
        /// stack, at the top of a top stack.
        /// </summary>
        public static bool NewestFirst(NoticeCorner corner)
        {
            return corner == NoticeCorner.TopRight || corner == NoticeCorner.TopLeft;
        }

        /// <summary>The shape for each kind; status is never shown by color alone (design-system.md 9.1).</summary>
        public static IconName IconFor(NoticeKind kind)
        {
            return kind switch
            {
                NoticeKind.Success => IconName.Check,
                NoticeKind.Warning => IconName.Warning,
                NoticeKind.Error => IconName.Close,
                _ => IconName.Info,
            };
        }

        private static bool GoesBefore(NoticeRequest a, NoticeRequest b)
        {
            int byPriority = Priority(a.Kind).CompareTo(Priority(b.Kind));
            return byPriority > 0 || (byPriority == 0 && a.Sequence < b.Sequence);
        }
    }
}
