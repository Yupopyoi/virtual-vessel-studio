// The connection on this PC (streaming detailed design 14): publishing to a local RTMP server (ffmpeg in
// listen mode, when it is on the PATH), and TLS with RTMP connect to YouTube's RTMPS ingest (no publish, so no
// stream starts; skipped without a network).

#include "rtmp_client.h"

#include <windows.h>

#include <cstdio>
#include <string>

using namespace vv::stream;

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

    // ffmpeg receiving one stream on 127.0.0.1 and throwing it away; null when ffmpeg is not on the PATH.
    HANDLE StartListener(const char* url)
    {
        std::string command = std::string("ffmpeg -hide_banner -loglevel error -listen 1 -timeout 20 -i ") + url + " -f null -";
        STARTUPINFOA startup = { sizeof(startup) };
        PROCESS_INFORMATION process = {};
        if (!CreateProcessA(nullptr, command.data(), nullptr, nullptr, FALSE, CREATE_NO_WINDOW, nullptr, nullptr, &startup, &process))
        {
            return nullptr;
        }

        CloseHandle(process.hThread);
        Sleep(1500); // Until it listens.
        return process.hProcess;
    }

    void LocalPublish()
    {
        HANDLE listener = StartListener("rtmp://127.0.0.1:19351/live/test-key");
        if (listener == nullptr)
        {
            std::printf("SKIP local publish: ffmpeg is not on the PATH\n");
            return;
        }

        RtmpUrl url;
        ParseRtmpUrl("rtmp://127.0.0.1:19351/live", url);
        RtmpClient client(CreateTransport(false));
        std::string error;
        PublishFailure failure = client.Publish(url, "test-key", false, 5000, error);
        std::printf("local publish: %d %s\n", static_cast<int>(failure), error.c_str());
        Check(failure == PublishFailure::None, "publishing to a local RTMP server");
        Check(error.find("test-key") == std::string::npos, "the key never appears in an error");
        client.Close("test-key");
        WaitForSingleObject(listener, 5000);
        TerminateProcess(listener, 0);
        CloseHandle(listener);

        RtmpClient nobody(CreateTransport(false));
        ParseRtmpUrl("rtmp://127.0.0.1:19352/live", url);
        failure = nobody.Publish(url, "secret-key", false, 2000, error);
        Check(failure == PublishFailure::Unreachable && error.find("secret-key") == std::string::npos, "no server is unreachable, and says so without the key");
    }

    void YouTubeTls()
    {
        RtmpUrl url;
        ParseRtmpUrl("rtmps://a.rtmps.youtube.com/live2", url);
        RtmpClient client(CreateTransport(true));
        std::string error;
        PublishFailure failure = client.TestConnect(url, 8000, error);
        if (failure == PublishFailure::Unreachable && error.find("name resolution") != std::string::npos)
        {
            std::printf("SKIP YouTube: no network (%s)\n", error.c_str());
            return;
        }

        std::printf("YouTube RTMPS connect: %d %s\n", static_cast<int>(failure), error.c_str());
        Check(failure == PublishFailure::None, "TLS and RTMP connect to YouTube's RTMPS ingest");
    }
}

int main()
{
    LocalPublish();
    YouTubeTls();
    std::printf(s_failures == 0 ? "PASS\n" : "FAIL (%d)\n", s_failures);
    return s_failures == 0 ? 0 : 1;
}
