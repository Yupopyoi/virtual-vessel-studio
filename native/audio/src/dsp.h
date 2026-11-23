// Small signal functions of the mix: levels, gains, and the bus limiter. Each takes its state as an
// argument, so the mix's behavior is tested with plain inputs and outputs.
#pragma once

#include <cmath>
#include <cstddef>
#include <limits>
#include <vector>

namespace vv::audio
{
    constexpr float MinGainDb = -60.0f;
    constexpr float MaxGainDb = 12.0f;

    // -inf (or below -120 dB) is silence.
    // Mono samples at another rate brought to `to` by linear interpolation: enough for a preview the user
    // listens to, not for the stream. Pure.
    inline std::vector<float> ResampleLinear(const float* samples, size_t count, int from, int to)
    {
        if (count == 0 || from <= 0 || to <= 0)
        {
            return {};
        }

        if (from == to)
        {
            return std::vector<float>(samples, samples + count);
        }

        size_t frames = static_cast<size_t>((static_cast<double>(count) * to) / from);
        std::vector<float> out(frames);
        for (size_t i = 0; i < frames; i++)
        {
            double at = (static_cast<double>(i) * from) / to;
            size_t left = static_cast<size_t>(at);
            size_t right = left + 1 < count ? left + 1 : left;
            float t = static_cast<float>(at - static_cast<double>(left));
            out[i] = samples[left] + ((samples[right] - samples[left]) * t);
        }

        return out;
    }

    inline float DbToLinear(float db)
    {
        return db <= -120.0f ? 0.0f : std::pow(10.0f, db / 20.0f);
    }

    inline float LinearToDb(float linear)
    {
        return linear <= 1e-6f ? -std::numeric_limits<float>::infinity() : 20.0f * std::log10(linear);
    }

    inline float ClampGainDb(float db)
    {
        return std::isnan(db) ? 0.0f : (db < MinGainDb ? MinGainDb : (db > MaxGainDb ? MaxGainDb : db));
    }

    // Multiplies a block by a gain moving linearly from `from` to `to`, so a change is not heard as a click,
    // and returns the highest absolute sample after the gain. Works in place.
    inline float ApplyRamp(float* frames, int count, int channels, float from, float to)
    {
        float peak = 0.0f;
        float step = count > 0 ? (to - from) / static_cast<float>(count) : 0.0f;
        for (int i = 0; i < count; i++)
        {
            float gain = from + (step * static_cast<float>(i + 1));
            for (int c = 0; c < channels; c++)
            {
                float value = frames[(i * channels) + c] * gain;
                frames[(i * channels) + c] = value;
                float magnitude = std::fabs(value);
                peak = magnitude > peak ? magnitude : peak;
            }
        }

        return peak;
    }

    inline float Peak(const float* frames, int count, int channels)
    {
        float peak = 0.0f;
        for (int i = 0; i < count * channels; i++)
        {
            float magnitude = std::fabs(frames[i]);
            peak = magnitude > peak ? magnitude : peak;
        }

        return peak;
    }

    // Ducking (BGM and SE detailed design 8.4): one source lowered while another, the voice, is heard. Worked out
    // once per block from the voice's level in the block before.
    struct DuckState
    {
        float gain = 1.0f; // Linear, applied to the lowered source.
        int hold = 0;      // Blocks left before it comes back.
    };

    // About 0.4 s kept down after the voice falls below the threshold, so it does not move between words.
    constexpr int DuckHoldBlocks = 40;

    // Per 10 ms block: down with a time constant of about 30 ms, back with one of about 0.5 s.
    constexpr float DuckAttack = 0.283468689f;  // 1 - exp(-10 / 30)
    constexpr float DuckRelease = 0.019801327f; // 1 - exp(-10 / 500)

    // The next block's state from the voice's peak (linear), the threshold and depth (linear gains). Pure.
    inline DuckState Duck(DuckState state, float voicePeak, float threshold, float depth, bool enabled)
    {
        float target = 1.0f;
        if (!enabled)
        {
            state.hold = 0;
        }
        else if (voicePeak >= threshold && voicePeak > 0.0f)
        {
            state.hold = DuckHoldBlocks;
            target = depth;
        }
        else if (state.hold > 0)
        {
            state.hold--;
            target = depth;
        }

        float rate = target < state.gain ? DuckAttack : DuckRelease;
        state.gain += (target - state.gain) * rate;
        return state;
    }

    // A peak limiter without lookahead: the gain drops at once to keep every sample under the ceiling and
    // comes back over about 50 ms, so a loud moment is held down without pumping the rest.
    struct LimiterState
    {
        float gain = 1.0f;
    };

    constexpr float LimiterCeiling = 0.891250938f; // -1 dBFS

    // Release per sample at 48 kHz for a time constant of about 50 ms.
    constexpr float LimiterRelease = 0.000416580f; // 1 - exp(-1 / (0.05 * 48000))

    inline LimiterState Limit(float* frames, int count, int channels, LimiterState state)
    {
        for (int i = 0; i < count; i++)
        {
            float loudest = 0.0f;
            for (int c = 0; c < channels; c++)
            {
                float magnitude = std::fabs(frames[(i * channels) + c]);
                loudest = magnitude > loudest ? magnitude : loudest;
            }

            state.gain += (1.0f - state.gain) * LimiterRelease;
            if (loudest * state.gain > LimiterCeiling)
            {
                state.gain = LimiterCeiling / loudest;
            }

            for (int c = 0; c < channels; c++)
            {
                frames[(i * channels) + c] *= state.gain;
            }
        }

        return state;
    }
}
