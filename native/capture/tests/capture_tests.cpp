// Tests of the capture plugin. The exchange, the retry policy, the row copy, and choosing windows are
// pure and run anywhere; the C ABI checks need only DXGI. `--hardware` also duplicates a real monitor
// and captures a window this program shows from a second copy of itself, for a moment (nothing is
// kept): run it by hand on a PC with a display.

#include <windows.h>

#include <algorithm>
#include <chrono>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <set>
#include <string>
#include <thread>
#include <vector>

#include "desktop_duplication.h"
#include "frame_exchange.h"
#include "retry_policy.h"
#include "vv_capture.h"
#include "window_capture.h"

using namespace vv::capture;

namespace
{
    int s_failures = 0;

    void Check(bool condition, const char* expression, int line)
    {
        if (!condition)
        {
            std::fprintf(stderr, "capture_tests.cpp(%d): check failed: %s\n", line, expression);
            s_failures++;
        }
    }

#define CHECK(condition) Check((condition), #condition, __LINE__)

    bool IsPermutation(const ExchangeState& state)
    {
        std::set<int> slots{state.write, state.latest, state.read};
        return slots == std::set<int>{0, 1, 2};
    }

    void Exchange_PublishedFrame_IsTakenOnce()
    {
        ExchangeState state;
        int written = state.write;
        state = Publish(state);

        ExchangeState taken = Take(state, 0);
        CHECK(taken.read == written);
        CHECK(taken.taken == 1);
        CHECK(IsPermutation(taken));
        CHECK(Take(taken, 1).read == taken.read);
        CHECK(Take(taken, 0).read == taken.read);
    }

    void Exchange_NothingNew_IsNotTaken()
    {
        ExchangeState state;
        CHECK(Take(state, 0).read == state.read);
    }

    void Exchange_AFrameReplacedBeforeTaken_CountsAsOverwritten()
    {
        ExchangeState state = Publish(Publish(ExchangeState()));
        CHECK(state.overwritten == 1);

        state = Take(state, 0);
        CHECK(state.taken == 2);

        state = Publish(state);
        CHECK(state.overwritten == 1);
    }

    void Exchange_TheWriterNeverGetsTheReadersBuffer()
    {
        // Any order of publishing and taking keeps the three buffers apart.
        ExchangeState state;
        unsigned seed = 12345;
        for (int i = 0; i < 10000; i++)
        {
            seed = seed * 1103515245u + 12345u;
            int readBefore = state.read;
            if ((seed >> 16) % 3 == 0)
            {
                state = Take(state, state.taken);
            }
            else
            {
                state = Publish(state);
                CHECK(state.read == readBefore);
            }

            CHECK(IsPermutation(state));
            CHECK(state.numbers[state.read] == state.taken);
        }
    }

    void FrameExchange_HandsOverAcrossThreads()
    {
        FrameExchange exchange;
        constexpr int Frames = 2000;
        std::thread writer([&exchange]
        {
            for (int i = 1; i <= Frames; i++)
            {
                FrameBuffer& buffer = exchange.Writable();
                buffer.pixels.assign(64, static_cast<uint8_t>(i));
                buffer.width = 4;
                buffer.height = 4;
                exchange.Publish();
            }
        });

        // Every taken frame is whole: all its bytes come from the same write.
        uint64_t last = 0;
        bool whole = true;
        auto deadline = std::chrono::steady_clock::now() + std::chrono::seconds(10);
        while (last < Frames && std::chrono::steady_clock::now() < deadline)
        {
            uint64_t number = 0;
            const FrameBuffer* frame = exchange.Take(last, number);
            if (frame == nullptr)
            {
                std::this_thread::yield();
                continue;
            }

            CHECK(number > last);
            for (uint8_t value : frame->pixels)
            {
                whole = whole && value == static_cast<uint8_t>(number);
            }

            last = number;
        }

        writer.join();
        CHECK(whole);
        CHECK(last == Frames);
    }

