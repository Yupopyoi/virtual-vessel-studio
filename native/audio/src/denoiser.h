// Noise removal with RNNoise for a processing stage (audio detailed design 8.3). It keeps its state between
// calls, so a stream goes through one instance, from one thread.
#pragma once

struct DenoiseState;

namespace vv::audio
{
    class Denoiser
    {
    public:
        Denoiser() = default;
        ~Denoiser();
        Denoiser(const Denoiser&) = delete;
        Denoiser& operator=(const Denoiser&) = delete;

        // Cleans mono samples of -1 to 1 in place; count is a multiple of 480. False if it cannot start.
        bool Process(float* samples, int count);

    private:
        DenoiseState* _state = nullptr;
    };
}
