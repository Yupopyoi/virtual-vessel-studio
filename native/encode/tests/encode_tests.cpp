// Encodes synthetic frames to a temporary MP4 with each codec the system has a hardware encoder for, at
// the stream's size and rate, measures the encoder, and reads the file back to check its format: the
// codec, the H.264 profile, and the color tags. Then checks that an encoder closed without frames leaves
// no file, and that a bad configuration is refused.

#include "vv_encode.h"

#include "vv_audio_track.h"

#include <windows.h>

#include <codecapi.h>
#include <mfapi.h>
#include <mfidl.h>
#include <mfreadwrite.h>
#include <wrl/client.h>

#include <chrono>
#include <cmath>
#include <cstdio>
#include <string>
#include <thread>
#include <vector>

using Microsoft::WRL::ComPtr;

namespace
{
    constexpr int32_t Width = 1920;
    constexpr int32_t Height = 1080;
    constexpr int32_t Fps = 30;

    std::wstring TempPath(const wchar_t* name)
    {
        wchar_t folder[MAX_PATH];
        GetTempPathW(MAX_PATH, folder);
        return std::wstring(folder) + name;
    }

    // The file's video type as a player sees it: the subtype, the profile, the matrix, and the range.
    bool ReadFormat(const std::wstring& path, GUID& subtype, UINT32& profile, UINT32& matrix, UINT32& range)
    {
        ComPtr<IMFSourceReader> reader;
        if (FAILED(MFCreateSourceReaderFromURL(path.c_str(), nullptr, &reader)))
        {
            return false;
        }

        ComPtr<IMFMediaType> type;
        if (FAILED(reader->GetNativeMediaType(static_cast<DWORD>(MF_SOURCE_READER_FIRST_VIDEO_STREAM), 0, &type)))
        {
            return false;
        }

        type->GetGUID(MF_MT_SUBTYPE, &subtype);
        profile = MFGetAttributeUINT32(type.Get(), MF_MT_MPEG2_PROFILE, 0);
        matrix = MFGetAttributeUINT32(type.Get(), MF_MT_YUV_MATRIX, 0);
        range = MFGetAttributeUINT32(type.Get(), MF_MT_VIDEO_NOMINAL_RANGE, 0);
        return true;
    }

    // A stand-in for the audio engine's stream tap: a continuous 440 Hz tone timed on the QPC clock, which
    // began half a second before the recording, so the encoder must leave out what came before its first frame.
    struct ToneTap
    {
        int64_t anchor = 0;
        int64_t frequency = 1;
        int64_t read = 0;

        static int32_t Read(void* context, float* out, int32_t frames, int64_t until_qpc, int64_t* first_qpc)
        {
            auto* tap = static_cast<ToneTap*>(context);
            LARGE_INTEGER now;
            QueryPerformanceCounter(&now);
            int64_t until = until_qpc < now.QuadPart ? until_qpc : now.QuadPart;
            int64_t available = until < tap->anchor ? 0 : ((until - tap->anchor) * vv::encode::AudioRate) / tap->frequency + 1;
            int64_t count = available - tap->read;
            count = count < frames ? count : frames;
            if (count <= 0)
            {
                return 0;
            }

            *first_qpc = tap->anchor + ((tap->read * tap->frequency) / vv::encode::AudioRate);
            for (int64_t i = 0; i < count; i++)
            {
                float value = 0.5f * std::sin(static_cast<float>(tap->read + i) * 2.0f * 3.14159265f * 440.0f / vv::encode::AudioRate);
                out[i * 2] = value;
                out[(i * 2) + 1] = value;
            }

            tap->read += count;
            return static_cast<int32_t>(count);
        }
    };

    // The file's audio as a player reads it: the first sample's time and the total, in 100 ns units.
    bool ReadAudio(const std::wstring& path, GUID& subtype, int64_t& first, int64_t& end)
    {
        ComPtr<IMFSourceReader> reader;
        if (FAILED(MFCreateSourceReaderFromURL(path.c_str(), nullptr, &reader)))
        {
            return false;
        }

        ComPtr<IMFMediaType> type;
        if (FAILED(reader->GetNativeMediaType(static_cast<DWORD>(MF_SOURCE_READER_FIRST_AUDIO_STREAM), 0, &type)))
        {
            return false;
        }

        type->GetGUID(MF_MT_SUBTYPE, &subtype);
        first = -1;
        end = 0;
        for (;;)
        {
            DWORD flags = 0;
            LONGLONG time = 0;
            ComPtr<IMFSample> sample;
            if (FAILED(reader->ReadSample(static_cast<DWORD>(MF_SOURCE_READER_FIRST_AUDIO_STREAM), 0, nullptr, &flags, &time, &sample)) || (flags & MF_SOURCE_READERF_ENDOFSTREAM))
            {
                break;
            }

            if (sample)
            {
                LONGLONG duration = 0;
                sample->GetSampleDuration(&duration);
                first = first < 0 ? time : first;
                end = time + duration;
            }
        }

        return first >= 0;
    }

