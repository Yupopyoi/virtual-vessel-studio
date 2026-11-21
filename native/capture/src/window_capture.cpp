#include "window_capture.h"

#include <d3d11.h>
#include <dwmapi.h>
#include <dxgi.h>
#include <inspectable.h>
#include <windows.graphics.capture.interop.h>
#include <windows.graphics.directx.direct3d11.interop.h>
#include <winrt/base.h>
#include <winrt/Windows.Foundation.h>
#include <winrt/Windows.Graphics.Capture.h>
#include <winrt/Windows.Graphics.DirectX.Direct3D11.h>
#include <wrl/client.h>

#include <algorithm>
#include <cwctype>
#include <functional>
#include <iterator>
#include <mutex>
#include <system_error>
#include <thread>
#include <unordered_map>
#include <utility>

#include "desktop_duplication.h"

using Microsoft::WRL::ComPtr;
namespace wgc = winrt::Windows::Graphics::Capture;
namespace wgd = winrt::Windows::Graphics::DirectX;

namespace vv::capture
{
    namespace
    {
        // Smaller windows are a tray icon's, a launcher's splash, or a hidden helper's, not a game's.
        constexpr int32_t MinimumWidth = 160;
        constexpr int32_t MinimumHeight = 120;

        // How often the frame pool is asked for a new frame. Asking, instead of being called back, leaves no handler
        // that could run after the capture ended; 2 ms is well under a frame at 120 fps and costs next to nothing.
        constexpr DWORD PollMilliseconds = 2;

        // Failures in a row before the session gives up, and the pause between tries.
        constexpr int MaximumFailures = 5;
        constexpr int RetryMilliseconds = 1000;

        // What a capture of one window ended with, when the window itself is gone.
        constexpr HRESULT WindowClosed = HRESULT_FROM_WIN32(ERROR_INVALID_WINDOW_HANDLE);

        // Coordinates in physical pixels whatever the process's DPI awareness, so the client area and the window
        // image (always physical) agree. Restores the thread's previous awareness when it goes.
        class PhysicalPixels
        {
        public:
            PhysicalPixels()
                : _previous(SetThreadDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2))
            {
            }

            ~PhysicalPixels()
            {
                if (_previous != nullptr)
                {
                    SetThreadDpiAwarenessContext(_previous);
                }
            }

            PhysicalPixels(const PhysicalPixels&) = delete;
            PhysicalPixels& operator=(const PhysicalPixels&) = delete;

        private:
            DPI_AWARENESS_CONTEXT _previous;
        };

        std::wstring ProcessPath(DWORD processId)
        {
            HANDLE process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, FALSE, processId);
            if (process == nullptr)
            {
                return std::wstring();
            }

