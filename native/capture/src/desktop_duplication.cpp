#include "desktop_duplication.h"

#include <algorithm>
#include <cstring>
#include <new>

using Microsoft::WRL::ComPtr;

namespace vv::capture
{
    namespace
    {
        // Gives an acquired frame back to the desktop on every path, and as early as the caller says.
        class AcquiredFrame
        {
        public:
            explicit AcquiredFrame(IDXGIOutputDuplication* duplication)
                : _duplication(duplication)
            {
            }

            ~AcquiredFrame()
            {
                Release();
            }

            AcquiredFrame(const AcquiredFrame&) = delete;
            AcquiredFrame& operator=(const AcquiredFrame&) = delete;

            void Release()
            {
                if (_duplication != nullptr)
                {
                    _duplication->ReleaseFrame();
                    _duplication = nullptr;
                }
            }

        private:
            IDXGIOutputDuplication* _duplication;
        };

        // Calls `visit` for every output attached to the desktop, on every adapter, until it
        // returns false. An adapter whose outputs cannot be listed is skipped.
        template <typename Visit>
        HRESULT ForEachOutput(IDXGIFactory1* factory, Visit visit)
        {
            for (UINT adapterIndex = 0;; adapterIndex++)
            {
                ComPtr<IDXGIAdapter1> adapter;
                HRESULT hr = factory->EnumAdapters1(adapterIndex, &adapter);
                if (hr == DXGI_ERROR_NOT_FOUND)
                {
                    return S_OK;
                }

                if (FAILED(hr))
                {
                    return hr;
                }

                for (UINT outputIndex = 0;; outputIndex++)
                {
                    ComPtr<IDXGIOutput> output;
                    if (FAILED(adapter->EnumOutputs(outputIndex, &output)))
                    {
                        break;
                    }

                    DXGI_OUTPUT_DESC desc;
                    if (FAILED(output->GetDesc(&desc)) || !desc.AttachedToDesktop)
                    {
                        continue;
                    }

                    if (!visit(adapter.Get(), output.Get(), desc))
                    {
                        return S_OK;
                    }
                }
            }
        }
    }

    HRESULT ListMonitors(std::vector<vv_capture_monitor>& monitors)
    {
        monitors.clear();

        // A new factory each time: a factory keeps the adapters and outputs as they were when made.
        ComPtr<IDXGIFactory1> factory;
        HRESULT hr = CreateDXGIFactory1(IID_PPV_ARGS(&factory));
        if (FAILED(hr))
        {
            return hr;
        }

        return ForEachOutput(factory.Get(), [&monitors](IDXGIAdapter1*, IDXGIOutput*, const DXGI_OUTPUT_DESC& desc)
        {
            vv_capture_monitor monitor = {};
            wcsncpy_s(monitor.device_name, desc.DeviceName, _TRUNCATE);
            monitor.left = desc.DesktopCoordinates.left;
            monitor.top = desc.DesktopCoordinates.top;
            monitor.right = desc.DesktopCoordinates.right;
            monitor.bottom = desc.DesktopCoordinates.bottom;
            monitor.rotation = static_cast<int32_t>(desc.Rotation);

            // The desktop coordinates shrink for a caller that is not DPI aware; the display mode does not.
            DEVMODEW mode = {};
            mode.dmSize = sizeof(mode);
            if (EnumDisplaySettingsW(desc.DeviceName, ENUM_CURRENT_SETTINGS, &mode))
            {
                monitor.width = static_cast<int32_t>(mode.dmPelsWidth);
                monitor.height = static_cast<int32_t>(mode.dmPelsHeight);
            }
            else
            {
                monitor.width = monitor.right - monitor.left;
                monitor.height = monitor.bottom - monitor.top;
            }

            MONITORINFO info = {};
            info.cbSize = sizeof(info);
            monitor.is_primary = GetMonitorInfoW(desc.Monitor, &info) && (info.dwFlags & MONITORINFOF_PRIMARY) != 0 ? 1 : 0;
            monitors.push_back(monitor);
            return true;
        });
    }

