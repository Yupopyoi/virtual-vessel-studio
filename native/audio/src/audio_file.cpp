#include "audio_file.h"

#include <mfapi.h>
#include <mferror.h>
#include <mfidl.h>
#include <mfreadwrite.h>
#include <wrl/client.h>

#include <new>

using Microsoft::WRL::ComPtr;

namespace vv::audio
{
    namespace
    {
        // The COM objects live in here, so they are released before Media Foundation and COM shut down.
        HRESULT Read(const wchar_t* path, DecodedFile& decoded)
        {
            constexpr DWORD Audio = static_cast<DWORD>(MF_SOURCE_READER_FIRST_AUDIO_STREAM);
            ComPtr<IMFSourceReader> reader;
            HRESULT hr = MFCreateSourceReaderFromURL(path, nullptr, &reader);
            if (SUCCEEDED(hr))
            {
                hr = reader->SetStreamSelection(static_cast<DWORD>(MF_SOURCE_READER_ALL_STREAMS), FALSE);
            }

            if (SUCCEEDED(hr))
            {
                hr = reader->SetStreamSelection(Audio, TRUE);
            }

            // Only the sample format is asked for; the rate and the channels stay the file's own.
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
                hr = reader->SetCurrentMediaType(Audio, nullptr, wanted.Get());
            }

            ComPtr<IMFMediaType> actual;
            if (SUCCEEDED(hr))
            {
                hr = reader->GetCurrentMediaType(Audio, &actual);
            }

            if (FAILED(hr))
            {
                return hr;
            }

            UINT32 channels = MFGetAttributeUINT32(actual.Get(), MF_MT_AUDIO_NUM_CHANNELS, 0);
            UINT32 rate = MFGetAttributeUINT32(actual.Get(), MF_MT_AUDIO_SAMPLES_PER_SECOND, 0);
            if (channels == 0 || rate == 0 || MFGetAttributeUINT32(actual.Get(), MF_MT_AUDIO_BITS_PER_SAMPLE, 0) != 32)
            {
                return MF_E_INVALIDMEDIATYPE;
            }

            decoded.rate = static_cast<int>(rate);
            const size_t limit = static_cast<size_t>(rate) * MaxFileSeconds;
            while (true)
            {
                DWORD flags = 0;
                ComPtr<IMFSample> sample;
                hr = reader->ReadSample(Audio, 0, nullptr, &flags, nullptr, &sample);
                if (FAILED(hr) || (flags & MF_SOURCE_READERF_ENDOFSTREAM) != 0)
                {
                    return hr;
                }

                // Read at the start, the format does not change midway in the files this is for.
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

                std::vector<float> mono = ToMono(reinterpret_cast<const float*>(data), length / (sizeof(float) * channels), static_cast<int>(channels));
                buffer->Unlock();
                if (decoded.samples.size() + mono.size() > limit)
                {
                    return FileTooLong;
                }

                decoded.samples.insert(decoded.samples.end(), mono.begin(), mono.end());
            }
        }
    }

    HRESULT DecodeFile(const wchar_t* path, DecodedFile& decoded)
    {
        decoded = DecodedFile();

        // The plugin delay-loads Media Foundation, so the engine still starts where it is missing; it is
        // checked here, because a delay-load that fails would crash instead of returning.
        HMODULE platform = LoadLibraryExW(L"mfplat.dll", nullptr, LOAD_LIBRARY_SEARCH_SYSTEM32);
        HMODULE readers = LoadLibraryExW(L"mfreadwrite.dll", nullptr, LOAD_LIBRARY_SEARCH_SYSTEM32);
        if (platform == nullptr || readers == nullptr)
        {
            if (platform != nullptr)
            {
                FreeLibrary(platform);
            }

            if (readers != nullptr)
            {
                FreeLibrary(readers);
            }

            return NoMediaFoundation;
        }

        HRESULT com = CoInitializeEx(nullptr, COINIT_MULTITHREADED);
        HRESULT hr = MFStartup(MF_VERSION, MFSTARTUP_LITE);
        if (SUCCEEDED(hr))
        {
            try
            {
                hr = Read(path, decoded);
            }
            catch (const std::bad_alloc&)
            {
                hr = E_OUTOFMEMORY;
            }

            MFShutdown();
        }

        if (SUCCEEDED(com))
        {
            CoUninitialize();
        }

        FreeLibrary(readers);
        FreeLibrary(platform);
        if (FAILED(hr))
        {
            decoded = DecodedFile();
        }

        return hr;
    }

    std::vector<float> ToMono(const float* frames, size_t count, int channels)
    {
        std::vector<float> mono(count);
        for (size_t i = 0; i < count; i++)
        {
            float sum = 0.0f;
            for (int c = 0; c < channels; c++)
            {
                sum += frames[(i * channels) + c];
            }

            mono[i] = sum / static_cast<float>(channels);
        }

        return mono;
    }
}
