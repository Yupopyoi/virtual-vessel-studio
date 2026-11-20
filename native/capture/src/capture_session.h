#pragma once

#include <string>

#include "session_core.h"

namespace vv::capture
{
    // One monitor duplicated on a thread of its own (capture detailed design 8.2). The thread owns
    // every D3D11 and DXGI object; the caller sees only the status and the exchanged frames.
    class Session final : public SessionCore
    {
    public:
        Session(std::wstring deviceName, bool drawPointer);

        // Stops the thread and waits for it.
        ~Session() override;

    protected:
        void Capture() override;

    private:
        const std::wstring _deviceName;
        const bool _drawPointer;
    };
}
