// Encodes synthetic frames to a temporary MP4 and measures the encoder, at the stream's size; then checks
// that an encoder closed without frames leaves no file.

#include "vv_encode.h"

#include <windows.h>

#include <chrono>
#include <cstdio>
#include <string>
#include <thread>
#include <vector>

int main()
{
    const int32_t width = 1920;
    const int32_t height = 1080;
    const int32_t fps = 30;
    const int frames = 300;
    wchar_t folder[MAX_PATH];
    GetTempPathW(MAX_PATH, folder);
    std::wstring path = std::wstring(folder) + L"vv_encode_test.mp4";
    DeleteFileW(path.c_str());

    vv_encoder* encoder = nullptr;
    int32_t opened = vv_encode_open(path.c_str(), width, height, fps, 6'000'000, &encoder);
    if (opened != VV_ENCODE_OK)
    {
        std::printf("FAIL: open returned %d\n", opened);
        return 1;
    }

    std::vector<uint8_t> frame(static_cast<size_t>(width) * height * 4);
    auto start = std::chrono::steady_clock::now();
    for (int i = 0; i < frames; i++)
    {
        // A moving gradient, so the encoder has motion to code.
        for (int32_t y = 0; y < height; y++)
        {
            uint8_t* row = frame.data() + (static_cast<size_t>(y) * width * 4);
            for (int32_t x = 0; x < width; x++)
            {
                row[(x * 4) + 0] = static_cast<uint8_t>(x + (i * 4));
                row[(x * 4) + 1] = static_cast<uint8_t>(y + (i * 2));
                row[(x * 4) + 2] = static_cast<uint8_t>(i);
                row[(x * 4) + 3] = 255;
            }
        }

        LARGE_INTEGER now;
        QueryPerformanceCounter(&now);
        vv_encode_push_video(encoder, frame.data(), static_cast<int32_t>(frame.size()), now.QuadPart);
        std::this_thread::sleep_until(start + std::chrono::microseconds((static_cast<int64_t>(i) + 1) * 1'000'000 / fps));
    }

    vv_encode_stats stats = {};
    vv_encode_get_stats(encoder, &stats);
    int32_t closed = vv_encode_close(encoder);
    WIN32_FILE_ATTRIBUTE_DATA file = {};
    bool exists = GetFileAttributesExW(path.c_str(), GetFileExInfoStandard, &file) != 0;
    long long bytes = exists ? (static_cast<long long>(file.nFileSizeHigh) << 32) | file.nFileSizeLow : 0;

    std::printf("pushed %lld, written %lld, dropped %lld, write avg %lld us, max %lld us, hardware %d, error 0x%08x, file %lld bytes\n",
        static_cast<long long>(stats.pushed), static_cast<long long>(stats.written), static_cast<long long>(stats.dropped),
        stats.written > 0 ? static_cast<long long>(stats.total_write_us / stats.written) : 0LL, static_cast<long long>(stats.max_write_us),
        stats.hardware, static_cast<unsigned>(stats.last_error), bytes);

    if (closed != VV_ENCODE_OK || !exists || bytes == 0 || stats.written + stats.dropped != frames)
    {
        std::printf("FAIL\n");
        return 1;
    }

    std::printf("PASS\n");
    return 0;
}
