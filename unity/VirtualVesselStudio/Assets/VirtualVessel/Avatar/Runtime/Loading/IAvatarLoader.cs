using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace VirtualVessel.Avatar
{
    /// <summary>
    /// Loads one model format (avatar detailed design 4, system design 7.3). The only place that knows a
    /// format's library; the rest of Avatar sees <see cref="IAvatarModel"/>.
    /// </summary>
    public interface IAvatarLoader
    {
        /// <summary>Such as "vrm"; written to the profile.</summary>
        string Format { get; }

        /// <summary>File extensions with the dot, in lower case, such as ".vrm".</summary>
        IReadOnlyList<string> Extensions { get; }

        /// <summary>
        /// Builds the avatar from a file, hidden. Starts on the main thread and spreads the work over
        /// several frames.
        /// </summary>
        /// <exception cref="AvatarLoadException">The file cannot be loaded; nothing is left behind.</exception>
        /// <exception cref="OperationCanceledException">Canceled; nothing is left behind.</exception>
        Task<IAvatarModel> LoadAsync(string path, CancellationToken cancellationToken);
    }

    /// <summary>What went wrong with an avatar, as shown to the user (avatar detailed design 11).</summary>
    public enum AvatarProblem
    {
        None,

        /// <summary>The file is not a model this application reads.</summary>
        NotVrm,

        TooLarge,

        /// <summary>The model lacks a required humanoid bone (8.2).</summary>
        MissingBones,

        LoadFailed,

        /// <summary>The same file is already registered.</summary>
        AlreadyImported,

        /// <summary>The avatar's files could not be written or read; check free space at the save location.</summary>
        StorageFailed,
    }

    /// <summary>A model that cannot be loaded, with the problem to show.</summary>
    public sealed class AvatarLoadException : Exception
    {
        public AvatarLoadException(AvatarProblem problem, string message, Exception innerException = null)
            : base(message, innerException)
        {
            Problem = problem;
        }

        public AvatarProblem Problem { get; }
    }
}