    void RetryPolicy_GivesUpAfterFiveRetries()
    {
        CHECK(RetryDelayMilliseconds(1, E_FAIL, false) == 250);
        CHECK(RetryDelayMilliseconds(5, E_FAIL, true) == 4000);
        CHECK(RetryDelayMilliseconds(MaxFailures, E_FAIL, true) == -1);
        CHECK(CountFailure(2, E_FAIL, true) == 3);
    }

    void RetryPolicy_WaitsForTheSecureDesktop_OnlyAfterRunning()
    {
        CHECK(RetryDelayMilliseconds(100, E_ACCESSDENIED, true) == 1000);
        CHECK(CountFailure(3, E_ACCESSDENIED, true) == 3);
        CHECK(CountFailure(3, E_ACCESSDENIED, false) == 4);
        CHECK(RetryDelayMilliseconds(MaxFailures, E_ACCESSDENIED, false) == -1);
    }

    void CopyRows_DropsThePadding_PutsTheBottomRowFirst_AndChecksThePitch()
    {
        // Two rows of two pixels, each row padded to 12 bytes; the desktop gives the top row first.
        std::vector<uint8_t> source(24, 0xEE);
        for (int i = 0; i < 8; i++)
        {
            source[i] = static_cast<uint8_t>(i);
            source[12 + i] = static_cast<uint8_t>(100 + i);
        }

        FrameBuffer target;
        CHECK(CopyRows(target, source.data(), 12, 2, 2));
        CHECK(target.width == 2 && target.height == 2);
        CHECK(target.pixels.size() == 16);
        CHECK(target.pixels[0] == 100 && target.pixels[7] == 107 && target.pixels[8] == 0 && target.pixels[15] == 7);

        CHECK(!CopyRows(target, source.data(), 4, 2, 2));
        CHECK(!CopyRows(target, nullptr, 12, 2, 2));
        CHECK(!CopyRows(target, source.data(), 12, 0, 2));
    }

    FrameBuffer Filled(int32_t width, int32_t height, uint32_t color)
    {
        FrameBuffer buffer;
        buffer.width = width;
        buffer.height = height;
        buffer.pixels.resize(static_cast<size_t>(width) * static_cast<size_t>(height) * 4);
        for (size_t i = 0; i < buffer.pixels.size(); i += 4)
        {
            std::memcpy(&buffer.pixels[i], &color, 4);
        }

        return buffer;
    }

    // The pixel at (x, y) counted from the top, in a buffer stored bottom row first.
    uint32_t PixelAt(const FrameBuffer& buffer, int32_t x, int32_t y)
    {
        uint32_t value;
        std::memcpy(&value, &buffer.pixels[(static_cast<size_t>(buffer.height - 1 - y) * static_cast<size_t>(buffer.width) + static_cast<size_t>(x)) * 4], 4);
        return value;
    }

    void DrawPointer_BlendsAColorShape_AndClipsAtTheEdge()
    {
        FrameBuffer image = Filled(4, 4, 0xFF000000u);
        Pointer pointer;
        pointer.visible = true;
        pointer.shape.Type = DXGI_OUTDUPL_POINTER_SHAPE_TYPE_COLOR;
        pointer.shape.Width = 2;
        pointer.shape.Height = 2;
        pointer.shape.Pitch = 8;

        // Opaque white, half-transparent white, and two clear pixels; placed at the right edge.
        const uint32_t shape[4] = {0xFFFFFFFFu, 0x80FFFFFFu, 0x00000000u, 0x00000000u};
        pointer.shapeBytes.assign(reinterpret_cast<const uint8_t*>(shape), reinterpret_cast<const uint8_t*>(shape) + sizeof(shape));
        pointer.x = 3;
        pointer.y = 0;

        DrawPointer(image, pointer);
        CHECK(PixelAt(image, 3, 0) == 0xFFFFFFFFu);
        CHECK(PixelAt(image, 2, 0) == 0xFF000000u);
        CHECK(PixelAt(image, 3, 1) == 0xFF000000u);

        pointer.x = 0;
        DrawPointer(image, pointer);
        CHECK((PixelAt(image, 1, 0) & 0xFFu) == 0x80u);

        Pointer hidden = pointer;
        hidden.visible = false;
        FrameBuffer untouched = Filled(4, 4, 0xFF000000u);
        DrawPointer(untouched, hidden);
        CHECK(PixelAt(untouched, 0, 0) == 0xFF000000u);
    }

