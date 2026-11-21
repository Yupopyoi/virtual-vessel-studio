// The C API of vv_stream.h over the stream session (stream_session.h).

#include "stream_session.h"

#include <windows.h>

#include <cstring>
#include <new>

using namespace vv::stream;
using namespace vv::stream::session;

extern "C"
{
    int32_t vv_stream_api_version(void)
    {
        return VV_STREAM_API_VERSION;
    }

    int32_t vv_stream_open(const vv_stream_config* config, vv_stream** stream)
    {
        if (config == nullptr || stream == nullptr || config->url == nullptr || config->key == nullptr || config->key[0] == '\0' || config->width <= 0 ||
            config->height <= 0 || (config->width % 2) != 0 || (config->height % 2) != 0 || config->fps <= 0 || config->video_bitrate <= 0 ||
            (config->codec != VV_STREAM_CODEC_H264 && config->codec != VV_STREAM_CODEC_AV1))
        {
            return VV_STREAM_INVALID_ARGUMENT;
        }

        RtmpUrl url;
        if (!ParseRtmpUrl(config->url, url))
        {
            return VV_STREAM_INVALID_ARGUMENT;
        }

        *stream = nullptr;
        try
        {
            auto created = new vv_stream();
            created->url = url;
            created->key = config->key;
            created->video.codec = config->codec == VV_STREAM_CODEC_AV1 ? VideoCodec::Av1 : VideoCodec::H264;
            created->video.width = config->width;
            created->video.height = config->height;
            created->video.fps = config->fps;
            created->video.bitrate = config->video_bitrate;
            created->video.keyframeSeconds = config->keyframe_seconds > 0 ? config->keyframe_seconds : 2;
            created->audioBitrate = config->audio_bitrate;
            created->audioRead = config->audio_read;
            created->audioContext = config->audio_context;
            LARGE_INTEGER frequency;
            QueryPerformanceFrequency(&frequency);
            created->qpcFrequency = frequency.QuadPart;
            created->stats.state = VV_STREAM_STATE_CONNECTING;
            for (Slot& slot : created->slots)
            {
                slot.data.resize(static_cast<size_t>(config->width) * static_cast<size_t>(config->height) * 3 / 2);
            }

            created->encoder = std::thread([created] { created->Encode(); });
            created->sender = std::thread([created] { created->Send(); });
            *stream = created;
            return VV_STREAM_OK;
        }
        catch (const std::bad_alloc&)
        {
            return VV_STREAM_OUT_OF_MEMORY;
        }
        catch (...)
        {
            return VV_STREAM_SYSTEM_ERROR;
        }
    }

    int32_t vv_stream_push_video(vv_stream* stream, const uint8_t* nv12, int32_t length, int64_t qpc)
    {
        if (stream == nullptr || nv12 == nullptr || static_cast<size_t>(length) != stream->slots[0].data.size())
        {
            return VV_STREAM_INVALID_ARGUMENT;
        }

        int index = -1;
        {
            std::lock_guard<std::mutex> lock(stream->mutex);
            stream->stats.frames_pushed++;
            for (int i = 0; i < SlotCount; i++)
            {
                if (stream->slots[i].state == SlotState::Free)
                {
                    index = i;
                    stream->slots[i].state = SlotState::Filling;
                    break;
                }
            }

            if (index < 0)
            {
                stream->stats.frames_dropped++;
                return VV_STREAM_DROPPED;
            }
        }

        Slot& slot = stream->slots[index];
        std::memcpy(slot.data.data(), nv12, slot.data.size());
        {
            std::lock_guard<std::mutex> lock(stream->mutex);
            slot.qpc = qpc;
            slot.state = SlotState::Full;
            stream->queue.push_back(index);
        }

        stream->wake.notify_one();
        return VV_STREAM_OK;
    }

    int32_t vv_stream_get_stats(vv_stream* stream, vv_stream_stats* stats)
    {
        if (stream == nullptr || stats == nullptr)
        {
            return VV_STREAM_INVALID_ARGUMENT;
        }

        int64_t queueMs = stream->sendQueue.DurationMs();
        int64_t queueDropped = stream->sendQueue.Dropped();
        std::lock_guard<std::mutex> lock(stream->mutex);
        *stats = stream->stats;
        stats->queue_ms = queueMs;
        stats->frames_dropped += queueDropped;
        stats->frames_dropped_queue = queueDropped;
        return VV_STREAM_OK;
    }

    int32_t vv_stream_close(vv_stream* stream)
    {
        if (stream == nullptr)
        {
            return VV_STREAM_INVALID_ARGUMENT;
        }

        {
            std::lock_guard<std::mutex> lock(stream->mutex);
            stream->closing = true;
        }

        stream->wake.notify_all();
        stream->encoder.join();
        stream->stopSending = true;
        stream->sendQueue.Wake();
        stream->sender.join();
        int32_t result = stream->stats.problem == VV_STREAM_PROBLEM_NONE ? VV_STREAM_OK : VV_STREAM_SYSTEM_ERROR;
        delete stream;
        return result;
    }

    int32_t vv_stream_test_connection(const char* url, int32_t timeout_ms)
    {
        RtmpUrl parsed;
        if (url == nullptr || !ParseRtmpUrl(url, parsed))
        {
            return VV_STREAM_PROBLEM_UNREACHABLE;
        }

        RtmpClient client(CreateTransport(parsed.secure));
        std::string error;
        PublishFailure failure = client.TestConnect(parsed, timeout_ms > 0 ? timeout_ms : ConnectTimeoutMs, error);
        return failure == PublishFailure::None ? VV_STREAM_PROBLEM_NONE : failure == PublishFailure::Rejected ? VV_STREAM_PROBLEM_REJECTED : VV_STREAM_PROBLEM_UNREACHABLE;
    }
}
