// The mix (audio detailed design 8.1, 8.2): sources' input rings, their gains and sends, the stream bus,
// and the stream tap. It has no threads and no devices: the engine calls MixBlock every 10 ms on its mix
// thread, and the tests call it directly with synthetic input.
#pragma once

#include "dsp.h"
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

    // A source starts playing once this much is waiting, so the device's packets and the mix's blocks
    // can arrive out of step without running dry (audio detailed design 15: about 20 ms).
    constexpr size_t PrimeFrames = 960;

    // How far the average fill may wander from the one it settled at before a sample is dropped or repeated.
    constexpr double DriftTolerance = 96.0; // 2 ms

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

        // The stream tap (vv_audio_stream_tap.read); one reader thread.
        int32_t ReadStream(float* out, int32_t frames, int64_t untilQpc, int64_t* firstQpc);

        // The stream bus's peak since the last call, linear.
        float TakeBusPeak();

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
            std::atomic<int64_t> overruns{ 0 };
            std::atomic<int64_t> underruns{ 0 };
            std::atomic<int64_t> driftCorrections{ 0 };
            std::atomic<float> peak{ 0.0f };

            // Mix thread only.
            bool primed = false;
            int blocksSincePrimed = 0;
            double fillAverage = 0.0;
            double fillTarget = 0.0;
            float appliedGain = 1.0f;
            float appliedOutput = 1.0f;
        };

        // Reads one block of a source into the scratch block, following its fill; false when it has nothing to play.
        bool ReadSource(Slot& slot);

        static void RaisePeak(std::atomic<float>& peak, float value);

        int64_t _frequency;
        Slot _slots[MaxSources];
        std::vector<float> _scratch;
        std::vector<float> _block;
        LimiterState _limiter;

        // The stream bus: overwritten in a circle, so with no reader it simply keeps the last seconds.
        std::vector<float> _bus;
        size_t _busMask = 0;
        std::atomic<uint64_t> _busWritten{ 0 };
        std::atomic<int64_t> _busAnchor{ 0 };
        std::atomic<bool> _busAnchored{ false };
        std::atomic<float> _busPeak{ 0.0f };
        uint64_t _busRead = 0; // Reader only.
    };
}