    void DrawPointer_AppliesMasks()
    {
        // Monochrome, 8 pixels wide and 1 tall: AND mask row, then XOR mask row.
        FrameBuffer image = Filled(8, 1, 0xFF102030u);
        Pointer mono;
        mono.visible = true;
        mono.shape.Type = DXGI_OUTDUPL_POINTER_SHAPE_TYPE_MONOCHROME;
        mono.shape.Width = 8;
        mono.shape.Height = 2;
        mono.shape.Pitch = 1;
        mono.shapeBytes = {0x7F, 0xC0};

        DrawPointer(image, mono);
        CHECK(PixelAt(image, 0, 0) == 0xFFFFFFFFu);   // AND 0, XOR 1: white.
        CHECK(PixelAt(image, 1, 0) == 0xFFEFDFCFu);   // AND 1, XOR 1: inverted.
        CHECK(PixelAt(image, 2, 0) == 0xFF102030u);   // AND 1, XOR 0: unchanged.

        // Masked color: alpha 0 replaces, 0xFF inverts by the color.
        FrameBuffer masked = Filled(2, 1, 0xFF102030u);
        Pointer color;
        color.visible = true;
        color.shape.Type = DXGI_OUTDUPL_POINTER_SHAPE_TYPE_MASKED_COLOR;
        color.shape.Width = 2;
        color.shape.Height = 1;
        color.shape.Pitch = 8;
        const uint32_t shape[2] = {0x00ABCDEFu, 0xFFFFFFFFu};
        color.shapeBytes.assign(reinterpret_cast<const uint8_t*>(shape), reinterpret_cast<const uint8_t*>(shape) + sizeof(shape));
        DrawPointer(masked, color);
        CHECK(PixelAt(masked, 0, 0) == 0xFFABCDEFu);
        CHECK(PixelAt(masked, 1, 0) == 0xFFEFDFCFu);
    }


    void Window_ExecutableName_IsTheLowerCaseFileName()
    {
        CHECK(ExecutableName(L"C:\\Games\\My Game\\Game.EXE") == L"game.exe");
        CHECK(ExecutableName(L"game.exe") == L"game.exe");
        CHECK(ExecutableName(L"") == L"");
    }

    WindowCandidate Candidate(const wchar_t* executable, DWORD process, int32_t width, int32_t height)
    {
        WindowCandidate window;
        window.executable = executable;
        window.processId = process;
        window.width = width;
        window.height = height;
        window.title = executable;
        return window;
    }

    void Window_LargestPerProgram_KeepsTheLargest_AndLeavesOutOwnAndSmallWindows()
    {
        std::vector<WindowCandidate> windows = {
            Candidate(L"launcher.exe", 10, 800, 600),
            Candidate(L"game.exe", 20, 640, 360),
            Candidate(L"studio.exe", 30, 1920, 1080),
            Candidate(L"game.exe", 21, 1920, 1080),
            Candidate(L"tray.exe", 40, 32, 32),
            Candidate(L"launcher.exe", 10, 400, 300),
        };

        std::vector<WindowCandidate> chosen = LargestPerProgram(windows, 30);
        CHECK(chosen.size() == 2);
        CHECK(chosen.size() == 2 && chosen[0].executable == L"launcher.exe" && chosen[0].width == 800);
        CHECK(chosen.size() == 2 && chosen[1].executable == L"game.exe" && chosen[1].processId == 21);
    }

    void Window_ClientBox_IsTheClientAreaInTheImage_Clamped()
    {
        RECT frame = { 100, 50, 900, 650 };
        RECT client = { 108, 81, 892, 642 };
        RECT box = ClientBox(frame, client, 800, 600);
        CHECK(box.left == 8 && box.top == 31 && box.right == 792 && box.bottom == 592);

        RECT larger = { 90, 40, 950, 700 };
        box = ClientBox(frame, larger, 800, 600);
        CHECK(box.left == 0 && box.top == 0 && box.right == 800 && box.bottom == 600);

        RECT apart = { 2000, 2000, 2100, 2100 };
        box = ClientBox(frame, apart, 800, 600);
        CHECK(box.right == 0 && box.bottom == 0);
    }

