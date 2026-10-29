#include "transport.h"

#include <ws2tcpip.h>

#include <string>

namespace vv::stream
{
    namespace
    {
        // A send that cannot finish in this time means the connection is gone.
        constexpr DWORD SendTimeoutMs = 10'000;

        std::string WsaError(const char* what)
        {
            return std::string(what) + " failed (WSA " + std::to_string(WSAGetLastError()) + ")";
        }
    }

    TcpTransport::TcpTransport()
    {
        WSADATA data;
        _started = WSAStartup(MAKEWORD(2, 2), &data) == 0;
    }

    TcpTransport::~TcpTransport()
    {
        Close();
        if (_started)
        {
            WSACleanup();
        }
    }

    bool TcpTransport::Connect(const std::string& host, uint16_t port, int timeoutMs, std::string& error)
    {
        Close();
        addrinfo hints = {};
        hints.ai_family = AF_UNSPEC;
        hints.ai_socktype = SOCK_STREAM;
        hints.ai_protocol = IPPROTO_TCP;
        addrinfo* found = nullptr;
        if (getaddrinfo(host.c_str(), std::to_string(port).c_str(), &hints, &found) != 0 || found == nullptr)
        {
            error = WsaError("name resolution");
            return false;
        }

        for (addrinfo* address = found; address != nullptr && _socket == INVALID_SOCKET; address = address->ai_next)
        {
            SOCKET candidate = socket(address->ai_family, address->ai_socktype, address->ai_protocol);
            if (candidate == INVALID_SOCKET)
            {
                continue;
            }

            // Connect without blocking, so the timeout holds; then back to blocking for sending.
            u_long nonBlocking = 1;
            ioctlsocket(candidate, FIONBIO, &nonBlocking);
            int result = connect(candidate, address->ai_addr, static_cast<int>(address->ai_addrlen));
            if (result == SOCKET_ERROR && WSAGetLastError() == WSAEWOULDBLOCK)
            {
                fd_set writable;
                fd_set failed;
                FD_ZERO(&writable);
                FD_ZERO(&failed);
                FD_SET(candidate, &writable);
                FD_SET(candidate, &failed);
                timeval timeout = { timeoutMs / 1000, (timeoutMs % 1000) * 1000 };
                result = select(0, nullptr, &writable, &failed, &timeout) == 1 && FD_ISSET(candidate, &writable) ? 0 : SOCKET_ERROR;
            }

            if (result == 0)
            {
                u_long blocking = 0;
                ioctlsocket(candidate, FIONBIO, &blocking);
                BOOL noDelay = TRUE;
                setsockopt(candidate, IPPROTO_TCP, TCP_NODELAY, reinterpret_cast<const char*>(&noDelay), sizeof(noDelay));
                DWORD sendTimeout = SendTimeoutMs;
                setsockopt(candidate, SOL_SOCKET, SO_SNDTIMEO, reinterpret_cast<const char*>(&sendTimeout), sizeof(sendTimeout));
                _socket = candidate;
            }
            else
            {
                error = WsaError("connect");
                closesocket(candidate);
            }
        }

        freeaddrinfo(found);
        return _socket != INVALID_SOCKET;
    }

    bool TcpTransport::SendAll(const uint8_t* data, size_t length, std::string& error)
    {
        size_t sent = 0;
        while (sent < length)
        {
            int run = send(_socket, reinterpret_cast<const char*>(data + sent), static_cast<int>(length - sent < 1 << 20 ? length - sent : 1 << 20), 0);
            if (run <= 0)
            {
                error = WsaError("send");
                return false;
            }

            sent += static_cast<size_t>(run);
        }

        return true;
    }

    int TcpTransport::Receive(uint8_t* buffer, size_t capacity, int timeoutMs, std::string& error)
    {
        fd_set readable;
        FD_ZERO(&readable);
        FD_SET(_socket, &readable);
        timeval timeout = { timeoutMs / 1000, (timeoutMs % 1000) * 1000 };
        int ready = select(0, &readable, nullptr, nullptr, &timeout);
        if (ready == 0)
        {
            return 0;
        }

        if (ready < 0)
        {
            error = WsaError("select");
            return -1;
        }

        int received = recv(_socket, reinterpret_cast<char*>(buffer), static_cast<int>(capacity), 0);
        if (received <= 0)
        {
            error = received == 0 ? "the server closed the connection" : WsaError("recv");
            return -1;
        }

        return received;
    }

    void TcpTransport::Close()
    {
        if (_socket != INVALID_SOCKET)
        {
            shutdown(_socket, SD_BOTH);
            closesocket(_socket);
            _socket = INVALID_SOCKET;
        }
    }

    std::unique_ptr<Transport> CreateTransport(bool secure)
    {
        if (secure)
        {
            return CreateTlsTransport();
        }

        return std::make_unique<TcpTransport>();
    }
}
