// The stream's Media Foundation encoders (streaming detailed design 8.1): every frame given to them comes back out,
// those they hold at the end by draining, so a stream does not lose its last frames when it is closed.

#include "mft_encoders.h"

#include <windows.h>

#include <mfapi.h>

#include <cstdio>
#include <cstring>
#include <string>
#include <vector>

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

    void VideoComesOut(VideoCodec codec, const char* name)
    {
        VideoEncoderConfig config;
        config.codec = codec;
        config.width = 640;
        config.height = 360;
        config.fps = 30;
        config.bitrate = 2'000'000;
        VideoEncoder encoder;
        std::string error;
        if (!encoder.Open(config, error))
        {
            std::printf("SKIP %s: %s\n", name, error.c_str());
            return;
        }

        constexpr int Frames = 30;
        std::vector<uint8_t> nv12(640 * 360 * 3 / 2, 128);
        std::vector<EncodedFrame> out;
        bool ok = true;
        for (int i = 0; i < Frames && ok; i++)
        {
            std::memset(nv12.data(), (i * 8) & 0xFF, 640 * 360);
            ok = encoder.Encode(nv12.data(), nv12.size(), i * 10'000'000LL / 30, i == 0, out, error);
        }

        Check(ok, "encoding the frames");
        size_t beforeDrain = out.size();
        Check(encoder.Drain(1000, out, error), "draining the video encoder");
        std::printf("%s (%s): %zu of %d frames before draining, %zu after\n", name, encoder.Hardware() ? "hardware" : "software", beforeDrain, Frames, out.size());
        Check(out.size() == Frames, "every frame came out");
        Check(!out.empty() && out.back().time == (Frames - 1) * 10'000'000LL / 30, "the last frame came out last");
        Check(encoder.Drain(1000, out, error) && out.size() == Frames, "draining again gives nothing more");
    }

    void AudioComesOut()
    {
        AacEncoder encoder;
        std::string error;
        Check(encoder.Open(160'000, error), "opening the AAC encoder");

        // 10 000 samples: 9 full AAC frames of 1024 and part of a tenth.
        std::vector<int16_t> pcm(10'000 * 2, 0);
        std::vector<EncodedFrame> out;
        Check(encoder.Encode(pcm.data(), 10'000, 0, out, error), "encoding the audio");
        size_t beforeDrain = out.size();
        Check(encoder.Drain(out, error), "draining the audio encoder");
        std::printf("AAC: %zu frames before draining, %zu after\n", beforeDrain, out.size());
        Check(out.size() >= 10, "the part of the last AAC frame came out");
    }
}

int main()
{
    CoInitializeEx(nullptr, COINIT_MULTITHREADED);
    MFStartup(MF_VERSION);
    VideoComesOut(VideoCodec::H264, "H.264");
    VideoComesOut(VideoCodec::Av1, "AV1");
    AudioComesOut();
    MFShutdown();
    CoUninitialize();
    std::printf(s_failures == 0 ? "PASS\n" : "FAIL (%d)\n", s_failures);
    return s_failures == 0 ? 0 : 1;
}
