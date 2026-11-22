using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using VirtualVessel.UI.Lighting;

namespace VirtualVessel.Stage
{
    /// <summary>
    /// The stage view's guides for one frame (Stage detailed design 7.4.6): line meshes on objects that are active only
    /// while the stage view camera renders, so no other camera draws them and the stream never shows them. A mesh
    /// drawn with a camera given (Graphics.DrawMesh, RenderMesh) was not drawn by this camera under URP (2026-11-22),
    /// so the objects are switched on and off around its rendering instead. The chosen thing's lines show through
    /// what is in front of them; the others follow depth. Main thread only.
    /// </summary>
    public sealed class StageViewGuides : IDisposable
    {
        public const string ShaderPath = "StageGuide";

        /// <summary>How far from the face a whole-stage light is shown: it has no place of its own.</summary>
        public const float DirectionalRadius = 2.4f;

        /// <summary>The others are faint, so the chosen light stands out.</summary>
        public const float FaintAlpha = 0.35f;

        private readonly Lines _depth;
        private readonly Lines _onTop;
        private readonly Material _depthMaterial;
        private readonly Material _onTopMaterial;
        private readonly GameObject _root;
        private Camera _camera;

        public StageViewGuides()
        {
            Shader shader = Resources.Load<Shader>(ShaderPath);
            if (shader == null)
            {
                throw new InvalidOperationException($"The shader '{ShaderPath}' is missing from Resources.");
            }

            _depthMaterial = new Material(shader) { name = "StageGuides" };
            _depthMaterial.SetFloat("_ZTest", (float)CompareFunction.LessEqual);
            _onTopMaterial = new Material(shader) { name = "StageGuidesOnTop" };
            _onTopMaterial.SetFloat("_ZTest", (float)CompareFunction.Always);
            _root = new GameObject("StageViewGuides");
            _root.SetActive(false);
            _depth = new Lines("StageGuides", _root.transform, _depthMaterial);
            _onTop = new Lines("StageGuidesOnTop", _root.transform, _onTopMaterial);
            RenderPipelineManager.beginCameraRendering += OnBeginCamera;
            RenderPipelineManager.endCameraRendering += OnEndCamera;
        }

        /// <summary>Whether the guides would be drawn by <paramref name="rendering"/>: only by the stage view camera. Pure.</summary>
        public static bool DrawsFor(Camera rendering, Camera view)
        {
            return rendering != null && rendering == view;
        }

        /// <summary>The segments this frame, as pairs of points; for tests.</summary>
        internal int SegmentCount => (_depth.Points.Count + _onTop.Points.Count) / 2;

        public void Clear()
        {
            _depth.Clear();
            _onTop.Clear();
        }

        /// <summary>Adds a segment: on top of everything when <paramref name="onTop"/>, else hidden behind what is in front.</summary>
        public void Add(Vector3 from, Vector3 to, Color color, bool onTop)
        {
            Lines lines = onTop ? _onTop : _depth;
            lines.Points.Add(from);
            lines.Points.Add(to);
            lines.Colors.Add(color);
            lines.Colors.Add(color);
        }

