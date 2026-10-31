#include "amf0.h"

#include <cstring>

namespace vv::stream::amf0
{
    void WriteNumber(Bytes& out, double value)
    {
        uint64_t bits = 0;
        std::memcpy(&bits, &value, sizeof(bits));
        PutU8(out, Number);
        PutU32(out, static_cast<uint32_t>(bits >> 32));
        PutU32(out, static_cast<uint32_t>(bits));
    }

    void WriteBoolean(Bytes& out, bool value)
    {
        PutU8(out, Boolean);
        PutU8(out, value ? 1 : 0);
    }

    void WriteString(Bytes& out, const std::string& value)
    {
        PutU8(out, String);
        PutU16(out, static_cast<uint32_t>(value.size()));
        PutText(out, value);
    }

    void WriteNull(Bytes& out)
    {
        PutU8(out, Null);
    }

    void WriteKey(Bytes& out, const std::string& key)
    {
        PutU16(out, static_cast<uint32_t>(key.size()));
        PutText(out, key);
    }

    void WriteObjectStart(Bytes& out)
    {
        PutU8(out, Object);
    }

    void WriteEcmaArrayStart(Bytes& out, uint32_t count)
    {
        PutU8(out, EcmaArray);
        PutU32(out, count);
    }

    void WriteObjectEnd(Bytes& out)
    {
        PutU16(out, 0);
        PutU8(out, ObjectEnd);
    }

    const Value* Value::Find(const std::string& key) const
    {
        for (const auto& property : properties)
        {
            if (property.first == key)
            {
                return &property.second;
            }
        }

        return nullptr;
    }

    namespace
    {
        // Deep enough for any answer a server gives; a deeper one is refused rather than recursed into.
        constexpr int MaxDepth = 8;

        bool ReadValue(const uint8_t* data, size_t length, size_t& at, Value& value, int depth);

        bool ReadText(const uint8_t* data, size_t length, size_t& at, std::string& text)
        {
            if (at + 2 > length)
            {
                return false;
            }

            size_t size = GetU16(data + at);
            at += 2;
            if (at + size > length)
            {
                return false;
            }

            text.assign(reinterpret_cast<const char*>(data + at), size);
            at += size;
            return true;
        }

        bool ReadProperties(const uint8_t* data, size_t length, size_t& at, Value& value, int depth)
        {
            for (;;)
            {
                std::string key;
                if (!ReadText(data, length, at, key))
                {
                    return false;
                }

                if (key.empty() && at < length && data[at] == ObjectEnd)
                {
                    at++;
                    return true;
                }

                Value property;
                if (!ReadValue(data, length, at, property, depth + 1))
                {
                    return false;
                }

                value.properties.emplace_back(std::move(key), std::move(property));
            }
        }

        bool ReadValue(const uint8_t* data, size_t length, size_t& at, Value& value, int depth)
        {
            if (at >= length || depth > MaxDepth)
            {
                return false;
            }

            value.type = static_cast<Marker>(data[at++]);
            switch (value.type)
            {
                case Number:
                {
                    if (at + 8 > length)
                    {
                        return false;
                    }

                    uint64_t bits = (static_cast<uint64_t>(GetU32(data + at)) << 32) | GetU32(data + at + 4);
                    std::memcpy(&value.number, &bits, sizeof(bits));
                    at += 8;
                    return true;
                }

                case Boolean:
                    if (at >= length)
                    {
                        return false;
                    }

                    value.boolean = data[at++] != 0;
                    return true;
                case String:
                    return ReadText(data, length, at, value.text);
                case Null:
                case Undefined:
                    return true;
                case Object:
                    return ReadProperties(data, length, at, value, depth);
                case EcmaArray:
                    if (at + 4 > length)
                    {
                        return false;
                    }

                    at += 4; // The count is a hint; the properties end with the object end marker.
                    return ReadProperties(data, length, at, value, depth);
                case StrictArray:
                {
                    if (at + 4 > length)
                    {
                        return false;
                    }

                    uint32_t count = GetU32(data + at);
                    at += 4;
                    for (uint32_t i = 0; i < count; i++)
                    {
                        Value item;
                        if (!ReadValue(data, length, at, item, depth + 1))
                        {
                            return false;
                        }

                        value.items.push_back(std::move(item));
                    }

                    return true;
                }

                default:
                    return false;
            }
        }
    }

    bool ReadAll(const uint8_t* data, size_t length, std::vector<Value>& values)
    {
        size_t at = 0;
        while (at < length)
        {
            Value value;
            if (!ReadValue(data, length, at, value, 0))
            {
                return false;
            }

            values.push_back(std::move(value));
        }

        return true;
    }
}
