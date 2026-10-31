#include "mft_encoders.h"

#include "vv_audio_track.h"

#include <codecapi.h>
#include <mfapi.h>
#include <mferror.h>
#include <strmif.h>

#include <cstring>

using Microsoft::WRL::ComPtr;

namespace vv::stream
{
    namespace
    {
        std::string HrError(const char* what, HRESULT hr)
        {
            char code[16];
            snprintf(code, sizeof(code), "0x%08lX", static_cast<unsigned long>(hr));
            return std::string(what) + " failed (" + code + ")";
        }

        // The first transform of the category that turns input into output, with the flags given.
        ComPtr<IMFTransform> FindTransform(const GUID& category, UINT32 flags, const MFT_REGISTER_TYPE_INFO& input, const MFT_REGISTER_TYPE_INFO& output)
        {
            IMFActivate** activates = nullptr;
            UINT32 count = 0;
            ComPtr<IMFTransform> transform;
            if (SUCCEEDED(MFTEnumEx(category, flags | MFT_ENUM_FLAG_SORTANDFILTER, &input, &output, &activates, &count)))
            {
                for (UINT32 i = 0; i < count && !transform; i++)
                {
                    activates[i]->ActivateObject(IID_PPV_ARGS(&transform));
                }

                for (UINT32 i = 0; i < count; i++)
                {
                    activates[i]->Release();
                }

                CoTaskMemFree(activates);
            }

            return transform;
        }

        void SetCodecValue(ICodecAPI* codec, const GUID& key, UINT32 value)
        {
            VARIANT variant;
            VariantInit(&variant);
            variant.vt = VT_UI4;
            variant.ulVal = value;
            codec->SetValue(&key, &variant);
        }

        ComPtr<IMFSample> MakeSample(const uint8_t* data, size_t length, int64_t time, int64_t duration)
        {
            ComPtr<IMFMediaBuffer> buffer;
            ComPtr<IMFSample> sample;
            BYTE* target = nullptr;
            if (FAILED(MFCreateMemoryBuffer(static_cast<DWORD>(length), &buffer)) || FAILED(buffer->Lock(&target, nullptr, nullptr)))
            {
                return nullptr;
            }

            std::memcpy(target, data, length);
            buffer->Unlock();
            buffer->SetCurrentLength(static_cast<DWORD>(length));
            if (FAILED(MFCreateSample(&sample)))
            {
                return nullptr;
            }

            sample->AddBuffer(buffer.Get());
            sample->SetSampleTime(time);
            sample->SetSampleDuration(duration);
            return sample;
        }

        // ProcessOutput into one frame; S_OK with a frame, or the transform's own code (such as needing more input).
        HRESULT ProcessOne(IMFTransform* transform, DWORD stream, bool providesSamples, DWORD size, EncodedFrame& frame)
        {
            MFT_OUTPUT_DATA_BUFFER output = {};
            output.dwStreamID = stream;
            ComPtr<IMFSample> own;
            if (!providesSamples)
            {
                ComPtr<IMFMediaBuffer> buffer;
                if (FAILED(MFCreateMemoryBuffer(size > 0 ? size : 1 << 20, &buffer)) || FAILED(MFCreateSample(&own)))
                {
                    return E_OUTOFMEMORY;
                }

                own->AddBuffer(buffer.Get());
                output.pSample = own.Get();
            }

            DWORD status = 0;
            HRESULT hr = transform->ProcessOutput(0, 1, &output, &status);
            if (output.pEvents != nullptr)
            {
                output.pEvents->Release();
            }

            ComPtr<IMFSample> sample;
            sample.Attach(providesSamples ? output.pSample : nullptr);
            IMFSample* result = providesSamples ? sample.Get() : own.Get();
            if (FAILED(hr) || result == nullptr)
            {
                return hr;
            }

            ComPtr<IMFMediaBuffer> contiguous;
            BYTE* data = nullptr;
            DWORD length = 0;
            if (FAILED(result->ConvertToContiguousBuffer(&contiguous)) || FAILED(contiguous->Lock(&data, nullptr, &length)))
            {
                return E_FAIL;
            }

            frame.data.assign(data, data + length);
            contiguous->Unlock();
            LONGLONG time = 0;
            result->GetSampleTime(&time);
            frame.time = time;
            frame.keyframe = MFGetAttributeUINT32(result, MFSampleExtension_CleanPoint, 0) != 0;
            return S_OK;
        }
    }

