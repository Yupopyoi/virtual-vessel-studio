// The plugin end to end on this PC (streaming detailed design 14): synthetic NV12 video and a tone, encoded
// and published to a local ffmpeg in listen mode that writes what it receives to an FLV file, which ffprobe
// then reads back. Skipped when ffmpeg is not on the PATH.

#include "vv_stream.h"

#include <windows.h>

#include <cmath>
#include <cstdio>
#include <string>
#include <vector>

namespace
{
    int s_failures = 0;

    void Check(bool condition, const char* what)
    {
        if (!condition)
        {
            std::printf("FAIL: %s\n", what);
            s_failures++;
        }
    }

    int64_t Qpc()
    {
        LARGE_INTEGER now;
        QueryPerformanceCounter(&now);
        return now.QuadPart;
    }

    int64_t QpcFrequency()
    {
        LARGE_INTEGER frequency;
        QueryPerformanceFrequency(&frequency);
        return frequency.QuadPart;
    }

    // A 440 Hz tone that has "played" since start, handed out as the audio engine's stream tap would.
    struct Tone
    {
        int64_t startQpc = 0;
        int64_t frequency = 1;
        int64_t position = 0;

        static int32_t Read(void* context, float* out, int32_t frames, int64_t untilQpc, int64_t* firstQpc)
        {
            auto tone = static_cast<Tone*>(context);
            int64_t available = (untilQpc - tone->startQpc) * 48'000 / tone->frequency - tone->position;
            int32_t count = static_cast<int32_t>(available < frames ? (available > 0 ? available : 0) : frames);
            *firstQpc = tone->startQpc + tone->position * tone->frequency / 48'000;
            for (int32_t i = 0; i < count; i++)
            {
                float sample = 0.25f * std::sin(2.0f * 3.14159265f * 440.0f * static_cast<float>(tone->position + i) / 48'000.0f);
                out[i * 2] = sample;
                out[i * 2 + 1] = sample;
            }

            tone->position += count;
            return count;
        }
    };

    HANDLE Run(const std::string& command)
    {
        std::string line = command;
        STARTUPINFOA startup = { sizeof(startup) };
        PROCESS_INFORMATION process = {};
        if (!CreateProcessA(nullptr, line.data(), nullptr, nullptr, FALSE, CREATE_NO_WINDOW, nullptr, nullptr, &startup, &process))
        {
            return nullptr;
        }

        CloseHandle(process.hThread);
        return process.hProcess;
    }

    std::string Capture(const std::string& command)
    {
        std::string output;
        FILE* pipe = _popen(command.c_str(), "r");
        if (pipe == nullptr)
        {
            return output;
        }

        char buffer[256];
        while (std::fgets(buffer, sizeof(buffer), pipe) != nullptr)
        {
            output += buffer;
        }

        _pclose(pipe);
        return output;
    }

