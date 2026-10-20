using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using VirtualVessel.Core.State;

namespace VirtualVessel.Avatar
{
    public enum AvatarState
    {
        /// <summary>No avatar is chosen.</summary>
        None,

        Loading,

        Ready,

        /// <summary>The chosen avatar could not be loaded; <see cref="IAvatarService.Problem"/> says why.</summary>
        Error,
    }

    /// <summary>
    /// What other modules use (avatar detailed design 5.1): the registered avatars and the one in use.
    /// All members are used on the main thread; file work runs on other threads.
    /// </summary>
    public interface IAvatarService
    {
        /// <summary>Registered avatars, oldest first, including ones that cannot be loaded.</summary>
        IReadOnlyObservable<IReadOnlyList<AvatarEntry>> Avatars { get; }

        /// <summary>The avatar in use, or null.</summary>
        IReadOnlyObservable<string> CurrentAvatarId { get; }

        IReadOnlyObservable<AvatarState> State { get; }

        /// <summary>The latest problem to show; None after a success.</summary>
        IReadOnlyObservable<AvatarProblem> Problem { get; }

        /// <summary>File extensions that can be registered, such as ".vrm", for the file-picking screen.</summary>
        IReadOnlyList<string> Extensions { get; }

        /// <summary>
        /// Registers a model file (avatar detailed design 6.2). <paramref name="confirm"/> is shown the
        /// model's information and conditions before it is registered; false declines. The first avatar
        /// registered is put into use.
        /// </summary>
        Task<AvatarImportResult> ImportAsync(string path, Func<AvatarModelInfo, Task<bool>> confirm, CancellationToken cancellationToken);

        /// <summary>
        /// Loads the avatar and puts it on the stream in place of the current one, which stays if loading
        /// fails. Null takes the current avatar away.
        /// </summary>
        Task SelectAsync(string avatarId, CancellationToken cancellationToken);

        /// <summary>Unregisters the avatar and deletes its folder; the UI asks first.</summary>
        Task RemoveAsync(string avatarId, CancellationToken cancellationToken);

        /// <summary>How strongly the user's face moves the avatar (avatar detailed design 7.4). Never null.</summary>
        IReadOnlyObservable<FaceTuning> FaceTuning { get; }

        /// <summary>Applies <paramref name="tuning"/> from the next observation and saves it.</summary>
        void SetFaceTuning(FaceTuning tuning);

        /// <summary>
        /// What the avatar's face shows this frame, for showing the result of tuning; null without an
        /// avatar. Rewritten every frame, so read it within the frame and do not keep it.
        /// </summary>
        FaceTarget Face { get; }
    }

    /// <summary>Where the avatar goes on the stream: its Unity layer (Video) and spawn point (Stage).</summary>
    public readonly struct AvatarPlacement
    {
        public AvatarPlacement(int layer, Pose spawn)
        {
            Layer = layer;
            Spawn = spawn;
        }

        public int Layer { get; }

        public Pose Spawn { get; }
    }
}
