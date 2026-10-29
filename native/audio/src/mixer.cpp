#include "mixer.h"

#include <algorithm>
#include <cstring>

namespace vv::audio
{
    namespace
    {
        // About 2.7 s of the stream bus, so a reader that starts late or pauses still finds what it needs.
        constexpr size_t BusFrames = 1 << 17;

        // About 1.4 s per source: room for a device's burst after a stall of the mix.
        constexpr size_t InputFrames = 1 << 16;

        // The settled fill is measured over the first half second after a source starts playing.
        constexpr int SettleBlocks = 50;
        constexpr double FillSmoothing = 0.05;
    }

    Mixer::Mixer(int64_t qpcFrequency)
        : _frequency(qpcFrequency > 0 ? qpcFrequency : 1),
          _scratch(static_cast<size_t>(BlockFrames + 1) * Channels),
          _block(static_cast<size_t>(BlockFrames) * Channels),
          _bus(BusFrames * Channels),
          _busMask(BusFrames - 1)
    {
    }

    bool Mixer::Add(int* id)
    {
        for (int i = 0; i < MaxSources; i++)
        {
            Slot& slot = _slots[i];
            if (!slot.used)
            {
                slot.input = std::make_unique<FrameRing>(InputFrames);
                slot.gain = 1.0f;
                slot.muted = false;
                slot.send = 1.0f;
                slot.overruns = 0;
                slot.underruns = 0;
                slot.driftCorrections = 0;
                slot.peak = 0.0f;
                slot.primed = false;
                slot.blocksSincePrimed = 0;
                slot.fillAverage = 0.0;
                slot.fillTarget = 0.0;
                slot.appliedGain = 1.0f;
                slot.appliedOutput = 1.0f;
                slot.used = true;
                *id = i;
                return true;
            }
        }

        return false;
    }

    void Mixer::Remove(int id)
    {
        if (Used(id))
        {
            _slots[id].used = false;
        }
    }

    bool Mixer::Used(int id) const
    {
        return id >= 0 && id < MaxSources && _slots[id].used;
    }

    void Mixer::Write(int id, const float* frames, size_t count)
    {
        if (!Used(id))
        {
            return;
        }

        Slot& slot = _slots[id];
        size_t written = slot.input->Write(frames, count);
        if (written < count)
        {
            slot.overruns += static_cast<int64_t>(count - written);
        }
    }

    void Mixer::SetGain(int id, float gainDb, bool muted)
    {
        if (Used(id))
        {
            _slots[id].gain = DbToLinear(ClampGainDb(gainDb));
            _slots[id].muted = muted;
        }
    }

    void Mixer::SetSend(int id, int bus, float sendDb)
    {
        if (Used(id) && bus == 0)
        {
            _slots[id].send = DbToLinear(sendDb > MaxGainDb ? MaxGainDb : sendDb);
        }
    }

    SourceCounters Mixer::TakeCounters(int id)
    {
        SourceCounters counters;
        if (!Used(id))
        {
            return counters;
        }

        Slot& slot = _slots[id];
        counters.overruns = slot.overruns.load();
        counters.underruns = slot.underruns.load();
        counters.driftCorrections = slot.driftCorrections.load();
        counters.peak = slot.peak.exchange(0.0f);
        return counters;
    }

    bool Mixer::ReadSource(Slot& slot)
    {
        size_t available = slot.input->Available();
        if (!slot.primed)
        {
            if (available < PrimeFrames)
            {
                return false;
            }

            slot.primed = true;
            slot.blocksSincePrimed = 0;
            slot.fillAverage = static_cast<double>(available);
        }

        if (available < static_cast<size_t>(BlockFrames))
        {
            // Ran dry: what is left is let go, and the source waits to fill up again, so it does not stutter.
            slot.underruns++;
            slot.input->Read(nullptr, available);
            slot.primed = false;
            return false;
        }

        slot.fillAverage += (static_cast<double>(available) - slot.fillAverage) * FillSmoothing;
        slot.blocksSincePrimed++;
        if (slot.blocksSincePrimed == SettleBlocks)
        {
            slot.fillTarget = slot.fillAverage;
        }

        // The device's clock runs a little fast or slow against the mix's; one sample now and then keeps
        // the fill where it settled (audio detailed design 8.2).
        int take = BlockFrames;
        if (slot.blocksSincePrimed > SettleBlocks)
        {
            if (slot.fillAverage > slot.fillTarget + DriftTolerance && available > static_cast<size_t>(BlockFrames))
            {
                take = BlockFrames + 1;
                slot.fillAverage -= 1.0;
                slot.driftCorrections++;
            }
            else if (slot.fillAverage < slot.fillTarget - DriftTolerance)
            {
                take = BlockFrames - 1;
                slot.fillAverage += 1.0;
                slot.driftCorrections++;
            }
        }

        slot.input->Read(_scratch.data(), static_cast<size_t>(take));
        if (take == BlockFrames - 1)
        {
            // Repeat the last frame to fill the block.
            std::memcpy(&_scratch[static_cast<size_t>(BlockFrames - 1) * Channels], &_scratch[static_cast<size_t>(BlockFrames - 2) * Channels], Channels * sizeof(float));
        }

        // With one frame too many, the last one is simply not used.
        return true;
    }

