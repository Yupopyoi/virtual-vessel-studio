// TLS over TCP with Windows' SChannel, for rtmps (streaming detailed design 1, 12). Windows checks the
// server's certificate chain and that it is for the host connected to (SCH_CRED_AUTO_CRED_VALIDATION with the
// host as the target name); a failed check fails the connection, and nothing here can turn it off.

#include "transport.h"

#define SECURITY_WIN32
#include <schannel.h>
#include <security.h>
#include <sspi.h>

#include <algorithm>
#include <cstring>
#include <string>
#include <vector>

#pragma comment(lib, "secur32.lib")

namespace vv::stream
{
    namespace
    {
        constexpr DWORD ContextRequests = ISC_REQ_SEQUENCE_DETECT | ISC_REQ_REPLAY_DETECT | ISC_REQ_CONFIDENTIALITY | ISC_REQ_EXTENDED_ERROR |
            ISC_REQ_ALLOCATE_MEMORY | ISC_REQ_STREAM;

        constexpr size_t ReceiveChunk = 16 * 1024;

        std::string SecurityError(const char* what, SECURITY_STATUS status)
        {
            char code[16];
            snprintf(code, sizeof(code), "0x%08lX", static_cast<unsigned long>(status));
            return std::string(what) + " failed (" + code + ")";
        }

        std::wstring Wide(const std::string& text)
        {
            int length = MultiByteToWideChar(CP_UTF8, 0, text.c_str(), -1, nullptr, 0);
            std::wstring wide(length > 0 ? static_cast<size_t>(length) : 1, L'\0');
            MultiByteToWideChar(CP_UTF8, 0, text.c_str(), -1, wide.data(), length);
            wide.resize(wcslen(wide.c_str()));
            return wide;
        }

        class TlsTransport : public Transport
        {
        public:
            ~TlsTransport() override
            {
                Close();
            }

            bool Connect(const std::string& host, uint16_t port, int timeoutMs, std::string& error) override
            {
                Close();
                if (!_tcp.Connect(host, port, timeoutMs, error))
                {
                    return false;
                }

                SCHANNEL_CRED credentials = {};
                credentials.dwVersion = SCHANNEL_CRED_VERSION;
                credentials.dwFlags = SCH_CRED_AUTO_CRED_VALIDATION | SCH_CRED_NO_DEFAULT_CREDS | SCH_USE_STRONG_CRYPTO;
                TimeStamp expiry;
                SECURITY_STATUS status = AcquireCredentialsHandleW(nullptr, const_cast<wchar_t*>(UNISP_NAME_W), SECPKG_CRED_OUTBOUND, nullptr, &credentials,
                    nullptr, nullptr, &_credentials, &expiry);
                if (status != SEC_E_OK)
                {
                    error = SecurityError("TLS credentials", status);
                    _tcp.Close();
                    return false;
                }

                _hasCredentials = true;
                _host = Wide(host);
                if (!Handshake(timeoutMs, error))
                {
                    Close();
                    return false;
                }

                QueryContextAttributes(&_context, SECPKG_ATTR_STREAM_SIZES, &_sizes);
                return true;
            }

            bool SendAll(const uint8_t* data, size_t length, std::string& error) override
            {
                std::vector<uint8_t> message(_sizes.cbHeader + _sizes.cbMaximumMessage + _sizes.cbTrailer);
                size_t at = 0;
                while (at < length)
                {
                    size_t run = std::min<size_t>(length - at, _sizes.cbMaximumMessage);
                    std::memcpy(message.data() + _sizes.cbHeader, data + at, run);
                    SecBuffer buffers[4] = {
                        { _sizes.cbHeader, SECBUFFER_STREAM_HEADER, message.data() },
                        { static_cast<unsigned long>(run), SECBUFFER_DATA, message.data() + _sizes.cbHeader },
                        { _sizes.cbTrailer, SECBUFFER_STREAM_TRAILER, message.data() + _sizes.cbHeader + run },
                        { 0, SECBUFFER_EMPTY, nullptr },
                    };
                    SecBufferDesc description = { SECBUFFER_VERSION, 4, buffers };
                    SECURITY_STATUS status = EncryptMessage(&_context, 0, &description, 0);
                    if (status != SEC_E_OK)
                    {
                        error = SecurityError("TLS encrypt", status);
                        return false;
                    }

                    size_t total = buffers[0].cbBuffer + buffers[1].cbBuffer + buffers[2].cbBuffer;
                    if (!_tcp.SendAll(message.data(), total, error))
                    {
                        return false;
                    }

                    at += run;
                }

                return true;
            }

