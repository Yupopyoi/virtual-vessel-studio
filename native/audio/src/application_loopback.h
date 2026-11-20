// A program's own sound, without a device (audio detailed design 8.7): Windows' process loopback gives what one
// program and the programs it started play, apart from everything else on the PC. It is how the sound of a game
// on this PC is taken, with its window as the video (capture detailed design 8.6).
#pragma once

#include <windows.h>

#include <audioclient.h>
#include <wrl/client.h>

#include <cstdint>
#include <string>
#include <vector>

namespace vv::audio
{
    // The device IDs of a program's sound start with this, followed by the program's file name ("app:game.exe").
    inline constexpr wchar_t ApplicationPrefix[] = L"app:";

    // The program's file name in an application device ID, or empty when the ID is a device's. Pure.
    std::wstring ApplicationOf(const std::wstring& deviceId);

    struct ProcessEntry
    {
        DWORD id = 0;
        DWORD parent = 0;
        std::wstring executable; // The file name, as the system lists it.
    };

    // The process whose sound to take for the program: the first one of that file name (any case) that was not
    // started by another of the same name, so that a program's helper processes come with it as its tree. 0 when
    // none is running. Pure.
    DWORD RootProcess(const std::vector<ProcessEntry>& processes, const std::wstring& executable);

    // Opens the sound of the program in shared mode as 48 kHz float stereo, with event wakes on `events`. `ended`
    // receives a handle that is signaled when the process exits, for the caller to close; the loopback itself
    // goes quiet then, without failing. E_NOTFOUND when the program is not running.
    HRESULT OpenApplicationLoopback(const std::wstring& executable, REFERENCE_TIME bufferDuration, HANDLE events, Microsoft::WRL::ComPtr<IAudioClient>& client, HANDLE& ended);
}
