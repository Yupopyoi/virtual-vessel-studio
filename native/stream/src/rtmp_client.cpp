#include "rtmp_client.h"

#include <chrono>

namespace vv::stream
{
    namespace
    {
        constexpr double ConnectTransaction = 1;
        constexpr double ReleaseTransaction = 2;
        constexpr double FcPublishTransaction = 3;
        constexpr double CreateStreamTransaction = 4;
        constexpr double PublishTransaction = 5;

        int64_t NowMs()
        {
            return std::chrono::duration_cast<std::chrono::milliseconds>(std::chrono::steady_clock::now().time_since_epoch()).count();
        }

        // onStatus codes that mean the server refused (a wrong key, a stream already live, ...).
        bool IsRefusal(const rtmp::ServerCommand& command)
        {
            return command.name == "_error" || command.level == "error" || command.code.find("Rejected") != std::string::npos ||
                command.code.find("BadName") != std::string::npos || command.code.find("Failed") != std::string::npos;
        }
    }

    RtmpClient::RtmpClient(std::unique_ptr<Transport> transport)
        : _transport(std::move(transport))
    {
    }

    PublishFailure RtmpClient::Handshake(const RtmpUrl& url, int timeoutMs, std::string& error)
    {
        if (!_transport->Connect(url.host, url.port, timeoutMs, error))
        {
            return PublishFailure::Unreachable;
        }

        Bytes c0c1 = rtmp::HandshakeC0C1();
        if (!_transport->SendAll(c0c1.data(), c0c1.size(), error))
        {
            return PublishFailure::Unreachable;
        }

        // S0, S1, and S2; C2 echoes S1.
        Bytes server;
        uint8_t buffer[4096];
        int64_t deadline = NowMs() + timeoutMs;
        while (server.size() < 1 + (2 * rtmp::HandshakeSize))
        {
            int64_t left = deadline - NowMs();
            int received = left > 0 ? _transport->Receive(buffer, sizeof(buffer), static_cast<int>(left), error) : 0;
            if (received <= 0)
            {
                error = received == 0 ? "the RTMP handshake timed out" : error;
                return PublishFailure::Unreachable;
            }

            server.insert(server.end(), buffer, buffer + received);
        }

        if (server[0] != 3)
        {
            error = "the server does not speak RTMP version 3";
            return PublishFailure::Unreachable;
        }

        if (!_transport->SendAll(server.data() + 1, rtmp::HandshakeSize, error))
        {
            return PublishFailure::Unreachable;
        }

        // Anything after S2 is already the server's first chunks.
        size_t used = 1 + (2 * rtmp::HandshakeSize);
        std::vector<rtmp::Message> messages;
        if (server.size() > used && !_reader.Feed(server.data() + used, server.size() - used, messages))
        {
            error = "the server sent data that is not RTMP";
            return PublishFailure::Lost;
        }

        for (const rtmp::Message& message : messages)
        {
            Handle(message, error);
        }

        return PublishFailure::None;
    }

    PublishFailure RtmpClient::ConnectApp(const RtmpUrl& url, bool av1, int timeoutMs, std::string& error)
    {
        if (!SendMessage(rtmp::ControlChunks, rtmp::SetChunkSizeMessage(rtmp::OutgoingChunkSize), error) ||
            !SendMessage(rtmp::CommandChunks, rtmp::Connect(url.app, url.tcUrl, av1), error))
        {
            return PublishFailure::Lost;
        }

        rtmp::ServerCommand answer;
        PublishFailure failure = Await(ConnectTransaction, timeoutMs, answer, error);
        if (failure == PublishFailure::None && IsRefusal(answer))
        {
            error = "the server refused connect (" + answer.code + ")";
            return PublishFailure::Rejected;
        }

        return failure;
    }

    PublishFailure RtmpClient::Publish(const RtmpUrl& url, const std::string& key, bool av1, int timeoutMs, std::string& error)
    {
        PublishFailure failure = Handshake(url, timeoutMs, error);
        if (failure == PublishFailure::None)
        {
            failure = ConnectApp(url, av1, timeoutMs, error);
        }

        if (failure != PublishFailure::None)
        {
            return failure;
        }

        if (!SendMessage(rtmp::CommandChunks, rtmp::ReleaseStream(ReleaseTransaction, key), error) ||
            !SendMessage(rtmp::CommandChunks, rtmp::FcPublish(FcPublishTransaction, key), error) ||
            !SendMessage(rtmp::CommandChunks, rtmp::CreateStream(CreateStreamTransaction), error))
        {
            return PublishFailure::Lost;
        }

        rtmp::ServerCommand answer;
        failure = Await(CreateStreamTransaction, timeoutMs, answer, error);
        if (failure != PublishFailure::None)
        {
            return failure;
        }

        if (IsRefusal(answer) || answer.number <= 0)
        {
            error = "the server refused createStream (" + answer.code + ")";
            return PublishFailure::Rejected;
        }

        _streamId = static_cast<uint32_t>(answer.number);
        if (!SendMessage(rtmp::CommandChunks, rtmp::Publish(_streamId, PublishTransaction, key), error))
        {
            return PublishFailure::Lost;
        }

        // publish is answered by onStatus; its code says whether the stream was taken.
        for (;;)
        {
            failure = Await(0, timeoutMs, answer, error);
            if (failure != PublishFailure::None)
            {
                return failure;
            }

            if (answer.code == "NetStream.Publish.Start")
            {
                _published = true;
                return PublishFailure::None;
            }

            if (IsRefusal(answer))
            {
                error = "the server refused publish (" + answer.code + ")";
                return PublishFailure::Rejected;
            }
        }
    }

