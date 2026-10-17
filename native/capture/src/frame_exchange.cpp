#include "frame_exchange.h"

#include <algorithm>
#include <utility>

namespace vv::capture
{
    ExchangeState Publish(const ExchangeState& state)
    {
        ExchangeState next = state;
        if (next.numbers[next.latest] > next.taken)
        {
            next.overwritten++;
        }

        next.lastNumber++;
        next.numbers[next.write] = next.lastNumber;
        std::swap(next.write, next.latest);
        return next;
    }

    ExchangeState Take(const ExchangeState& state, uint64_t after)
    {
        // After a take, the latest slot holds the reader's previous frame; never hand it back.
        if (state.numbers[state.latest] <= std::max(after, state.taken))
        {
            return state;
        }

        ExchangeState next = state;
        std::swap(next.read, next.latest);
        next.taken = next.numbers[next.read];
        return next;
    }

    FrameBuffer& FrameExchange::Writable()
    {
        std::lock_guard<std::mutex> lock(_mutex);
        return _buffers[_state.write];
    }

    void FrameExchange::Publish()
    {
        std::lock_guard<std::mutex> lock(_mutex);
        _state = vv::capture::Publish(_state);
    }

    const FrameBuffer* FrameExchange::Take(uint64_t after, uint64_t& number)
    {
        std::lock_guard<std::mutex> lock(_mutex);
        ExchangeState next = vv::capture::Take(_state, after);
        if (next.read == _state.read)
        {
            return nullptr;
        }

        _state = next;
        number = _state.taken;
        return &_buffers[_state.read];
    }

    void FrameExchange::Snapshot(uint64_t& lastNumber, uint64_t& overwritten, int32_t& width, int32_t& height) const
    {
        std::lock_guard<std::mutex> lock(_mutex);
        lastNumber = _state.lastNumber;
        overwritten = _state.overwritten;
        const FrameBuffer& latest = _buffers[_state.numbers[_state.latest] >= _state.numbers[_state.read] ? _state.latest : _state.read];
        width = latest.width;
        height = latest.height;
    }
}
