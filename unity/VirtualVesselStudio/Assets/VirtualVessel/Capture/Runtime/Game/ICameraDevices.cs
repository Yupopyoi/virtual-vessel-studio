using System;
using System.Collections.Generic;
using UnityEngine;

namespace VirtualVessel.Capture.Game
{
    /// <summary>
    /// The boundary to Unity's camera devices, so that tests can run the game source without a capture
    /// board (capture detailed design 4). WebCamTexture never leaves the Capture module.
    /// </summary>
    internal interface ICameraDevices
    {
        /// <summary>The camera devices there are now, by name.</summary>
        IReadOnlyList<CaptureDevice> List();

        /// <summary>Opens and plays <paramref name="deviceName"/>, asking for a size and frame rate it may not honor.</summary>
        ICameraStream Open(string deviceName, int width, int height, int frameRate);
    }

    /// <summary>An open camera device.</summary>
    internal interface ICameraStream : IDisposable
    {
        Texture Texture { get; }

        /// <summary>True on frames where a new image arrived.</summary>
        bool DidUpdateThisFrame { get; }

        bool IsPlaying { get; }

        /// <summary>The actual size; very small (16) until the first image arrives.</summary>
        int Width { get; }

        int Height { get; }
    }

    /// <summary>The real devices through <see cref="WebCamTexture"/>, as the prototype did (capture detailed design 8.1).</summary>
    internal sealed class UnityCameraDevices : ICameraDevices
    {
        public IReadOnlyList<CaptureDevice> List()
        {
            WebCamDevice[] devices = WebCamTexture.devices;
            var list = new List<CaptureDevice>(devices.Length);
            foreach (WebCamDevice device in devices)
            {
                // The name is the only identifier Unity offers; it survives unplugging, unlike the index.
                list.Add(new CaptureDevice(device.name, device.name));
            }

            return list;
        }

        public ICameraStream Open(string deviceName, int width, int height, int frameRate)
        {
            var texture = new WebCamTexture(deviceName, width, height, frameRate) { name = "Camera " + deviceName };
            texture.Play();
            return new Stream(texture);
        }

        private sealed class Stream : ICameraStream
        {
            private WebCamTexture _texture;

            public Stream(WebCamTexture texture)
            {
                _texture = texture;
            }

            public Texture Texture => _texture;

            public bool DidUpdateThisFrame => _texture != null && _texture.didUpdateThisFrame;

            public bool IsPlaying => _texture != null && _texture.isPlaying;

            public int Width => _texture != null ? _texture.width : 0;

            public int Height => _texture != null ? _texture.height : 0;

            public void Dispose()
            {
                if (_texture == null)
                {
                    return;
                }

                _texture.Stop();
                if (UnityEngine.Application.isPlaying)
                {
                    UnityEngine.Object.Destroy(_texture);
                }
                else
                {
                    UnityEngine.Object.DestroyImmediate(_texture);
                }

                _texture = null;
            }
        }
    }
}
