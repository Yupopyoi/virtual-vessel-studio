using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using VirtualVessel.Core.Geometry;
using VirtualVessel.Core.State;
using VirtualVessel.UI.Commands;
using VirtualVessel.UI.Frames;
using VirtualVessel.Video;

namespace VirtualVessel.Stage
{
    /// <summary>
    /// The avatar's frame in the placement editor (Stage detailed design 7.2): moved and resized like a video, its crop
    /// choosing how much of the avatar shows. It sets how the Avatar Camera shoots the avatar. Main thread only.
    /// </summary>
    internal sealed class AvatarFrame : IFrameSource, IDisposable
    {
        public const string FrameId = "avatar";
        public const string FrameNameKey = "stage.avatar.name";

        /// <summary>The avatar is drawn in front of every flat video, so its frame stays in front of theirs.</summary>
        public const int FrontOrder = 1_000_000;

        private readonly IAvatarCamera _camera;
        private readonly IReadOnlyObservable<float?> _height;
        private readonly Pose _spawn;
        private readonly double _streamAspect;
        private readonly Action<AvatarFrame> _committed;
        private readonly ObservableValue<FrameLayout> _layout;
        private readonly ObservableValue<bool> _visible = new ObservableValue<bool>(false);
        private readonly IReadOnlyObservable<string> _noName = new ObservableValue<string>(string.Empty);
        private readonly IDisposable _heightChanges;

        /// <param name="height">The avatar's height in meters; null without an avatar (avatar detailed design 6.5).</param>
        public AvatarFrame(IAvatarCamera camera, IReadOnlyObservable<float?> height, Pose spawn, double streamAspect, FrameLayout layout, Action<AvatarFrame> committed)
        {
            _camera = camera ?? throw new ArgumentNullException(nameof(camera));
            _height = height ?? throw new ArgumentNullException(nameof(height));
            _spawn = spawn;
            _streamAspect = streamAspect;
            _committed = committed;
            _layout = new ObservableValue<FrameLayout>(InFront(layout));
            _heightChanges = height.Subscribe(_ => Apply(_layout.Value));
            Apply(_layout.Value);
        }

        public string Id => FrameId;

        public string NameKey => FrameNameKey;

        public IReadOnlyObservable<string> DisplayName => _noName;

        public IReadOnlyObservable<bool> Visible => _visible;

        public double SourceAspect => AvatarFramingMath.SourceAspect;

        /// <summary>No picture of its own: in crop mode the editor shows only the outline of the whole square.</summary>
        public Texture Video => null;

        public IReadOnlyObservable<FrameLayout> Layout => _layout;

        public bool StaysInFront => true;

        public bool CanFlip => false;

        public void PreviewLayout(FrameLayout layout)
        {
            if (ScreenPlacementMath.IsUsable(layout))
            {
                Apply(InFront(layout));
            }
        }

        public Task CommitLayoutAsync(FrameLayout layout, CancellationToken cancellationToken)
        {
            if (!ScreenPlacementMath.IsUsable(layout))
            {
                Apply(_layout.Value);
                throw new UserFacingException(CaptureScreen.InvalidPlacementKey, "The avatar frame got an unusable placement.");
            }

            _layout.Value = InFront(layout);
            Apply(_layout.Value);
            _committed?.Invoke(this);
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            _heightChanges.Dispose();
            _camera.SetFraming(null);
        }

        /// <summary>The layout as kept: in front, and never flipped, which the avatar does not support. Pure.</summary>
        public static FrameLayout InFront(FrameLayout layout)
        {
            return layout.WithOrder(FrontOrder).WithFlipHorizontal(false);
        }

        private void Apply(FrameLayout layout)
        {
            AvatarFraming? framing = _height.Value is float height ? AvatarFramingMath.Framing(_spawn, height, layout, _streamAspect) : null;
            _visible.Value = framing != null;
            _camera.SetFraming(framing);
        }
    }
}
