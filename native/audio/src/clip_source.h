// Reading an audio file a piece at a time (BGM and SE detailed design 8.1, 8.2): BGM is decoded ahead on the deck's
// thread, so a long track never has to fit in memory. The decoding is Media Foundation's, behind an interface the
// tests replace with synthetic sound.
#pragma once

#include <windows.h>

#include <cstddef>
#include <cstdint>
#include <memory>
#include <string>
#include <vector>

namespace vv::audio
{
    struct ClipFormat
    {
        int rate = 0;
        int channels = 0;
        int64_t frames = 0; // At the file's rate; 0 when the file does not say.
    };

    class ClipSource
    {
    public:
        virtual ~ClipSource() = default;

        virtual ClipFormat Format() const = 0;

        // Replaces `frames` with the next interleaved frames, in the file's own channels and rate; empty at the end.
        // S_OK, or the failing HRESULT.
        virtual HRESULT Read(std::vector<float>& frames) = 0;

        // Back to the start, for a loop.
        virtual HRESULT Rewind() = 0;
    };

    // Opens a file with Media Foundation on the calling thread, which must also close it (COM and Media Foundation
    // are started for it and shut down with it). Null with the HRESULT on failure; NoMediaFoundation where Windows
    // has none.
    std::unique_ptr<ClipSource> OpenMediaFile(const std::wstring& path, HRESULT& result);

    // A whole file at 48 kHz stereo, for SE: up to maxSeconds, else FileTooLong. S_OK or the failing HRESULT.
    HRESULT DecodeStereo(const std::wstring& path, int maxSeconds, std::vector<float>& stereo);
}
