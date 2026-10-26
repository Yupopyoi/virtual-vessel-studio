using System;
using System.Collections.Generic;
using UnityEngine.UIElements;
using VirtualVessel.Tracking;
using VirtualVessel.UI.Binding;
using VirtualVessel.UI.Controls;
using VirtualVessel.UI.Localization;

namespace VirtualVessel.UI.Screens
{
    /// <summary>String table keys of the arms and hands in view (docs/&lt;lang&gt;/ui/screens/live.md 2).</summary>
    [StringKeys]
    public static class LimbStrings
    {
        public const string Title = "live.limbs";
        public const string LeftArmSeen = "live.limbs.left-arm.seen";
        public const string LeftArmPartly = "live.limbs.left-arm.partly";
        public const string LeftArmUnseen = "live.limbs.left-arm.unseen";
        public const string RightArmSeen = "live.limbs.right-arm.seen";
        public const string RightArmPartly = "live.limbs.right-arm.partly";
        public const string RightArmUnseen = "live.limbs.right-arm.unseen";
        public const string LeftHandSeen = "live.limbs.left-hand.seen";
        public const string LeftHandUnseen = "live.limbs.left-hand.unseen";
        public const string RightHandSeen = "live.limbs.right-hand.seen";
        public const string RightHandUnseen = "live.limbs.right-hand.unseen";
    }

    /// <summary>How much of an arm or a hand the camera sees, as the avatar uses it.</summary>
    public enum LimbSeen
    {
        /// <summary>Not in view: the avatar holds it, then lets it rest.</summary>
        Unseen,

        /// <summary>The upper arm moves, but the wrist is hidden or out of the image.</summary>
        Partly,

        Seen,
    }

    /// <summary>
    /// Which of the person's arms and hands the camera sees, on the live screen (docs/&lt;lang&gt;/ui/screens/live.md
    /// 2): a chip each, by the person's own sides, with a shape and a word, never color alone. The same
    /// test the avatar uses (<see cref="BodyPose.IsSeen(BodyJoint)"/>), so the user can tell why an arm or a
    /// hand rests. Shown only while tracking runs.
    /// </summary>
    public sealed class LimbStatusRow : VisualElement
    {
        public const string UssClass = "vv-limbs";

        /// <summary>How often the chips follow the tracking, in milliseconds.</summary>
        public const long RefreshIntervalMs = 200;

        private readonly ITrackingSource _tracking;

        public LimbStatusRow(ITrackingSource tracking)
        {
            _tracking = tracking ?? throw new ArgumentNullException(nameof(tracking));
            AddToClassList(UssClass);
            Add(Ui.Text(LimbStrings.Title, UssClass + "__title", "vv-text-caption"));
            LeftArm = Ui.Chip(StatusKind.Idle, LimbStrings.LeftArmUnseen);
            LeftHand = Ui.Chip(StatusKind.Idle, LimbStrings.LeftHandUnseen);
            RightArm = Ui.Chip(StatusKind.Idle, LimbStrings.RightArmUnseen);
            RightHand = Ui.Chip(StatusKind.Idle, LimbStrings.RightHandUnseen);
            foreach (StatusChip chip in new[] { LeftArm, LeftHand, RightArm, RightHand })
            {
                chip.AddToClassList(UssClass + "__chip");
                Add(chip);
            }

            Refresh();
            PanelLifetime.WhileAttached(this, _ =>
            {
                IVisualElementScheduledItem refresh = schedule.Execute(Refresh).Every(RefreshIntervalMs);
                var subscription = _tracking.State.Subscribe(_ => Refresh());
                return new DisposeAll(new List<IDisposable> { subscription, new DisposeAction(refresh.Pause) });
            });
        }

        public StatusChip LeftArm { get; }

        public StatusChip LeftHand { get; }

        public StatusChip RightArm { get; }

        public StatusChip RightHand { get; }

        /// <summary>How much of the person's arm the avatar can use. Pure.</summary>
        public static LimbSeen Arm(BodyPose body, bool left)
        {
            if (body == null || !body.IsUsable)
            {
                return LimbSeen.Unseen;
            }

            bool upper = body.IsSeen(left ? BodyJoint.LeftShoulder : BodyJoint.RightShoulder) && body.IsSeen(left ? BodyJoint.LeftElbow : BodyJoint.RightElbow);
            if (!upper)
            {
                return LimbSeen.Unseen;
            }

            return body.IsSeen(left ? BodyJoint.LeftWrist : BodyJoint.RightWrist) ? LimbSeen.Seen : LimbSeen.Partly;
        }

        /// <summary>Whether the person's hand is seen. Pure.</summary>
        public static LimbSeen Hand(HandPose hand)
        {
            return hand != null && hand.IsUsable ? LimbSeen.Seen : LimbSeen.Unseen;
        }

        /// <summary>The chip's look for <paramref name="seen"/>: a check when seen, a warning when partly, an outline when not. Pure.</summary>
        public static StatusKind KindOf(LimbSeen seen)
        {
            return seen == LimbSeen.Seen ? StatusKind.Success : seen == LimbSeen.Partly ? StatusKind.Warning : StatusKind.Idle;
        }

        private void Refresh()
        {
            bool running = _tracking.State.Value == TrackingState.Running;
            style.display = running ? DisplayStyle.Flex : DisplayStyle.None;
            if (!running)
            {
                return;
            }

            // Read within this frame: the frame object is overwritten by the next update.
            TrackingFrame frame = _tracking.Current;
            Show(LeftArm, Arm(frame?.Body, left: true), LimbStrings.LeftArmSeen, LimbStrings.LeftArmPartly, LimbStrings.LeftArmUnseen);
            Show(RightArm, Arm(frame?.Body, left: false), LimbStrings.RightArmSeen, LimbStrings.RightArmPartly, LimbStrings.RightArmUnseen);
            Show(LeftHand, Hand(frame?.LeftHand), LimbStrings.LeftHandSeen, null, LimbStrings.LeftHandUnseen);
            Show(RightHand, Hand(frame?.RightHand), LimbStrings.RightHandSeen, null, LimbStrings.RightHandUnseen);
        }

        private static void Show(StatusChip chip, LimbSeen seen, string seenKey, string partlyKey, string unseenKey)
        {
            chip.Status = KindOf(seen);
            chip.TextKey = seen == LimbSeen.Seen ? seenKey : seen == LimbSeen.Partly ? partlyKey : unseenKey;
        }
    }
}
