#pragma once

#include <d3d11.h>
#include <dxgi1_2.h>
#include <wrl/client.h>

#include <string>
#include <vector>

#include "frame_exchange.h"
#include "vv_capture.h"

namespace vv::capture
{
    // Returned by Duplication::Create when no monitor attached to the desktop has the name.
    constexpr HRESULT MonitorNotFound = HRESULT_FROM_WIN32(ERROR_NOT_FOUND);

    // The monitors attached to the desktop, on every GPU (the prototype saw only the first GPU's).
    HRESULT ListMonitors(std::vector<vv_capture_monitor>& monitors);

    // Copies `height` rows of `width` BGRA pixels from a mapped texture with row `pitch` into
    // `target`, without the row padding. The target's memory is reused and grows only when the size
    // does, so capturing allocates nothing per frame. False when the rows do not fit the pitch or
    // memory runs out.
    bool CopyRows(FrameBuffer& target, const uint8_t* source, size_t pitch, int32_t width, int32_t height);

    // One monitor's duplication, with its D3D11 device on the GPU the monitor is attached to.
    // Used only by the session's capture thread.
    class Duplication
    {
    public:
        HRESULT Create(const std::wstring& deviceName);

        void Release();

        bool IsCreated() const
        {
            return _duplication != nullptr;
        }

        // Waits up to `timeoutMilliseconds` for a new image and publishes it to `exchange`.
        // S_OK: a frame was published. S_FALSE: nothing new (a timeout, or only the pointer moved).
        // A failure means the duplication is lost and must be created again.
        HRESULT Capture(FrameExchange& exchange, UINT timeoutMilliseconds);

    private:
        HRESULT EnsureStaging(const D3D11_TEXTURE2D_DESC& desc);

        Microsoft::WRL::ComPtr<ID3D11Device> _device;
        Microsoft::WRL::ComPtr<ID3D11DeviceContext> _context;
        Microsoft::WRL::ComPtr<IDXGIOutputDuplication> _duplication;
        Microsoft::WRL::ComPtr<ID3D11Texture2D> _staging;
        UINT _stagingWidth = 0;
        UINT _stagingHeight = 0;
    };
}
