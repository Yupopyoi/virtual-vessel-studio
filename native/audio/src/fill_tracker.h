// Keeps a ring between two clocks at a steady fill (audio detailed design 8.2): it waits for the ring to fill
// up before playing, starts again when it runs dry, and takes a frame more or less now and then, so neither
// side's clock runs the ring full or empty. The mix uses it per source and the monitor output per device. Pure
// apart from its own state.
#pragma once

#include <cstddef>

namespace vv::audio
{
    // A source starts playing once this much is waiting, so the device's packets and the mix's blocks
    // can arrive out of step without running dry (audio detailed design 15: about 20 ms).
    constexpr size_t PrimeFrames = 960;

    // How far the average fill may wander from the one it settled at before a sample is dropped or repeated.
    constexpr double DriftTolerance = 96.0; // 2 ms

    struct FillStep
    {
        int take = 0;           // Frames to read for this block; 0 for silence.
        bool ranDry = false;    // What is left should be let go, and the ring fill up again.
        bool corrected = false; // A frame more or less than asked for, to follow the other clock.
    };

    class FillTracker
    {
    public:
        // What to do for a block of `wanted` frames with `available` waiting.
        FillStep Next(size_t available, int wanted)
        {
            FillStep step;
            if (!_primed)
            {
                if (available < PrimeFrames)
                {
                    return step;
                }

                _primed = true;
                _blocks = 0;
                _average = static_cast<double>(available);
            }

            if (available < static_cast<size_t>(wanted))
            {
                _primed = false;
                step.ranDry = true;
                return step;
            }

            _average += (static_cast<double>(available) - _average) * Smoothing;
            _blocks++;
            if (_blocks == SettleBlocks)
            {
                _target = _average;
            }

            step.take = wanted;
            if (_blocks > SettleBlocks)
            {
                if (_average > _target + DriftTolerance && available > static_cast<size_t>(wanted))
                {
                    step.take = wanted + 1;
                    step.corrected = true;
                    _average -= 1.0;
                }
                else if (_average < _target - DriftTolerance)
                {
                    step.take = wanted - 1;
                    step.corrected = true;
                    _average += 1.0;
                }
            }

            return step;
        }

    private:
        // The settled fill is measured over the first half second after playing starts.
        static constexpr int SettleBlocks = 50;
        static constexpr double Smoothing = 0.05;

        bool _primed = false;
        int _blocks = 0;
        double _average = 0.0;
        double _target = 0.0;
    };
}
