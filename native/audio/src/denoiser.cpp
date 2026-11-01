#include "denoiser.h"

#include <rnnoise.h>

namespace vv::audio
{
    namespace
    {
        constexpr int FrameSize = 480;

        // RNNoise works at the scale of 16-bit samples.
        constexpr float Scale = 32768.0f;
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

        float frame[FrameSize];
        for (int at = 0; at + FrameSize <= count; at += FrameSize)
        {
            for (int i = 0; i < FrameSize; i++)
            {
                frame[i] = samples[at + i] * Scale;
            }

            rnnoise_process_frame(_state, frame, frame);
            for (int i = 0; i < FrameSize; i++)
            {
                samples[at + i] = frame[i] / Scale;
            }
        }

        return true;
    }
}
