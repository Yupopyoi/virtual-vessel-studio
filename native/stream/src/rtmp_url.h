// An RTMP(S) server URL (streaming detailed design 8.3): rtmp[s]://host[:port]/app[/instance]. The stream key
// is not part of it; it is the name the stream is published under. Pure.
#pragma once

#include <cstdint>
#include <string>

namespace vv::stream
{
    struct RtmpUrl
    {
        bool secure = false;
        std::string host;
        uint16_t port = 0;
        std::string app;   // The path after the host, without the leading slash.
        std::string tcUrl; // scheme://host[:port]/app, as RTMP connect sends it.
    };

    // False for anything but rtmp:// or rtmps:// with a host and an application.
    inline bool ParseRtmpUrl(const std::string& url, RtmpUrl& out)
    {
        RtmpUrl parsed;
        std::string rest;
        if (url.rfind("rtmps://", 0) == 0)
        {
            parsed.secure = true;
            rest = url.substr(8);
        }
        else if (url.rfind("rtmp://", 0) == 0)
        {
            rest = url.substr(7);
        }
        else
        {
            return false;
        }

        size_t slash = rest.find('/');
        if (slash == std::string::npos || slash == 0)
        {
            return false;
        }

        std::string authority = rest.substr(0, slash);
        parsed.app = rest.substr(slash + 1);
        while (!parsed.app.empty() && parsed.app.back() == '/')
        {
            parsed.app.pop_back();
        }

        if (parsed.app.empty() || parsed.app.find_first_of(" \t\r\n") != std::string::npos)
        {
            return false;
        }

        parsed.port = parsed.secure ? 443 : 1935;
        size_t colon = authority.rfind(':');
        if (colon != std::string::npos)
        {
            std::string port = authority.substr(colon + 1);
            if (port.empty() || port.size() > 5 || port.find_first_not_of("0123456789") != std::string::npos)
            {
                return false;
            }

            unsigned long value = std::stoul(port);
            if (value == 0 || value > 65535)
            {
                return false;
            }

            parsed.port = static_cast<uint16_t>(value);
            authority = authority.substr(0, colon);
        }

        if (authority.empty())
        {
            return false;
        }

        parsed.host = authority;
        parsed.tcUrl = (parsed.secure ? "rtmps://" : "rtmp://") + rest.substr(0, slash) + "/" + parsed.app;
        out = parsed;
        return true;
    }
}
