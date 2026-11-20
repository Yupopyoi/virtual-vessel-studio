#include "clip_convert.h"

namespace vv::audio
{
    namespace
    {
        constexpr int Stereo = 2;

        // The 4-point, third-order Hermite curve through x1 and x2 at t in [0, 1). Pure.
        float Hermite(float x0, float x1, float x2, float x3, float t)
        {
            float c1 = 0.5f * (x2 - x0);
            float c2 = x0 - (2.5f * x1) + (2.0f * x2) - (0.5f * x3);
            float c3 = (0.5f * (x3 - x0)) + (1.5f * (x1 - x2));
            return (((((c3 * t) + c2) * t) + c1) * t) + x1;
        }
    }

    void ToStereo(const float* frames, size_t count, int channels, float* stereo)
    {
        for (size_t i = 0; i < count; i++)
        {
            const float* frame = frames + (i * static_cast<size_t>(channels));
            float left = frame[0];
            float right = channels > 1 ? frame[1] : frame[0];
            stereo[i * Stereo] = left;
            stereo[(i * Stereo) + 1] = right;
        }
    }

    StereoResampler::StereoResampler(int from, int to)
        : _from(from), _to(to), _step(static_cast<double>(from) / to), _position(1.0), _history(Stereo, 0.0f)
    {
        // One frame of silence before the first: the curve needs a frame before the one it starts from. The first
        // output frame is the first input frame.
    }

    void StereoResampler::Process(const float* stereo, size_t count, std::vector<float>& out)
    {
        if (_from == _to)
        {
            out.insert(out.end(), stereo, stereo + (count * Stereo));
            return;
        }

        _history.insert(_history.end(), stereo, stereo + (count * Stereo));
        size_t frames = _history.size() / Stereo;

        // Each output frame needs the frame before its position and two after: so it waits while those are missing.
        while (_position + 2.0 < static_cast<double>(frames))
        {
            size_t at = static_cast<size_t>(_position);
            float t = static_cast<float>(_position - static_cast<double>(at));
            for (int c = 0; c < Stereo; c++)
            {
                float x0 = _history[((at - 1) * Stereo) + c];
                float x1 = _history[(at * Stereo) + c];
                float x2 = _history[((at + 1) * Stereo) + c];
                float x3 = _history[((at + 2) * Stereo) + c];
                out.push_back(Hermite(x0, x1, x2, x3, t));
            }

            _position += _step;
        }

        // Keep the frames from the one before the next position; drop the rest, and count the position from there.
        size_t keepFrom = static_cast<size_t>(_position) - 1;
        _history.erase(_history.begin(), _history.begin() + static_cast<std::ptrdiff_t>(keepFrom * Stereo));
        _position -= static_cast<double>(keepFrom);
    }

    std::vector<float> ResampleStereo(const float* stereo, size_t count, int from, int to)
    {
        std::vector<float> out;
        if (count == 0 || from <= 0 || to <= 0)
        {
            return out;
        }

        if (from == to)
        {
            return std::vector<float>(stereo, stereo + (count * Stereo));
        }

        out.reserve(static_cast<size_t>((static_cast<double>(count) * to) / from + 4) * Stereo);
        StereoResampler resampler(from, to);
        resampler.Process(stereo, count, out);

        // Two frames of silence carry the last input frames through the curve.
        float tail[4] = {};
        resampler.Process(tail, 2, out);
        return out;
    }
}