    bool CopyRows(FrameBuffer& target, const uint8_t* source, size_t pitch, int32_t width, int32_t height)
    {
        if (source == nullptr || width <= 0 || height <= 0)
        {
            return false;
        }

        // The prototype copied with a size taken from the display mode list and caught the access
        // violations; checking the pitch keeps every read inside the mapped rows.
        const size_t rowBytes = static_cast<size_t>(width) * 4;
        if (pitch < rowBytes)
        {
            return false;
        }

        try
        {
            target.pixels.resize(rowBytes * static_cast<size_t>(height));
        }
        catch (const std::bad_alloc&)
        {
            return false;
        }

        uint8_t* destination = target.pixels.data();
        for (int32_t y = 0; y < height; y++)
        {
            std::memcpy(destination + rowBytes * static_cast<size_t>(height - 1 - y), source + pitch * static_cast<size_t>(y), rowBytes);
        }

        target.width = width;
        target.height = height;
        return true;
    }

    void DrawPointer(FrameBuffer& target, const Pointer& pointer)
    {
        const DXGI_OUTDUPL_POINTER_SHAPE_INFO& shape = pointer.shape;
        const bool monochrome = shape.Type == DXGI_OUTDUPL_POINTER_SHAPE_TYPE_MONOCHROME;
        const int32_t shapeWidth = static_cast<int32_t>(shape.Width);

        // A monochrome shape holds the AND mask and then the XOR mask, each half its height.
        const int32_t shapeHeight = static_cast<int32_t>(monochrome ? shape.Height / 2 : shape.Height);
        const size_t pitch = shape.Pitch;
        const size_t rowBytes = monochrome ? static_cast<size_t>((shapeWidth + 7) / 8) : static_cast<size_t>(shapeWidth) * 4;
        const size_t needed = pitch * static_cast<size_t>(monochrome ? shapeHeight * 2 : shapeHeight);
        if (!pointer.visible || shapeWidth <= 0 || shapeHeight <= 0 || pitch < rowBytes || pointer.shapeBytes.size() < needed
            || target.pixels.size() < static_cast<size_t>(target.width) * static_cast<size_t>(target.height) * 4)
        {
            return;
        }

        const int32_t left = std::max(0, pointer.x);
        const int32_t top = std::max(0, pointer.y);
        const int32_t right = std::min(target.width, pointer.x + shapeWidth);
        const int32_t bottom = std::min(target.height, pointer.y + shapeHeight);
        const uint8_t* bytes = pointer.shapeBytes.data();
        for (int32_t imageY = top; imageY < bottom; imageY++)
        {
            const int32_t row = imageY - pointer.y;
            uint8_t* destinationRow = target.pixels.data() + static_cast<size_t>(target.height - 1 - imageY) * static_cast<size_t>(target.width) * 4;
            for (int32_t imageX = left; imageX < right; imageX++)
            {
                const int32_t column = imageX - pointer.x;
                uint8_t* pixel = destinationRow + static_cast<size_t>(imageX) * 4;
                uint32_t value;
                std::memcpy(&value, pixel, 4);
                if (monochrome)
                {
                    const uint8_t bit = static_cast<uint8_t>(0x80 >> (column % 8));
                    const bool andBit = (bytes[static_cast<size_t>(row) * pitch + static_cast<size_t>(column / 8)] & bit) != 0;
                    const bool xorBit = (bytes[static_cast<size_t>(row + shapeHeight) * pitch + static_cast<size_t>(column / 8)] & bit) != 0;
                    value = (value & (andBit ? 0xFFFFFFFFu : 0xFF000000u)) ^ (xorBit ? 0x00FFFFFFu : 0u);
                }
                else
                {
                    const uint8_t* source = bytes + static_cast<size_t>(row) * pitch + static_cast<size_t>(column) * 4;
                    uint32_t color;
                    std::memcpy(&color, source, 4);
                    if (shape.Type == DXGI_OUTDUPL_POINTER_SHAPE_TYPE_MASKED_COLOR)
                    {
                        // The alpha byte is the mask: 0 replaces the pixel, anything else inverts it by the color.
                        value = source[3] == 0 ? (color | 0xFF000000u) : (value ^ (color & 0x00FFFFFFu));
                    }
                    else
                    {
                        const uint32_t alpha = source[3];
                        uint8_t blended[4];
                        for (int channel = 0; channel < 3; channel++)
                        {
                            blended[channel] = static_cast<uint8_t>((source[channel] * alpha + pixel[channel] * (255 - alpha) + 127) / 255);
                        }

                        blended[3] = 0xFF;
                        std::memcpy(&value, blended, 4);
                    }
                }

                std::memcpy(pixel, &value, 4);
            }
        }
    }

