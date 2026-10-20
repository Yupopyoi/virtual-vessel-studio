using UnityEngine;

namespace VirtualVessel.Stage
{
    /// <summary>
    /// Where avatars stand (system design 11.6, avatar detailed design 6.5). Stage Scenes will provide
    /// their own points; until then there is one default point.
    /// </summary>
    public interface IAvatarSpawnPoints
    {
        /// <summary>World position and rotation for an avatar whose reference pose faces +Z.</summary>
        Pose Default { get; }
    }
}
