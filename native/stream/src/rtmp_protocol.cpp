#include "rtmp_protocol.h"

#include <algorithm>

namespace vv::stream::rtmp
{
    namespace
    {
        constexpr uint32_t ExtendedTimestamp = 0xFFFFFF;

        // A server's chunk size and messages are accepted up to this, so a bad value cannot make the reader
        // hold gigabytes; a publisher only receives short commands and control messages.
        constexpr uint32_t MaxIncomingChunkSize = 1 << 20;
        constexpr uint32_t MaxIncomingMessage = 1 << 20;

        void PutBasicHeader(Bytes& out, uint32_t format, uint32_t chunkStream)
        {
            // Chunk streams 2 to 63 fit the one-byte form, which is all this publisher uses.
            PutU8(out, (format << 6) | (chunkStream & 0x3F));
        }

        Message Command(uint32_t streamId, const Bytes& payload)
        {
            Message message;
            message.type = CommandAmf0;
            message.streamId = streamId;
            message.payload = payload;
            return message;
        }

        Message NameCommand(const char* command, double transaction, const std::string& name)
        {
            Bytes payload;
            amf0::WriteString(payload, command);
            amf0::WriteNumber(payload, transaction);
            amf0::WriteNull(payload);
            amf0::WriteString(payload, name);
            return Command(0, payload);
        }
    }

    void WriteChunks(Bytes& out, uint32_t chunkStream, const Message& message, uint32_t chunkSize)
    {
        bool extended = message.timestamp >= ExtendedTimestamp;
        size_t at = 0;
        size_t length = message.payload.size();
        do
        {
            if (at == 0)
            {
                PutBasicHeader(out, 0, chunkStream);
                PutU24(out, extended ? ExtendedTimestamp : message.timestamp);
                PutU24(out, static_cast<uint32_t>(length));
                PutU8(out, message.type);
                PutU32Le(out, message.streamId);
            }
            else
            {
                PutBasicHeader(out, 3, chunkStream);
            }

            if (extended)
            {
                PutU32(out, message.timestamp);
            }

            size_t run = std::min<size_t>(chunkSize, length - at);
            PutBytes(out, message.payload.data() + at, run);
            at += run;
        } while (at < length);
    }

    Bytes HandshakeC0C1()
    {
        Bytes out;
        out.reserve(1 + HandshakeSize);
        PutU8(out, 3);
        PutU32(out, 0);
        PutU32(out, 0);

        // The rest is any data the server echoes back; a fixed pattern is enough for the simple handshake.
        for (size_t i = 8; i < HandshakeSize; i++)
        {
            PutU8(out, static_cast<uint32_t>((i * 131) & 0xFF));
        }

        return out;
    }

    Message SetChunkSizeMessage(uint32_t size)
    {
        Message message;
        message.type = SetChunkSize;
        PutU32(message.payload, size & 0x7FFFFFFF);
        return message;
    }

    Message AcknowledgementMessage(uint32_t sequence)
    {
        Message message;
        message.type = Acknowledgement;
        PutU32(message.payload, sequence);
        return message;
    }

    Message PingResponse(uint32_t timestamp)
    {
        Message message;
        message.type = UserControl;
        PutU16(message.payload, 7);
        PutU32(message.payload, timestamp);
        return message;
    }

    Message Connect(const std::string& app, const std::string& tcUrl, bool av1)
    {
        Bytes payload;
        amf0::WriteString(payload, "connect");
        amf0::WriteNumber(payload, 1);
        amf0::WriteObjectStart(payload);
        amf0::WriteKey(payload, "app");
        amf0::WriteString(payload, app);
        amf0::WriteKey(payload, "type");
        amf0::WriteString(payload, "nonprivate");
        amf0::WriteKey(payload, "flashVer");
        amf0::WriteString(payload, "FMLE/3.0 (compatible; Virtual Vessel Studio)");
        amf0::WriteKey(payload, "tcUrl");
        amf0::WriteString(payload, tcUrl);
        if (av1)
        {
            amf0::WriteKey(payload, "fourCcList");
            PutU8(payload, amf0::StrictArray);
            PutU32(payload, 1);
            amf0::WriteString(payload, "av01");
        }

        amf0::WriteObjectEnd(payload);
        return Command(0, payload);
    }

    Message ReleaseStream(double transaction, const std::string& name)
    {
        return NameCommand("releaseStream", transaction, name);
    }

    Message FcPublish(double transaction, const std::string& name)
    {
        return NameCommand("FCPublish", transaction, name);
    }

    Message CreateStream(double transaction)
    {
        Bytes payload;
        amf0::WriteString(payload, "createStream");
        amf0::WriteNumber(payload, transaction);
        amf0::WriteNull(payload);
        return Command(0, payload);
    }

    Message Publish(uint32_t streamId, double transaction, const std::string& name)
    {
        Bytes payload;
        amf0::WriteString(payload, "publish");
        amf0::WriteNumber(payload, transaction);
        amf0::WriteNull(payload);
        amf0::WriteString(payload, name);
        amf0::WriteString(payload, "live");
        return Command(streamId, payload);
    }

