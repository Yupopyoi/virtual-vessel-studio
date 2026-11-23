using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using VirtualVessel.UI.Lighting;

namespace VirtualVessel.Stage
{
    /// <summary>Where a light stands and how bright Unity is told to make it (Stage detailed design 7.3). Pure.</summary>
    public static class StageLightPlacement
    {
        /// <summary>
        /// What the lights aim at: the face of an avatar of about 1.5 m on the spawn point, a little above
        /// the camera's height (video detailed design 7.5).
        /// </summary>
        public static Vector3 Target => new Vector3(0f, 1.3f, 0f);

        /// <summary>
        /// The unit vector from <see cref="Target"/> toward the light. The camera is on -Z looking along +Z,
        /// so yaw 0 is on the camera's side and a positive yaw on the screen's right (+X).
        /// </summary>
        public static Vector3 Offset(float yaw, float pitch)
        {
            float y = yaw * Mathf.Deg2Rad;
            float p = pitch * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(y) * Mathf.Cos(p), Mathf.Sin(p), -Mathf.Cos(y) * Mathf.Cos(p));
        }

        public static Vector3 Position(StageLight light)
        {
            return Target + Offset(light.Yaw, light.Pitch) * light.Distance;
        }

        /// <summary>The light's rotation, toward <see cref="Target"/>.</summary>
        public static Quaternion Rotation(StageLight light)
        {
            Vector3 forward = -Offset(light.Yaw, light.Pitch);

            // Straight above or below, "up" cannot be the world's up; the camera's way is used instead.
            Vector3 up = Mathf.Abs(light.Pitch) > 89f ? Vector3.forward : Vector3.up;
            return Quaternion.LookRotation(forward, up);
        }

        /// <summary>
        /// Unity's intensity for the light. A spot dims with the square of its distance; it is raised by
        /// that much so that the same brightness lights the face the same whether it is a spot or not, and
        /// at any distance.
        /// </summary>
        public static float UnityIntensity(StageLight light)
        {
            return light.Kind == StageLightKind.Spot ? light.Intensity * light.Distance * light.Distance : light.Intensity;
        }

        /// <summary>A spot's reach: well past the face, so its fall-off near the end does not show on the avatar.</summary>
        public static float Range(StageLight light)
        {
            return light.Distance * 3f;
        }
    }

    /// <summary>
    /// The lights Unity renders and the ambient light, made to match a <see cref="StageLighting"/>
    /// (Stage detailed design 7.3). Main thread only. The scene's ambient light is put back on dispose.
    /// </summary>
    internal sealed class StageLightRig : IDisposable
    {
        private readonly List<Light> _lights = new List<Light>();
        private readonly SphericalHarmonicsL2 _sceneAmbient;

        public StageLightRig()
        {
            _sceneAmbient = RenderSettings.ambientProbe;
        }

        /// <summary>The lights as made, in order; for tests.</summary>
        public IReadOnlyList<Light> Lights => _lights;

        public void Apply(StageLighting lighting)
        {
            while (_lights.Count > lighting.Lights.Count)
            {
                Destroy(_lights[_lights.Count - 1].gameObject);
                _lights.RemoveAt(_lights.Count - 1);
            }

            while (_lights.Count < lighting.Lights.Count)
            {
                // The first keeps the name of the light the stage always had.
                var light = new GameObject(_lights.Count == 0 ? "StageDefaultLight" : $"StageLight{_lights.Count + 1}").AddComponent<Light>();
                light.shadows = LightShadows.None;
                _lights.Add(light);
            }

            for (int i = 0; i < _lights.Count; i++)
            {
                Set(_lights[i], lighting.Lights[i]);
            }

            RenderSettings.ambientProbe = _sceneAmbient * lighting.Ambient;
        }

        public void Dispose()
        {
            foreach (Light light in _lights)
            {
                Destroy(light.gameObject);
            }

            _lights.Clear();
            RenderSettings.ambientProbe = _sceneAmbient;
        }

        private static void Set(Light target, StageLight light)
        {
            target.type = light.Kind == StageLightKind.Spot ? LightType.Spot : LightType.Directional;
            target.transform.SetPositionAndRotation(StageLightPlacement.Position(light), StageLightPlacement.Rotation(light));
            target.intensity = StageLightPlacement.UnityIntensity(light);
            target.color = StageLightColors.ColorOf(light);
            target.range = StageLightPlacement.Range(light);
            target.spotAngle = light.SpotAngle;

            // A soft edge: a hard ring of light on a face looks like a mistake.
            target.innerSpotAngle = light.SpotAngle * 0.6f;
        }

        private static void Destroy(UnityEngine.Object target)
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
    }
}