    void Abi_ChecksItsArguments()
    {
        CHECK(vv_capture_api_version() == VV_CAPTURE_API_VERSION);
        CHECK(vv_capture_list_monitors(nullptr, 1) == VV_CAPTURE_INVALID_ARGUMENT);
        CHECK(vv_capture_list_monitors(nullptr, -1) == VV_CAPTURE_INVALID_ARGUMENT);

        vv_capture_session* session = reinterpret_cast<vv_capture_session*>(1);
        CHECK(vv_capture_open(nullptr, 0, &session) == VV_CAPTURE_INVALID_ARGUMENT);
        CHECK(session == nullptr);
        CHECK(vv_capture_open(L"", 0, &session) == VV_CAPTURE_INVALID_ARGUMENT);
        CHECK(vv_capture_open(L"\\\\.\\DISPLAY1-but-much-longer-than-32", 0, &session) == VV_CAPTURE_INVALID_ARGUMENT);
        CHECK(vv_capture_open(L"\\\\.\\DISPLAY1", 0, nullptr) == VV_CAPTURE_INVALID_ARGUMENT);
        CHECK(vv_capture_open(L"\\\\.\\DISPLAY1", 4, &session) == VV_CAPTURE_INVALID_ARGUMENT);

        CHECK(vv_capture_list_windows(nullptr, 1) == VV_CAPTURE_INVALID_ARGUMENT);
        CHECK(vv_capture_list_windows(nullptr, -1) == VV_CAPTURE_INVALID_ARGUMENT);
        session = reinterpret_cast<vv_capture_session*>(1);
        CHECK(vv_capture_open_window(nullptr, 0, &session) == VV_CAPTURE_INVALID_ARGUMENT);
        CHECK(session == nullptr);
        CHECK(vv_capture_open_window(L"", 0, &session) == VV_CAPTURE_INVALID_ARGUMENT);
        CHECK(vv_capture_open_window(L"game.exe", 0, nullptr) == VV_CAPTURE_INVALID_ARGUMENT);
        CHECK(vv_capture_open_window(L"game.exe", 4, &session) == VV_CAPTURE_INVALID_ARGUMENT);
        std::wstring tooLong(VV_CAPTURE_EXECUTABLE_LENGTH, L'a');
        CHECK(vv_capture_open_window(tooLong.c_str(), 0, &session) == VV_CAPTURE_INVALID_ARGUMENT);

        vv_capture_status status = {};
        CHECK(vv_capture_get_status(nullptr, &status) == VV_CAPTURE_INVALID_ARGUMENT);
        vv_capture_frame frame = {};
        CHECK(vv_capture_take_frame(nullptr, 0, &frame) == VV_CAPTURE_INVALID_ARGUMENT);
        vv_capture_close(nullptr);
    }

    void Abi_ListsMonitors()
    {
        int32_t count = vv_capture_list_monitors(nullptr, 0);
        CHECK(count >= 0);

        std::vector<vv_capture_monitor> monitors(static_cast<size_t>(count > 0 ? count : 1));
        CHECK(vv_capture_list_monitors(monitors.data(), static_cast<int32_t>(monitors.size())) == count);
        for (int32_t i = 0; i < count; i++)
        {
            const vv_capture_monitor& monitor = monitors[static_cast<size_t>(i)];
            CHECK(monitor.device_name[0] != L'\0');
            CHECK(monitor.width > 0 && monitor.height > 0);
            CHECK(monitor.right > monitor.left && monitor.bottom > monitor.top);
            std::printf("monitor %ls %dx%d, desktop %dx%d%s\n", monitor.device_name, monitor.width, monitor.height, monitor.right - monitor.left, monitor.bottom - monitor.top, monitor.is_primary ? " (primary)" : "");
        }
    }

