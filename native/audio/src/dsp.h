// Small signal functions of the mix: levels, gains, and the bus limiter. Each takes its state as an
// argument, so the mix's behavior is tested with plain inputs and outputs.
#pragma once

#include <cmath>
#include <limits>

namespace vv::audio
{
    constexpr float MinGainDb = -60.0f;
    constexpr float MaxGainDb = 12.0f;

    // -inf (or below -120 dB) is silence.
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
