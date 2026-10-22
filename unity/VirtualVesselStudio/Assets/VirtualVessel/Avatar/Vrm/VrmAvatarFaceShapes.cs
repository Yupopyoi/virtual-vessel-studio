using System;
using System.Collections.Generic;
using UnityEngine;

namespace VirtualVessel.Avatar.Vrm
{
    /// <summary>
    /// <see cref="IAvatarFaceShapes"/> on the model's blend shapes (avatar detailed design 7.4). A
    /// name may be on several meshes; each gets the weight. Weights are written after UniVRM's own
    /// processing, which sets the shapes its expressions use, so the values set here win.
    /// </summary>
    internal sealed class VrmAvatarFaceShapes : IAvatarFaceShapes
    {
        // Unity's blend shape weights run to 100.
        private const float UnityFull = 100f;

        private readonly List<string> _names = new List<string>();
        private readonly List<(SkinnedMeshRenderer Renderer, int Index)[]> _targets = new List<(SkinnedMeshRenderer Renderer, int Index)[]>();
        private readonly float[] _weights;
        private readonly bool[] _set;

        public VrmAvatarFaceShapes(IEnumerable<SkinnedMeshRenderer> renderers)
        {
            var byName = new Dictionary<string, List<(SkinnedMeshRenderer, int)>>(StringComparer.Ordinal);
            foreach (SkinnedMeshRenderer renderer in renderers ?? Array.Empty<SkinnedMeshRenderer>())
            {
                Mesh mesh = renderer != null ? renderer.sharedMesh : null;
                for (int i = 0; mesh != null && i < mesh.blendShapeCount; i++)
                {
                    string name = mesh.GetBlendShapeName(i);
                    if (!byName.TryGetValue(name, out List<(SkinnedMeshRenderer, int)> targets))
                    {
                        targets = new List<(SkinnedMeshRenderer, int)>();
                        byName.Add(name, targets);
                        _names.Add(name);
                    }

                    targets.Add((renderer, i));
                }
            }

            foreach (string name in _names)
            {
                _targets.Add(byName[name].ToArray());
            }

            _weights = new float[_names.Count];
            _set = new bool[_names.Count];
        }

        public IReadOnlyList<string> Names => _names;

        public void SetWeight(int index, float weight)
        {
            if (index < 0 || index >= _weights.Length)
            {
                return;
            }

            _weights[index] = Mathf.Clamp01(weight);
            _set[index] = true;
        }

        /// <summary>Writes the weights set since the last call onto the meshes.</summary>
        public void Apply()
        {
            for (int i = 0; i < _set.Length; i++)
            {
                if (!_set[i])
                {
                    continue;
                }

                _set[i] = false;
                foreach ((SkinnedMeshRenderer renderer, int index) in _targets[i])
                {
                    if (renderer != null)
                    {
                        renderer.SetBlendShapeWeight(index, _weights[i] * UnityFull);
                    }
                }
            }
        }
    }
}