    vv_capture_status WaitForState(vv_capture_session* session, int32_t until, int milliseconds)
    {
        vv_capture_status status = {};
        auto deadline = std::chrono::steady_clock::now() + std::chrono::milliseconds(milliseconds);
        do
        {
            vv_capture_get_status(session, &status);
            if (status.state == until)
            {
                break;
            }

            std::this_thread::sleep_for(std::chrono::milliseconds(10));
        }
        while (std::chrono::steady_clock::now() < deadline);

        return status;
    }

    void Abi_AMonitorThatIsNotThere_IsLost()
    {
        vv_capture_session* session = nullptr;
        CHECK(vv_capture_open(L"\\\\.\\NO-SUCH-DISPLAY", 0, &session) == VV_CAPTURE_OK);
        CHECK(session != nullptr);

        vv_capture_status status = WaitForState(session, VV_CAPTURE_MONITOR_LOST, 3000);
        CHECK(status.state == VV_CAPTURE_MONITOR_LOST);
        vv_capture_close(session);
    }

    void Abi_ListsWindows()
    {
        int32_t count = vv_capture_list_windows(nullptr, 0);
        CHECK(count >= 0);
        std::vector<vv_capture_window> windows(static_cast<size_t>(count > 0 ? count : 1));
        int32_t listed = vv_capture_list_windows(windows.data(), static_cast<int32_t>(windows.size()));
        CHECK(listed >= 0);
        for (int32_t i = 0; i < std::min(listed, static_cast<int32_t>(windows.size())); i++)
        {
            const vv_capture_window& window = windows[static_cast<size_t>(i)];
            CHECK(window.executable[0] != L'\0');
            CHECK(window.width > 0 && window.height > 0);
            CHECK(window.process_id != GetCurrentProcessId());
        }

        std::printf("windows: %d programs with a window\n", listed);
    }

    void Abi_AProgramThatIsNotRunning_IsLost()
    {
        vv_capture_session* session = nullptr;
        CHECK(vv_capture_open_window(L"no-such-program.exe", 0, &session) == VV_CAPTURE_OK);
        CHECK(session != nullptr);
        vv_capture_status status = WaitForState(session, VV_CAPTURE_MONITOR_LOST, 3000);
        CHECK(status.state == VV_CAPTURE_MONITOR_LOST);
        vv_capture_close(session);
    }

    // The window a second copy of this program shows for the window capture test: its client area is filled
    // with pure green, in physical pixels.
    constexpr int TestWindowWidth = 640;
    constexpr int TestWindowHeight = 360;

    LRESULT CALLBACK TestWindowProcedure(HWND window, UINT message, WPARAM wParam, LPARAM lParam)
    {
        if (message == WM_PAINT)
        {
            PAINTSTRUCT paint;
            HDC dc = BeginPaint(window, &paint);
            HBRUSH green = CreateSolidBrush(RGB(0, 255, 0));
            RECT client;
            GetClientRect(window, &client);
            FillRect(dc, &client, green);
            DeleteObject(green);
            EndPaint(window, &paint);
            return 0;
        }

        if (message == WM_DESTROY)
        {
            PostQuitMessage(0);
            return 0;
        }

        return DefWindowProcW(window, message, wParam, lParam);
    }

    void CALLBACK CloseTestWindow(HWND window, UINT, UINT_PTR, DWORD)
    {
        DestroyWindow(window);
    }

