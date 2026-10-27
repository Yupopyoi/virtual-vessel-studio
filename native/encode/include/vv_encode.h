// VirtualVessel.Encode.Native: the stream video encoded to H.264 in an MP4 file through Media
// Foundation's Sink Writer, with a hardware encoder when the system has one (recording detailed design).
//
// The caller pushes BGRA frames from one thread; they are copied into a few slots and encoded on a
// thread of the encoder's own, so the caller never waits for the encoder. A push with no free slot is
// dropped and counted. Times are QPC values (QueryPerformanceCounter), the clock the whole application
// shares; the file's timeline starts at the first frame.

#ifndef VV_ENCODE_H
#define VV_ENCODE_H

#include <stdint.h>
#include <wchar.h>

#if defined(VV_ENCODE_EXPORTS)
#define VV_ENCODE_API __declspec(dllexport)
#else
#define VV_ENCODE_API __declspec(dllimport)
#endif

#ifdef __cplusplus
extern "C"
{
#endif

#define VV_ENCODE_API_VERSION 1

#define VV_ENCODE_OK 0
#define VV_ENCODE_INVALID_ARGUMENT (-1)
#define VV_ENCODE_OUT_OF_MEMORY (-2)
#define VV_ENCODE_SYSTEM_ERROR (-3)
#define VV_ENCODE_DROPPED 1

typedef struct vv_encoder vv_encoder;

typedef struct vv_encode_stats
{
    int64_t pushed;          // Frames handed in.
    int64_t written;         // Frames the Sink Writer accepted.
    int64_t dropped;         // Frames dropped because no slot was free.
    int64_t last_write_us;   // How long the last WriteSample took, in microseconds.
    int64_t max_write_us;    // The longest WriteSample.
    int64_t total_write_us;  // All WriteSample time together.
    int32_t last_error;      // The last failing HRESULT, or 0.
    int32_t hardware;        // 1 when the Sink Writer reports a hardware encoder in use, else 0.
} vv_encode_stats;

VV_ENCODE_API int32_t vv_encode_api_version(void);

// Opens an MP4 file at path and starts the encoding thread. bitrate is in bits per second.
VV_ENCODE_API int32_t vv_encode_open(const wchar_t* path, int32_t width, int32_t height, int32_t fps, int32_t bitrate, vv_encoder** encoder);

// Copies one BGRA frame (rows bottom to top, as Unity reads them back) taken at qpc for encoding.
// Returns VV_ENCODE_DROPPED when every slot is still waiting for the encoder.
VV_ENCODE_API int32_t vv_encode_push_video(vv_encoder* encoder, const uint8_t* bgra, int32_t length, int64_t qpc);

VV_ENCODE_API int32_t vv_encode_get_stats(vv_encoder* encoder, vv_encode_stats* stats);

// Encodes what is waiting, finishes the file, and frees the encoder. A file without any frame is deleted.
VV_ENCODE_API int32_t vv_encode_close(vv_encoder* encoder);

#ifdef __cplusplus
}
#endif

#endif
