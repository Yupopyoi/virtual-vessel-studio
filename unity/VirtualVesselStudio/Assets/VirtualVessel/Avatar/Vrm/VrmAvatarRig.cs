using System;
using UnityEngine;
using UniVRM10;

namespace VirtualVessel.Avatar.Vrm
{
    /// <summary>
    /// <see cref="IAvatarRig"/> on UniVRM's control rig: a copy of the skeleton whose bones all have
    /// identity rotation in the reference pose, which UniVRM maps onto the model's own bone axes
    /// (avatar detailed design 5.2).
    /// </summary>
    internal sealed class VrmAvatarRig : IAvatarRig
    {
        private readonly Transform _root;
        private readonly Transform[] _bones = new Transform[AvatarBones.Count];
        private readonly float[] _lengths = new float[AvatarBones.Count];
        private readonly Quaternion[] _rotations = new Quaternion[AvatarBones.Count];
        private readonly bool[] _set = new bool[AvatarBones.Count];

        // Bones the model has, parents first, so setting a parent never moves a child already set.
        private readonly int[] _order;
        private Vector3? _rootPosition;

        public VrmAvatarRig(Vrm10RuntimeControlRig controlRig, Transform root)
        {
            _root = root != null ? root : throw new ArgumentNullException(nameof(root));
            if (controlRig != null)
            {
                for (int i = 0; i < AvatarBones.Count; i++)
                {
                    _bones[i] = controlRig.GetBoneTransform((HumanBodyBones)i);
                }
            }

            int count = 0;
            var depths = new int[AvatarBones.Count];
            for (int i = 0; i < AvatarBones.Count; i++)
            {
                if (_bones[i] == null)
                {
                    continue;
                }

                count++;
                depths[i] = DepthOf(_bones[i], root);
                foreach (AvatarBone child in AvatarBones.ChildCandidates((AvatarBone)i))
                {
                    Transform childBone = _bones[(int)child];
                    if (childBone != null)
                    {
                        _lengths[i] = Vector3.Distance(_bones[i].position, childBone.position);
                        break;
                    }
                }
            }

            _order = new int[count];
            int next = 0;
            for (int i = 0; i < AvatarBones.Count; i++)
            {
                if (_bones[i] != null)
                {
                    _order[next++] = i;
                }
            }

            Array.Sort(_order, (a, b) => depths[a] != depths[b] ? depths[a].CompareTo(depths[b]) : a.CompareTo(b));
        }

        public bool Has(AvatarBone bone)
        {
            return _bones[(int)bone] != null;
        }

        public float BoneLength(AvatarBone bone)
        {
            return _lengths[(int)bone];
        }

        public void SetRotation(AvatarBone bone, Quaternion rotation)
        {
            _rotations[(int)bone] = rotation;
            _set[(int)bone] = true;
        }

        public void SetRootPosition(Vector3 position)
        {
            _rootPosition = position;
        }

        public void ResetPose()
        {
            for (int i = 0; i < _bones.Length; i++)
            {
                _set[i] = false;
                if (_bones[i] != null)
                {
                    _bones[i].localRotation = Quaternion.identity;
                }
            }
        }

        /// <summary>Puts the values set since the last call on the control rig.</summary>
        public void Apply()
        {
            Quaternion root = _root.rotation;
            if (_rootPosition is Vector3 position && _bones[(int)AvatarBone.Hips] != null)
            {
                _bones[(int)AvatarBone.Hips].position = _root.TransformPoint(position);
                _rootPosition = null;
            }

            foreach (int i in _order)
            {
                if (_set[i])
                {
                    _bones[i].rotation = root * _rotations[i];
                    _set[i] = false;
                }
            }
        }

        private static int DepthOf(Transform bone, Transform root)
        {
            int depth = 0;
            for (Transform current = bone; current != null && current != root; current = current.parent)
            {
                depth++;
            }

            return depth;
        }
    }
}