            wchar_t path[MAX_PATH * 2] = {};
            DWORD length = static_cast<DWORD>(std::size(path));
            bool found = QueryFullProcessImageNameW(process, 0, path, &length) != FALSE;
            CloseHandle(process);
            return found ? std::wstring(path, length) : std::wstring();
        }

        BOOL CALLBACK Collect(HWND window, LPARAM parameter)
        {
            auto windows = reinterpret_cast<std::vector<WindowCandidate>*>(parameter);
            if (!IsWindowVisible(window) || GetWindow(window, GW_OWNER) != nullptr || window == GetShellWindow() ||
                (GetWindowLongW(window, GWL_EXSTYLE) & WS_EX_TOOLWINDOW) != 0)
            {
                return TRUE;
            }

            // Cloaked windows are on another virtual desktop, or suspended apps that show nothing.
            DWORD cloaked = 0;
            if (SUCCEEDED(DwmGetWindowAttribute(window, DWMWA_CLOAKED, &cloaked, sizeof(cloaked))) && cloaked != 0)
            {
                return TRUE;
            }

            wchar_t title[VV_CAPTURE_TITLE_LENGTH] = {};
            int titleLength = GetWindowTextW(window, title, VV_CAPTURE_TITLE_LENGTH);
            RECT client = {};
            if (titleLength <= 0 || !GetClientRect(window, &client))
            {
                return TRUE;
            }

            WindowCandidate candidate;
            candidate.handle = window;
            candidate.title.assign(title, static_cast<size_t>(titleLength));
            GetWindowThreadProcessId(window, &candidate.processId);
            candidate.executable = ExecutableName(ProcessPath(candidate.processId));
            candidate.width = client.right - client.left;
            candidate.height = client.bottom - client.top;
            if (!candidate.executable.empty())
            {
                windows->push_back(std::move(candidate));
            }

            return TRUE;
        }

        std::vector<WindowCandidate> Candidates()
        {
            PhysicalPixels physical;
            std::vector<WindowCandidate> windows;
            EnumWindows(&Collect, reinterpret_cast<LPARAM>(&windows));
            return LargestPerProgram(windows, GetCurrentProcessId());
        }

        void CopyText(wchar_t* target, size_t capacity, const std::wstring& text)
        {
            size_t length = std::min(text.size(), capacity - 1);
            std::copy_n(text.data(), length, target);
            target[length] = L'\0';
        }

        // The HRESULT of the exception being handled.
        HRESULT CurrentError()
        {
            try
            {
                throw;
            }
            catch (const winrt::hresult_error& error)
            {
                return error.code();
            }
            catch (const std::bad_alloc&)
            {
                return E_OUTOFMEMORY;
            }
            catch (...)
            {
                return E_UNEXPECTED;
            }
        }

        // The window image's client area copied to the CPU, bottom row first; the staging texture is kept until
        // the size changes, so a frame allocates nothing.
        class Reader
        {
        public:
            explicit Reader(ID3D11Device* device)
                : _device(device)
            {
                device->GetImmediateContext(&_context);
            }

            HRESULT Read(ID3D11Texture2D* image, const RECT& box, FrameBuffer& target)
            {
                UINT width = static_cast<UINT>(box.right - box.left);
                UINT height = static_cast<UINT>(box.bottom - box.top);
                if (!_staging || width != _width || height != _height)
                {
                    D3D11_TEXTURE2D_DESC desc = {};
                    desc.Width = width;
                    desc.Height = height;
                    desc.MipLevels = 1;
                    desc.ArraySize = 1;
                    desc.Format = DXGI_FORMAT_B8G8R8A8_UNORM;
                    desc.SampleDesc.Count = 1;
                    desc.Usage = D3D11_USAGE_STAGING;
                    desc.CPUAccessFlags = D3D11_CPU_ACCESS_READ;
                    _staging.Reset();
                    HRESULT created = _device->CreateTexture2D(&desc, nullptr, &_staging);
                    if (FAILED(created))
                    {
                        return created;
                    }

                    _width = width;
                    _height = height;
                }

                D3D11_BOX source = { static_cast<UINT>(box.left), static_cast<UINT>(box.top), 0, static_cast<UINT>(box.right), static_cast<UINT>(box.bottom), 1 };
                _context->CopySubresourceRegion(_staging.Get(), 0, 0, 0, 0, image, 0, &source);
                D3D11_MAPPED_SUBRESOURCE mapped = {};
                HRESULT hr = _context->Map(_staging.Get(), 0, D3D11_MAP_READ, 0, &mapped);
                if (FAILED(hr))
                {
                    return hr;
                }

                bool copied = CopyRows(target, static_cast<const uint8_t*>(mapped.pData), mapped.RowPitch, static_cast<int32_t>(width), static_cast<int32_t>(height));
                _context->Unmap(_staging.Get(), 0);
                return copied ? S_OK : E_OUTOFMEMORY;
            }

        private:
            ID3D11Device* _device;
            ComPtr<ID3D11DeviceContext> _context;
            ComPtr<ID3D11Texture2D> _staging;
            UINT _width = 0;
            UINT _height = 0;
        };

        // Where the client area is in the window's image now; the whole image when that cannot be told.
        RECT CurrentClientBox(HWND window, int32_t imageWidth, int32_t imageHeight)
        {
            RECT whole = { 0, 0, imageWidth, imageHeight };
            RECT frame = {};
            RECT client = {};
            POINT origin = {};
            if (FAILED(DwmGetWindowAttribute(window, DWMWA_EXTENDED_FRAME_BOUNDS, &frame, sizeof(frame))) || !GetClientRect(window, &client) ||
                !ClientToScreen(window, &origin))
            {
                return whole;
            }

            RECT clientOnScreen = { origin.x, origin.y, origin.x + client.right, origin.y + client.bottom };
            RECT box = ClientBox(frame, clientOnScreen, imageWidth, imageHeight);
            return box.right > box.left && box.bottom > box.top ? box : whole;
        }

        // Keeps the process's multithreaded apartment for as long as the process lives, so the capture threads run
        // in it without entering and leaving it. C++/WinRT keeps the factories it got for the process; when the
        // last thread left the apartment, COM tore it down and the next capture called a released factory (a crash
        // in the native test, 2026-11-20).
        void KeepApartment()
        {
            static std::once_flag s_once;
            std::call_once(s_once, []
            {
                CO_MTA_USAGE_COOKIE cookie = nullptr;
                CoIncrementMTAUsage(&cookie);
            });
        }

        void Close(const wgc::Direct3D11CaptureFramePool& pool, const wgc::GraphicsCaptureSession& session)
        {
            try
            {
                if (pool)
                {
                    pool.Close();
                }

                if (session)
                {
                    session.Close();
                }
            }
            catch (...)
            {
                // Nothing more can be done for a capture that does not close; letting it go releases it.
            }
        }

        // Closes and lets go of a capture whose window is gone, on a thread of its own. When the program was ended
        // by force, closing it could block inside Windows for good (Windows 11 build 26300, 2026-11-20: in about
        // half the tries of the native test, never when the window closed normally). Neither this session's thread
        // nor the caller closing the session must wait for that, so the thread is left to finish on its own.
        void CloseAside(wgc::GraphicsCaptureItem item, wgc::Direct3D11CaptureFramePool pool, wgc::GraphicsCaptureSession session)
        {
            try
            {
                // In the process's multithreaded apartment, kept for good by KeepApartment.
                std::thread([item = std::move(item), pool = std::move(pool), session = std::move(session)]() mutable
                {
                    Close(pool, session);
                    session = nullptr;
                    pool = nullptr;
                    item = nullptr;
                }).detach();
            }
            catch (const std::system_error&)
            {
                // No thread to spare: the objects are released here, as before.
            }
        }

        // Captures `window` until `stopping` (S_OK), the window goes (WindowClosed), or capturing fails. Calls
        // `running` once frames are being captured.
        HRESULT CaptureWindow(HWND window, bool drawPointer, FrameExchange& exchange, const std::function<bool()>& stopping, const std::function<void()>& running)
        {
            ComPtr<ID3D11Device> device;
            HRESULT hr = D3D11CreateDevice(nullptr, D3D_DRIVER_TYPE_HARDWARE, nullptr, D3D11_CREATE_DEVICE_BGRA_SUPPORT, nullptr, 0, D3D11_SDK_VERSION, &device, nullptr, nullptr);
            ComPtr<IDXGIDevice> dxgi;
            if (SUCCEEDED(hr))
            {
                hr = device.As(&dxgi);
            }

            winrt::com_ptr<IInspectable> inspectable;
            if (SUCCEEDED(hr))
            {
                hr = CreateDirect3D11DeviceFromDXGIDevice(dxgi.Get(), inspectable.put());
            }

            if (FAILED(hr))
            {
                return hr;
            }

            hr = S_OK;
            wgc::GraphicsCaptureItem item{ nullptr };
            wgc::Direct3D11CaptureFramePool pool{ nullptr };
            wgc::GraphicsCaptureSession session{ nullptr };
            try
            {
                auto direct3D = inspectable.as<wgd::Direct3D11::IDirect3DDevice>();
                auto interop = winrt::get_activation_factory<wgc::GraphicsCaptureItem, IGraphicsCaptureItemInterop>();
                winrt::check_hresult(interop->CreateForWindow(window, winrt::guid_of<wgc::GraphicsCaptureItem>(), winrt::put_abi(item)));

                winrt::Windows::Graphics::SizeInt32 size = item.Size();
                pool = wgc::Direct3D11CaptureFramePool::CreateFreeThreaded(direct3D, wgd::DirectXPixelFormat::B8G8R8A8UIntNormalized, 2, size);
                session = pool.CreateCaptureSession(item);
                session.IsCursorCaptureEnabled(drawPointer);
                try
                {
                    // Windows 11 can leave out the yellow border it draws around a captured window.
                    session.IsBorderRequired(false);
                }
                catch (...)
                {
                }

                Reader reader(device.Get());
                session.StartCapture();
                running();
                while (!stopping())
                {
                    if (!IsWindow(window))
                    {
                        hr = WindowClosed;
                        break;
                    }

                    wgc::Direct3D11CaptureFrame frame = pool.TryGetNextFrame();
                    if (!frame)
                    {
                        Sleep(PollMilliseconds);
                        continue;
                    }

                    winrt::Windows::Graphics::SizeInt32 content = frame.ContentSize();
                    if (content.Width != size.Width || content.Height != size.Height)
                    {
                        // The window was resized: frames of the new size come once the pool has it.
                        size = content;
                        pool.Recreate(direct3D, wgd::DirectXPixelFormat::B8G8R8A8UIntNormalized, 2, size);
                        continue;
                    }

                    auto access = frame.Surface().as<::Windows::Graphics::DirectX::Direct3D11::IDirect3DDxgiInterfaceAccess>();
                    ComPtr<ID3D11Texture2D> image;
                    hr = access->GetInterface(IID_PPV_ARGS(&image));
                    if (SUCCEEDED(hr))
                    {
                        PhysicalPixels physical;
                        hr = reader.Read(image.Get(), CurrentClientBox(window, content.Width, content.Height), exchange.Writable());
                    }

                    if (FAILED(hr))
                    {
                        break;
                    }

                    exchange.Publish();
                }
            }
            catch (...)
            {
                hr = CurrentError();
            }

            if (IsWindow(window))
            {
                Close(pool, session);
            }
            else
            {
                CloseAside(std::move(item), std::move(pool), std::move(session));
            }

            return hr;
        }
    }

    std::wstring ExecutableName(const std::wstring& path)
    {
        size_t slash = path.find_last_of(L"\\/");
        std::wstring name = slash == std::wstring::npos ? path : path.substr(slash + 1);
        std::transform(name.begin(), name.end(), name.begin(), [](wchar_t c) { return static_cast<wchar_t>(std::towlower(c)); });
        return name;
    }

    std::vector<WindowCandidate> LargestPerProgram(const std::vector<WindowCandidate>& windows, DWORD ownProcess)
    {
        std::vector<WindowCandidate> chosen;
        std::unordered_map<std::wstring, size_t> index;
        for (const WindowCandidate& window : windows)
        {
            if (window.processId == ownProcess || window.width < MinimumWidth || window.height < MinimumHeight)
            {
                continue;
            }

            auto found = index.find(window.executable);
            if (found == index.end())
            {
                index.emplace(window.executable, chosen.size());
                chosen.push_back(window);
            }
            else if (static_cast<int64_t>(window.width) * window.height > static_cast<int64_t>(chosen[found->second].width) * chosen[found->second].height)
            {
                chosen[found->second] = window;
            }
        }

        return chosen;
    }

    RECT ClientBox(const RECT& frameBounds, const RECT& clientOnScreen, int32_t imageWidth, int32_t imageHeight)
    {
        RECT box = {
            std::clamp<LONG>(clientOnScreen.left - frameBounds.left, 0, imageWidth),
            std::clamp<LONG>(clientOnScreen.top - frameBounds.top, 0, imageHeight),
            std::clamp<LONG>(clientOnScreen.right - frameBounds.left, 0, imageWidth),
            std::clamp<LONG>(clientOnScreen.bottom - frameBounds.top, 0, imageHeight),
        };
        if (box.right <= box.left || box.bottom <= box.top)
        {
            return RECT{};
        }

        return box;
    }

    HRESULT ListWindows(std::vector<vv_capture_window>& windows)
    {
        try
        {
            windows.clear();
            for (const WindowCandidate& candidate : Candidates())
            {
                vv_capture_window window = {};
                CopyText(window.executable, VV_CAPTURE_EXECUTABLE_LENGTH, candidate.executable);
                CopyText(window.title, VV_CAPTURE_TITLE_LENGTH, candidate.title);
                window.process_id = candidate.processId;
                window.width = candidate.width;
                window.height = candidate.height;
                windows.push_back(window);
            }

            return S_OK;
        }
        catch (const std::bad_alloc&)
        {
            return E_OUTOFMEMORY;
        }
    }

    HWND FindProgramWindow(const std::wstring& executable)
    {
        std::wstring wanted = ExecutableName(executable);
        for (const WindowCandidate& candidate : Candidates())
        {
            if (candidate.executable == wanted)
            {
                return candidate.handle;
            }
        }

        return nullptr;
    }

    WindowSession::WindowSession(std::wstring executable, bool drawPointer)
        : _executable(std::move(executable))
        , _drawPointer(drawPointer)
    {
    }

    WindowSession::~WindowSession()
    {
        StopAndJoin();
    }

    void WindowSession::Capture()
    {
        KeepApartment();
        int failures = 0;
        bool wasRunning = false;
        while (!Stopping())
        {
            HWND window = FindProgramWindow(_executable);
            if (window == nullptr)
            {
                // The program is not running, or shows no window: the source waits for it (capture detailed design 8.6).
                SetState(VV_CAPTURE_MONITOR_LOST, HRESULT_FROM_WIN32(ERROR_NOT_FOUND));
                break;
            }

            bool started = false;
            HRESULT hr = CaptureWindow(window, _drawPointer, Exchange(), [this] { return Stopping(); }, [this, &started, wasRunning]
            {
                started = true;
                SetRunning(wasRunning);
            });
            if (Stopping())
            {
                break;
            }

            if (hr == WindowClosed)
            {
                SetState(VV_CAPTURE_MONITOR_LOST, hr);
                break;
            }

            // Failures in a row: one that ran first starts the count again.
            wasRunning = wasRunning || started;
            failures = started ? 1 : failures + 1;
            if (failures > MaximumFailures)
            {
                SetState(VV_CAPTURE_FAILED, hr);
                break;
            }

            SetState(wasRunning ? VV_CAPTURE_RECOVERING : VV_CAPTURE_STARTING, hr);
            Wait(RetryMilliseconds);
        }
    }
}
