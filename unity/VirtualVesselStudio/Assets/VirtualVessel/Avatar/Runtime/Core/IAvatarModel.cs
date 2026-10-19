using System;
using System.Collections.Generic;
using UnityEngine;

namespace VirtualVessel.Avatar
{
    /// <summary>
    /// A format-independent skeleton (avatar detailed design 5.2). Rotations are relative to the
    /// reference pose (T pose), where every bone's rotation is identity, so differences in the bone axes
    /// of each model never reach the code that computes poses. Main thread only.
    /// </summary>
    public interface IAvatarRig
    {
        bool Has(AvatarBone bone);

        /// <summary>The distance to the bone's child in the reference pose, in meters; 0 for a bone without one.</summary>
        float BoneLength(AvatarBone bone);

        /// <summary>
        /// The bone's rotation from the reference pose, in the avatar root's space. Applied by
        /// <see cref="IAvatarModel.Apply"/>, parents before children, so the order of calls does not matter.
        /// Ignored for a bone the model does not have.
        /// </summary>
        void SetRotation(AvatarBone bone, Quaternion rotation);

        /// <summary>The hips position in the avatar root's space, for full body (later).</summary>
        void SetRootPosition(Vector3 position);

        /// <summary>Back to the reference pose.</summary>
        void ResetPose();
    }

    /// <summary>Format-independent expressions and gaze (avatar detailed design 5.2). Main thread only.</summary>
    public interface IAvatarExpressions
    {
        bool Has(AvatarExpression expression);

        /// <summary>0 to 1; ignored for an expression the model does not have.</summary>
        void SetWeight(AvatarExpression expression, float weight);

        void SetLookAt(float yawDegrees, float pitchDegrees);

        /// <summary>The model's own expression names, for passing values straight through later (Perfect Sync).</summary>
        IReadOnlyList<string> CustomNames { get; }
    }

    /// <summary>
    /// One avatar built by an <see cref="IAvatarLoader"/>: its objects, skeleton, expressions, and what
    /// the file says about it. Disposing destroys every object, mesh, texture, and material it made.
    /// </summary>
    public interface IAvatarModel : IDisposable
    {
        /// <summary>The avatar's root object, facing +Z in the reference pose.</summary>
        Transform Root { get; }

        IAvatarRig Rig { get; }

        IAvatarExpressions Expressions { get; }

        AvatarModelInfo Info { get; }

        void SetVisible(bool visible);

        /// <summary>
        /// Applies the rotations, expressions, and gaze set this frame, then moves secondary motion
        /// (hair, clothes). Called once per frame after the values are set (avatar detailed design 8.3).
        /// </summary>
        void Apply();
    }
}
