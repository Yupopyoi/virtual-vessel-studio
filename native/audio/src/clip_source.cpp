#include "clip_source.h"

#include "audio_file.h"
#include "clip_convert.h"
#include "mixer.h"

#include <mfapi.h>
#include <mferror.h>
#include <mfidl.h>
#include <mfreadwrite.h>
#include <propvarutil.h>
#include <wrl/client.h>

#include <new>

using Microsoft::WRL::ComPtr;

namespace vv::audio
{
    namespace
    {
        constexpr DWORD Audio = static_cast<DWORD>(MF_SOURCE_READER_FIRST_AUDIO_STREAM);

        // COM and Media Foundation for one thread, while a file is open on it. Media Foundation is delay-loaded,
        // so it is looked for first: a delay-load that fails would crash instead of returning.
        class MediaFoundationScope
        {
        public:
            MediaFoundationScope()
            {
                _platform = LoadLibraryExW(L"mfplat.dll", nullptr, LOAD_LIBRARY_SEARCH_SYSTEM32);
                _readers = LoadLibraryExW(L"mfreadwrite.dll", nullptr, LOAD_LIBRARY_SEARCH_SYSTEM32);
                if (_platform == nullptr || _readers == nullptr)
                {
                    Result = NoMediaFoundation;
                    return;
                }

                _com = CoInitializeEx(nullptr, COINIT_MULTITHREADED);
                Result = MFStartup(MF_VERSION, MFSTARTUP_LITE);
                _started = SUCCEEDED(Result);
            }

            ~MediaFoundationScope()
            {
                if (_started)
                {
                    MFShutdown();
                }

                if (SUCCEEDED(_com) && _platform != nullptr && _readers != nullptr)
                {
                    CoUninitialize();
                }

                if (_readers != nullptr)
                {
                    FreeLibrary(_readers);
                }

                if (_platform != nullptr)
                {
                    FreeLibrary(_platform);
                }
            }

            MediaFoundationScope(const MediaFoundationScope&) = delete;
            MediaFoundationScope& operator=(const MediaFoundationScope&) = delete;

            HRESULT Result = S_OK;

        private:
            HMODULE _platform = nullptr;
            HMODULE _readers = nullptr;
            HRESULT _com = E_FAIL;
            bool _started = false;
        };

        class MediaFileSource final : public ClipSource
        {
        public:
            explicit MediaFileSource(std::unique_ptr<MediaFoundationScope> scope)
                : _scope(std::move(scope))
            {
            }

            ~MediaFileSource() override
            {
                // The reader goes before Media Foundation shuts down.
                _reader.Reset();
            }

            HRESULT Open(const std::wstring& path)
            {
                HRESULT hr = MFCreateSourceReaderFromURL(path.c_str(), nullptr, &_reader);
                if (SUCCEEDED(hr))
                {
                    hr = _reader->SetStreamSelection(static_cast<DWORD>(MF_SOURCE_READER_ALL_STREAMS), FALSE);
                }

                if (SUCCEEDED(hr))
                {
                    hr = _reader->SetStreamSelection(Audio, TRUE);
                }

                // Only the sample format is asked for; the rate and channels stay the file's, and are converted here.
                ComPtr<IMFMediaType> wanted;
                if (SUCCEEDED(hr))
                {
                    hr = MFCreateMediaType(&wanted);
                }

                if (SUCCEEDED(hr))
                {
                    hr = wanted->SetGUID(MF_MT_MAJOR_TYPE, MFMediaType_Audio);
                }

                if (SUCCEEDED(hr))
                {
                    hr = wanted->SetGUID(MF_MT_SUBTYPE, MFAudioFormat_Float);
                }

                if (SUCCEEDED(hr))
                {
                    hr = _reader->SetCurrentMediaType(Audio, nullptr, wanted.Get());
                }

                ComPtr<IMFMediaType> actual;
                if (SUCCEEDED(hr))
                {
                    hr = _reader->GetCurrentMediaType(Audio, &actual);
                }

                if (FAILED(hr))
                {
                    return hr;
                }

                _format.channels = static_cast<int>(MFGetAttributeUINT32(actual.Get(), MF_MT_AUDIO_NUM_CHANNELS, 0));
                _format.rate = static_cast<int>(MFGetAttributeUINT32(actual.Get(), MF_MT_AUDIO_SAMPLES_PER_SECOND, 0));
                if (_format.channels == 0 || _format.rate == 0 || MFGetAttributeUINT32(actual.Get(), MF_MT_AUDIO_BITS_PER_SAMPLE, 0) != 32)
                {
                    return MF_E_INVALIDMEDIATYPE;
                }

                // The length, from the file's duration in 100 ns units; some files do not say.
                PROPVARIANT duration;
                PropVariantInit(&duration);
                if (SUCCEEDED(_reader->GetPresentationAttribute(static_cast<DWORD>(MF_SOURCE_READER_MEDIASOURCE), MF_PD_DURATION, &duration)) && duration.vt == VT_UI8)
                {
                    _format.frames = static_cast<int64_t>((duration.uhVal.QuadPart * static_cast<ULONGLONG>(_format.rate)) / 10'000'000ULL);
                }

                PropVariantClear(&duration);
                return S_OK;
            }

