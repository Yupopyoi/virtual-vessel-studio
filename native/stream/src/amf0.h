// AMF0, the encoding of RTMP's commands and the stream's metadata: writing the few kinds a publisher sends,
// and reading what servers answer (_result, _error, onStatus). Pure.
#pragma once

#include "bytes.h"

#include <cstdint>
#include <memory>
#include <string>
#include <utility>
#include <vector>

namespace vv::stream::amf0
{
    enum Marker : uint8_t
    {
        Number = 0x00,
        Boolean = 0x01,
        String = 0x02,
        Object = 0x03,
        Null = 0x05,
        Undefined = 0x06,
        EcmaArray = 0x08,
        ObjectEnd = 0x09,
        StrictArray = 0x0A,
    };

    void WriteNumber(Bytes& out, double value);
    void WriteBoolean(Bytes& out, bool value);
    void WriteString(Bytes& out, const std::string& value);
    void WriteNull(Bytes& out);

    // An object's property name (without a marker), followed by its value.
    void WriteKey(Bytes& out, const std::string& key);
    void WriteObjectStart(Bytes& out);
    void WriteEcmaArrayStart(Bytes& out, uint32_t count);
    void WriteObjectEnd(Bytes& out);

    struct Value
    {
        Marker type = Undefined;
        double number = 0.0;
        bool boolean = false;
        std::string text;
        std::vector<std::pair<std::string, Value>> properties; // Objects and ECMA arrays.
        std::vector<Value> items;                               // Strict arrays.

        // A property of an object, or nullptr.
        const Value* Find(const std::string& key) const;
    };

    // Reads the values one after another; false when the data ends inside one or holds a kind this reader
    // does not know (which a publisher never needs).
    bool ReadAll(const uint8_t* data, size_t length, std::vector<Value>& values);
}
