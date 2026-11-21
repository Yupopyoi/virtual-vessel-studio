#pragma once

#include <windows.h>

#include <string>
#include <vector>

#include "session_core.h"
#include "vv_capture.h"

namespace vv::capture
{
    // A top-level window seen while listing, before choosing one per program.
    struct WindowCandidate
    {
        HWND handle = nullptr;
        std::wstring executable;    // The program's file name, lower case.
        std::wstring title;
        DWORD processId = 0;
        int32_t width = 0;          // The client area.
        int32_t height = 0;
    };

    // The file name of a path, lower case: "C:\Games\Game.exe" gives "game.exe". Pure.
    std::wstring ExecutableName(const std::wstring& path);

    // One window per program, its largest, in the order the programs were first seen, leaving out the windows of
    // `ownProcess` and windows too small to be a game's (a tray icon's or a hidden helper's). Pure.
    std::vector<WindowCandidate> LargestPerProgram(const std::vector<WindowCandidate>& windows, DWORD ownProcess);

    // The part of a captured window image that is its client area: the client rectangle and the window's frame
    // bounds are in screen coordinates, the image covers the frame bounds. Clamped to the image; empty when they
    // do not overlap. Pure.
    RECT ClientBox(const RECT& frameBounds, const RECT& clientOnScreen, int32_t imageWidth, int32_t imageHeight);

    // The windows of other programs that could be captured, one per program.
    HRESULT ListWindows(std::vector<vv_capture_window>& windows);

    // The largest window of the program, or null.
    HWND FindProgramWindow(const std::wstring& executable);

    // One program's window captured through Windows.Graphics.Capture on a thread of its own (capture detailed
    // design 8.6). Only the client area is kept, so a windowed game's title bar and borders are left out.
    class WindowSession final : public SessionCore
    {
    public:
        WindowSession(std::wstring executable, bool drawPointer);

        ~WindowSession() override;

    protected:
        void Capture() override;

    private:
        const std::wstring _executable;
        const bool _drawPointer;
    };
}
