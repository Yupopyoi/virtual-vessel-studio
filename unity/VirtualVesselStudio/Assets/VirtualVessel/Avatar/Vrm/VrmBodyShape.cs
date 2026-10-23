using System.Collections.Generic;
using UnityEngine;
using UniVRM10;

namespace VirtualVessel.Avatar.Vrm
{
    /// <summary>
    /// Measures a VRM's <see cref="AvatarBodyShape"/> right after it loads, while it is still in the
    /// reference pose (avatar detailed design 7.6): the bones from the control rig, and each mesh point
    /// in the reference pose grouped by the humanoid bone that the bone moving it most hangs from.
    /// </summary>
    internal static class VrmBodyShape
    {
        private static readonly AvatarBone[] s_measured =
        {
            AvatarBone.Head,
            AvatarBone.LeftUpperArm,
            AvatarBone.LeftLowerArm,
            AvatarBone.LeftHand,
            AvatarBone.RightUpperArm,
            AvatarBone.RightLowerArm,
            AvatarBone.RightHand,
        };

        public static AvatarBodyShape Measure(Vrm10Instance instance, Vrm10RuntimeControlRig controlRig)
        {
            if (instance == null || controlRig == null)
            {
                return null;
            }

            Transform root = instance.transform;
            var rest = new Vector3[AvatarBones.Count];
            var has = new bool[AvatarBones.Count];
            for (int i = 0; i < AvatarBones.Count; i++)
            {
                Transform bone = controlRig.GetBoneTransform((HumanBodyBones)i);
                if (bone != null)
                {
                    rest[i] = root.InverseTransformPoint(bone.position);
                    has[i] = true;
                }
            }

            // The model's own bones that group the points; the head takes its hair and ears, a hand its fingers.
            var groups = new Dictionary<Transform, AvatarBone>();
            foreach (AvatarBone bone in s_measured)
            {
                Transform transform = instance.Humanoid.GetBoneTransform((HumanBodyBones)bone);
                if (transform != null)
                {
                    groups[transform] = bone;
                }
            }

            var points = new Dictionary<AvatarBone, List<Vector3>>();
            foreach (AvatarBone bone in s_measured)
            {
                points[bone] = new List<Vector3>();
            }

            Matrix4x4 toRoot = root.worldToLocalMatrix;
            foreach (SkinnedMeshRenderer renderer in instance.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                AddSkinned(renderer, groups, toRoot, points);
            }

            foreach (MeshFilter filter in instance.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh != null && Group(filter.transform, groups) is AvatarBone bone)
                {
                    Matrix4x4 toRootSpace = toRoot * filter.transform.localToWorldMatrix;
                    foreach (Vector3 vertex in filter.sharedMesh.vertices)
                    {
                        points[bone].Add(toRootSpace.MultiplyPoint3x4(vertex));
                    }
                }
            }

            var found = new Dictionary<AvatarBone, IReadOnlyList<Vector3>>();
            foreach (KeyValuePair<AvatarBone, List<Vector3>> pair in points)
            {
                found[pair.Key] = pair.Value;
            }

            return AvatarBodyShape.Measure(rest, has, found);
        }

        private static void AddSkinned(SkinnedMeshRenderer renderer, Dictionary<Transform, AvatarBone> groups, Matrix4x4 toRoot, Dictionary<AvatarBone, List<Vector3>> points)
        {
            Mesh mesh = renderer.sharedMesh;
            Transform[] bones = renderer.bones;
            if (mesh == null || bones == null || bones.Length == 0)
            {
                return;
            }

            // Per bone, once: the group it belongs to and the matrix from the mesh's bind space to the root's.
            Matrix4x4[] bindposes = mesh.bindposes;
            var boneGroups = new AvatarBone?[bones.Length];
            var toRootSpace = new Matrix4x4[bones.Length];
            bool any = false;
            for (int i = 0; i < bones.Length && i < bindposes.Length; i++)
            {
                boneGroups[i] = bones[i] != null ? Group(bones[i], groups) : null;
                if (boneGroups[i] != null)
                {
                    toRootSpace[i] = toRoot * bones[i].localToWorldMatrix * bindposes[i];
                    any = true;
                }
            }

            if (!any)
            {
                return;
            }

            Vector3[] vertices = mesh.vertices;
            BoneWeight[] weights = mesh.boneWeights;
            int count = Mathf.Min(vertices.Length, weights.Length);
            for (int v = 0; v < count; v++)
            {
                int strongest = Strongest(weights[v]);
                if (strongest < boneGroups.Length && boneGroups[strongest] is AvatarBone bone)
                {
                    points[bone].Add(toRootSpace[strongest].MultiplyPoint3x4(vertices[v]));
                }
            }
        }

        private static int Strongest(BoneWeight weight)
        {
            int index = weight.boneIndex0;
            float most = weight.weight0;
            if (weight.weight1 > most)
            {
                index = weight.boneIndex1;
                most = weight.weight1;
            }

            if (weight.weight2 > most)
            {
                index = weight.boneIndex2;
                most = weight.weight2;
            }

            return weight.weight3 > most ? weight.boneIndex3 : index;
        }

        /// <summary>The measured bone <paramref name="transform"/> is or hangs from; null for the rest of the body.</summary>
        private static AvatarBone? Group(Transform transform, Dictionary<Transform, AvatarBone> groups)
        {
            for (Transform current = transform; current != null; current = current.parent)
            {
                if (groups.TryGetValue(current, out AvatarBone bone))
                {
                    return bone;
                }
            }

            return null;
        }
    }
}
