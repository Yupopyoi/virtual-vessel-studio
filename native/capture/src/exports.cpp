// The C ABI of vv_capture.h. Every function checks its arguments (capture detailed design 13) and
// keeps C++ exceptions from crossing into the caller.

#include "vv_capture.h"

#include <algorithm>
#include <memory>
#include <new>
#include <string>
#include <vector>

#include "capture_session.h"
#include "desktop_duplication.h"
#include "window_capture.h"

// A monitor's or a window's session behind the one handle type.
struct vv_capture_session
{
    explicit vv_capture_session(std::unique_ptr<vv::capture::SessionCore> started)
        : session(std::move(started))
    {
    }

    std::unique_ptr<vv::capture::SessionCore> session;
};

namespace
{
    int32_t Start(std::unique_ptr<vv::capture::SessionCore> core, vv_capture_session** session)
    {
        auto created = std::make_unique<vv_capture_session>(std::move(core));
        created->session->Start();
        *session = created.release();
        return VV_CAPTURE_OK;
    }
}

extern "C"
{
    int32_t vv_capture_api_version(void)
    {
        return VV_CAPTURE_API_VERSION;
    }

    int32_t vv_capture_list_monitors(vv_capture_monitor* monitors, int32_t capacity)
    {
        if (capacity < 0 || (capacity > 0 && monitors == nullptr))
        {
            return VV_CAPTURE_INVALID_ARGUMENT;
        }

        try
        {
            std::vector<vv_capture_monitor> found;
            if (FAILED(vv::capture::ListMonitors(found)))
            {
                return VV_CAPTURE_SYSTEM_ERROR;
            }

            std::copy_n(found.begin(), std::min(found.size(), static_cast<size_t>(capacity)), monitors);
            return static_cast<int32_t>(found.size());
        }
        catch (const std::bad_alloc&)
        {
            return VV_CAPTURE_OUT_OF_MEMORY;
        }
        catch (...)
        {
            return VV_CAPTURE_SYSTEM_ERROR;
        }
    }

    int32_t vv_capture_open(const wchar_t* device_name, uint32_t flags, vv_capture_session** session)
    {
        if (session == nullptr)
        {
            return VV_CAPTURE_INVALID_ARGUMENT;
        }

        *session = nullptr;
        if (device_name == nullptr)
        {
            return VV_CAPTURE_INVALID_ARGUMENT;
        }

        size_t length = wcsnlen(device_name, VV_CAPTURE_DEVICE_NAME_LENGTH);
        if (length == 0 || length >= VV_CAPTURE_DEVICE_NAME_LENGTH || (flags & ~VV_CAPTURE_DRAW_POINTER) != 0)
        {
            return VV_CAPTURE_INVALID_ARGUMENT;
        }

        try
        {
            return Start(std::make_unique<vv::capture::Session>(std::wstring(device_name, length), (flags & VV_CAPTURE_DRAW_POINTER) != 0), session);
        }
        catch (const std::bad_alloc&)
        {
            return VV_CAPTURE_OUT_OF_MEMORY;
        }
        catch (...)
        {
            return VV_CAPTURE_SYSTEM_ERROR;
        }
    }

    void vv_capture_close(vv_capture_session* session)
    {
        delete session;
    }

    int32_t vv_capture_get_status(const vv_capture_session* session, vv_capture_status* status)
    {
        if (session == nullptr || status == nullptr)
        {
            return VV_CAPTURE_INVALID_ARGUMENT;
        }

        *status = session->session->Status();
        return VV_CAPTURE_OK;
    }

    int32_t vv_capture_take_frame(vv_capture_session* session, uint64_t after, vv_capture_frame* frame)
    {
        if (session == nullptr || frame == nullptr)
        {
            return VV_CAPTURE_INVALID_ARGUMENT;
        }

        *frame = {};
        return session->session->Take(after, *frame) ? 1 : 0;
    }

    int32_t vv_capture_list_windows(vv_capture_window* windows, int32_t capacity)
    {
        if (capacity < 0 || (capacity > 0 && windows == nullptr))
        {
            return VV_CAPTURE_INVALID_ARGUMENT;
        }

        try
        {
            std::vector<vv_capture_window> found;
            if (FAILED(vv::capture::ListWindows(found)))
            {
                return VV_CAPTURE_SYSTEM_ERROR;
            }

            std::copy_n(found.begin(), std::min(found.size(), static_cast<size_t>(capacity)), windows);
            return static_cast<int32_t>(found.size());
        }
        catch (const std::bad_alloc&)
        {
            return VV_CAPTURE_OUT_OF_MEMORY;
        }
        catch (...)
        {
            return VV_CAPTURE_SYSTEM_ERROR;
        }
    }

    int32_t vv_capture_open_window(const wchar_t* executable, uint32_t flags, vv_capture_session** session)
    {
        if (session == nullptr)
        {
            return VV_CAPTURE_INVALID_ARGUMENT;
        }

        *session = nullptr;
        if (executable == nullptr)
        {
            return VV_CAPTURE_INVALID_ARGUMENT;
        }

        size_t length = wcsnlen(executable, VV_CAPTURE_EXECUTABLE_LENGTH);
        if (length == 0 || length >= VV_CAPTURE_EXECUTABLE_LENGTH || (flags & ~VV_CAPTURE_DRAW_POINTER) != 0)
        {
            return VV_CAPTURE_INVALID_ARGUMENT;
        }

        try
        {
            return Start(std::make_unique<vv::capture::WindowSession>(std::wstring(executable, length), (flags & VV_CAPTURE_DRAW_POINTER) != 0), session);
        }
        catch (const std::bad_alloc&)
        {
            return VV_CAPTURE_OUT_OF_MEMORY;
        }
        catch (...)
        {
            return VV_CAPTURE_SYSTEM_ERROR;
        }
    }
}
