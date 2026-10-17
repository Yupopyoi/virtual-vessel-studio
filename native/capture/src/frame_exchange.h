#pragma once

#include <cstdint>
#include <mutex>
#include <vector>

namespace vv::capture
{
    // Which of three buffers each side owns, and the frame numbers. The steps are pure functions
    // that return the next state, so the hand-over can be tested without threads.
    struct ExchangeState
    {
        int write = 0;              // Owned by the capture thread.
        int latest = 1;             // The newest finished frame, waiting to be taken.
        int read = 2;               // Owned by the reader until its next take.
        uint64_t numbers[3] = {};   // The frame number in each buffer; 0 for none.
        uint64_t lastNumber = 0;    // The number given to the last published frame.
        uint64_t taken = 0;         // The number of the frame the reader took last.
        uint64_t overwritten = 0;   // Published frames replaced before they were taken.
    };

    // The written buffer becomes the latest, numbered one more than the last. A latest frame that
    // was never taken counts as overwritten.
    ExchangeState Publish(const ExchangeState& state);

    // Gives the reader the latest frame when it is newer than both `after` and the frame it took
    // last; otherwise returns the state unchanged.
    ExchangeState Take(const ExchangeState& state, uint64_t after);

    struct FrameBuffer
    {
        std::vector<uint8_t> pixels;
        int32_t width = 0;
        int32_t height = 0;
    };

    // Hands frames from the capture thread to the reader through three buffers. Only the index
    // swaps are locked; copying into and out of the buffers happens outside the lock, so neither
    // side waits for the other's copy (the prototype had no exclusion and could mix images).
    class FrameExchange
    {
    public:
        // The buffer the capture thread fills next. Only the capture thread touches it.
        FrameBuffer& Writable();

        void Publish();

        // The taken frame and its number, or null when there is nothing newer than `after`.
        // It stays unchanged until the next take.
        const FrameBuffer* Take(uint64_t after, uint64_t& number);

        // A consistent copy of the counters and the latest frame's size.
        void Snapshot(uint64_t& lastNumber, uint64_t& overwritten, int32_t& width, int32_t& height) const;

    private:
        mutable std::mutex _mutex;
        FrameBuffer _buffers[3];
        ExchangeState _state;
    };
}
