// An RTMP publisher over a transport (streaming detailed design 7, 8.1): the handshake, connect, createStream,
// and publish, then the stream's messages. The stream key is the published name; it is sent in the publish
// commands and never kept in an error or a log line.
#pragma once

#include "rtmp_protocol.h"
#include "rtmp_url.h"
#include "transport.h"

#include <cstdint>
#include <memory>
#include <string>

namespace vv::stream
{
    // Why connecting or sending failed, as the user is told (streaming detailed design 6, 12).
    enum class PublishFailure
    {
        None,
        Unreachable, // Name resolution, TCP, or TLS.
        Rejected,    // The server refused connect or publish: usually a wrong key.
        Lost,        // The connection closed or failed after it was up.
    };

    class RtmpClient
    {
    public:
        explicit RtmpClient(std::unique_ptr<Transport> transport);

        // Connects and publishes under key, waiting up to timeoutMs for each step. On failure, error is a
        // description without the key.
        PublishFailure Publish(const RtmpUrl& url, const std::string& key, bool av1, int timeoutMs, std::string& error);

        // Connects as far as RTMP connect, without publishing: the setup screen's connection test.
        PublishFailure TestConnect(const RtmpUrl& url, int timeoutMs, std::string& error);

        // Sends a message on the published stream (audio, video, or data).
        bool Send(uint32_t chunkStream, rtmp::Message message, std::string& error);

        // Reads what the server sent without waiting, and answers pings and acknowledgements.
        bool Poll(std::string& error);

        // Ends the stream politely (FCUnpublish, deleteStream) and closes the connection.
        void Close(const std::string& key);

        uint64_t BytesSent() const
        {
            return _sent;
        }

    private:
        PublishFailure Handshake(const RtmpUrl& url, int timeoutMs, std::string& error);
        PublishFailure ConnectApp(const RtmpUrl& url, bool av1, int timeoutMs, std::string& error);

        // Waits for the answer to a transaction (or for an onStatus when transaction is 0).
        PublishFailure Await(double transaction, int timeoutMs, rtmp::ServerCommand& answer, std::string& error);

        bool SendMessage(uint32_t chunkStream, const rtmp::Message& message, std::string& error);
        bool Handle(const rtmp::Message& message, std::string& error);

        std::unique_ptr<Transport> _transport;
        rtmp::ChunkReader _reader;
        std::vector<rtmp::ServerCommand> _commands;
        uint32_t _streamId = 0;
        uint32_t _windowAckSize = 2'500'000;
        uint64_t _acknowledged = 0;
        uint64_t _sent = 0;
        bool _published = false;
    };
}
