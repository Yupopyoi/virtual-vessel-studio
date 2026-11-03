// Reads a whole audio file with Media Foundation (audio detailed design 5.4): WAV, M4A (AAC), MP3, and whatever
// else Windows can decode. Windows' own decoders do the work, so the plugin carries no codec.
#pragma once

#include <windows.h>

#include <cstddef>
#include <vector>

namespace vv::audio
{
    // The longest file read, so a wrong choice cannot fill the memory.
    constexpr int MaxFileSeconds = 600;

    // A file longer than MaxFileSeconds.
    constexpr HRESULT FileTooLong = __HRESULT_FROM_WIN32(ERROR_FILE_TOO_LARGE);

    // Windows without Media Foundation: the N editions without their media pack.
    constexpr HRESULT NoMediaFoundation = __HRESULT_FROM_WIN32(ERROR_MOD_NOT_FOUND);

    struct DecodedFile
    {
        std::vector<float> samples;
        int rate = 0;
    };

    // The file mixed to mono floats at its own rate; S_OK, or the failing HRESULT (FileTooLong among them).
    HRESULT DecodeFile(const wchar_t* path, DecodedFile& decoded);

    // Interleaved frames mixed to mono by their average. Pure.
    std::vector<float> ToMono(const float* frames, size_t count, int channels);
}
