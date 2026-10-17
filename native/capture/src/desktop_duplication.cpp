#include "desktop_duplication.h"

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
        if (pitch == rowBytes)
        {
            std::memcpy(destination, source, rowBytes * static_cast<size_t>(height));
        }
        else
        {
            for (int32_t y = 0; y < height; y++)
            {
                std::memcpy(destination + rowBytes * static_cast<size_t>(y), source + pitch * static_cast<size_t>(y), rowBytes);
            }
        }

        target.width = width;
        target.height = height;
        return true;
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
        if (info.LastPresentTime.QuadPart == 0)
        {
            return S_FALSE;
        }

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
        D3D11_MAPPED_SUBRESOURCE mapped = {};
        hr = _context->Map(_staging.Get(), 0, D3D11_MAP_READ, 0, &mapped);
        if (FAILED(hr))
        {
            return hr;
        }

        // Map waits for the copy, so the desktop image can go back before the slower CPU copy.
        frame.Release();

        bool copied = CopyRows(exchange.Writable(), static_cast<const uint8_t*>(mapped.pData), mapped.RowPitch, static_cast<int32_t>(desc.Width), static_cast<int32_t>(desc.Height));
        _context->Unmap(_staging.Get(), 0);
        if (!copied)
        {
            return E_OUTOFMEMORY;
        }

        exchange.Publish();
        return S_OK;
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
