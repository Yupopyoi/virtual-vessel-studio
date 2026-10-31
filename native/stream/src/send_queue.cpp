#include "send_queue.h"

#include "rtmp_protocol.h"

namespace vv::stream
{
    namespace
    {
        uint32_t Span(const std::deque<Packet>& packets)
        {
            if (packets.size() < 2)
            {
                return 0;
            }

            // Signed: audio runs a little ahead of the video, so the front can be later than the back. Unsigned,
            // that reads as 49 days of queue and drops video on a connection with nothing waiting.
            int32_t span = static_cast<int32_t>(packets.back().timestamp - packets.front().timestamp);
            return span > 0 ? static_cast<uint32_t>(span) : 0;
        }
    }

    void SendQueue::Push(Packet packet)
    {
        {
            std::lock_guard<std::mutex> lock(_mutex);
            bool video = packet.type == rtmp::Video && !packet.sequenceHeader;
            if (video)
            {
                if (!_dropping && Span(_packets) > _limitMs)
                {
                    _dropping = true;
                }

                if (_dropping && !packet.keyframe)
                {
                    _dropped++;
                    return;
                }

                if (_dropping && packet.keyframe)
                {
                    // Start again from this keyframe: the video waiting before it is stale; audio stays.
                    for (auto it = _packets.begin(); it != _packets.end();)
                    {
                        if (it->type == rtmp::Video && !it->sequenceHeader)
                        {
                            it = _packets.erase(it);
                            _dropped++;
                        }
                        else
                        {
                            ++it;
                        }
                    }

                    _dropping = false;
                }
            }

            _packets.push_back(std::move(packet));
        }

        _ready.notify_one();
    }

    bool SendQueue::Pop(Packet& packet, int waitMs)
    {
        std::unique_lock<std::mutex> lock(_mutex);
        if (_packets.empty())
        {
            _ready.wait_for(lock, std::chrono::milliseconds(waitMs));
        }

        if (_packets.empty())
        {
            return false;
        }

        packet = std::move(_packets.front());
        _packets.pop_front();
        return true;
    }

    void SendQueue::Clear()
    {
        std::lock_guard<std::mutex> lock(_mutex);
        _packets.clear();
        _dropping = false;
    }

    uint32_t SendQueue::DurationMs() const
    {
        std::lock_guard<std::mutex> lock(_mutex);
        return Span(_packets);
    }

    int64_t SendQueue::Dropped() const
    {
        std::lock_guard<std::mutex> lock(_mutex);
        return _dropped;
    }

    void SendQueue::Wake()
    {
        _ready.notify_all();
    }
}
