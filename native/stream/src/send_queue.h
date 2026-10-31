// The send queue between encoding and sending (streaming detailed design 8.2): when more than its limit waits,
// video is dropped up to the next keyframe, so a slow connection does not keep adding delay; audio is kept.
#pragma once

#include "bytes.h"

#include <chrono>
#include <condition_variable>
#include <cstdint>
#include <deque>
#include <mutex>

namespace vv::stream
{
    struct Packet
    {
        uint8_t type = 0;       // RTMP message type: audio, video, or data.
        uint32_t timestamp = 0; // Milliseconds from the stream's start.
        bool keyframe = false;
        bool sequenceHeader = false;
        Bytes body;
    };

    class SendQueue
    {
    public:
        explicit SendQueue(uint32_t limitMs)
            : _limitMs(limitMs)
        {
        }

        // Adds a packet; a video packet is dropped (and counted) while the queue is over its limit, until the
        // next keyframe, which clears what video was waiting before it.
        void Push(Packet packet);

        // Takes the oldest packet, waiting up to waitMs; false when none came.
        bool Pop(Packet& packet, int waitMs);

        // Drops everything waiting (a new connection starts again from a keyframe).
        void Clear();

        // How many milliseconds of stream wait, from the oldest to the newest packet.
        uint32_t DurationMs() const;

        int64_t Dropped() const;

        // Wakes a waiting Pop, so the sender can see it is asked to stop.
        void Wake();

    private:
        mutable std::mutex _mutex;
        std::condition_variable _ready;
        std::deque<Packet> _packets;
        uint32_t _limitMs;
        bool _dropping = false;
        int64_t _dropped = 0;
    };
}
