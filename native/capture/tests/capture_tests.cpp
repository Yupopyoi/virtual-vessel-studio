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

    void CopyRows_DropsThePadding_AndChecksThePitch()
    {
        // Two rows of two pixels, each row padded to 12 bytes.
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
        CHECK(target.pixels[7] == 7 && target.pixels[8] == 100 && target.pixels[15] == 107);

        CHECK(!CopyRows(target, source.data(), 4, 2, 2));
        CHECK(!CopyRows(target, nullptr, 12, 2, 2));
        CHECK(!CopyRows(target, source.data(), 12, 0, 2));
    }

    void Abi_ChecksItsArguments()
    {
        CHECK(vv_capture_api_version() == VV_CAPTURE_API_VERSION);
        CHECK(vv_capture_list_monitors(nullptr, 1) == VV_CAPTURE_INVALID_ARGUMENT);
        CHECK(vv_capture_list_monitors(nullptr, -1) == VV_CAPTURE_INVALID_ARGUMENT);

        vv_capture_session* session = reinterpret_cast<vv_capture_session*>(1);
        CHECK(vv_capture_open(nullptr, &session) == VV_CAPTURE_INVALID_ARGUMENT);
        CHECK(session == nullptr);
        CHECK(vv_capture_open(L"", &session) == VV_CAPTURE_INVALID_ARGUMENT);
        CHECK(vv_capture_open(L"\\\\.\\DISPLAY1-but-much-longer-than-32", &session) == VV_CAPTURE_INVALID_ARGUMENT);
        CHECK(vv_capture_open(L"\\\\.\\DISPLAY1", nullptr) == VV_CAPTURE_INVALID_ARGUMENT);

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
        CHECK(vv_capture_open(L"\\\\.\\NO-SUCH-DISPLAY", &session) == VV_CAPTURE_OK);
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
        CHECK(vv_capture_open(monitor.device_name, &session) == VV_CAPTURE_OK);

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
    CopyRows_DropsThePadding_AndChecksThePitch();
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
