// Turning a file's frames into the mix's format, 48 kHz float stereo (BGM and SE detailed design 8.1): the channel
// layout first, then the rate. The rate converter keeps its state across calls, so a track read in pieces, and a
// loop that goes back to its start, have no seam.
#pragma once

#include <cstddef>
#include <vector>

namespace vv::audio
{
    // Interleaved frames of any channel count to stereo: mono goes to both sides, stereo stays, and more channels
    // keep their front left and right (the first two). Changes only `stereo`, which holds frames * 2. Pure apart
    // from that.
    void ToStereo(const float* frames, size_t count, int channels, float* stereo);

    // Stereo from one rate to another by 4-point Hermite interpolation: smoother than linear for music, and cheap.
    // A track decoded in pieces goes through one converter, so it continues across the pieces.
    class StereoResampler
    {
    public:
        StereoResampler(int from, int to);

        // Converts `count` stereo frames and appends the result to `out`. The output trails the input by about two
        // frames, which the next call, or the next loop of a track, carries on.
        void Process(const float* stereo, size_t count, std::vector<float>& out);

        int From() const
        {
            return _from;
        }

    private:
        int _from;
        int _to;
        double _step;      // Input frames per output frame.
        double _position;  // Where the next output frame is, in input frames from the first of _history.
        std::vector<float> _history; // The last frames of the input, which the next output still needs.
    };

    // A whole file's stereo frames at one rate brought to another. Pure.
    std::vector<float> ResampleStereo(const float* stereo, size_t count, int from, int to);
}
