// A ring of interleaved stereo float frames between one writer thread and one reader thread, without
// locks: the writer only moves the write count and the reader only the read count.
#pragma once

#include <atomic>
#include <cstddef>
#include <cstdint>
#include <cstring>
#include <vector>

namespace vv::audio
{
    constexpr int Channels = 2;

    class FrameRing
    {
    public:
        // capacityFrames is rounded up to a power of two, so positions wrap with a mask.
        explicit FrameRing(size_t capacityFrames)
        {
            size_t capacity = 1;
            while (capacity < capacityFrames)
            {
                capacity <<= 1;
            }

            _mask = capacity - 1;
            _data.assign(capacity * Channels, 0.0f);
        }

        size_t Capacity() const
        {
            return _mask + 1;
        }

        // Frames waiting to be read. Either thread may ask.
        size_t Available() const
        {
            return static_cast<size_t>(_written.load(std::memory_order_acquire) - _read.load(std::memory_order_acquire));
        }

        // Writer: copies as many frames as fit and returns how many; the rest is dropped, and the caller counts it.
        size_t Write(const float* frames, size_t count)
        {
            uint64_t written = _written.load(std::memory_order_relaxed);
            size_t room = Capacity() - static_cast<size_t>(written - _read.load(std::memory_order_acquire));
            size_t n = count < room ? count : room;
            for (size_t i = 0; i < n;)
            {
                size_t at = static_cast<size_t>((written + i) & _mask);
                size_t run = Capacity() - at < n - i ? Capacity() - at : n - i;
                if (frames != nullptr)
                {
                    std::memcpy(&_data[at * Channels], frames + (i * Channels), run * Channels * sizeof(float));
                }
                else
                {
                    std::memset(&_data[at * Channels], 0, run * Channels * sizeof(float));
                }

                i += run;
            }

            _written.store(written + n, std::memory_order_release);
            return n;
        }

        // Reader: copies up to count frames (all of them with a null target skips them) and returns how many.
        size_t Read(float* out, size_t count)
        {
            uint64_t read = _read.load(std::memory_order_relaxed);
            size_t available = static_cast<size_t>(_written.load(std::memory_order_acquire) - read);
            size_t n = count < available ? count : available;
            for (size_t i = 0; out != nullptr && i < n;)
            {
                size_t at = static_cast<size_t>((read + i) & _mask);
                size_t run = Capacity() - at < n - i ? Capacity() - at : n - i;
                std::memcpy(out + (i * Channels), &_data[at * Channels], run * Channels * sizeof(float));
                i += run;
            }

            _read.store(read + n, std::memory_order_release);
            return n;
        }

    private:
        std::vector<float> _data;
        size_t _mask = 0;
        std::atomic<uint64_t> _written{ 0 };
        std::atomic<uint64_t> _read{ 0 };
    };
}
