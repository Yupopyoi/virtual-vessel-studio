// VirtualVessel.Audio.Native: the audio engine of the Audio module (audio detailed design). Sources capture
// from input devices with WASAPI, each with gain, mute, and a send to the stream bus; a mix thread adds them
// every 10 ms, limits the bus to -1 dBFS, and keeps it for the encoder to read through the stream tap.
//
// Audio inside is 48 kHz, 32-bit float, stereo (interleaved). Times are QPC values (QueryPerformanceCounter),
// the clock the whole application shares, so the encoder can line audio up with the video frames.
#ifndef VV_AUDIO_H
#define VV_AUDIO_H

#include <stdint.h>
#include <wchar.h>

#if defined(VV_AUDIO_EXPORTS)
#define VV_AUDIO_API __declspec(dllexport)
#else
#define VV_AUDIO_API __declspec(dllimport)
#endif

#ifdef __cplusplus
extern "C"
{
#endif

#define VV_AUDIO_API_VERSION 1

#define VV_AUDIO_OK 0
#define VV_AUDIO_INVALID_ARGUMENT (-1)
#define VV_AUDIO_OUT_OF_MEMORY (-2)
#define VV_AUDIO_SYSTEM_ERROR (-3)
#define VV_AUDIO_NOT_FOUND (-4)

#define VV_AUDIO_SAMPLE_RATE 48000
#define VV_AUDIO_MAX_SOURCES 16

#define VV_AUDIO_BUS_STREAM 0

// What a source is doing.
#define VV_AUDIO_SOURCE_OFF 0          // No device chosen.
#define VV_AUDIO_SOURCE_STARTING 1     // Opening the device.
#define VV_AUDIO_SOURCE_RUNNING 2
#define VV_AUDIO_SOURCE_UNAVAILABLE 3  // The device is missing or unplugged; it is tried again every second.
#define VV_AUDIO_SOURCE_ERROR 4        // The device cannot be opened (in exclusive use, access denied); tried again every second.

// Why a source is in error.
#define VV_AUDIO_PROBLEM_NONE 0
#define VV_AUDIO_PROBLEM_IN_USE 1          // Another application uses the device exclusively.
#define VV_AUDIO_PROBLEM_ACCESS_DENIED 2   // Windows' privacy settings deny microphone access.
#define VV_AUDIO_PROBLEM_FAILED 3          // Any other failure; last_error has the HRESULT.

typedef struct vv_audio_engine vv_audio_engine;

typedef struct vv_audio_device
{
    wchar_t id[256];        // The endpoint ID, stable across restarts.
    wchar_t name[128];      // The friendly name, for the user.
    int32_t is_default;     // 1 for Windows' default input device (console role).
} vv_audio_device;

typedef struct vv_audio_source_status
{
    int32_t state;              // VV_AUDIO_SOURCE_*
    int32_t problem;            // VV_AUDIO_PROBLEM_*
    int32_t last_error;         // The last failing HRESULT, or 0.
    float peak_db;              // The highest level since the last call, after gain (before mute), in dBFS; -inf for silence.
    int64_t overruns;           // Frames the device delivered with no room to keep them.
    int64_t underruns;          // Mix blocks the source had too little audio for (played as silence).
    int64_t drift_corrections;  // Samples dropped or repeated to follow the device's clock.
} vv_audio_source_status;

// The stream bus for the encoder: plain values, so the encoder needs no link to this DLL.
typedef struct vv_audio_stream_tap
{
    void* context;
    // Copies up to `frames` stereo float frames of the stream bus, ending no later than `until_qpc`, and
    // returns how many; writes the QPC of the first one. One reader at a time.
    int32_t (*read)(void* context, float* out, int32_t frames, int64_t until_qpc, int64_t* first_qpc);
} vv_audio_stream_tap;

VV_AUDIO_API int32_t vv_audio_api_version(void);

// Lists the active input (capture) devices. Writes up to `capacity` and the total count.
VV_AUDIO_API int32_t vv_audio_list_inputs(vv_audio_device* devices, int32_t capacity, int32_t* count);

// Starts the engine's mix thread. Destroy stops every thread and releases the devices.
VV_AUDIO_API int32_t vv_audio_create(vv_audio_engine** engine);
VV_AUDIO_API void vv_audio_destroy(vv_audio_engine* engine);

VV_AUDIO_API int32_t vv_audio_add_source(vv_audio_engine* engine, int32_t* source);
VV_AUDIO_API int32_t vv_audio_remove_source(vv_audio_engine* engine, int32_t source);

// Chooses the source's device by endpoint ID; null or empty turns it off.
VV_AUDIO_API int32_t vv_audio_set_device(vv_audio_engine* engine, int32_t source, const wchar_t* device_id);

// Gain in dB (-60 to +12); muted silences the source but keeps its level meter.
VV_AUDIO_API int32_t vv_audio_set_gain(vv_audio_engine* engine, int32_t source, float gain_db, int32_t muted);

// How much of the source reaches a bus, in dB; -inf for none.
VV_AUDIO_API int32_t vv_audio_set_send(vv_audio_engine* engine, int32_t source, int32_t bus, float send_db);

VV_AUDIO_API int32_t vv_audio_get_source_status(vv_audio_engine* engine, int32_t source, vv_audio_source_status* status);

// The stream bus's highest level since the last call, after the limiter, in dBFS.
VV_AUDIO_API int32_t vv_audio_get_bus_peak(vv_audio_engine* engine, int32_t bus, float* peak_db);

// How far the stream bus is behind the moment the sound reached the device, in microseconds.
VV_AUDIO_API int64_t vv_audio_stream_latency_us(vv_audio_engine* engine);

VV_AUDIO_API int32_t vv_audio_get_stream_tap(vv_audio_engine* engine, vv_audio_stream_tap* tap);

#ifdef __cplusplus
}
#endif

#endif
