// VirtualVessel.Capture.Native: monitor capture through DXGI Desktop Duplication, for
// SubScreenCapture (capture detailed design 8.2).
//
// A session duplicates one monitor on a thread of its own and hands frames over through three
// buffers in turn, so neither side waits for the other. The caller uses a session from one thread
// at a time (the managed main thread); the capture thread never calls back into the caller.

#ifndef VV_CAPTURE_H
#define VV_CAPTURE_H

#include <stdint.h>
#include <wchar.h>

#if defined(VV_CAPTURE_EXPORTS)
#define VV_CAPTURE_API __declspec(dllexport)
#else
#define VV_CAPTURE_API __declspec(dllimport)
#endif

#ifdef __cplusplus
extern "C"
{
#endif

// Changes whenever a function or a structure changes; the managed side refuses another version.
#define VV_CAPTURE_API_VERSION 2

// Function results.
#define VV_CAPTURE_OK 0
#define VV_CAPTURE_INVALID_ARGUMENT (-1)
#define VV_CAPTURE_OUT_OF_MEMORY (-2)
#define VV_CAPTURE_SYSTEM_ERROR (-3)

// Session states (vv_capture_status.state).
#define VV_CAPTURE_STARTING 0       // Looking for the monitor and creating the duplication.
#define VV_CAPTURE_RUNNING 1        // Duplicating. A frame comes whenever the screen changes.
#define VV_CAPTURE_RECOVERING 2     // The duplication was lost (a mode change, the secure desktop) and is being created again.
#define VV_CAPTURE_MONITOR_LOST 3   // The monitor is not connected any more. The session has stopped.
#define VV_CAPTURE_FAILED 4         // It gave up; last_error has the HRESULT. The session has stopped.

#define VV_CAPTURE_DEVICE_NAME_LENGTH 32

// Flags of vv_capture_open.
#define VV_CAPTURE_DRAW_POINTER 1u  // Draw the mouse pointer into the frames, as the screen shows it.

typedef struct vv_capture_monitor
{
    wchar_t device_name[VV_CAPTURE_DEVICE_NAME_LENGTH];  // Such as \\.\DISPLAY2, as in DXGI_OUTPUT_DESC.
    int32_t width;          // The current display mode in physical pixels, whatever the caller's DPI awareness.
    int32_t height;
    int32_t left;           // Desktop coordinates. Scaled down for a caller that is not DPI aware.
    int32_t top;
    int32_t right;
    int32_t bottom;
    int32_t rotation;       // DXGI_MODE_ROTATION.
    int32_t is_primary;     // 1 for the main monitor.
} vv_capture_monitor;

typedef struct vv_capture_status
{
    int32_t state;          // VV_CAPTURE_STARTING and so on.
    int32_t last_error;     // HRESULT of the last failure; 0 when there is none.
    int32_t width;          // Size of the latest frame; 0 before the first.
    int32_t height;
    uint64_t frame_number;  // Number of the latest frame, one more per frame; 0 before the first.
    uint64_t overwritten;   // Frames replaced by a newer one before they were taken.
    uint32_t recoveries;    // Times the duplication was created again after being lost.
    uint32_t reserved;
} vv_capture_status;

typedef struct vv_capture_frame
{
    const uint8_t* data;    // B8G8R8A8 rows of width * 4 bytes, bottom to top as in Unity's textures, without gaps.
    int32_t width;
    int32_t height;
    uint64_t frame_number;
} vv_capture_frame;

typedef struct vv_capture_session vv_capture_session;

VV_CAPTURE_API int32_t vv_capture_api_version(void);

// Writes up to `capacity` monitors attached to the desktop, on every GPU, and returns how many
// there are (possibly more than `capacity`), or a negative result.
VV_CAPTURE_API int32_t vv_capture_list_monitors(vv_capture_monitor* monitors, int32_t capacity);

// Starts duplicating the monitor named `device_name` on a thread of its own and returns at once;
// the outcome comes through vv_capture_get_status. `flags` are VV_CAPTURE_DRAW_POINTER or 0. The
// session must be closed.
VV_CAPTURE_API int32_t vv_capture_open(const wchar_t* device_name, uint32_t flags, vv_capture_session** session);

// Stops the thread, waiting for it (about 100 ms at most), and releases everything. Accepts null.
VV_CAPTURE_API void vv_capture_close(vv_capture_session* session);

VV_CAPTURE_API int32_t vv_capture_get_status(const vv_capture_session* session, vv_capture_status* status);

// When a frame newer than `after` is ready, takes it for reading and returns 1; returns 0 when there
// is none, or a negative result. The frame stays valid and unchanged until the next take or the close.
VV_CAPTURE_API int32_t vv_capture_take_frame(vv_capture_session* session, uint64_t after, vv_capture_frame* frame);

#ifdef __cplusplus
}
#endif

#endif
