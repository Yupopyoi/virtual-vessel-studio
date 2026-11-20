// The stream session behind the C API of vv_stream.h: the frame slots, the encoding thread, the send queue, and
// the sending thread (streaming detailed design 8, 9). Encoding is in stream_encoding.cpp, sending in
// stream_sending.cpp, and the C API in exports.cpp.
#pragma once

#include "vv_stream.h"

#include <windows.h>

#include "flv.h"
#include "mft_encoders.h"
#include "rtmp_client.h"
#include "send_queue.h"

#include <atomic>
#include <chrono>
#include <condition_variable>
#include <cstdint>
#include <deque>
#include <mutex>
#include <string>
#include <thread>
#include <vector>

namespace vv::stream::session
{
    // As for recording: enough to ride out a slow encode, few enough to keep the delay low.
    inline constexpr int SlotCount = 4;

    // The send queue's limit before video is dropped to the next keyframe (streaming detailed design 8.2).
    inline constexpr uint32_t QueueLimitMs = 3'000;

    // Connecting and each RTMP step may take this long.
    inline constexpr int ConnectTimeoutMs = 10'000;

    // At the end, what is queued is sent for at most this long.
    inline constexpr int CloseFlushMs = 2'000;

    // At the end, the video encoder gives back the frames it held within this; a hardware encoder takes a few ms.
    inline constexpr int CloseDrainMs = 500;

    inline constexpr int32_t AudioChunkFrames = 4800;

    inline int64_t NowMs()
    {
        return std::chrono::duration_cast<std::chrono::milliseconds>(std::chrono::steady_clock::now().time_since_epoch()).count();
    }

    enum class SlotState
    {
        Free,
        Filling,
        Full,
    };

    struct Slot
    {
        std::vector<uint8_t> data;
        int64_t qpc = 0;
        SlotState state = SlotState::Free;
    };
}

struct vv_stream
{
    vv::stream::RtmpUrl url;
    std::string key;
    vv::stream::VideoEncoderConfig video;
    int32_t audioBitrate = 0;
    vv_stream_audio_read audioRead = nullptr;
    void* audioContext = nullptr;
    int64_t qpcFrequency = 1;

    std::mutex mutex;
    std::condition_variable wake;
    vv::stream::session::Slot slots[vv::stream::session::SlotCount];
    std::deque<int> queue;
    bool closing = false;
    vv_stream_stats stats = {};

    vv::stream::SendQueue sendQueue{ vv::stream::session::QueueLimitMs };
    std::atomic<bool> stopSending{ false };
    std::atomic<bool> requestKeyframe{ false };

    // The sequence headers, kept so each new connection can send them again.
    std::mutex headersMutex;
    vv::stream::Bytes videoHeader;
    vv::stream::Bytes audioHeader;

    std::thread encoder;
    std::thread sender;

    // Encoding thread only.
    bool started = false;
    int64_t startQpc = 0;
    int64_t audioWritten = 0;
    std::vector<float> audioFloats;
    std::vector<int16_t> audioPcm;

    ~vv_stream()
    {
        // The key is not left in freed memory.
        SecureZeroMemory(key.data(), key.size());
    }

    void SetState(int32_t state, int32_t problem)
    {
        std::lock_guard<std::mutex> lock(mutex);
        stats.state = state;
        stats.problem = problem;
    }

    int32_t State()
    {
        std::lock_guard<std::mutex> lock(mutex);
        return stats.state;
    }

    // ---- Encoding (stream_encoding.cpp)

    void Encode();

    // Closing: the frames the encoders still hold are queued, so the stream does not end a frame and a fraction
    // of a second of audio early. A failed drain only loses those frames, as before; the stream is ending anyway.
    void DrainEncoders(vv::stream::VideoEncoder& videoEncoder, vv::stream::AacEncoder& aacEncoder);

    void QueueVideo(const vv::stream::EncodedFrame& frame);

    // The stream bus up to this frame, placed by its time as for recording (recording detailed design 8.1),
    // encoded to AAC and queued.
    bool PumpAudio(vv::stream::AacEncoder& aac, int64_t untilQpc, int64_t videoEnd, std::string& error);

    void QueueAudio(const std::vector<vv::stream::EncodedFrame>& frames);

    bool Silence(vv::stream::AacEncoder& aac, int64_t frames, std::vector<vv::stream::EncodedFrame>& out, std::string& error);

    // ---- Sending (stream_sending.cpp)

    void Send();

    void WaitBeforeRetry(int attempt);

    // Sends the stream on a published connection until it is asked to stop (true) or the connection is lost (false).
    bool Publish(vv::stream::RtmpClient& client, std::string& error);

    bool SendPacket(vv::stream::RtmpClient& client, const vv::stream::Packet& packet, uint32_t zero, std::string& error);

    static bool SendBody(vv::stream::RtmpClient& client, uint8_t type, uint32_t timestamp, const vv::stream::Bytes& body, std::string& error);
};
