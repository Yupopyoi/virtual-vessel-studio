// The stream's sending thread (streaming detailed design 8.2, 9): connecting, publishing, sending, reconnecting.

#include "stream_session.h"

#include "reconnect.h"

using namespace vv::stream;
using namespace vv::stream::session;

void vv_stream::Send()
{
    bool everLive = false;
    int attempt = 0;
    int64_t lostAt = 0;
    while (!stopSending.load())
    {
        RtmpClient client(CreateTransport(url.secure));
        std::string error;
        PublishFailure failure = client.Publish(url, key, video.codec == VideoCodec::Av1, ConnectTimeoutMs, error);
        if (failure != PublishFailure::None)
        {
            // Before the first success the stream fails at once, so the user can fix the URL or the key.
            if (!everLive || failure == PublishFailure::Rejected || NowMs() - lostAt > GiveUpAfterMs)
            {
                int32_t problem = failure == PublishFailure::Rejected ? VV_STREAM_PROBLEM_REJECTED
                    : everLive                                       ? VV_STREAM_PROBLEM_CONNECTION_LOST
                                                                     : VV_STREAM_PROBLEM_UNREACHABLE;
                SetState(VV_STREAM_STATE_FAILED, problem);
                return;
            }

            WaitBeforeRetry(attempt++);
            continue;
        }

        everLive = true;
        attempt = 0;
        SetState(VV_STREAM_STATE_LIVE, VV_STREAM_PROBLEM_NONE);
        bool lost = !Publish(client, error);
        publishing = false;
        client.Close(key);
        if (!lost)
        {
            break;
        }

        lostAt = NowMs();
        {
            std::lock_guard<std::mutex> lock(mutex);
            stats.reconnects++;
        }

        SetState(VV_STREAM_STATE_RECONNECTING, VV_STREAM_PROBLEM_CONNECTION_LOST);
        WaitBeforeRetry(attempt++);
    }

    if (State() != VV_STREAM_STATE_FAILED)
    {
        SetState(VV_STREAM_STATE_CLOSED, VV_STREAM_PROBLEM_NONE);
    }
}

void vv_stream::WaitBeforeRetry(int attempt)
{
    int64_t until = NowMs() + ReconnectDelayMs(attempt);
    while (!stopSending.load() && NowMs() < until)
    {
        std::this_thread::sleep_for(std::chrono::milliseconds(50));
    }

    // What waited meanwhile is stale; the next connection starts from a new keyframe.
    sendQueue.Clear();
}

bool vv_stream::Publish(RtmpClient& client, std::string& error)
{
    flv::StreamInfo info;
    info.width = video.width;
    info.height = video.height;
    info.frameRate = video.fps;
    info.videoKbps = video.bitrate / 1000;
    info.audioKbps = audioBitrate / 1000;
    info.av1 = video.codec == VideoCodec::Av1;
    rtmp::Message metadata;
    metadata.type = rtmp::DataAmf0;
    metadata.payload = flv::MetaData(info);
    if (!client.Send(rtmp::CommandChunks, metadata, error))
    {
        return false;
    }

    // Each connection starts at a keyframe, with the sequence headers before it, and its own zero time.
    sendQueue.Clear();
    requestKeyframe = true;
    publishing = true;
    bool waitingForKeyframe = true;
    uint32_t zero = 0;
    int64_t flushUntil = 0;
    for (;;)
    {
        if (stopSending.load() && flushUntil == 0)
        {
            flushUntil = NowMs() + CloseFlushMs;
        }

        if (flushUntil != 0 && (NowMs() > flushUntil || sendQueue.DurationMs() == 0))
        {
            Packet last;
            while (NowMs() <= flushUntil && sendQueue.Pop(last, 0))
            {
                if (!waitingForKeyframe && !SendPacket(client, last, zero, error))
                {
                    return true;
                }
            }

            return true;
        }

        Packet packet;
        if (!sendQueue.Pop(packet, 100))
        {
            if (!client.Poll(error))
            {
                return false;
            }

            continue;
        }

        if (waitingForKeyframe)
        {
            if (packet.type != rtmp::Video || !packet.keyframe)
            {
                continue;
            }

            waitingForKeyframe = false;
            zero = packet.timestamp;
            Bytes videoHeaderCopy;
            Bytes audioHeaderCopy;
            {
                std::lock_guard<std::mutex> lock(headersMutex);
                videoHeaderCopy = videoHeader;
                audioHeaderCopy = audioHeader;
            }

            if (!SendBody(client, rtmp::Video, 0, videoHeaderCopy, error) || (!audioHeaderCopy.empty() && !SendBody(client, rtmp::Audio, 0, audioHeaderCopy, error)))
            {
                return false;
            }
        }

        if (!SendPacket(client, packet, zero, error) || !client.Poll(error))
        {
            return false;
        }

        std::lock_guard<std::mutex> lock(mutex);
        stats.bytes_sent = static_cast<int64_t>(client.BytesSent());
        stats.frames_sent += packet.type == rtmp::Video ? 1 : 0;
    }
}

bool vv_stream::SendPacket(RtmpClient& client, const Packet& packet, uint32_t zero, std::string& error)
{
    uint32_t timestamp = packet.timestamp >= zero ? packet.timestamp - zero : 0;
    return SendBody(client, packet.type, timestamp, packet.body, error);
}

bool vv_stream::SendBody(RtmpClient& client, uint8_t type, uint32_t timestamp, const Bytes& body, std::string& error)
{
    if (body.empty())
    {
        return true;
    }

    rtmp::Message message;
    message.type = type;
    message.timestamp = timestamp;
    message.payload = body;
    return client.Send(type == rtmp::Audio ? rtmp::AudioChunks : rtmp::VideoChunks, message, error);
}