            ClipFormat Format() const override
            {
                return _format;
            }

            HRESULT Read(std::vector<float>& frames) override
            {
                frames.clear();
                while (true)
                {
                    DWORD flags = 0;
                    ComPtr<IMFSample> sample;
                    HRESULT hr = _reader->ReadSample(Audio, 0, nullptr, &flags, nullptr, &sample);
                    if (FAILED(hr) || (flags & MF_SOURCE_READERF_ENDOFSTREAM) != 0)
                    {
                        return hr;
                    }

                    // The format is read at the start; these files do not change it midway.
                    if ((flags & MF_SOURCE_READERF_CURRENTMEDIATYPECHANGED) != 0)
                    {
                        return MF_E_INVALIDMEDIATYPE;
                    }

                    if (!sample)
                    {
                        continue;
                    }

                    ComPtr<IMFMediaBuffer> buffer;
                    hr = sample->ConvertToContiguousBuffer(&buffer);
                    BYTE* data = nullptr;
                    DWORD length = 0;
                    if (SUCCEEDED(hr))
                    {
                        hr = buffer->Lock(&data, nullptr, &length);
                    }

                    if (FAILED(hr))
                    {
                        return hr;
                    }

                    const float* values = reinterpret_cast<const float*>(data);
                    frames.assign(values, values + (length / sizeof(float)));
                    buffer->Unlock();
                    if (!frames.empty())
                    {
                        return S_OK;
                    }
                }
            }

            HRESULT Rewind() override
            {
                PROPVARIANT start;
                InitPropVariantFromInt64(0, &start);
                HRESULT hr = _reader->SetCurrentPosition(GUID_NULL, start);
                PropVariantClear(&start);
                return hr;
            }

        private:
            std::unique_ptr<MediaFoundationScope> _scope;
            ComPtr<IMFSourceReader> _reader;
            ClipFormat _format;
        };
    }

    std::unique_ptr<ClipSource> OpenMediaFile(const std::wstring& path, HRESULT& result)
    {
        try
        {
            auto scope = std::make_unique<MediaFoundationScope>();
            if (FAILED(scope->Result))
            {
                result = scope->Result;
                return nullptr;
            }

            auto source = std::make_unique<MediaFileSource>(std::move(scope));
            result = source->Open(path);
            if (FAILED(result))
            {
                return nullptr;
            }

            return source;
        }
        catch (const std::bad_alloc&)
        {
            result = E_OUTOFMEMORY;
            return nullptr;
        }
    }

    HRESULT DecodeStereo(const std::wstring& path, int maxSeconds, std::vector<float>& stereo)
    {
        stereo.clear();
        HRESULT hr = S_OK;
        std::unique_ptr<ClipSource> source = OpenMediaFile(path, hr);
        if (!source)
        {
            return hr;
        }

        try
        {
            ClipFormat format = source->Format();
            const size_t limit = static_cast<size_t>(format.rate) * static_cast<size_t>(maxSeconds);
            StereoResampler resampler(format.rate, SampleRate);
            std::vector<float> frames;
            std::vector<float> pair;
            size_t read = 0;
            while (true)
            {
                hr = source->Read(frames);
                if (FAILED(hr) || frames.empty())
                {
                    break;
                }

                size_t count = frames.size() / static_cast<size_t>(format.channels);
                read += count;
                if (read > limit)
                {
                    hr = FileTooLong;
                    break;
                }

                pair.resize(count * 2);
                ToStereo(frames.data(), count, format.channels, pair.data());
                resampler.Process(pair.data(), count, stereo);
            }

            if (SUCCEEDED(hr) && format.rate != SampleRate)
            {
                float tail[4] = {};
                resampler.Process(tail, 2, stereo);
            }
        }
        catch (const std::bad_alloc&)
        {
            hr = E_OUTOFMEMORY;
        }

        if (FAILED(hr))
        {
            stereo.clear();
        }

        return hr;
    }
}
