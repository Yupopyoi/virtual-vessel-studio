// The connection under RTMP: TCP, or TLS over TCP through Windows' SChannel for rtmps (streaming detailed
// design 1, 12). Errors are short descriptions with Windows' codes; they never hold what was sent.
#pragma once

#include <winsock2.h>

#include <cstdint>
#include <memory>
#include <string>
#include <vector>

namespace vv::stream
{
    class Transport
    {
    public:
        virtual ~Transport() = default;

        // Connects within the timeout; on failure, error says what failed.
        virtual bool Connect(const std::string& host, uint16_t port, int timeoutMs, std::string& error) = 0;

        // Sends everything, or fails (the connection is then lost).
        virtual bool SendAll(const uint8_t* data, size_t length, std::string& error) = 0;

        // Receives what has arrived, waiting up to timeoutMs: bytes received, 0 when nothing came, -1 when
        // the connection is closed or failed.
        virtual int Receive(uint8_t* buffer, size_t capacity, int timeoutMs, std::string& error) = 0;

        virtual void Close() = 0;
    };

    class TcpTransport : public Transport
    {
    public:
        TcpTransport();
        ~TcpTransport() override;

        bool Connect(const std::string& host, uint16_t port, int timeoutMs, std::string& error) override;
        bool SendAll(const uint8_t* data, size_t length, std::string& error) override;
        int Receive(uint8_t* buffer, size_t capacity, int timeoutMs, std::string& error) override;
        void Close() override;

    private:
        SOCKET _socket = INVALID_SOCKET;
        bool _started = false;
    };

    // TLS over TCP. The server's certificate is checked as Windows normally checks it (the chain and the host
    // name); there is no way to skip it.
    std::unique_ptr<Transport> CreateTlsTransport();

    std::unique_ptr<Transport> CreateTransport(bool secure);
}
