using System;
using UniGLTF;
using UnityEngine;
using UniVRM10;

namespace VirtualVessel.Avatar.Vrm
{
    /// <summary>
    /// One VRM avatar built by UniVRM. UniVRM's own per-frame update is turned off so that
    /// <see cref="Apply"/> runs it after the pose is set and secondary motion follows the pose
    /// (avatar detailed design 8.3).
    /// </summary>
    internal sealed class VrmAvatarModel : IAvatarModel
    {
        private readonly Vrm10Instance _instance;
        private readonly RuntimeGltfInstance _gltf;
        private readonly VrmAvatarRig _rig;

        public VrmAvatarModel(Vrm10Instance instance, AvatarModelInfo info)
        {
            _instance = instance != null ? instance : throw new ArgumentNullException(nameof(instance));
            _gltf = instance.GetComponent<RuntimeGltfInstance>();
            Info = info ?? throw new ArgumentNullException(nameof(info));
            _instance.UpdateType = Vrm10Instance.UpdateTypes.None;
            _instance.LookAtTargetType = VRM10ObjectLookAt.LookAtTargetTypes.YawPitchValue;
            Vrm10Runtime runtime = _instance.Runtime;
            _rig = new VrmAvatarRig(runtime.ControlRig, _instance.transform);
            Expressions = new VrmAvatarExpressions(runtime, _instance.Vrm != null ? _instance.Vrm.LookAt : null);
        }

        public Transform Root => _instance != null ? _instance.transform : null;

        public IAvatarRig Rig => _rig;

        public IAvatarExpressions Expressions { get; }

        public AvatarModelInfo Info { get; }

        /// <summary>
        /// One of the model's own bones, for tests. UniVRM points the Animator at its control rig, so the
        /// Animator's bones are the rig's, which UniVRM copies onto these before gaze moves the eyes.
        /// </summary>
        internal Transform ModelBone(HumanBodyBones bone)
        {
            return _instance != null ? _instance.Humanoid.GetBoneTransform(bone) : null;
        }

        public void SetVisible(bool visible)
        {
            if (_gltf == null)
            {
                return;
            }

            foreach (Renderer renderer in _gltf.VisibleRenderers)
            {
                renderer.enabled = visible;
            }
        }

        public void Apply()
        {
            if (_instance == null)
            {
                return;
            }

            _rig.Apply();
            _instance.Runtime.Process();
        }

        public void Dispose()
        {
            // Destroying the root lets RuntimeGltfInstance destroy the meshes, textures, and materials it made.
            if (_gltf != null)
            {
                _gltf.Dispose();
            }
            else if (_instance != null)
            {
                UnityObjectDestroyer.DestroyRuntimeOrEditor(_instance.gameObject);
            }
        }
    }
}
