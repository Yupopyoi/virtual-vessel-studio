// VirtualVessel.Stream.Native: the stream encoded with Media Foundation (H.264 or AV1, and AAC), muxed into
// FLV, and sent over RTMP or RTMPS (TLS through SChannel), with reconnection (streaming detailed design).
//
// The caller pushes NV12 frames from one thread; they are copied into a few slots and encoded on a thread of
// the stream's own, and a second thread sends. Times are QPC values, the clock the whole application shares.
// The stream key is used only to publish; it never appears in an error, a log, or the stats.
#ifndef VV_STREAM_H
#define VV_STREAM_H

#include <stdint.h>

#if defined(VV_STREAM_EXPORTS)
#define VV_STREAM_API __declspec(dllexport)
#else
#define VV_STREAM_API __declspec(dllimport)
#endif

#ifdef __cplusplus
extern "C"
{
#endif

#define VV_STREAM_API_VERSION 1

#define VV_STREAM_OK 0
#define VV_STREAM_INVALID_ARGUMENT (-1)
#define VV_STREAM_OUT_OF_MEMORY (-2)
#define VV_STREAM_SYSTEM_ERROR (-3)
#define VV_STREAM_DROPPED 1

#define VV_STREAM_CODEC_H264 0
#define VV_STREAM_CODEC_AV1 1

// Where the stream is (streaming detailed design 6).
#define VV_STREAM_STATE_CONNECTING 0
#define VV_STREAM_STATE_LIVE 1
#define VV_STREAM_STATE_RECONNECTING 2
#define VV_STREAM_STATE_FAILED 3
#define VV_STREAM_STATE_CLOSED 4

#define VV_STREAM_PROBLEM_NONE 0
#define VV_STREAM_PROBLEM_UNREACHABLE 1
#define VV_STREAM_PROBLEM_REJECTED 2
#define VV_STREAM_PROBLEM_CONNECTION_LOST 3
#define VV_STREAM_PROBLEM_ENCODER_FAILED 4

// The audio engine's stream tap (vv_audio_stream_tap.read), as for recording.
typedef int32_t (*vv_stream_audio_read)(void* context, float* out, int32_t frames, int64_t until_qpc, int64_t* first_qpc);

typedef struct vv_stream vv_stream;

typedef struct vv_stream_config
{
    const char* url;          // rtmp://host[:port]/app or rtmps://..., UTF-8.
    const char* key;          // The stream key, UTF-8; copied, and never logged.
    int32_t width;            // Even.
    int32_t height;           // Even.
    int32_t fps;
    int32_t codec;            // VV_STREAM_CODEC_*.
    int32_t video_bitrate;    // Bits per second, constant.
    int32_t audio_bitrate;    // AAC, 96 to 192 kbit/s.
    int32_t keyframe_seconds; // 2 for YouTube.
    vv_stream_audio_read audio_read; // Null for a video-only stream.
    void* audio_context;
} vv_stream_config;

typedef struct vv_stream_stats
{
    int32_t state;            // VV_STREAM_STATE_*
    int32_t problem;          // VV_STREAM_PROBLEM_*
    int32_t hardware;         // 1 when a hardware encoder is in use.
    int32_t reconnects;
    int64_t bytes_sent;
    int64_t queue_ms;         // How much is waiting to be sent.
    int64_t frames_pushed;
    int64_t frames_sent;
    int64_t frames_dropped;   // No free slot, or dropped to the next keyframe when the queue was full.
    int64_t encode_us_total;  // Video encoding time, for the average.
    int64_t av_offset_us;     // Where the audio ends against the video at the last frame.
} vv_stream_stats;

VV_STREAM_API int32_t vv_stream_api_version(void);

// Checks the URL's form and starts connecting and encoding; it does not wait for the connection.
VV_STREAM_API int32_t vv_stream_open(const vv_stream_config* config, vv_stream** stream);

// Copies one NV12 frame (rows top to bottom) taken at qpc. VV_STREAM_DROPPED when every slot is busy.
VV_STREAM_API int32_t vv_stream_push_video(vv_stream* stream, const uint8_t* nv12, int32_t length, int64_t qpc);

VV_STREAM_API int32_t vv_stream_get_stats(vv_stream* stream, vv_stream_stats* stats);

// Sends what is queued (for up to 2 s), ends the stream, and frees it.
VV_STREAM_API int32_t vv_stream_close(vv_stream* stream);

// Connects to the server as far as RTMP connect, without publishing (the setup screen's connection test).
// Returns a VV_STREAM_PROBLEM_* code.
VV_STREAM_API int32_t vv_stream_test_connection(const char* url, int32_t timeout_ms);

#ifdef __cplusplus
}
#endif

#endif