    VideoEncoder::~VideoEncoder()
    {
        if (_transform && _started)
        {
            _transform->ProcessMessage(MFT_MESSAGE_NOTIFY_END_OF_STREAM, 0);
        }
    }

    bool VideoEncoder::Open(const VideoEncoderConfig& config, std::string& error)
    {
        _config = config;
        MFT_REGISTER_TYPE_INFO input = { MFMediaType_Video, MFVideoFormat_NV12 };
        MFT_REGISTER_TYPE_INFO output = { MFMediaType_Video, config.codec == VideoCodec::Av1 ? MFVideoFormat_AV1 : MFVideoFormat_H264 };
        _transform = FindTransform(MFT_CATEGORY_VIDEO_ENCODER, MFT_ENUM_FLAG_HARDWARE, input, output);
        _hardware = static_cast<bool>(_transform);
        if (!_transform && config.codec == VideoCodec::H264)
        {
            _transform = FindTransform(MFT_CATEGORY_VIDEO_ENCODER, MFT_ENUM_FLAG_SYNCMFT, input, output);
        }

        if (!_transform)
        {
            error = config.codec == VideoCodec::Av1 ? "no AV1 hardware encoder" : "no H.264 encoder";
            return false;
        }

        // Hardware encoders are asynchronous: unlocked, then driven by their events.
        ComPtr<IMFAttributes> attributes;
        if (SUCCEEDED(_transform->GetAttributes(&attributes)) && MFGetAttributeUINT32(attributes.Get(), MF_TRANSFORM_ASYNC, FALSE))
        {
            attributes->SetUINT32(MF_TRANSFORM_ASYNC_UNLOCK, TRUE);
            _transform.As(&_events);
        }

        return Configure(error);
    }

