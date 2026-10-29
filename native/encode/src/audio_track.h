// The recording's audio track (recording detailed design 8.1): the stream bus's frames placed on the file's
// timeline, which starts at the first video frame. Pure functions, so the timing is tested without devices.
#pragma once

#include <cstddef>
#include <cstdint>

namespace vv::encode
{
    constexpr int32_t AudioRate = 48000;
    constexpr int32_t AudioChannels = 2;

    // QPC ticks to the file's 100 ns units; split so the product stays in range for days.
    inline int64_t TicksToFileTime(int64_t ticks, int64_t frequency)
    {
        return ((ticks / frequency) * 10'000'000) + (((ticks % frequency) * 10'000'000) / frequency);
    }

    // Frames to the file's 100 ns units.
    inline int64_t FramesToFileTime(int64_t frames)
    {
        return ((frames / AudioRate) * 10'000'000) + (((frames % AudioRate) * 10'000'000) / AudioRate);
    }

    // How many frames of a chunk that starts at chunkQpc come before the file starts at startQpc, and are
    // left out (rounded up, so the first kept frame is not before the video).
    inline int64_t FramesBefore(int64_t chunkQpc, int64_t startQpc, int64_t frequency)
    {
        if (chunkQpc >= startQpc)
        {
            return 0;
        }

        int64_t ticks = startQpc - chunkQpc;
        int64_t whole = (ticks / frequency) * AudioRate;
        int64_t rest = (ticks % frequency) * AudioRate;
        return whole + (rest / frequency) + (rest % frequency != 0 ? 1 : 0);
    }

    // AAC in Media Foundation takes 96, 128, 160, or 192 kbit/s; a bitrate is rounded to the nearest of them.
    inline uint32_t AacBytesPerSecond(int32_t bitrate)
    {
        const uint32_t allowed[] = { 12000, 16000, 20000, 24000 };
        uint32_t wanted = bitrate > 0 ? static_cast<uint32_t>(bitrate) / 8 : 20000;
        uint32_t best = allowed[0];
        for (uint32_t value : allowed)
        {
            uint32_t distance = value > wanted ? value - wanted : wanted - value;
            uint32_t bestDistance = best > wanted ? best - wanted : wanted - best;
            best = distance < bestDistance ? value : best;
        }

        return best;
    }

    // Float samples to 16-bit PCM, which the AAC encoder takes; out of range is clipped.
    inline void ToPcm16(const float* in, int16_t* out, size_t samples)
    {
        for (size_t i = 0; i < samples; i++)
        {
            float value = in[i] * 32767.0f;
            value = value > 32767.0f ? 32767.0f : (value < -32768.0f ? -32768.0f : value);
            out[i] = static_cast<int16_t>(value);
        }
    }
}
