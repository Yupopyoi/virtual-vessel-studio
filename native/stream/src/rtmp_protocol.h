// RTMP's messages and chunks (streaming detailed design 8.1): what a publisher writes, and a reader for the
// server's answers. No sockets here; the client gives the bytes to a transport. Pure.
#pragma once

#include "amf0.h"
#include "bytes.h"

#include <cstdint>
#include <map>
#include <string>
#include <vector>

namespace vv::stream::rtmp
{
    enum MessageType : uint8_t
    {
        SetChunkSize = 1,
        Abort = 2,
        Acknowledgement = 3,
        UserControl = 4,
        WindowAckSize = 5,
        SetPeerBandwidth = 6,
        Audio = 8,
        Video = 9,
        DataAmf0 = 18,
        CommandAmf0 = 20,
    };

    // The chunk streams this publisher uses: protocol control, commands, audio, video.
    enum ChunkStream : uint32_t
    {
        ControlChunks = 2,
        CommandChunks = 3,
        AudioChunks = 4,
        VideoChunks = 6,
    };

    // The size of chunks this publisher sends, set once at the start; large enough that most audio fits in one.
    constexpr uint32_t OutgoingChunkSize = 4096;

    constexpr size_t HandshakeSize = 1536;

    struct Message
    {
        uint8_t type = 0;
        uint32_t streamId = 0;
        uint32_t timestamp = 0; // Milliseconds.
        Bytes payload;
    };

    // Appends a message as chunks: a full header first (type 0), then one-byte headers (type 3). Timestamps
    // from 0xFFFFFF on (after 4.6 hours) go in the extended timestamp, in every chunk.
    void WriteChunks(Bytes& out, uint32_t chunkStream, const Message& message, uint32_t chunkSize);

    // C0 and C1: version 3, then 1536 bytes of time, zeros, and filler.
    Bytes HandshakeC0C1();

    // ---- Commands a publisher sends

    Message SetChunkSizeMessage(uint32_t size);
    Message AcknowledgementMessage(uint32_t sequence);
    Message PingResponse(uint32_t timestamp);

    // connect: the application and tcUrl; with AV1, the Enhanced RTMP fourCcList the server checks.
    Message Connect(const std::string& app, const std::string& tcUrl, bool av1);
    Message ReleaseStream(double transaction, const std::string& name);
    Message FcPublish(double transaction, const std::string& name);
    Message CreateStream(double transaction);
    Message Publish(uint32_t streamId, double transaction, const std::string& name);
    Message FcUnpublish(double transaction, const std::string& name);
    Message DeleteStream(double transaction, uint32_t streamId);

    // A command the server sent: its name (_result, _error, onStatus, ...), transaction, the number after the
    // command object (createStream's stream ID), and the information object's "code" and "level".
    struct ServerCommand
    {
        std::string name;
        double transaction = 0;
        double number = 0;
        std::string code;
        std::string level;
    };

    bool ReadCommand(const Message& message, ServerCommand& command);

    // Reassembles the server's chunks into messages, keeping each chunk stream's last header and the chunk
    // size the server sets.
    class ChunkReader
    {
    public:
        // Takes what arrived; whole messages come out in order. False on data that cannot be RTMP.
        bool Feed(const uint8_t* data, size_t length, std::vector<Message>& messages);

        uint64_t Received() const
        {
            return _received;
        }

    private:
        struct StreamState
        {
            uint32_t timestamp = 0;
            uint32_t delta = 0;
            uint32_t length = 0;
            uint8_t type = 0;
            uint32_t streamId = 0;
            bool extended = false;
            Bytes partial;
        };

        // Parses one chunk from the start of the buffer; 0 when it has not all arrived, SIZE_MAX on bad data.
        size_t ParseChunk(std::vector<Message>& messages);

        Bytes _buffer;
        std::map<uint32_t, StreamState> _streams;
        uint32_t _chunkSize = 128;
        uint64_t _received = 0;
    };
}
