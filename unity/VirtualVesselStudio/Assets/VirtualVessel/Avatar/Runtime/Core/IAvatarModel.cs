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

        /// <summary>
        /// Where the eyes look, as a share of the model's own eye range, -1 to 1: positive
        /// <paramref name="yaw"/> toward the avatar's own right, positive <paramref name="pitch"/> up.
        /// A share, not an angle, because each model maps look angles onto its eyes differently.
        /// </summary>
        void SetLook(float yaw, float pitch);

        /// <summary>The model's own expression names, for passing values straight through later (Perfect Sync).</summary>
        IReadOnlyList<string> CustomNames { get; }
    }

    /// <summary>
    /// A model's own face shapes (blend shapes), by name, for moving the face part by part
    /// (avatar detailed design 7.4). The names follow the model maker's convention, such as VRoid's;
    /// <see cref="FacePartProfile"/> says which name is which part. Main thread only.
    /// </summary>
    public interface IAvatarFaceShapes
    {
        /// <summary>Every face shape the model has, each name once.</summary>
        IReadOnlyList<string> Names { get; }

        /// <summary>
        /// 0 to 1 for the shape at <paramref name="index"/> in <see cref="Names"/>, applied by
        /// <see cref="IAvatarModel.Apply"/> after the expressions, so it wins over them.
        /// </summary>
        void SetWeight(int index, float weight);
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

        /// <summary>The model's own face shapes; null when it has none to move.</summary>
        IAvatarFaceShapes FaceShapes { get; }

        AvatarModelInfo Info { get; }

        void SetVisible(bool visible);

        /// <summary>
        /// Applies the rotations, expressions, and gaze set this frame, then moves secondary motion
        /// (hair, clothes). Called once per frame after the values are set (avatar detailed design 8.3).
        /// </summary>
        void Apply();
    }
}
