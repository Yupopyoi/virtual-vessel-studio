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

    // A file time (100 ns units) in frames.
    inline int64_t FileTimeToFrames(int64_t fileTime)
    {
        return ((fileTime / 10'000'000) * AudioRate) + (((fileTime % 10'000'000) * AudioRate) / 10'000'000);
    }

    // Where a chunk that starts at chunkQpc lands on the file's timeline, in frames from its zero at
    // startQpc, rounded to the nearest frame; negative before the file starts.
    inline int64_t FrameAt(int64_t chunkQpc, int64_t startQpc, int64_t frequency)
    {
        int64_t ticks = chunkQpc >= startQpc ? chunkQpc - startQpc : startQpc - chunkQpc;
        int64_t frames = ((ticks / frequency) * AudioRate) + ((((ticks % frequency) * AudioRate) + (frequency / 2)) / frequency);
        return chunkQpc >= startQpc ? frames : -frames;
    }

    // How a chunk joins the audio written so far: silence to fill a gap before it, or frames of it to leave
    // out where it overlaps what was written (or comes before the file). Within the tolerance it simply
    // follows on, so the rounding of times never adds or drops a frame of a continuous bus.
    struct Placement
    {
        int64_t silence = 0;
        int64_t skip = 0;
    };

    constexpr int64_t PlacementTolerance = 48; // 1 ms

    inline Placement Place(int64_t position, int64_t frames, int64_t written)
    {
        Placement placement;
        int64_t delta = position - written;
        if (delta > PlacementTolerance)
        {
            placement.silence = delta;
        }
        else if (delta < -PlacementTolerance)
        {
            placement.skip = -delta < frames ? -delta : frames;
        }

        return placement;
    }

    // The audio may fall at most this far behind the video before silence fills it, so a stalled bus never
    // holds the video back: the file writer waits for the stream that is behind.
    constexpr int64_t MaxAudioLagFrames = AudioRate;

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
