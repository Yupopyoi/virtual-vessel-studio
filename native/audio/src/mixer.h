// The mix (audio detailed design 8.1, 8.2): sources' input rings, their gains and sends, the stream bus,
// and the stream tap. It has no threads and no devices: the engine calls MixBlock every 10 ms on its mix
// thread, and the tests call it directly with synthetic input.
#pragma once

#include "dsp.h"
#include "fill_tracker.h"
#include "frame_ring.h"

#include <atomic>
#include <cstdint>
#include <memory>
#include <vector>

namespace vv::audio
{
    constexpr int SampleRate = 48000;
    constexpr int BlockFrames = 480; // 10 ms
    constexpr int MaxSources = 16;

    struct SourceCounters
    {
        int64_t overruns = 0;
        int64_t underruns = 0;
        int64_t driftCorrections = 0;
        float peak = 0.0f; // Linear; the highest since the last TakeCounters.
    };

    class Mixer
    {
    public:
        explicit Mixer(int64_t qpcFrequency);

        // Sources. The engine calls these under its lock; the mix thread holds the same lock while mixing.
        bool Add(int* id);
        void Remove(int id);
        bool Used(int id) const;

        // The writer side of a source's input: the capture thread, or a test.
        void Write(int id, const float* frames, size_t count);

        void SetGain(int id, float gainDb, bool muted);
        void SetSend(int id, int bus, float sendDb);

        // The counts, and the peak since the last call (which is reset).
        SourceCounters TakeCounters(int id);

        // Mixes one block of BlockFrames into the stream bus. blockQpc is the QPC of its first frame; the
        // first block anchors the bus's timeline, and each next block follows it without a gap.
        void MixBlock(int64_t blockQpc);

        // The user's own monitor. Never delayed: the stream delay is for what the avatar's motion has to meet
        // (audio detailed design 8.5), and what the user hears comes at once. Gain applies after the limiter.
        void SetMonitor(float gainDb, bool muted);

        // The reader side of the monitor bus: the output thread.
        size_t MonitorAvailable() const
        {
            return _monitor.Available();
        }

        size_t ReadMonitor(float* out, size_t frames)
        {
            return _monitor.Read(out, frames);
        }

        void DiscardMonitor()
        {
            _monitor.Read(nullptr, _monitor.Available());
        }

        // The monitor bus's peak since the last call, linear, after its gain.
        float TakeMonitorPeak();

        // The stream tap (vv_audio_stream_tap.read); one reader thread.
        int32_t ReadStream(float* out, int32_t frames, int64_t untilQpc, int64_t* firstQpc);

        // The stream bus's peak since the last call, linear.
        float TakeBusPeak();

        // Delays the stream bus by this many QPC ticks for its readers: each frame is given as heard that much
        // later, so a recording puts the voice with the avatar's motion, which tracking delays (audio
        // detailed design 8.5). A change while reading shows as a jump the reader fills or leaves out.
        void SetStreamDelay(int64_t ticks);

        // The QPC of the bus frame at index, from the anchor. Pure.
        static int64_t FrameQpc(int64_t anchorQpc, uint64_t index, int64_t frequency);

    private:
        struct Slot
        {
            bool used = false;
            std::unique_ptr<FrameRing> input;
            std::atomic<float> gain{ 1.0f };
            std::atomic<bool> muted{ false };
            std::atomic<float> send{ 1.0f };
            std::atomic<float> monitorSend{ 0.0f };
            std::atomic<int64_t> overruns{ 0 };
            std::atomic<int64_t> underruns{ 0 };
            std::atomic<int64_t> driftCorrections{ 0 };
            std::atomic<float> peak{ 0.0f };

            // Mix thread only.
            FillTracker fill;
            float appliedGain = 1.0f;
            float appliedOutput = 1.0f;
            float appliedMonitor = 0.0f;
        };

        // Reads one block of a source into the scratch block, following its fill; false when it has nothing to play.
        bool ReadSource(Slot& slot);

        static void RaisePeak(std::atomic<float>& peak, float value);

        int64_t _frequency;
        Slot _slots[MaxSources];
        std::vector<float> _scratch;
        std::vector<float> _block;
        LimiterState _limiter;

        // The monitor bus: unlike the stream bus it is played once, so it is a ring its reader empties.
        std::vector<float> _monitorScratch;
        std::vector<float> _monitorBlock;
        LimiterState _monitorLimiter;
        FrameRing _monitor;
        std::atomic<float> _monitorGain{ 1.0f };
        std::atomic<bool> _monitorMuted{ false };
        float _appliedMonitorGain = 1.0f;
        std::atomic<float> _monitorPeak{ 0.0f };

        // The stream bus: overwritten in a circle, so with no reader it simply keeps the last seconds.
        std::vector<float> _bus;
        size_t _busMask = 0;
        std::atomic<uint64_t> _busWritten{ 0 };
        std::atomic<int64_t> _busAnchor{ 0 };
        std::atomic<bool> _busAnchored{ false };
        std::atomic<float> _busPeak{ 0.0f };
        uint64_t _busRead = 0; // Reader only.
        std::atomic<int64_t> _streamDelay{ 0 };
    };
}