    bool VideoEncoder::Configure(std::string& error)
    {
        DWORD inputs = 0;
        DWORD outputs = 0;
        _transform->GetStreamCount(&inputs, &outputs);
        if (_transform->GetStreamIDs(1, &_inputStream, 1, &_outputStream) == E_NOTIMPL)
        {
            _inputStream = 0;
            _outputStream = 0;
        }

        // Constant bitrate, a keyframe every few seconds, and no B-frames: RTMP sends pictures in order (8.3).
        ComPtr<ICodecAPI> codec;
        if (SUCCEEDED(_transform.As(&codec)))
        {
            SetCodecValue(codec.Get(), CODECAPI_AVEncCommonRateControlMode, eAVEncCommonRateControlMode_CBR);
            SetCodecValue(codec.Get(), CODECAPI_AVEncCommonMeanBitRate, static_cast<UINT32>(_config.bitrate));
            SetCodecValue(codec.Get(), CODECAPI_AVEncMPVGOPSize, static_cast<UINT32>(_config.fps * _config.keyframeSeconds));
            SetCodecValue(codec.Get(), CODECAPI_AVEncMPVDefaultBPictureCount, 0);
            SetCodecValue(codec.Get(), CODECAPI_AVLowLatencyMode, TRUE);
        }

        ComPtr<IMFMediaType> output;
        HRESULT hr = MFCreateMediaType(&output);
        if (SUCCEEDED(hr))
        {
            output->SetGUID(MF_MT_MAJOR_TYPE, MFMediaType_Video);
            output->SetGUID(MF_MT_SUBTYPE, _config.codec == VideoCodec::Av1 ? MFVideoFormat_AV1 : MFVideoFormat_H264);
            output->SetUINT32(MF_MT_AVG_BITRATE, static_cast<UINT32>(_config.bitrate));
            output->SetUINT32(MF_MT_INTERLACE_MODE, MFVideoInterlace_Progressive);
            if (_config.codec == VideoCodec::H264)
            {
                output->SetUINT32(MF_MT_MPEG2_PROFILE, eAVEncH264VProfile_High);
            }

            output->SetUINT32(MF_MT_YUV_MATRIX, MFVideoTransferMatrix_BT709);
            output->SetUINT32(MF_MT_VIDEO_PRIMARIES, MFVideoPrimaries_BT709);
            output->SetUINT32(MF_MT_TRANSFER_FUNCTION, MFVideoTransFunc_709);
            output->SetUINT32(MF_MT_VIDEO_NOMINAL_RANGE, MFNominalRange_16_235);
            MFSetAttributeSize(output.Get(), MF_MT_FRAME_SIZE, static_cast<UINT32>(_config.width), static_cast<UINT32>(_config.height));
            MFSetAttributeRatio(output.Get(), MF_MT_FRAME_RATE, static_cast<UINT32>(_config.fps), 1);
            MFSetAttributeRatio(output.Get(), MF_MT_PIXEL_ASPECT_RATIO, 1, 1);
            hr = _transform->SetOutputType(_outputStream, output.Get(), 0);
        }

        if (FAILED(hr))
        {
            error = HrError("the video encoder's output type", hr);
            return false;
        }

        ComPtr<IMFMediaType> input;
        hr = MFCreateMediaType(&input);
        if (SUCCEEDED(hr))
        {
            input->SetGUID(MF_MT_MAJOR_TYPE, MFMediaType_Video);
            input->SetGUID(MF_MT_SUBTYPE, MFVideoFormat_NV12);
            input->SetUINT32(MF_MT_INTERLACE_MODE, MFVideoInterlace_Progressive);
            input->SetUINT32(MF_MT_VIDEO_NOMINAL_RANGE, MFNominalRange_16_235);
            input->SetUINT32(MF_MT_YUV_MATRIX, MFVideoTransferMatrix_BT709);
            MFSetAttributeSize(input.Get(), MF_MT_FRAME_SIZE, static_cast<UINT32>(_config.width), static_cast<UINT32>(_config.height));
            MFSetAttributeRatio(input.Get(), MF_MT_FRAME_RATE, static_cast<UINT32>(_config.fps), 1);
            MFSetAttributeRatio(input.Get(), MF_MT_PIXEL_ASPECT_RATIO, 1, 1);
            hr = _transform->SetInputType(_inputStream, input.Get(), 0);
        }

        if (FAILED(hr))
        {
            error = HrError("the video encoder's input type", hr);
            return false;
        }

        MFT_OUTPUT_STREAM_INFO info = {};
        _transform->GetOutputStreamInfo(_outputStream, &info);
        _providesSamples = (info.dwFlags & (MFT_OUTPUT_STREAM_PROVIDES_SAMPLES | MFT_OUTPUT_STREAM_CAN_PROVIDE_SAMPLES)) != 0;
        _outputSize = info.cbSize;
        _transform->ProcessMessage(MFT_MESSAGE_NOTIFY_BEGIN_STREAMING, 0);
        _transform->ProcessMessage(MFT_MESSAGE_NOTIFY_START_OF_STREAM, 0);
        _started = true;
        return true;
    }

    bool VideoEncoder::Feed(const uint8_t* nv12, size_t length, int64_t time, bool forceKeyframe, std::string& error)
    {
        if (forceKeyframe)
        {
            ComPtr<ICodecAPI> codec;
            if (SUCCEEDED(_transform.As(&codec)))
            {
                SetCodecValue(codec.Get(), CODECAPI_AVEncVideoForceKeyFrame, 1);
            }
        }

        ComPtr<IMFSample> sample = MakeSample(nv12, length, time, 10'000'000LL / _config.fps);
        HRESULT hr = sample ? _transform->ProcessInput(_inputStream, sample.Get(), 0) : E_OUTOFMEMORY;
        if (FAILED(hr))
        {
            error = HrError("video encode input", hr);
            return false;
        }

        return true;
    }