    HRESULT Duplication::Create(const std::wstring& deviceName)
    {
        Release();

        ComPtr<IDXGIFactory1> factory;
        HRESULT hr = CreateDXGIFactory1(IID_PPV_ARGS(&factory));
        if (FAILED(hr))
        {
            return hr;
        }

        bool found = false;
        ComPtr<IDXGIAdapter1> adapter;
        ComPtr<IDXGIOutput1> output;
        hr = ForEachOutput(factory.Get(), [&](IDXGIAdapter1* candidateAdapter, IDXGIOutput* candidate, const DXGI_OUTPUT_DESC& desc)
        {
            if (_wcsicmp(desc.DeviceName, deviceName.c_str()) != 0)
            {
                return true;
            }

            found = true;
            adapter = candidateAdapter;
            candidate->QueryInterface(IID_PPV_ARGS(&output));
            return false;
        });

        if (FAILED(hr))
        {
            return hr;
        }

        if (!found)
        {
            return MonitorNotFound;
        }

        if (output == nullptr)
        {
            return E_NOINTERFACE;
        }

        // The device must be on the GPU the monitor is attached to: duplicating from another GPU
        // fails on PCs with two GPUs, and the prototype always used the first one.
        hr = D3D11CreateDevice(adapter.Get(), D3D_DRIVER_TYPE_UNKNOWN, nullptr, 0, nullptr, 0, D3D11_SDK_VERSION, &_device, nullptr, &_context);
        if (SUCCEEDED(hr))
        {
            hr = output->DuplicateOutput(_device.Get(), &_duplication);
        }

        if (FAILED(hr))
        {
            Release();
        }

        return hr;
    }

    void Duplication::Release()
    {
        _hasImage = false;
        _pointer = Pointer();
        _staging.Reset();
        _stagingWidth = 0;
        _stagingHeight = 0;
        _duplication.Reset();
        _context.Reset();
        _device.Reset();
    }