    PublishFailure RtmpClient::TestConnect(const RtmpUrl& url, int timeoutMs, std::string& error)
    {
        PublishFailure failure = Handshake(url, timeoutMs, error);
        if (failure == PublishFailure::None)
        {
            failure = ConnectApp(url, false, timeoutMs, error);
        }

        _transport->Close();
        return failure;
    }

    PublishFailure RtmpClient::Await(double transaction, int timeoutMs, rtmp::ServerCommand& answer, std::string& error)
    {
        int64_t deadline = NowMs() + timeoutMs;
        uint8_t buffer[8192];
        for (;;)
        {
            for (size_t i = 0; i < _commands.size(); i++)
            {
                const rtmp::ServerCommand& command = _commands[i];
                bool matches = transaction == 0 ? command.name == "onStatus" : (command.transaction == transaction && (command.name == "_result" || command.name == "_error"));
                if (matches)
                {
                    answer = command;
                    _commands.erase(_commands.begin() + static_cast<ptrdiff_t>(i));
                    return PublishFailure::None;
                }
            }

            int64_t left = deadline - NowMs();
            if (left <= 0)
            {
                error = "the server did not answer in time";
                return PublishFailure::Unreachable;
            }

            int received = _transport->Receive(buffer, sizeof(buffer), static_cast<int>(left), error);
            if (received < 0)
            {
                return PublishFailure::Lost;
            }

            std::vector<rtmp::Message> messages;
            if (received > 0 && !_reader.Feed(buffer, static_cast<size_t>(received), messages))
            {
                error = "the server sent data that is not RTMP";
                return PublishFailure::Lost;
            }

            for (const rtmp::Message& message : messages)
            {
                if (!Handle(message, error))
                {
                    return PublishFailure::Lost;
                }
            }
        }
    }

    bool RtmpClient::Handle(const rtmp::Message& message, std::string& error)
    {
        switch (message.type)
        {
            case rtmp::WindowAckSize:
                if (message.payload.size() >= 4)
                {
                    _windowAckSize = GetU32(message.payload.data());
                }

                return true;
            case rtmp::UserControl:
                // A ping request (event 6) is answered with its timestamp (event 7).
                if (message.payload.size() >= 6 && GetU16(message.payload.data()) == 6)
                {
                    return SendMessage(rtmp::ControlChunks, rtmp::PingResponse(GetU32(message.payload.data() + 2)), error);
                }

                return true;
            case rtmp::CommandAmf0:
            {
                rtmp::ServerCommand command;
                if (rtmp::ReadCommand(message, command))
                {
                    _commands.push_back(command);
                }

                return true;
            }

            default:
                return true;
        }
    }

    bool RtmpClient::SendMessage(uint32_t chunkStream, const rtmp::Message& message, std::string& error)
    {
        Bytes out;
        rtmp::WriteChunks(out, chunkStream, message, chunkStream == rtmp::ControlChunks && message.type == rtmp::SetChunkSize ? 128 : rtmp::OutgoingChunkSize);
        if (!_transport->SendAll(out.data(), out.size(), error))
        {
            return false;
        }

        _sent += out.size();
        return true;
    }

    bool RtmpClient::Send(uint32_t chunkStream, rtmp::Message message, std::string& error)
    {
        message.streamId = _streamId;
        return SendMessage(chunkStream, message, error);
    }

    bool RtmpClient::Poll(std::string& error)
    {
        uint8_t buffer[8192];
        for (;;)
        {
            int received = _transport->Receive(buffer, sizeof(buffer), 0, error);
            if (received < 0)
            {
                return false;
            }

            if (received == 0)
            {
                break;
            }

            std::vector<rtmp::Message> messages;
            if (!_reader.Feed(buffer, static_cast<size_t>(received), messages))
            {
                error = "the server sent data that is not RTMP";
                return false;
            }

            for (const rtmp::Message& message : messages)
            {
                if (!Handle(message, error))
                {
                    return false;
                }
            }
        }

        // Commands the stream does not wait for (a late onStatus) are let go, but an error closes it.
        for (const rtmp::ServerCommand& command : _commands)
        {
            if (_published && IsRefusal(command))
            {
                error = "the server ended the stream (" + command.code + ")";
                _commands.clear();
                return false;
            }
        }

        _commands.clear();

        // Acknowledge what was received once a window's worth has come in, as RTMP asks of every peer.
        if (_reader.Received() - _acknowledged >= _windowAckSize)
        {
            _acknowledged = _reader.Received();
            return SendMessage(rtmp::ControlChunks, rtmp::AcknowledgementMessage(static_cast<uint32_t>(_acknowledged)), error);
        }

        return true;
    }

    void RtmpClient::Close(const std::string& key)
    {
        if (_published)
        {
            std::string ignored;
            SendMessage(rtmp::CommandChunks, rtmp::FcUnpublish(6, key), ignored);
            SendMessage(rtmp::CommandChunks, rtmp::DeleteStream(7, _streamId), ignored);
            _published = false;
        }

        _transport->Close();
    }
}
