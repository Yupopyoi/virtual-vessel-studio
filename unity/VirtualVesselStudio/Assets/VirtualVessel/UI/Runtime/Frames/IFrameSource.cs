using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using VirtualVessel.Core.Geometry;
using VirtualVessel.Core.State;

namespace VirtualVessel.UI.Frames
{
    /// <summary>
    /// One video placed on the stream screen, as the placement editor sees it (UI foundation detailed
    /// design 9.5). Stage implements it: it owns the confirmed layout, shows previews at once, and saves
    /// commits.
    /// </summary>
    public interface IFrameSource
    {
        /// <summary>A stable identifier, such as the capture source's id.</summary>
        string Id { get; }

        /// <summary>
        /// A string table key for the name, which the UI shows in the user's language, so that Stage need
        /// not know the language (UI foundation detailed design 9.8). Null to show <see cref="DisplayName"/>.
        /// </summary>
        string NameKey { get; }

        /// <summary>The name when there is no <see cref="NameKey"/>: data text such as a device name.</summary>
        IReadOnlyObservable<string> DisplayName { get; }

        /// <summary>
        /// Whether the video is on the stream now. A hidden video cannot be selected or grabbed in the
        /// preview, and the layer list marks it as hidden.
        /// </summary>
        IReadOnlyObservable<bool> Visible { get; }

        /// <summary>Width / height of the whole video in pixels, for fitting it to the screen.</summary>
        double SourceAspect { get; }

        /// <summary>
        /// The video, if the editor may show it: in crop mode it is drawn faintly at full size so that
        /// it is clear what can be brought back. Null shows only an outline there.
        /// </summary>
        Texture Video { get; }

        /// <summary>The confirmed layout (runtime state). The editor shows it whenever no edit is pending.</summary>
        IReadOnlyObservable<FrameLayout> Layout { get; }

        /// <summary>
        /// <c>LayoutChanging</c>: called on every step of a drag so that the stream preview follows the
        /// pointer. Not saved; a later commit or a revert follows.
        /// </summary>
        void PreviewLayout(FrameLayout layout);

        /// <summary>
        /// <c>LayoutCommitted</c>: confirms an edit. If Stage rejects it, <see cref="Layout"/> keeps the
        /// old value and the editor returns to it; a rejection the user should hear about is a
        /// <c>UserFacingException</c>.
        /// </summary>
        Task CommitLayoutAsync(FrameLayout layout, CancellationToken cancellationToken);
    }
}