    bool VideoEncoder::TakeOutput(std::vector<EncodedFrame>& out, std::string& error)
    {
        EncodedFrame frame;
        HRESULT hr = ProcessOne(_transform.Get(), _outputStream, _providesSamples, _outputSize, frame);
        if (hr == MF_E_TRANSFORM_NEED_MORE_INPUT)
        {
            return true;
        }

        if (hr == MF_E_TRANSFORM_STREAM_CHANGE)
        {
            // The encoder settled its output type (size, sequence header): take it and go on.
            ComPtr<IMFMediaType> type;
            if (SUCCEEDED(_transform->GetOutputAvailableType(_outputStream, 0, &type)))
            {
                _transform->SetOutputType(_outputStream, type.Get(), 0);
            }

            return true;
        }

        if (FAILED(hr))
        {
            error = HrError("video encode output", hr);
            return false;
        }

        out.push_back(std::move(frame));
        return true;
    }

    bool VideoEncoder::Encode(const uint8_t* nv12, size_t length, int64_t time, bool forceKeyframe, std::vector<EncodedFrame>& out, std::string& error)
    {
        if (!_events)
        {
            // Synchronous (software): one input, then every output it has.
            if (!Feed(nv12, length, time, forceKeyframe, error))
            {
                return false;
            }

            for (size_t before = SIZE_MAX; before != out.size();)
            {
                before = out.size();
                if (!TakeOutput(out, error))
                {
                    return false;
                }
            }

            return true;
        }

        // Asynchronous (hardware): wait until the encoder asks for input, taking outputs on the way, then take
        // what else is ready without waiting.
        bool fed = false;
        while (!fed)
        {
            if (_inputCredits > 0)
            {
                _inputCredits--;
                if (!Feed(nv12, length, time, forceKeyframe, error))
                {
                    return false;
                }

                fed = true;
                break;
            }

            ComPtr<IMFMediaEvent> event;
            HRESULT hr = _events->GetEvent(0, &event);
            if (FAILED(hr))
            {
                error = HrError("the video encoder's events", hr);
                return false;
            }

            MediaEventType type = MEUnknown;
            event->GetType(&type);
            if (type == METransformNeedInput)
            {
                _inputCredits++;
            }
            else if (type == METransformHaveOutput && !TakeOutput(out, error))
            {
                return false;
            }
        }

        for (;;)
        {
            ComPtr<IMFMediaEvent> event;
            HRESULT hr = _events->GetEvent(MF_EVENT_FLAG_NO_WAIT, &event);
            if (hr == MF_E_NO_EVENTS_AVAILABLE)
            {
                return true;
            }

            if (FAILED(hr))
            {
                error = HrError("the video encoder's events", hr);
                return false;
            }

            MediaEventType type = MEUnknown;
            event->GetType(&type);
            if (type == METransformNeedInput)
            {
                _inputCredits++;
            }
            else if (type == METransformHaveOutput && !TakeOutput(out, error))
            {
                return false;
            }
        }
    }

    AacEncoder::~AacEncoder()
    {
        if (_transform)
        {
            _transform->ProcessMessage(MFT_MESSAGE_NOTIFY_END_OF_STREAM, 0);
        }
    }