    Message FcUnpublish(double transaction, const std::string& name)
    {
        return NameCommand("FCUnpublish", transaction, name);
    }

    Message DeleteStream(double transaction, uint32_t streamId)
    {
        Bytes payload;
        amf0::WriteString(payload, "deleteStream");
        amf0::WriteNumber(payload, transaction);
        amf0::WriteNull(payload);
        amf0::WriteNumber(payload, streamId);
        return Command(0, payload);
    }

    bool ReadCommand(const Message& message, ServerCommand& command)
    {
        std::vector<amf0::Value> values;
        if (message.type != CommandAmf0 || !amf0::ReadAll(message.payload.data(), message.payload.size(), values) || values.size() < 2 ||
            values[0].type != amf0::String)
        {
            return false;
        }

        command = ServerCommand();
        command.name = values[0].text;
        command.transaction = values[1].type == amf0::Number ? values[1].number : 0;
        for (size_t i = 2; i < values.size(); i++)
        {
            const amf0::Value& value = values[i];
            if (value.type == amf0::Number)
            {
                command.number = value.number;
            }
            else if (value.type == amf0::Object)
            {
                const amf0::Value* code = value.Find("code");
                const amf0::Value* level = value.Find("level");
                if (code != nullptr && code->type == amf0::String)
                {
                    command.code = code->text;
                }

                if (level != nullptr && level->type == amf0::String)
                {
                    command.level = level->text;
                }
            }
        }

        return true;
    }

    bool ChunkReader::Feed(const uint8_t* data, size_t length, std::vector<Message>& messages)
    {
        _received += length;
        _buffer.insert(_buffer.end(), data, data + length);
        for (;;)
        {
            size_t used = ParseChunk(messages);
            if (used == SIZE_MAX)
            {
                return false;
            }

            if (used == 0)
            {
                return true;
            }

            _buffer.erase(_buffer.begin(), _buffer.begin() + static_cast<ptrdiff_t>(used));
        }
    }

    size_t ChunkReader::ParseChunk(std::vector<Message>& messages)
    {
        const uint8_t* data = _buffer.data();
        size_t length = _buffer.size();
        if (length < 1)
        {
            return 0;
        }

        size_t at = 0;
        uint32_t format = data[0] >> 6;
        uint32_t chunkStream = data[0] & 0x3F;
        at = 1;
        if (chunkStream == 0)
        {
            if (length < 2)
            {
                return 0;
            }

            chunkStream = 64 + data[1];
            at = 2;
        }
        else if (chunkStream == 1)
        {
            if (length < 3)
            {
                return 0;
            }

            chunkStream = 64 + data[1] + (static_cast<uint32_t>(data[2]) << 8);
            at = 3;
        }

        static const size_t headerSizes[] = { 11, 7, 3, 0 };
        size_t headerSize = headerSizes[format];
        if (length < at + headerSize)
        {
            return 0;
        }

        StreamState& state = _streams[chunkStream];
        bool startsMessage = state.partial.empty();
        uint32_t stamp = 0;
        if (format <= 2)
        {
            stamp = GetU24(data + at);
        }

        if (format <= 1)
        {
            state.length = GetU24(data + at + 3);
            state.type = data[at + 6];
            if (state.length > MaxIncomingMessage)
            {
                return SIZE_MAX;
            }
        }

        if (format == 0)
        {
            state.streamId = GetU32Le(data + at + 7);
        }

        at += headerSize;
        bool extended = format <= 2 ? stamp == ExtendedTimestamp : state.extended;
        if (extended)
        {
            if (length < at + 4)
            {
                return 0;
            }

            stamp = GetU32(data + at);
            at += 4;
        }

        size_t remaining = state.length - state.partial.size();
        size_t run = std::min<size_t>(remaining, _chunkSize);
        if (length < at + run)
        {
            return 0;
        }

        // The header applies only now that the whole chunk is here, so a chunk parsed twice is not counted twice.
        state.extended = extended;
        if (startsMessage)
        {
            if (format == 0)
            {
                state.timestamp = stamp;
                state.delta = 0;
            }
            else if (format <= 2)
            {
                state.delta = stamp;
                state.timestamp += stamp;
            }
            else
            {
                state.timestamp += state.delta;
            }
        }

        state.partial.insert(state.partial.end(), data + at, data + at + run);
        at += run;
        if (state.partial.size() == state.length)
        {
            Message message;
            message.type = state.type;
            message.streamId = state.streamId;
            message.timestamp = state.timestamp;
            message.payload.swap(state.partial);
            if (message.type == SetChunkSize && message.payload.size() >= 4)
            {
                uint32_t size = GetU32(message.payload.data()) & 0x7FFFFFFF;
                if (size == 0 || size > MaxIncomingChunkSize)
                {
                    return SIZE_MAX;
                }

                _chunkSize = size;
            }

            messages.push_back(std::move(message));
        }

        return at;
    }
}