            int Receive(uint8_t* buffer, size_t capacity, int timeoutMs, std::string& error) override
            {
                for (;;)
                {
                    if (!_plain.empty())
                    {
                        size_t run = std::min(capacity, _plain.size());
                        std::memcpy(buffer, _plain.data(), run);
                        _plain.erase(_plain.begin(), _plain.begin() + static_cast<ptrdiff_t>(run));
                        return static_cast<int>(run);
                    }

                    if (!_encrypted.empty())
                    {
                        int decrypted = Decrypt(error);
                        if (decrypted < 0)
                        {
                            return -1;
                        }

                        if (decrypted > 0)
                        {
                            continue;
                        }
                    }

                    // More is needed: wait for it.
                    uint8_t chunk[ReceiveChunk];
                    int received = _tcp.Receive(chunk, sizeof(chunk), timeoutMs, error);
                    if (received <= 0)
                    {
                        return received;
                    }

                    _encrypted.insert(_encrypted.end(), chunk, chunk + received);
                }
            }

            void Close() override
            {
                if (_hasContext)
                {
                    DeleteSecurityContext(&_context);
                    _hasContext = false;
                }

                if (_hasCredentials)
                {
                    FreeCredentialsHandle(&_credentials);
                    _hasCredentials = false;
                }

                _tcp.Close();
                _encrypted.clear();
                _plain.clear();
            }

        private:
            // The TLS handshake, sending each token InitializeSecurityContext gives and feeding it what comes back,
            // until the context is complete. Data that arrives with the last message is kept for Receive.
            bool Handshake(int timeoutMs, std::string& error)
            {
                SecBuffer outBuffer = { 0, SECBUFFER_TOKEN, nullptr };
                SecBufferDesc outDescription = { SECBUFFER_VERSION, 1, &outBuffer };
                DWORD flags = 0;
                TimeStamp expiry;
                SECURITY_STATUS status = InitializeSecurityContextW(&_credentials, nullptr, const_cast<wchar_t*>(_host.c_str()), ContextRequests, 0, 0, nullptr,
                    0, &_context, &outDescription, &flags, &expiry);
                if (status != SEC_I_CONTINUE_NEEDED)
                {
                    error = SecurityError("TLS start", status);
                    return false;
                }

                _hasContext = true;
                if (!SendToken(outBuffer, error))
                {
                    return false;
                }

                std::vector<uint8_t> received;
                for (;;)
                {
                    uint8_t chunk[ReceiveChunk];
                    int length = _tcp.Receive(chunk, sizeof(chunk), timeoutMs, error);
                    if (length <= 0)
                    {
                        error = length == 0 ? "TLS handshake timed out" : error;
                        return false;
                    }

                    received.insert(received.end(), chunk, chunk + length);
                    for (;;)
                    {
                        SecBuffer inBuffers[2] = {
                            { static_cast<unsigned long>(received.size()), SECBUFFER_TOKEN, received.data() },
                            { 0, SECBUFFER_EMPTY, nullptr },
                        };
                        SecBufferDesc inDescription = { SECBUFFER_VERSION, 2, inBuffers };
                        outBuffer = { 0, SECBUFFER_TOKEN, nullptr };
                        status = InitializeSecurityContextW(&_credentials, &_context, const_cast<wchar_t*>(_host.c_str()), ContextRequests, 0, 0,
                            &inDescription, 0, nullptr, &outDescription, &flags, &expiry);
                        if (status == SEC_E_INCOMPLETE_MESSAGE)
                        {
                            break; // Wait for the rest of the server's message.
                        }

                        if (status == SEC_E_OK || status == SEC_I_CONTINUE_NEEDED)
                        {
                            if (!SendToken(outBuffer, error))
                            {
                                return false;
                            }

                            KeepExtra(inBuffers[1], received);
                            if (status == SEC_E_OK)
                            {
                                _encrypted = received;
                                return true;
                            }

                            if (received.empty())
                            {
                                break;
                            }

                            continue;
                        }

                        FreeContextBuffer(outBuffer.pvBuffer);
                        error = SecurityError("TLS handshake (a certificate the server's name does not match, or one Windows does not trust, fails here)", status);
                        return false;
                    }
                }
            }

            bool SendToken(SecBuffer& token, std::string& error)
            {
                bool ok = true;
                if (token.cbBuffer > 0 && token.pvBuffer != nullptr)
                {
                    ok = _tcp.SendAll(static_cast<const uint8_t*>(token.pvBuffer), token.cbBuffer, error);
                }

                if (token.pvBuffer != nullptr)
                {
                    FreeContextBuffer(token.pvBuffer);
                    token.pvBuffer = nullptr;
                }

                return ok;
            }