    bool AacEncoder::Open(int bitrate, std::string& error)
    {
        MFT_REGISTER_TYPE_INFO input = { MFMediaType_Audio, MFAudioFormat_PCM };
        MFT_REGISTER_TYPE_INFO output = { MFMediaType_Audio, MFAudioFormat_AAC };
        _transform = FindTransform(MFT_CATEGORY_AUDIO_ENCODER, MFT_ENUM_FLAG_SYNCMFT, input, output);
        if (!_transform)
        {
            error = "no AAC encoder";
            return false;
        }

        ComPtr<IMFMediaType> outputType;
        HRESULT hr = MFCreateMediaType(&outputType);
        if (SUCCEEDED(hr))
        {
            outputType->SetGUID(MF_MT_MAJOR_TYPE, MFMediaType_Audio);
            outputType->SetGUID(MF_MT_SUBTYPE, MFAudioFormat_AAC);
            outputType->SetUINT32(MF_MT_AUDIO_BITS_PER_SAMPLE, 16);
            outputType->SetUINT32(MF_MT_AUDIO_SAMPLES_PER_SECOND, vv::encode::AudioRate);
            outputType->SetUINT32(MF_MT_AUDIO_NUM_CHANNELS, vv::encode::AudioChannels);
            outputType->SetUINT32(MF_MT_AUDIO_AVG_BYTES_PER_SECOND, vv::encode::AacBytesPerSecond(bitrate));
            outputType->SetUINT32(MF_MT_AAC_PAYLOAD_TYPE, 0); // Raw frames, as FLV carries them.
            hr = _transform->SetOutputType(0, outputType.Get(), 0);
        }

        ComPtr<IMFMediaType> inputType;
        if (SUCCEEDED(hr))
        {
            hr = MFCreateMediaType(&inputType);
        }

        if (SUCCEEDED(hr))
        {
            inputType->SetGUID(MF_MT_MAJOR_TYPE, MFMediaType_Audio);
            inputType->SetGUID(MF_MT_SUBTYPE, MFAudioFormat_PCM);
            inputType->SetUINT32(MF_MT_AUDIO_BITS_PER_SAMPLE, 16);
            inputType->SetUINT32(MF_MT_AUDIO_SAMPLES_PER_SECOND, vv::encode::AudioRate);
            inputType->SetUINT32(MF_MT_AUDIO_NUM_CHANNELS, vv::encode::AudioChannels);
            inputType->SetUINT32(MF_MT_AUDIO_BLOCK_ALIGNMENT, vv::encode::AudioChannels * 2);
            inputType->SetUINT32(MF_MT_AUDIO_AVG_BYTES_PER_SECOND, vv::encode::AudioRate * vv::encode::AudioChannels * 2);
            hr = _transform->SetInputType(0, inputType.Get(), 0);
        }

        if (FAILED(hr))
        {
            error = HrError("the AAC encoder's types", hr);
            return false;
        }

        MFT_OUTPUT_STREAM_INFO info = {};
        _transform->GetOutputStreamInfo(0, &info);
        _outputSize = info.cbSize;
        _transform->ProcessMessage(MFT_MESSAGE_NOTIFY_BEGIN_STREAMING, 0);
        _transform->ProcessMessage(MFT_MESSAGE_NOTIFY_START_OF_STREAM, 0);
        return true;
    }

    bool AacEncoder::Encode(const int16_t* pcm, size_t frames, int64_t time, std::vector<EncodedFrame>& out, std::string& error)
    {
        ComPtr<IMFSample> sample = MakeSample(reinterpret_cast<const uint8_t*>(pcm), frames * vv::encode::AudioChannels * sizeof(int16_t), time,
            vv::encode::FramesToFileTime(static_cast<int64_t>(frames)));
        HRESULT hr = sample ? _transform->ProcessInput(0, sample.Get(), 0) : E_OUTOFMEMORY;
        if (FAILED(hr))
        {
            error = HrError("audio encode input", hr);
            return false;
        }

        for (;;)
        {
            EncodedFrame frame;
            hr = ProcessOne(_transform.Get(), 0, false, _outputSize, frame);
            if (hr == MF_E_TRANSFORM_NEED_MORE_INPUT)
            {
                return true;
            }

            if (FAILED(hr))
            {
                error = HrError("audio encode output", hr);
                return false;
            }

            out.push_back(std::move(frame));
        }
    }
}