        /// <summary>
        /// A light's guides: a spot's line to the face and its cone (a ring at the face and four lines to it), or a
        /// whole-stage light's five parallel arrows toward the face. Chosen, they are opaque and on top.
        /// </summary>
        public static void AddLight(StageViewGuides guides, StageLight light, bool chosen)
        {
            Color color = StageLightColors.ColorOf(light);
            color.a = chosen ? 1f : FaintAlpha;
            Vector3 target = StageLightPlacement.Target;
            if (light.Kind == StageLightKind.Spot)
            {
                Vector3 from = StageLightPlacement.Position(light);
                guides.Add(from, target, color, chosen);
                Vector3 axis = (target - from).normalized;
                float radius = Mathf.Tan(light.SpotAngle * 0.5f * Mathf.Deg2Rad) * Vector3.Distance(from, target);
                Vector3 u = Vector3.Cross(axis, Mathf.Abs(axis.y) > 0.95f ? Vector3.forward : Vector3.up).normalized;
                Vector3 v = Vector3.Cross(axis, u);
                const int sides = 32;
                Vector3 previous = target + u * radius;
                for (int i = 1; i <= sides; i++)
                {
                    float t = i * Mathf.PI * 2f / sides;
                    Vector3 point = target + (u * Mathf.Cos(t) + v * Mathf.Sin(t)) * radius;
                    guides.Add(previous, point, color, chosen);
                    if (i % (sides / 4) == 0)
                    {
                        guides.Add(from, point, color, chosen);
                    }

                    previous = point;
                }

                return;
            }

            Vector3 direction = -StageLightPlacement.Offset(light.Yaw, light.Pitch);
            Vector3 start = target - direction * DirectionalRadius;
            Vector3 right = Vector3.Cross(direction, Mathf.Abs(direction.y) > 0.95f ? Vector3.forward : Vector3.up).normalized;
            Vector3 up = Vector3.Cross(right, direction);
            foreach (Vector2 shift in new[] { Vector2.zero, new Vector2(0.18f, 0.18f), new Vector2(-0.18f, 0.18f), new Vector2(0.18f, -0.18f), new Vector2(-0.18f, -0.18f) })
            {
                Vector3 tail = start + right * shift.x + up * shift.y;
                Vector3 head = tail + direction * 0.7f;
                guides.Add(tail, head, color, chosen);

                // The arrowhead, so the direction reads without the face in view.
                guides.Add(head, head - direction * 0.12f + right * 0.06f, color, chosen);
                guides.Add(head, head - direction * 0.12f - right * 0.06f, color, chosen);
            }
        }

        /// <summary>Puts this frame's guides in the meshes, to be drawn by <paramref name="camera"/> only.</summary>
        public void Draw(Camera camera)
        {
            _camera = camera;
            _depth.Apply();
            _onTop.Apply();
        }

        public void Dispose()
        {
            RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
            RenderPipelineManager.endCameraRendering -= OnEndCamera;
            _depth.Dispose();
            _onTop.Dispose();
            DestroyObject(_root);
            DestroyObject(_depthMaterial);
            DestroyObject(_onTopMaterial);
        }

        private void OnBeginCamera(ScriptableRenderContext context, Camera camera)
        {
            if (_root != null)
            {
                _root.SetActive(DrawsFor(camera, _camera));
            }
        }

        private void OnEndCamera(ScriptableRenderContext context, Camera camera)
        {
            if (_root != null)
            {
                _root.SetActive(false);
            }
        }

        private static void DestroyObject(UnityEngine.Object target)
        {
            if (target == null)
            {
                return;
            }

            if (UnityEngine.Application.isPlaying)
            {
                UnityEngine.Object.Destroy(target);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(target);
            }
        }

        private sealed class Lines : IDisposable
        {
            private readonly List<int> _indices = new List<int>();
            private readonly Mesh _mesh;

            public Lines(string name, Transform parent, Material material)
            {
                _mesh = new Mesh { name = name };
                _mesh.MarkDynamic();
                var holder = new GameObject(name);
                holder.transform.SetParent(parent, false);
                holder.AddComponent<MeshFilter>().sharedMesh = _mesh;
                var renderer = holder.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }

            public List<Vector3> Points { get; } = new List<Vector3>();

            public List<Color> Colors { get; } = new List<Color>();

            public void Clear()
            {
                Points.Clear();
                Colors.Clear();
            }

            public void Apply()
            {
                _indices.Clear();
                for (int i = 0; i < Points.Count; i++)
                {
                    _indices.Add(i);
                }

                _mesh.Clear();
                _mesh.SetVertices(Points);
                _mesh.SetColors(Colors);
                _mesh.SetIndices(_indices, MeshTopology.Lines, 0);
                _mesh.RecalculateBounds();
            }

            public void Dispose()
            {
                DestroyObject(_mesh);
            }
        }
    }
}