            // Leaves in data only the bytes SChannel did not use (SECBUFFER_EXTRA), which belong to the next message.
            static void KeepExtra(const SecBuffer& extra, std::vector<uint8_t>& data)
            {
                if (extra.BufferType == SECBUFFER_EXTRA && extra.cbBuffer > 0 && extra.cbBuffer <= data.size())
                {
                    data.erase(data.begin(), data.end() - extra.cbBuffer);
                }
                else
                {
                    data.clear();
                }
            }

            // Decrypts the next record into _plain: 1 when something was decrypted, 0 when more must arrive, -1 on failure.
            int Decrypt(std::string& error)
            {
                SecBuffer buffers[4] = {
                    { static_cast<unsigned long>(_encrypted.size()), SECBUFFER_DATA, _encrypted.data() },
                    { 0, SECBUFFER_EMPTY, nullptr },
                    { 0, SECBUFFER_EMPTY, nullptr },
                    { 0, SECBUFFER_EMPTY, nullptr },
                };
                SecBufferDesc description = { SECBUFFER_VERSION, 4, buffers };
                SECURITY_STATUS status = DecryptMessage(&_context, &description, 0, nullptr);
                if (status == SEC_E_INCOMPLETE_MESSAGE)
                {
                    return 0;
                }

                if (status == SEC_I_CONTEXT_EXPIRED)
                {
                    error = "the server closed the TLS session";
                    return -1;
                }

                if (status != SEC_E_OK && status != SEC_I_RENEGOTIATE)
                {
                    error = SecurityError("TLS decrypt", status);
                    return -1;
                }

                std::vector<uint8_t> extra;
                for (const SecBuffer& buffer : buffers)
                {
                    if (buffer.BufferType == SECBUFFER_DATA && buffer.cbBuffer > 0)
                    {
                        const uint8_t* plain = static_cast<const uint8_t*>(buffer.pvBuffer);
                        _plain.insert(_plain.end(), plain, plain + buffer.cbBuffer);
                    }
                    else if (buffer.BufferType == SECBUFFER_EXTRA && buffer.cbBuffer > 0)
                    {
                        const uint8_t* more = static_cast<const uint8_t*>(buffer.pvBuffer);
                        extra.assign(more, more + buffer.cbBuffer);
                    }
                }

                _encrypted.swap(extra);

                // TLS 1.3 sends handshake messages after the handshake (session tickets); SChannel asks for them
                // to go back through InitializeSecurityContext, which needs nothing sent back here.
                if (status == SEC_I_RENEGOTIATE)
                {
                    SecBuffer inBuffers[2] = {
                        { static_cast<unsigned long>(_encrypted.size()), SECBUFFER_TOKEN, _encrypted.empty() ? nullptr : _encrypted.data() },
                        { 0, SECBUFFER_EMPTY, nullptr },
                    };
                    SecBufferDesc inDescription = { SECBUFFER_VERSION, 2, inBuffers };
                    SecBuffer outBuffer = { 0, SECBUFFER_TOKEN, nullptr };
                    SecBufferDesc outDescription = { SECBUFFER_VERSION, 1, &outBuffer };
                    DWORD flags = 0;
                    TimeStamp expiry;
                    SECURITY_STATUS renewed = InitializeSecurityContextW(&_credentials, &_context, const_cast<wchar_t*>(_host.c_str()), ContextRequests, 0, 0,
                        &inDescription, 0, nullptr, &outDescription, &flags, &expiry);
                    if (!SendToken(outBuffer, error))
                    {
                        return -1;
                    }

                    if (renewed != SEC_E_OK && renewed != SEC_I_CONTINUE_NEEDED && renewed != SEC_E_INCOMPLETE_MESSAGE)
                    {
                        error = SecurityError("TLS renegotiation", renewed);
                        return -1;
                    }

                    KeepExtra(inBuffers[1], _encrypted);
                }

                return 1;
            }

            TcpTransport _tcp;
            CredHandle _credentials = {};
            CtxtHandle _context = {};
            bool _hasCredentials = false;
            bool _hasContext = false;
            std::wstring _host;
            SecPkgContext_StreamSizes _sizes = {};
            std::vector<uint8_t> _encrypted;
            std::vector<uint8_t> _plain;
        };
    }

    std::unique_ptr<Transport> CreateTlsTransport()
    {
        return std::make_unique<TlsTransport>();
    }
}
