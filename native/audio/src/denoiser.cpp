#include "denoiser.h"

#include <rnnoise.h>

#include <algorithm>
#include <cmath>
#include <cstring>

namespace vv::audio
{
    namespace
    {
        // RNNoise works at the scale of 16-bit samples.
        constexpr float Scale = 32768.0f;

        constexpr int Decimation = 4;
        constexpr int MinLag = 12000 / 800;
        constexpr int MaxLag = 12000 / 60;
        constexpr int Window = 240; // 20 ms at 12 kHz

        constexpr float VoicedPeriodicity = 0.6f;
        constexpr float VoicedRms = 0.003f; // About -50 dBFS
    }

    float Denoiser::Periodicity(const float* samples, size_t count)
    {
        float low[AnalysisSamples / Decimation];
        size_t n = std::min(count, static_cast<size_t>(AnalysisSamples)) / Decimation;
        const float* from = samples + count - n * Decimation;
        for (size_t i = 0; i < n; i++)
        {
            low[i] = (from[i * 4] + from[i * 4 + 1] + from[i * 4 + 2] + from[i * 4 + 3]) * 0.25f;
        }

        if (n < static_cast<size_t>(Window + MaxLag))
        {
            return 0.0f;
        }

        const float* now = low + n - Window;
        double energy = 0.0;
        for (int i = 0; i < Window; i++)
        {
            energy += static_cast<double>(now[i]) * now[i];
        }

        float best = 0.0f;
        for (int lag = MinLag; lag <= MaxLag; lag++)
        {
            const float* then = now - lag;
            double cross = 0.0;
            double past = 0.0;
            for (int i = 0; i < Window; i++)
            {
                cross += static_cast<double>(now[i]) * then[i];
                past += static_cast<double>(then[i]) * then[i];
            }

            double norm = std::sqrt(energy * past);
            if (norm > 1e-12)
            {
                best = std::max(best, static_cast<float>(cross / norm));
            }
        }

        return best;
    }

    float Denoiser::NextVoiceWeight(float weight, float periodicity, float rms)
    {
        float target = periodicity > VoicedPeriodicity && rms > VoicedRms ? 1.0f : 0.0f;
        float speed = target > weight ? 0.5f : 0.7f;
        return weight + (target - weight) * speed;
    }

    Denoiser::~Denoiser()
    {
        if (_state != nullptr)
        {
            rnnoise_destroy(_state);
        }
    }

    bool Denoiser::Process(float* samples, int count)
    {
        if (_state == nullptr)
        {
            _state = rnnoise_create(nullptr);
            if (_state == nullptr)
            {
                return false;
            }
        }

        constexpr int Kept = DelaySamples + AnalysisSamples;
        float frame[FrameSize];
        for (int at = 0; at + FrameSize <= count; at += FrameSize)
        {
            std::memmove(_original, _original + FrameSize, (Kept - FrameSize) * sizeof(float));
            std::memcpy(_original + Kept - FrameSize, samples + at, FrameSize * sizeof(float));
            for (int i = 0; i < FrameSize; i++)
            {
                frame[i] = samples[at + i] * Scale;
            }

            rnnoise_process_frame(_state, frame, frame);

            // The original that RNNoise's output belongs to, and the 40 ms of it up to there.
            const float* aligned = _original + AnalysisSamples - FrameSize;
            double sum = 0.0;
            for (int i = 0; i < FrameSize; i++)
            {
                sum += static_cast<double>(aligned[i]) * aligned[i];
            }

            float rms = static_cast<float>(std::sqrt(sum / FrameSize));
            _voiceWeight = NextVoiceWeight(_voiceWeight, Periodicity(_original, AnalysisSamples), rms);
            for (int i = 0; i < FrameSize; i++)
            {
                float cleaned = frame[i] / Scale;
                samples[at + i] = cleaned + (aligned[i] - cleaned) * _voiceWeight;
            }
        }

        return true;
    }
}
