// Tests of the capture plugin. The exchange, the retry policy, and the row copy are pure and run
// anywhere; the C ABI checks need only DXGI. `--hardware` also duplicates a real monitor for a
// moment (nothing is kept): run it by hand on a PC with a display.

#include <windows.h>

#include <chrono>
#include <cstdio>
#include <cstring>
#include <set>
#include <string>
#include <thread>
#include <vector>

#include "desktop_duplication.h"
#include "frame_exchange.h"
#include "retry_policy.h"
#include "vv_capture.h"

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

    if (argc > 1 && std::strcmp(argv[1], "--hardware") == 0)
    {
        Hardware_DuplicatesTheFirstMonitor();
    }

    std::printf("%s: %d failed checks\n", s_failures == 0 ? "passed" : "FAILED", s_failures);
    return s_failures == 0 ? 0 : 1;
}
