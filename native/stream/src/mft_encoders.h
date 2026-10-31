// The stream's encoders (streaming detailed design 8.1): Media Foundation transforms used directly, since a
// stream needs each compressed frame as it comes, not a file. Video takes NV12 and gives H.264 (Annex B) or
// AV1 (OBUs) with the hardware encoder first; audio takes 16-bit PCM and gives raw AAC frames.
#pragma once

#include <windows.h>

#include <mfidl.h>
#include <mftransform.h>
#include <wrl/client.h>

#include <cstdint>
#include <string>
#include <vector>

namespace vv::stream
{
    struct EncodedFrame
    {
        std::vector<uint8_t> data;
        int64_t time = 0; // 100 ns units from the stream's start.
        bool keyframe = false;
    };

    enum class VideoCodec
    {
        H264,
        Av1,
    };

    struct VideoEncoderConfig
    {
        VideoCodec codec = VideoCodec::H264;
        int width = 0;
        int height = 0;
        int fps = 30;
        int bitrate = 6'000'000;
        int keyframeSeconds = 2;
    };

    class VideoEncoder
    {
    public:
        ~VideoEncoder();

        // Finds an encoder for the codec: hardware first, then (for H.264) Microsoft's software encoder.
        bool Open(const VideoEncoderConfig& config, std::string& error);

        // Encodes one NV12 frame (rows top to bottom) and appends what the encoder gave back.
        bool Encode(const uint8_t* nv12, size_t length, int64_t time, bool forceKeyframe, std::vector<EncodedFrame>& out, std::string& error);

        bool Hardware() const
        {
            return _hardware;
        }

    private:
        bool Configure(std::string& error);
        bool Feed(const uint8_t* nv12, size_t length, int64_t time, bool forceKeyframe, std::string& error);
        bool TakeOutput(std::vector<EncodedFrame>& out, std::string& error);

        VideoEncoderConfig _config;
        Microsoft::WRL::ComPtr<IMFTransform> _transform;
        Microsoft::WRL::ComPtr<IMFMediaEventGenerator> _events; // Only for asynchronous (hardware) encoders.
        DWORD _inputStream = 0;
        DWORD _outputStream = 0;
        bool _hardware = false;
        bool _providesSamples = false;
        DWORD _outputSize = 0;
        int _inputCredits = 0; // Asynchronous: inputs the encoder asked for and has not been given.
        bool _started = false;
    };

    class AacEncoder
    {
    public:
        ~AacEncoder();

        bool Open(int bitrate, std::string& error);

        // Encodes 48 kHz stereo 16-bit PCM and appends the AAC frames it completes (1024 samples each).
        bool Encode(const int16_t* pcm, size_t frames, int64_t time, std::vector<EncodedFrame>& out, std::string& error);

    private:
        Microsoft::WRL::ComPtr<IMFTransform> _transform;
        DWORD _outputSize = 0;
    };
}