    HRESULT Duplication::Capture(FrameExchange& exchange, UINT timeoutMilliseconds)
    {
        DXGI_OUTDUPL_FRAME_INFO info = {};
        ComPtr<IDXGIResource> resource;
        HRESULT hr = _duplication->AcquireNextFrame(timeoutMilliseconds, &info, &resource);
        if (hr == DXGI_ERROR_WAIT_TIMEOUT)
        {
            return S_FALSE;
        }

        if (FAILED(hr))
        {
            return hr;
        }

        AcquiredFrame frame(_duplication.Get());

        // A frame without a present time only moved the mouse pointer; the image is the same.
        const bool imageChanged = info.LastPresentTime.QuadPart != 0;
        const bool pointerChanged = _drawPointer && UpdatePointer(info);
        if (!imageChanged && !(pointerChanged && _hasImage))
        {
            return S_FALSE;
        }

        if (imageChanged)
        {
            ComPtr<ID3D11Texture2D> texture;
            hr = resource.As(&texture);
            if (FAILED(hr))
            {
                return hr;
            }

            D3D11_TEXTURE2D_DESC desc;
            texture->GetDesc(&desc);
            if (desc.Format != DXGI_FORMAT_B8G8R8A8_UNORM)
            {
                // DuplicateOutput always delivers this format; anything else would be uploaded wrongly.
                return DXGI_ERROR_UNSUPPORTED;
            }

            hr = EnsureStaging(desc);
            if (FAILED(hr))
            {
                return hr;
            }

            _context->CopyResource(_staging.Get(), texture.Get());
            _hasImage = true;
        }

        D3D11_MAPPED_SUBRESOURCE mapped = {};
        hr = _context->Map(_staging.Get(), 0, D3D11_MAP_READ, 0, &mapped);
        if (FAILED(hr))
        {
            return hr;
        }

        // Map waits for the copy, so the desktop image can go back before the slower CPU copy.
        frame.Release();

        FrameBuffer& target = exchange.Writable();
        bool copied = CopyRows(target, static_cast<const uint8_t*>(mapped.pData), mapped.RowPitch, static_cast<int32_t>(_stagingWidth), static_cast<int32_t>(_stagingHeight));
        _context->Unmap(_staging.Get(), 0);
        if (!copied)
        {
            return E_OUTOFMEMORY;
        }

        if (_drawPointer)
        {
            DrawPointer(target, _pointer);
        }

        exchange.Publish();
        return S_OK;
    }

    bool Duplication::UpdatePointer(const DXGI_OUTDUPL_FRAME_INFO& info)
    {
        bool changed = false;

        // The position is valid only in frames where the mouse was updated.
        if (info.LastMouseUpdateTime.QuadPart != 0)
        {
            const bool visible = info.PointerPosition.Visible != FALSE;
            changed = visible != _pointer.visible || (visible && (info.PointerPosition.Position.x != _pointer.x || info.PointerPosition.Position.y != _pointer.y));
            _pointer.visible = visible;
            _pointer.x = info.PointerPosition.Position.x;
            _pointer.y = info.PointerPosition.Position.y;
        }

        if (info.PointerShapeBufferSize > 0)
        {
            try
            {
                _pointer.shapeBytes.resize(info.PointerShapeBufferSize);
            }
            catch (const std::bad_alloc&)
            {
                _pointer.shape = {};
                return true;
            }

            UINT required = 0;
            if (FAILED(_duplication->GetFramePointerShape(info.PointerShapeBufferSize, _pointer.shapeBytes.data(), &required, &_pointer.shape)))
            {
                // Without a shape nothing is drawn; the screen itself is still captured.
                _pointer.shape = {};
            }

            changed = true;
        }

        return changed;
    }

    HRESULT Duplication::EnsureStaging(const D3D11_TEXTURE2D_DESC& desc)
    {
        // Kept until the size changes; the prototype created one every frame.
        if (_staging != nullptr && _stagingWidth == desc.Width && _stagingHeight == desc.Height)
        {
            return S_OK;
        }

        _staging.Reset();
        _stagingWidth = 0;
        _stagingHeight = 0;

        D3D11_TEXTURE2D_DESC staging = {};
        staging.Width = desc.Width;
        staging.Height = desc.Height;
        staging.MipLevels = 1;
        staging.ArraySize = 1;
        staging.Format = desc.Format;
        staging.SampleDesc.Count = 1;
        staging.Usage = D3D11_USAGE_STAGING;
        staging.CPUAccessFlags = D3D11_CPU_ACCESS_READ;
        HRESULT hr = _device->CreateTexture2D(&staging, nullptr, &_staging);
        if (FAILED(hr))
        {
            return hr;
        }

        _stagingWidth = desc.Width;
        _stagingHeight = desc.Height;
        return S_OK;
    }
}