    // Streams three seconds at 30 fps; false when the codec has no encoder on this PC.
    bool StreamTo(int32_t codec, const char* name, const std::string& file)
    {
        DeleteFileA(file.c_str());
        HANDLE listener = Run("ffmpeg -hide_banner -loglevel error -y -listen 1 -timeout 20 -i rtmp://127.0.0.1:19353/live/test-key -c copy -f flv \"" + file + "\"");
        Sleep(1500); // Until it listens.

        Tone tone;
        tone.startQpc = Qpc();
        tone.frequency = QpcFrequency();
        vv_stream_config config = {};
        config.url = "rtmp://127.0.0.1:19353/live";
        config.key = "test-key";
        config.width = 640;
        config.height = 360;
        config.fps = 30;
        config.codec = codec;
        config.video_bitrate = 2'000'000;
        config.audio_bitrate = 160'000;
        config.keyframe_seconds = 2;
        config.audio_read = &Tone::Read;
        config.audio_context = &tone;
        vv_stream* stream = nullptr;
        Check(vv_stream_open(&config, &stream) == VV_STREAM_OK, "opening a stream");

        std::vector<uint8_t> frame(640 * 360 * 3 / 2);
        vv_stream_stats stats = {};
        for (int i = 0; i < 90; i++)
        {
            for (size_t y = 0; y < 360; y++)
            {
                std::memset(frame.data() + y * 640, static_cast<int>((y + static_cast<size_t>(i) * 4) & 0xFF), 640);
            }

            std::memset(frame.data() + 640 * 360, 128, 640 * 360 / 2);
            vv_stream_push_video(stream, frame.data(), static_cast<int32_t>(frame.size()), Qpc());
            Sleep(33);
        }

        vv_stream_get_stats(stream, &stats);
        std::printf("%s: state %d problem %d hardware %d pushed %lld sent %lld dropped %lld bytes %lld queue %lld ms, av offset %lld us, encode %.2f ms/frame\n",
            name, stats.state, stats.problem, stats.hardware, stats.frames_pushed, stats.frames_sent, stats.frames_dropped, stats.bytes_sent, stats.queue_ms,
            stats.av_offset_us, stats.frames_sent > 0 ? static_cast<double>(stats.encode_us_total) / 1000.0 / static_cast<double>(stats.frames_sent) : 0.0);
        bool encoderMissing = stats.state == VV_STREAM_STATE_FAILED && stats.problem == VV_STREAM_PROBLEM_ENCODER_FAILED;
        vv_stream_close(stream);
        WaitForSingleObject(listener, 10'000);
        TerminateProcess(listener, 0);
        CloseHandle(listener);
        if (encoderMissing && codec == VV_STREAM_CODEC_AV1)
        {
            std::printf("SKIP %s: no encoder on this PC\n", name);
            return false;
        }

        Check(stats.state == VV_STREAM_STATE_LIVE && stats.problem == VV_STREAM_PROBLEM_NONE, "the stream is live");
        Check(stats.bytes_sent > 50'000, "the stream sent its frames");

        std::string probe = Capture("ffprobe -v error -count_packets -show_entries stream=codec_name,width,height,nb_read_packets -of csv=p=0 \"" + file + "\"");
        std::printf("%s ffprobe:\n%s", name, probe.c_str());
        Check(probe.find(codec == VV_STREAM_CODEC_AV1 ? "av1" : "h264") != std::string::npos, "the server received the video codec");
        Check(probe.find("640,360") != std::string::npos, "the server received the frame size");
        Check(probe.find("aac") != std::string::npos, "the server received AAC audio");

        // 90 frames, and AAC for as long as they took to push (Sleep rounds up to the timer, so about four seconds).
        int videoPackets = 0;
        int audioPackets = 0;
        for (size_t at = 0; at < probe.size();)
        {
            size_t end = probe.find('\n', at);
            std::string line = probe.substr(at, end == std::string::npos ? std::string::npos : end - at);
            size_t comma = line.rfind(',');
            int packets = comma == std::string::npos ? 0 : std::atoi(line.c_str() + comma + 1);
            (line.rfind("aac", 0) == 0 ? audioPackets : videoPackets) += packets;
            at = end == std::string::npos ? probe.size() : end + 1;
        }

        std::printf("%s: %d video packets, %d audio packets\n", name, videoPackets, audioPackets);
        Check(videoPackets >= 60, "most video frames arrived");
        Check(audioPackets >= 90, "most audio arrived");
        return true;
    }

    void Arguments()
    {
        vv_stream_config config = {};
        config.url = "http://example.com/live";
        config.key = "k";
        config.width = 640;
        config.height = 360;
        config.fps = 30;
        config.video_bitrate = 2'000'000;
        vv_stream* stream = nullptr;
        Check(vv_stream_open(&config, &stream) == VV_STREAM_INVALID_ARGUMENT, "a URL that is not RTMP is refused");
        config.url = "rtmp://127.0.0.1/live";
        config.key = "";
        Check(vv_stream_open(&config, &stream) == VV_STREAM_INVALID_ARGUMENT, "an empty key is refused");
        config.key = "k";
        config.width = 641;
        Check(vv_stream_open(&config, &stream) == VV_STREAM_INVALID_ARGUMENT, "an odd width is refused");
        Check(vv_stream_test_connection("rtmp://127.0.0.1:19354/live", 2000) == VV_STREAM_PROBLEM_UNREACHABLE, "no server is unreachable");
    }

    void Unreachable()
    {
        vv_stream_config config = {};
        config.url = "rtmp://127.0.0.1:19354/live";
        config.key = "test-key";
        config.width = 320;
        config.height = 180;
        config.fps = 30;
        config.video_bitrate = 1'000'000;
        vv_stream* stream = nullptr;
        Check(vv_stream_open(&config, &stream) == VV_STREAM_OK, "opening a stream to nobody");
        vv_stream_stats stats = {};
        for (int i = 0; i < 50; i++)
        {
            vv_stream_get_stats(stream, &stats);
            if (stats.state == VV_STREAM_STATE_FAILED)
            {
                break;
            }

            Sleep(100);
        }

        Check(stats.state == VV_STREAM_STATE_FAILED && stats.problem == VV_STREAM_PROBLEM_UNREACHABLE, "a first connection that fails is reported as unreachable");
        vv_stream_close(stream);
    }
}

int main()
{
    Check(vv_stream_api_version() == VV_STREAM_API_VERSION, "the API version");
    Arguments();
    Unreachable();

    char temp[MAX_PATH];
    GetTempPathA(MAX_PATH, temp);
    if (Capture("ffmpeg -hide_banner -version").find("ffmpeg") == std::string::npos)
    {
        std::printf("SKIP streaming: ffmpeg is not on the PATH\n");
    }
    else
    {
        StreamTo(VV_STREAM_CODEC_H264, "H.264", std::string(temp) + "vv_stream_test_h264.flv");
        StreamTo(VV_STREAM_CODEC_AV1, "AV1", std::string(temp) + "vv_stream_test_av1.flv");
    }

    std::printf(s_failures == 0 ? "PASS\n" : "FAIL (%d)\n", s_failures);
    return s_failures == 0 ? 0 : 1;
}