    void Mixer::MixBlock(int64_t blockQpc)
    {
        std::fill(_block.begin(), _block.end(), 0.0f);
        for (Slot& slot : _slots)
        {
            if (!slot.used || !ReadSource(slot))
            {
                continue;
            }

            // The meter shows the level after the gain and before mute, so a muted microphone still shows it hears.
            float gain = slot.gain.load();
            float output = slot.muted.load() ? 0.0f : slot.send.load();
            float peak = ApplyRamp(_scratch.data(), BlockFrames, Channels, slot.appliedGain, gain);
            ApplyRamp(_scratch.data(), BlockFrames, Channels, slot.appliedOutput, output);
            slot.appliedGain = gain;
            slot.appliedOutput = output;
            RaisePeak(slot.peak, peak);
            for (size_t i = 0; i < _block.size(); i++)
            {
                _block[i] += _scratch[i];
            }
        }

        _limiter = Limit(_block.data(), BlockFrames, Channels, _limiter);
        RaisePeak(_busPeak, Peak(_block.data(), BlockFrames, Channels));

        if (!_busAnchored.load(std::memory_order_acquire))
        {
            _busAnchor.store(blockQpc, std::memory_order_relaxed);
            _busAnchored.store(true, std::memory_order_release);
        }

        uint64_t written = _busWritten.load(std::memory_order_relaxed);
        for (int i = 0; i < BlockFrames; i++)
        {
            size_t at = static_cast<size_t>((written + static_cast<uint64_t>(i)) & _busMask);
            _bus[at * Channels] = _block[static_cast<size_t>(i) * Channels];
            _bus[(at * Channels) + 1] = _block[(static_cast<size_t>(i) * Channels) + 1];
        }

        _busWritten.store(written + BlockFrames, std::memory_order_release);
    }

    int64_t Mixer::FrameQpc(int64_t anchorQpc, uint64_t index, int64_t frequency)
    {
        // Split so the product stays in range for days of audio.
        int64_t seconds = static_cast<int64_t>(index / SampleRate);
        int64_t rest = static_cast<int64_t>(index % SampleRate);
        return anchorQpc + (seconds * frequency) + ((rest * frequency) / SampleRate);
    }

    int32_t Mixer::ReadStream(float* out, int32_t frames, int64_t untilQpc, int64_t* firstQpc)
    {
        if (out == nullptr || frames <= 0 || !_busAnchored.load(std::memory_order_acquire))
        {
            return 0;
        }

        int64_t anchor = _busAnchor.load(std::memory_order_relaxed);
        uint64_t written = _busWritten.load(std::memory_order_acquire);

        // Frames older than the ring holds were overwritten; keep a block of margin from the writer.
        uint64_t oldest = written > BusFrames - BlockFrames ? written - (BusFrames - BlockFrames) : 0;
        _busRead = _busRead < oldest ? oldest : _busRead;

        // Only frames that began by untilQpc: (until - anchor) / frequency seconds after the anchor.
        uint64_t byTime = 0;
        if (untilQpc >= anchor)
        {
            // Unsigned, so "until any time" (INT64_MAX) cannot overflow.
            uint64_t ticks = static_cast<uint64_t>(untilQpc - anchor);
            uint64_t frequency = static_cast<uint64_t>(_frequency);
            byTime = ((ticks / frequency) * SampleRate) + (((ticks % frequency) * SampleRate) / frequency) + 1;
        }

        uint64_t end = std::min({ written, byTime, _busRead + static_cast<uint64_t>(frames) });
        if (end <= _busRead)
        {
            return 0;
        }

        if (firstQpc != nullptr)
        {
            *firstQpc = FrameQpc(anchor, _busRead, _frequency);
        }

        int32_t count = static_cast<int32_t>(end - _busRead);
        for (int32_t i = 0; i < count; i++)
        {
            size_t at = static_cast<size_t>((_busRead + static_cast<uint64_t>(i)) & _busMask);
            out[static_cast<size_t>(i) * Channels] = _bus[at * Channels];
            out[(static_cast<size_t>(i) * Channels) + 1] = _bus[(at * Channels) + 1];
        }

        _busRead = end;
        return count;
    }

    float Mixer::TakeBusPeak()
    {
        return _busPeak.exchange(0.0f);
    }

    void Mixer::RaisePeak(std::atomic<float>& peak, float value)
    {
        float current = peak.load(std::memory_order_relaxed);
        while (value > current && !peak.compare_exchange_weak(current, value, std::memory_order_relaxed))
        {
        }
    }
}
