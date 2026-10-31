using System;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Rendering;

namespace VirtualVessel.Video.Recording
{
    /// <summary>The bytes of an NV12 frame. Pure.</summary>
    public static class Nv12Layout
    {
        /// <summary>What the encoder takes: rows of exactly the width.</summary>
        public static int FrameBytes(int width, int height)
        {
            return width * height * 3 / 2;
        }

        /// <summary>Row length in the GPU's buffer, which is written a word at a time.</summary>
        public static int Stride(int width)
        {
            return (width + 3) & ~3;
        }

        public static int BufferBytes(int width, int height)
        {
            return FrameBytes(Stride(width), height);
        }
    }

    /// <summary>
    /// Reads the stream video as NV12: a compute shader converts it into a buffer, and the buffer is read
    /// back (streaming detailed design 8.1). Created and released by the stream that uses it.
    /// </summary>
    public sealed class Nv12FrameReadback : IFrameReadback, IDisposable
    {
        private const string ShaderPath = "StreamNv12";
        private const int ThreadGroupSize = 8;

        private static readonly int s_source = Shader.PropertyToID("_Source");
        private static readonly int s_output = Shader.PropertyToID("_Output");
        private static readonly int s_width = Shader.PropertyToID("_Width");
        private static readonly int s_height = Shader.PropertyToID("_Height");
        private static readonly int s_stride = Shader.PropertyToID("_Stride");

        private readonly ComputeShader _shader;
        private readonly GraphicsBuffer _buffer;
        private readonly int _luma;
        private readonly int _chroma;
        private readonly int _width;
        private readonly int _height;
        private readonly int _stride;

        // Only when the width is not a multiple of 4, for rows of exactly the width.
        private NativeArray<byte> _tight;

        public Nv12FrameReadback(int width, int height)
        {
            _shader = Resources.Load<ComputeShader>(ShaderPath);
            if (_shader == null)
            {
                throw new InvalidOperationException($"The compute shader {ShaderPath} was not found in Resources.");
            }

            _width = width;
            _height = height;
            _stride = Nv12Layout.Stride(width);
            _buffer = new GraphicsBuffer(GraphicsBuffer.Target.Raw, Nv12Layout.BufferBytes(width, height) / sizeof(uint), sizeof(uint));
            _luma = _shader.FindKernel("Luma");
            _chroma = _shader.FindKernel("Chroma");
            if (_stride != width)
            {
                _tight = new NativeArray<byte>(Nv12Layout.FrameBytes(width, height), Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            }
        }

        public void Request(Texture texture, Action<AsyncGPUReadbackRequest> arrived)
        {
            _shader.SetInt(s_width, _width);
            _shader.SetInt(s_height, _height);
            _shader.SetInt(s_stride, _stride);
            _shader.SetTexture(_luma, s_source, texture);
            _shader.SetBuffer(_luma, s_output, _buffer);
            _shader.SetTexture(_chroma, s_source, texture);
            _shader.SetBuffer(_chroma, s_output, _buffer);
            _shader.Dispatch(_luma, Groups(_stride / 4), Groups(_height), 1);
            _shader.Dispatch(_chroma, Groups(_stride / 4), Groups(_height / 2), 1);
            AsyncGPUReadback.Request(_buffer, arrived);
        }

        public unsafe void Deliver(NativeArray<byte> data, long time, IFrameSink sink)
        {
            byte* source = (byte*)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(data);
            if (_stride == _width)
            {
                sink.Push((IntPtr)source, Nv12Layout.FrameBytes(_width, _height), time);
                return;
            }

            byte* target = (byte*)NativeArrayUnsafeUtility.GetUnsafePtr(_tight);
            int rows = _height + (_height / 2);
            for (int row = 0; row < rows; row++)
            {
                UnsafeUtility.MemCpy(target + ((long)row * _width), source + ((long)row * _stride), _width);
            }

            sink.Push((IntPtr)target, _tight.Length, time);
        }

        public void Dispose()
        {
            _buffer.Release();
            if (_tight.IsCreated)
            {
                _tight.Dispose();
            }
        }

        private static int Groups(int items)
        {
            return (items + ThreadGroupSize - 1) / ThreadGroupSize;
        }
    }
}