    // Three seconds of video with the tone: the file has an AAC track that starts with the video and lasts as long.
    bool EncodeWithAudio()
    {
        std::wstring path = TempPath(L"vv_encode_test_audio.mp4");
        DeleteFileW(path.c_str());
        LARGE_INTEGER frequency;
        LARGE_INTEGER now;
        QueryPerformanceFrequency(&frequency);
        QueryPerformanceCounter(&now);
        ToneTap tap;
        tap.frequency = frequency.QuadPart;
        tap.anchor = now.QuadPart - (frequency.QuadPart / 2);

        vv_encode_config config = { path.c_str(), Width, Height, Fps, 6'000'000, VV_ENCODE_CODEC_H264, 160'000, &ToneTap::Read, &tap };
        vv_encoder* encoder = nullptr;
        if (vv_encode_open(&config, &encoder) != VV_ENCODE_OK)
        {
            std::printf("FAIL audio: open\n");
            return false;
        }

        const int frames = 90;
        std::vector<uint8_t> frame(static_cast<size_t>(Width) * Height * 4, 128);
        auto start = std::chrono::steady_clock::now();
        for (int i = 0; i < frames; i++)
        {
            QueryPerformanceCounter(&now);
            vv_encode_push_video(encoder, frame.data(), static_cast<int32_t>(frame.size()), now.QuadPart);
            std::this_thread::sleep_until(start + std::chrono::microseconds((static_cast<int64_t>(i) + 1) * 1'000'000 / Fps));
        }

        vv_encode_stats stats = {};
        vv_encode_get_stats(encoder, &stats);
        int32_t closed = vv_encode_close(encoder);

        GUID subtype = {};
        int64_t first = 0;
        int64_t end = 0;
        bool read = ReadAudio(path, subtype, first, end);
        std::printf("audio: frames %lld, offset %lld us, first %.3f s, end %.3f s\n", static_cast<long long>(stats.audio_frames),
            static_cast<long long>(stats.av_offset_us), first / 1e7, end / 1e7);
        DeleteFileW(path.c_str());
        bool ok = closed == VV_ENCODE_OK && read && subtype == MFAudioFormat_AAC && first < 300'000 && std::llabs(end - 30'000'000) < 1'500'000 &&
            stats.audio_frames > vv::encode::AudioRate * 2;
        if (!ok)
        {
            std::printf("FAIL audio: the AAC track does not start with the video or last as long\n");
        }

        return ok;
    }

    // Video times on another base than the bus (as Unity's Stopwatch was, an hour off): the audio never
    // lines up, and the video must still be written at full rate, with silence keeping the audio near it.
    bool AudioOnAnotherClock(int64_t offsetSeconds)
    {
        std::wstring path = TempPath(L"vv_encode_test_clock.mp4");
        DeleteFileW(path.c_str());
        LARGE_INTEGER frequency;
        LARGE_INTEGER now;
        QueryPerformanceFrequency(&frequency);
        QueryPerformanceCounter(&now);
        ToneTap tap;
        tap.frequency = frequency.QuadPart;
        tap.anchor = now.QuadPart - (frequency.QuadPart / 2);
        const int64_t offset = offsetSeconds * frequency.QuadPart;

        vv_encode_config config = { path.c_str(), Width, Height, Fps, 6'000'000, VV_ENCODE_CODEC_H264, 160'000, &ToneTap::Read, &tap };
        vv_encoder* encoder = nullptr;
        if (vv_encode_open(&config, &encoder) != VV_ENCODE_OK)
        {
            std::printf("FAIL another clock: open\n");
            return false;
        }

        const int frames = 90;
        std::vector<uint8_t> frame(static_cast<size_t>(Width) * Height * 4, 128);
        auto start = std::chrono::steady_clock::now();
        for (int i = 0; i < frames; i++)
        {
            QueryPerformanceCounter(&now);
            vv_encode_push_video(encoder, frame.data(), static_cast<int32_t>(frame.size()), now.QuadPart + offset);
            std::this_thread::sleep_until(start + std::chrono::microseconds((static_cast<int64_t>(i) + 1) * 1'000'000 / Fps));
        }

        vv_encode_stats stats = {};
        vv_encode_get_stats(encoder, &stats);
        int32_t closed = vv_encode_close(encoder);
        std::printf("another clock (%+lld s): written %lld, dropped %lld, audio %lld, silence %lld\n", static_cast<long long>(offsetSeconds),
            static_cast<long long>(stats.written), static_cast<long long>(stats.dropped), static_cast<long long>(stats.audio_frames), static_cast<long long>(stats.audio_silence_frames));
        DeleteFileW(path.c_str());
        bool ok = closed == VV_ENCODE_OK && stats.dropped == 0 && stats.audio_frames <= vv::encode::AudioRate * 3 && stats.audio_frames >= vv::encode::AudioRate;
        if (!ok)
        {
            std::printf("FAIL: a bus on another clock held the video back or filled too much\n");
        }

        return ok;
    }

    bool AudioTiming()
    {
        vv::encode::Placement gap = vv::encode::Place(1000, 480, 0);
        vv::encode::Placement overlap = vv::encode::Place(-100, 480, 0);
        vv::encode::Placement before = vv::encode::Place(-1000, 480, 0);
        vv::encode::Placement follows = vv::encode::Place(30, 480, 0);
        bool ok = vv::encode::FrameAt(10'000'000, 0, 10'000'000) == 48000 && vv::encode::FrameAt(0, 10'000'000, 10'000'000) == -48000 &&
            vv::encode::FrameAt(104, 0, 10'000'000) == 0 && vv::encode::FrameAt(105, 0, 10'000'000) == 1 &&
            gap.silence == 1000 && gap.skip == 0 && overlap.skip == 100 && before.skip == 480 && follows.silence == 0 && follows.skip == 0 &&
            vv::encode::FileTimeToFrames(10'000'000) == 48000 && vv::encode::FramesToFileTime(48000) == 10'000'000 &&
            vv::encode::TicksToFileTime(10'000'000LL * 3600 * 24, 10'000'000) == 864'000'000'000LL && vv::encode::AacBytesPerSecond(160'000) == 20000 &&
            vv::encode::AacBytesPerSecond(150'000) == 20000 && vv::encode::AacBytesPerSecond(64'000) == 12000;
        float in[] = { 0.0f, 1.0f, -1.0f, 2.0f, -2.0f };
        int16_t out[5];
        vv::encode::ToPcm16(in, out, 5);
        ok = ok && out[0] == 0 && out[1] == 32767 && out[2] == -32767 && out[3] == 32767 && out[4] == -32768;
        if (!ok)
        {
            std::printf("FAIL: audio timing helpers\n");
        }

        return ok;
    }

    bool EncodeOne(int32_t codec, const wchar_t* name)
    {
        std::wstring path = TempPath(codec == VV_ENCODE_CODEC_AV1 ? L"vv_encode_test_av1.mp4" : L"vv_encode_test_h264.mp4");
        DeleteFileW(path.c_str());
        const int frames = 150;

        vv_encode_config config = { path.c_str(), Width, Height, Fps, 6'000'000, codec };
        vv_encoder* encoder = nullptr;
        int32_t opened = vv_encode_open(&config, &encoder);
        if (opened != VV_ENCODE_OK)
        {
            std::wprintf(L"FAIL %s: open returned %d\n", name, opened);
            return false;
        }

        std::vector<uint8_t> frame(static_cast<size_t>(Width) * Height * 4);
        auto start = std::chrono::steady_clock::now();
        for (int i = 0; i < frames; i++)
        {
            // A moving gradient, so the encoder has motion to code.
            for (int32_t y = 0; y < Height; y++)
            {
                uint8_t* row = frame.data() + (static_cast<size_t>(y) * Width * 4);
                for (int32_t x = 0; x < Width; x++)
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
            std::this_thread::sleep_until(start + std::chrono::microseconds((static_cast<int64_t>(i) + 1) * 1'000'000 / Fps));
        }

        vv_encode_stats stats = {};
        vv_encode_get_stats(encoder, &stats);
        int32_t closed = vv_encode_close(encoder);
        WIN32_FILE_ATTRIBUTE_DATA file = {};
        bool exists = GetFileAttributesExW(path.c_str(), GetFileExInfoStandard, &file) != 0;
        long long bytes = exists ? (static_cast<long long>(file.nFileSizeHigh) << 32) | file.nFileSizeLow : 0;
        std::wprintf(L"%s: pushed %lld, written %lld, dropped %lld, write avg %lld us, max %lld us, hardware %d, error 0x%08x, file %lld bytes\n",
            name, static_cast<long long>(stats.pushed), static_cast<long long>(stats.written), static_cast<long long>(stats.dropped),
            stats.written > 0 ? static_cast<long long>(stats.total_write_us / stats.written) : 0LL, static_cast<long long>(stats.max_write_us),
            stats.hardware, static_cast<unsigned>(stats.last_error), bytes);

        if (closed != VV_ENCODE_OK || !exists || bytes == 0 || stats.written + stats.dropped != frames)
        {
            std::wprintf(L"FAIL %s: the file was not written\n", name);
            return false;
        }

        GUID subtype = {};
        UINT32 profile = 0;
        UINT32 matrix = 0;
        UINT32 range = 0;
        if (!ReadFormat(path, subtype, profile, matrix, range))
        {
            std::wprintf(L"FAIL %s: the file cannot be read back\n", name);
            return false;
        }

        const GUID expected = codec == VV_ENCODE_CODEC_AV1 ? MFVideoFormat_AV1 : MFVideoFormat_H264;
        std::wprintf(L"%s: profile %u, matrix %u, range %u\n", name, profile, matrix, range);
        bool ok = subtype == expected && (codec != VV_ENCODE_CODEC_H264 || profile == eAVEncH264VProfile_High) && matrix == MFVideoTransferMatrix_BT709 &&
            range == MFNominalRange_16_235;
        if (!ok)
        {
            std::wprintf(L"FAIL %s: the file's format is not the one asked for\n", name);
        }

        DeleteFileW(path.c_str());
        return ok;
    }
}

int main()
{
    CoInitializeEx(nullptr, COINIT_MULTITHREADED);
    MFStartup(MF_VERSION);
    bool ok = vv_encode_api_version() == VV_ENCODE_API_VERSION;

    ok = AudioTiming() && ok;

    // H.264 always runs: without a hardware encoder the Sink Writer falls back to Microsoft's.
    ok = EncodeOne(VV_ENCODE_CODEC_H264, L"H.264") && ok;
    ok = EncodeWithAudio() && ok;
    ok = AudioOnAnotherClock(-3600) && ok;
    ok = AudioOnAnotherClock(3600) && ok;
    if (vv_encode_hardware_available(VV_ENCODE_CODEC_AV1))
    {
        ok = EncodeOne(VV_ENCODE_CODEC_AV1, L"AV1") && ok;
    }
    else
    {
        std::printf("SKIP AV1: no hardware AV1 encoder on this system\n");
    }

    // Closed without a frame: no unplayable file is left.
    std::wstring empty = TempPath(L"vv_encode_test_empty.mp4");
    vv_encode_config config = { empty.c_str(), Width, Height, Fps, 6'000'000, VV_ENCODE_CODEC_H264 };
    vv_encoder* encoder = nullptr;
    if (vv_encode_open(&config, &encoder) == VV_ENCODE_OK)
    {
        vv_encode_close(encoder);
        if (GetFileAttributesW(empty.c_str()) != INVALID_FILE_ATTRIBUTES)
        {
            std::printf("FAIL: an empty recording left its file\n");
            DeleteFileW(empty.c_str());
            ok = false;
        }
    }

    vv_encode_config odd = { empty.c_str(), Width + 1, Height, Fps, 6'000'000, VV_ENCODE_CODEC_H264 };
    vv_encode_config unknown = { empty.c_str(), Width, Height, Fps, 6'000'000, 7 };
    if (vv_encode_open(&odd, &encoder) != VV_ENCODE_INVALID_ARGUMENT || vv_encode_open(&unknown, &encoder) != VV_ENCODE_INVALID_ARGUMENT ||
        vv_encode_hardware_available(7) != 0)
    {
        std::printf("FAIL: a bad configuration was accepted\n");
        ok = false;
    }

    MFShutdown();
    CoUninitialize();
    std::printf(ok ? "PASS\n" : "FAIL\n");
    return ok ? 0 : 1;
}