    int ShowTestWindow()
    {
        SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
        WNDCLASSW type = {};
        type.lpfnWndProc = TestWindowProcedure;
        type.hInstance = GetModuleHandleW(nullptr);
        type.lpszClassName = L"VvCaptureTestWindow";
        type.hCursor = LoadCursorW(nullptr, IDC_ARROW);
        RegisterClassW(&type);
        RECT bounds = { 0, 0, TestWindowWidth, TestWindowHeight };
        AdjustWindowRectEx(&bounds, WS_OVERLAPPEDWINDOW, FALSE, 0);
        HWND window = CreateWindowExW(0, type.lpszClassName, L"Capture test window", WS_OVERLAPPEDWINDOW, 100, 100, bounds.right - bounds.left,
            bounds.bottom - bounds.top, nullptr, nullptr, type.hInstance, nullptr);
        ShowWindow(window, SW_SHOWNORMAL);

        // Closes itself in case the test that started it does not.
        SetTimer(window, 1, 20'000, &CloseTestWindow);
        MSG message;
        while (GetMessageW(&message, nullptr, 0, 0) > 0)
        {
            DispatchMessageW(&message);
        }

        return 0;
    }

    // The window closed normally, or its program ended by force: either way the source learns it is gone, and
    // closing the session does not wait for Windows to tear the capture down.
    void Hardware_CapturesAWindowsClientArea_AndLosesItWhenItCloses(bool endByForce)
    {
        wchar_t path[MAX_PATH];
        GetModuleFileNameW(nullptr, path, MAX_PATH);
        std::wstring command = L"\"" + std::wstring(path) + L"\" --show-window";
        STARTUPINFOW startup = { sizeof(startup) };
        PROCESS_INFORMATION child = {};
        if (!CreateProcessW(nullptr, command.data(), nullptr, nullptr, FALSE, 0, nullptr, nullptr, &startup, &child))
        {
            CHECK(!"the test window could not be shown");
            return;
        }

        std::wstring executable = ExecutableName(path);
        auto deadline = std::chrono::steady_clock::now() + std::chrono::seconds(5);
        while (FindProgramWindow(executable) == nullptr && std::chrono::steady_clock::now() < deadline)
        {
            std::this_thread::sleep_for(std::chrono::milliseconds(50));
        }

        // Stopped by the user while the window is still there: closing waits for the thread, briefly.
        vv_capture_session* session = nullptr;
        CHECK(vv_capture_open_window(executable.c_str(), 0, &session) == VV_CAPTURE_OK);
        vv_capture_status status = WaitForState(session, VV_CAPTURE_RUNNING, 3000);
        CHECK(status.state == VV_CAPTURE_RUNNING);
        auto stopping = std::chrono::steady_clock::now();
        vv_capture_close(session);
        long long stopMs = std::chrono::duration_cast<std::chrono::milliseconds>(std::chrono::steady_clock::now() - stopping).count();
        std::printf("hardware: stopping a window capture took %lld ms\n", stopMs);
        CHECK(stopMs < 1000);

        session = nullptr;
        CHECK(vv_capture_open_window(executable.c_str(), 0, &session) == VV_CAPTURE_OK);
        vv_capture_frame frame = {};
        deadline = std::chrono::steady_clock::now() + std::chrono::seconds(5);
        while (vv_capture_take_frame(session, 0, &frame) != 1 && std::chrono::steady_clock::now() < deadline)
        {
            std::this_thread::sleep_for(std::chrono::milliseconds(10));
        }

        status = {};
        vv_capture_get_status(session, &status);
        std::printf("hardware: window frame %llu, %dx%d, state %d, error 0x%08X\n", static_cast<unsigned long long>(frame.frame_number), frame.width, frame.height,
            status.state, static_cast<unsigned>(status.last_error));
        CHECK(status.state == VV_CAPTURE_RUNNING);
        CHECK(frame.width == TestWindowWidth && frame.height == TestWindowHeight);
        if (frame.data != nullptr && frame.width > 0 && frame.height > 0)
        {
            // The middle and a corner: only the green client area, no title bar or border. BGRA.
            const uint8_t* middle = frame.data + (static_cast<size_t>(frame.height / 2) * static_cast<size_t>(frame.width) + static_cast<size_t>(frame.width / 2)) * 4;
            const uint8_t* corner = frame.data + (static_cast<size_t>(frame.height - 1) * static_cast<size_t>(frame.width)) * 4;
            CHECK(middle[0] == 0 && middle[1] == 255 && middle[2] == 0);
            CHECK(corner[0] == 0 && corner[1] == 255 && corner[2] == 0);
        }

        if (endByForce)
        {
            TerminateProcess(child.hProcess, 0);
        }
        else
        {
            PostMessageW(FindProgramWindow(executable), WM_CLOSE, 0, 0);
        }

        WaitForSingleObject(child.hProcess, 5000);
        CloseHandle(child.hThread);
        CloseHandle(child.hProcess);
        status = WaitForState(session, VV_CAPTURE_MONITOR_LOST, 3000);
        CHECK(status.state == VV_CAPTURE_MONITOR_LOST);

        auto closing = std::chrono::steady_clock::now();
        vv_capture_close(session);
        long long closeMs = std::chrono::duration_cast<std::chrono::milliseconds>(std::chrono::steady_clock::now() - closing).count();
        std::printf("hardware: closing the window capture took %lld ms\n", closeMs);
        CHECK(closeMs < 1000);
    }

    void Hardware_DuplicatesTheFirstMonitor()
    {
        vv_capture_monitor monitor = {};
        if (vv_capture_list_monitors(&monitor, 1) < 1)
        {
            std::printf("hardware: no monitor attached; skipped\n");
            return;
        }

        vv_capture_session* session = nullptr;
        CHECK(vv_capture_open(monitor.device_name, VV_CAPTURE_DRAW_POINTER, &session) == VV_CAPTURE_OK);

        // The first frame after creating the duplication is the whole current image.
        vv_capture_frame frame = {};
        auto deadline = std::chrono::steady_clock::now() + std::chrono::seconds(5);
        while (vv_capture_take_frame(session, 0, &frame) != 1 && std::chrono::steady_clock::now() < deadline)
        {
            std::this_thread::sleep_for(std::chrono::milliseconds(10));
        }

        vv_capture_status status = {};
        vv_capture_get_status(session, &status);
        CHECK(status.state == VV_CAPTURE_RUNNING);
        CHECK(frame.data != nullptr);
        // A rotated monitor may come either way round.
        CHECK((frame.width == monitor.width && frame.height == monitor.height) || (frame.width == monitor.height && frame.height == monitor.width));
        std::printf("hardware: %ls frame %llu, %dx%d, state %d, error 0x%08X\n", monitor.device_name, static_cast<unsigned long long>(frame.frame_number), frame.width, frame.height, status.state, static_cast<unsigned>(status.last_error));

        auto closing = std::chrono::steady_clock::now();
        vv_capture_close(session);
        long long closeMs = std::chrono::duration_cast<std::chrono::milliseconds>(std::chrono::steady_clock::now() - closing).count();
        std::printf("hardware: closing took %lld ms\n", closeMs);
        CHECK(closeMs < 1000);
    }
}

int main(int argc, char** argv)
{
    // Unbuffered, so what was printed survives a crash.
    std::setvbuf(stdout, nullptr, _IONBF, 0);

    if (argc > 1 && std::strcmp(argv[1], "--show-window") == 0)
    {
        return ShowTestWindow();
    }

    Exchange_PublishedFrame_IsTakenOnce();
    Exchange_NothingNew_IsNotTaken();
    Exchange_AFrameReplacedBeforeTaken_CountsAsOverwritten();
    Exchange_TheWriterNeverGetsTheReadersBuffer();
    FrameExchange_HandsOverAcrossThreads();
    RetryPolicy_GivesUpAfterFiveRetries();
    RetryPolicy_WaitsForTheSecureDesktop_OnlyAfterRunning();
    CopyRows_DropsThePadding_PutsTheBottomRowFirst_AndChecksThePitch();
    DrawPointer_BlendsAColorShape_AndClipsAtTheEdge();
    DrawPointer_AppliesMasks();
    Abi_ChecksItsArguments();
    Abi_ListsMonitors();
    Abi_AMonitorThatIsNotThere_IsLost();
    Window_ExecutableName_IsTheLowerCaseFileName();
    Window_LargestPerProgram_KeepsTheLargest_AndLeavesOutOwnAndSmallWindows();
    Window_ClientBox_IsTheClientAreaInTheImage_Clamped();
    Abi_ListsWindows();
    Abi_AProgramThatIsNotRunning_IsLost();

    if (argc > 1 && std::strcmp(argv[1], "--hardware") == 0)
    {
        Hardware_DuplicatesTheFirstMonitor();
        Hardware_CapturesAWindowsClientArea_AndLosesItWhenItCloses(false);
        Hardware_CapturesAWindowsClientArea_AndLosesItWhenItCloses(true);
    }

    std::printf("%s: %d failed checks\n", s_failures == 0 ? "passed" : "FAILED", s_failures);
    return s_failures == 0 ? 0 : 1;
}
