// Noise removal with RNNoise for a processing stage (audio detailed design 8.3). It keeps its state between
// calls, so a stream goes through one instance, from one thread.
//
// RNNoise takes a steady sound for noise: a held note fell by 48 dB within two seconds and went silent. So while
// the input is voiced (periodic and not quiet), the original is blended back over what RNNoise made. Clicks and
// hiss are not periodic, so between words they are still removed.
#pragma once

#include <cstddef>

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

        // How periodic the samples are, 0 to 1: the best normalized autocorrelation over the lags of a voice's
        // pitch, 60 to 800 Hz, at a quarter of the 48 kHz rate. count is at least 1920 (40 ms). Pure.
        static float Periodicity(const float* samples, size_t count);

        // How much of the original to blend back after a frame: up quickly when a voice starts, down slowly
        // so a note's end is not cut. Pure.
        static float NextVoiceWeight(float weight, float periodicity, float rms);

        // RNNoise's output lags its input by this much (measured), so the original is delayed to meet it.
        static constexpr int DelaySamples = 960;

    private:
        static constexpr int FrameSize = 480;
        static constexpr int AnalysisSamples = 1920;

        DenoiseState* _state = nullptr;
        float _original[DelaySamples + AnalysisSamples] = {};
        float _voiceWeight = 0.0f;
    };
}
