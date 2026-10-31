// Byte buffers in network (big-endian) order, as RTMP, AMF0, and FLV write numbers.
#pragma once

#include <cstddef>
#include <cstdint>
#include <cstring>
#include <string>
#include <vector>

namespace vv::stream
{
    using Bytes = std::vector<uint8_t>;

    inline void PutU8(Bytes& out, uint32_t value)
    {
        out.push_back(static_cast<uint8_t>(value));
    }

    inline void PutU16(Bytes& out, uint32_t value)
    {
        out.push_back(static_cast<uint8_t>(value >> 8));
        out.push_back(static_cast<uint8_t>(value));
    }

    inline void PutU24(Bytes& out, uint32_t value)
    {
        out.push_back(static_cast<uint8_t>(value >> 16));
        out.push_back(static_cast<uint8_t>(value >> 8));
        out.push_back(static_cast<uint8_t>(value));
    }

    inline void PutU32(Bytes& out, uint32_t value)
    {
        out.push_back(static_cast<uint8_t>(value >> 24));
        out.push_back(static_cast<uint8_t>(value >> 16));
        out.push_back(static_cast<uint8_t>(value >> 8));
        out.push_back(static_cast<uint8_t>(value));
    }

    // Little-endian, as the RTMP chunk header writes the message stream ID.
    inline void PutU32Le(Bytes& out, uint32_t value)
    {
        out.push_back(static_cast<uint8_t>(value));
        out.push_back(static_cast<uint8_t>(value >> 8));
        out.push_back(static_cast<uint8_t>(value >> 16));
        out.push_back(static_cast<uint8_t>(value >> 24));
    }

    inline void PutBytes(Bytes& out, const uint8_t* data, size_t length)
    {
        out.insert(out.end(), data, data + length);
    }

    inline void PutText(Bytes& out, const std::string& text)
    {
        out.insert(out.end(), text.begin(), text.end());
    }

    inline uint32_t GetU16(const uint8_t* data)
    {
        return (static_cast<uint32_t>(data[0]) << 8) | data[1];
    }

    inline uint32_t GetU24(const uint8_t* data)
    {
        return (static_cast<uint32_t>(data[0]) << 16) | (static_cast<uint32_t>(data[1]) << 8) | data[2];
    }

    inline uint32_t GetU32(const uint8_t* data)
    {
        return (static_cast<uint32_t>(data[0]) << 24) | (static_cast<uint32_t>(data[1]) << 16) | (static_cast<uint32_t>(data[2]) << 8) | data[3];
    }

    inline uint32_t GetU32Le(const uint8_t* data)
    {
        return data[0] | (static_cast<uint32_t>(data[1]) << 8) | (static_cast<uint32_t>(data[2]) << 16) | (static_cast<uint32_t>(data[3]) << 24);
    }
}
