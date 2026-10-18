using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace VirtualVessel.Tracking.MediaPipe
{
    /// <summary>
    /// Reads camera images into <see cref="ImageBuffer"/>s for inference (tracking detailed design 9.1),
    /// as the prototype did with AsyncGPUReadback: the GPU copies the image upright, and the CPU gets it
    /// a frame or two later without the main thread waiting. One read at a time. Main thread only.
    /// </summary>
    internal sealed class CameraFrameReader : IDisposable
    {
        /// <summary>Wider images are scaled down: the models take far smaller inputs, and readback costs grow with size.</summary>
        public const int MaxWidth = 1280;

        // Unity textures start at the bottom row and MediaPipe images at the top one.
        private static readonly Vector2 s_flipScale = new Vector2(1f, -1f);
        private static readonly Vector2 s_flipOffset = new Vector2(0f, 1f);

        private readonly Action<AsyncGPUReadbackRequest> _onDone;
        private readonly Action<ImageBuffer, int, int, double, double> _completed;
        private RenderTexture _target;
        private ImageBuffer _buffer;
        private double _imageTime;
        private float _requestedAt;
        private bool _disposed;

        /// <param name="completed">Called on the main thread with the buffer, its size, the image time, and the readback seconds.</param>
        public CameraFrameReader(Action<ImageBuffer, int, int, double, double> completed)
        {
            _completed = completed ?? throw new ArgumentNullException(nameof(completed));
            _onDone = OnDone;
        }

        public bool Busy => _buffer != null;

        /// <summary>The size images are read at: the camera's, scaled to at most <see cref="MaxWidth"/>, even. Pure.</summary>
        public static (int Width, int Height) Size(int width, int height)
        {
            if (width <= MaxWidth)
            {
                return (width & ~1, height & ~1);
            }

            int scaled = (int)Math.Round(height * (double)MaxWidth / width);
            return (MaxWidth, scaled & ~1);
        }

        /// <summary>Starts reading <paramref name="source"/> into <paramref name="buffer"/>, which must hold Width × Height × 4 bytes.</summary>
        public void Read(Texture source, ImageBuffer buffer, int width, int height, double imageTime)
        {
            if (Busy)
            {
                throw new InvalidOperationException("A read is already pending.");
            }

            if (_target == null || _target.width != width || _target.height != height)
            {
                ReleaseTarget();

                // sRGB so the bytes are the camera's colors, as the models expect.
                _target = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { name = "Tracking camera image" };
                _target.Create();
            }

            Graphics.Blit(source, _target, s_flipScale, s_flipOffset);
            _buffer = buffer;
            _buffer.Reading = true;
            _imageTime = imageTime;
            _requestedAt = Time.realtimeSinceStartup;
            AsyncGPUReadback.RequestIntoNativeArray(ref buffer.Data, _target, 0, TextureFormat.RGBA32, _onDone);
        }

        public void Dispose()
        {
            _disposed = true;
            ReleaseTarget();
        }

        private void OnDone(AsyncGPUReadbackRequest request)
        {
            ImageBuffer buffer = _buffer;
            _buffer = null;
            if (buffer == null)
            {
                return;
            }

            buffer.Reading = false;
            if (_disposed || request.hasError)
            {
                return;
            }

            _completed(buffer, request.width, request.height, _imageTime, Time.realtimeSinceStartup - _requestedAt);
        }

        private void ReleaseTarget()
        {
            if (_target == null)
            {
                return;
            }

            _target.Release();
            if (UnityEngine.Application.isPlaying)
            {
                UnityEngine.Object.Destroy(_target);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(_target);
            }

            _target = null;
        }
    }
}
