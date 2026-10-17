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
    // `target`, without the row padding and bottom row first, the order of Unity's textures (the
    // desktop comes top row first, which showed the image upside down). The target's memory is
    // reused and grows only when the size does, so capturing allocates nothing per frame. False when
    // the rows do not fit the pitch or memory runs out.
    bool CopyRows(FrameBuffer& target, const uint8_t* source, size_t pitch, int32_t width, int32_t height);

    // The mouse pointer as Desktop Duplication reports it.
    struct Pointer
    {
        DXGI_OUTDUPL_POINTER_SHAPE_INFO shape = {};
        std::vector<uint8_t> shapeBytes;
        int32_t x = 0;          // The shape's top-left corner, relative to the monitor's image.
        int32_t y = 0;
        bool visible = false;
    };

    // Draws the pointer onto `target` (bottom row first, as CopyRows leaves it) the way Windows
    // does: color shapes are blended by their alpha, masked color shapes replace or invert by their
    // mask, and monochrome shapes apply an AND mask and then an XOR mask. Parts outside the image
    // are left out. Only `target` changes.
    void DrawPointer(FrameBuffer& target, const Pointer& pointer);

    // One monitor's duplication, with its D3D11 device on the GPU the monitor is attached to.
    // Used only by the session's capture thread.
    class Duplication
    {
    public:
        explicit Duplication(bool drawPointer)
            : _drawPointer(drawPointer)
        {
        }

        HRESULT Create(const std::wstring& deviceName);

        void Release();

        bool IsCreated() const
        {
            return _duplication != nullptr;
        }

        // Waits up to `timeoutMilliseconds` for a new image, or a pointer change when the pointer is
        // drawn, and publishes the frame to `exchange`. S_OK: a frame was published. S_FALSE: nothing
        // new. A failure means the duplication is lost and must be created again.
        HRESULT Capture(FrameExchange& exchange, UINT timeoutMilliseconds);

    private:
        HRESULT EnsureStaging(const D3D11_TEXTURE2D_DESC& desc);

        // Takes the pointer's new position and shape from a frame; true when what is drawn changes.
        bool UpdatePointer(const DXGI_OUTDUPL_FRAME_INFO& info);

        const bool _drawPointer;
        Microsoft::WRL::ComPtr<ID3D11Device> _device;
        Microsoft::WRL::ComPtr<ID3D11DeviceContext> _context;
        Microsoft::WRL::ComPtr<IDXGIOutputDuplication> _duplication;
        Microsoft::WRL::ComPtr<ID3D11Texture2D> _staging;
        UINT _stagingWidth = 0;
        UINT _stagingHeight = 0;

        // The staging texture keeps the last desktop image, so a pointer that moves over a still
        // screen is drawn again on that image without copying from the GPU.
        bool _hasImage = false;
        Pointer _pointer;
    };
}
