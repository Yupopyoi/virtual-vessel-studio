// The pure parts of streaming (streaming detailed design 14): URLs, AMF0, FLV tags for H.264, AAC, and AV1,
// and RTMP chunks written and read back.

#include "amf0.h"
#include "flv.h"
#include "rtmp_protocol.h"
#include "rtmp_url.h"

#include <cstdio>
#include <string>

using namespace vv::stream;

namespace
{
    int s_failures = 0;

    void Check(bool condition, const char* what)
    {
        if (!condition)
        {
            std::printf("FAIL: %s\n", what);
            s_failures++;
        }
    }

    void Urls()
    {
        RtmpUrl url;
        Check(ParseRtmpUrl("rtmps://a.rtmps.youtube.com/live2", url) && url.secure && url.host == "a.rtmps.youtube.com" && url.port == 443 && url.app == "live2" &&
                url.tcUrl == "rtmps://a.rtmps.youtube.com/live2",
            "YouTube's RTMPS URL");
        Check(ParseRtmpUrl("rtmp://live.example.com:1936/app/instance/", url) && !url.secure && url.port == 1936 && url.app == "app/instance",
            "a port and an instance");
        Check(ParseRtmpUrl("rtmp://127.0.0.1/live", url) && url.port == 1935, "the default port");
        Check(!ParseRtmpUrl("https://a.rtmps.youtube.com/live2", url), "only rtmp and rtmps");
        Check(!ParseRtmpUrl("rtmp://host", url) && !ParseRtmpUrl("rtmp:///live", url) && !ParseRtmpUrl("rtmp://host:99999/live", url) && !ParseRtmpUrl("rtmp://host:/live", url),
            "a host and an application are needed");
    }

    void Amf()
    {
        Bytes out;
        amf0::WriteString(out, "_result");
        amf0::WriteNumber(out, 4);
        amf0::WriteNull(out);
        amf0::WriteNumber(out, 1);
        std::vector<amf0::Value> values;
        Check(amf0::ReadAll(out.data(), out.size(), values) && values.size() == 4 && values[0].text == "_result" && values[1].number == 4 && values[3].number == 1,
            "AMF0 written and read back");

        Bytes status;
        amf0::WriteString(status, "onStatus");
        amf0::WriteNumber(status, 0);
        amf0::WriteNull(status);
        amf0::WriteObjectStart(status);
        amf0::WriteKey(status, "level");
        amf0::WriteString(status, "status");
        amf0::WriteKey(status, "code");
        amf0::WriteString(status, "NetStream.Publish.Start");
        amf0::WriteObjectEnd(status);
        rtmp::Message message;
        message.type = rtmp::CommandAmf0;
        message.payload = status;
        rtmp::ServerCommand command;
        Check(rtmp::ReadCommand(message, command) && command.name == "onStatus" && command.code == "NetStream.Publish.Start" && command.level == "status",
            "onStatus's code is read");

        Bytes broken = status;
        broken.resize(broken.size() - 5);
        values.clear();
        Check(!amf0::ReadAll(broken.data(), broken.size(), values), "a cut-off answer is refused");
    }

    void H264()
    {
        const uint8_t stream[] = { 0, 0, 0, 1, 0x67, 0x64, 0x00, 0x28, 0xAC, 0, 0, 1, 0x68, 0xEE, 0x3C, 0x80, 0, 0, 0, 1, 0x65, 0x88, 0x84 };
        std::vector<flv::Span> nals = flv::SplitAnnexB(stream, sizeof(stream));
        Check(nals.size() == 3 && flv::NalType(nals[0]) == 7 && nals[0].length == 5 && flv::NalType(nals[1]) == 8 && nals[1].length == 4 && flv::NalType(nals[2]) == 5,
            "Annex B is split into SPS, PPS, and a slice");

        Bytes header = flv::AvcSequenceHeader(nals[0], nals[1]);
        Check(header.size() == 5 + 6 + 2 + 5 + 1 + 2 + 4 && header[0] == 0x17 && header[1] == 0 && header[6] == 0x64 && header[8] == 0x28 && header[9] == 0xFF && header[10] == 0xE1,
            "the AVC sequence header");

        Bytes frame = flv::AvcFrame(nals, true);
        Check(frame.size() == 5 + 4 + 3 && frame[0] == 0x17 && frame[1] == 1 && frame[8] == 3 && frame[9] == 0x65, "a keyframe keeps the slice and drops SPS and PPS");
        Check(flv::AvcFrame(nals, false)[0] == 0x27, "an inter frame");
    }

    void Aac()
    {
        Bytes config = flv::AacLc48kStereoConfig();
        Bytes header = flv::AacSequenceHeader(config.data(), config.size());
        Check(header == Bytes({ 0xAF, 0x00, 0x11, 0x90 }), "AAC-LC 48 kHz stereo sequence header");
        const uint8_t raw[] = { 1, 2, 3 };
        Check(flv::AacFrame(raw, 3) == Bytes({ 0xAF, 0x01, 1, 2, 3 }), "an AAC frame");
    }

    void Av1()
    {
        // A sequence header OBU (type 1, with size): profile 0, not reduced, no timing info, one operating point
        // at level 8 (4.0) tier 0; then a temporal delimiter and a frame OBU.
        // Bits: profile 000, still 0, reduced 0, timing 0, initial_display_delay 0, op count 00000,
        // idc 000000000000, level 01000, tier 0 (read, as the level is above 7):
        // 0000 0000 | 0000 0000 | 0000 0000 | 0100 0000
        const uint8_t sequence[] = { 0x0A, 0x04, 0x00, 0x00, 0x00, 0x40 };
        const uint8_t unit[] = { 0x12, 0x00, 0x0A, 0x04, 0x00, 0x00, 0x00, 0x40, 0x32, 0x02, 0xAA, 0xBB };
        auto obus = flv::SplitObus(unit, sizeof(unit));
        Check(obus.size() == 3 && obus[0].first == flv::ObuTemporalDelimiter && obus[1].first == flv::ObuSequenceHeader && obus[2].first == 6 && obus[2].second.length == 4,
            "a temporal unit is split into its OBUs");

        flv::Av1Info info;
        Check(flv::ParseSequenceHeader(sequence + 2, 4, info) && info.profile == 0 && info.level == 8 && info.tier == 0, "the sequence header's profile and level");

        Bytes start = flv::Av1SequenceStart(info, flv::Span{ sequence, sizeof(sequence) });
        Check(start.size() == 5 + 4 + sizeof(sequence) && start[0] == 0x90 && std::string(start.begin() + 1, start.begin() + 5) == "av01" && start[5] == 0x81 && start[6] == 0x08 &&
                start[7] == 0x0C,
            "the Enhanced RTMP sequence start with the AV1 configuration record");

        Bytes frames = flv::Av1Frames(unit, sizeof(unit), true);
        Check(frames.size() == 5 + 6 + 4 && frames[0] == 0x91 && frames[5] == 0x0A, "coded frames without the temporal delimiter");
        Check(flv::Av1Frames(unit, sizeof(unit), false)[0] == 0xA1, "an inter frame");
    }

    void Chunks()
    {
        rtmp::Message message;
        message.type = rtmp::Video;
        message.streamId = 1;
        message.timestamp = 1234;
        message.payload.resize(10000);
        for (size_t i = 0; i < message.payload.size(); i++)
        {
            message.payload[i] = static_cast<uint8_t>(i);
        }

        Bytes out;
        rtmp::WriteChunks(out, rtmp::VideoChunks, message, 4096);
        Check(out.size() == 12 + 10000 + 2, "a 10000-byte message in three chunks of 4096");

        // The reader gets the chunk size first, as a server would send it, then the message in pieces.
        rtmp::ChunkReader reader;
        Bytes size;
        rtmp::WriteChunks(size, rtmp::ControlChunks, rtmp::SetChunkSizeMessage(4096), 128);
        std::vector<rtmp::Message> messages;
        Check(reader.Feed(size.data(), size.size(), messages) && messages.size() == 1, "a set chunk size");
        messages.clear();
        for (size_t at = 0; at < out.size(); at += 700)
        {
            size_t run = out.size() - at < 700 ? out.size() - at : 700;
            reader.Feed(out.data() + at, run, messages);
        }

        Check(messages.size() == 1 && messages[0].payload == message.payload && messages[0].timestamp == 1234 && messages[0].streamId == 1 && messages[0].type == rtmp::Video,
            "the message is read back whole from pieces");

        message.timestamp = 0x1000000;
        out.clear();
        rtmp::WriteChunks(out, rtmp::VideoChunks, message, 4096);
        Check(out.size() == 12 + 4 + 10000 + (1 + 4) * 2, "an extended timestamp in every chunk");
        messages.clear();
        Check(reader.Feed(out.data(), out.size(), messages) && messages.size() == 1 && messages[0].timestamp == 0x1000000, "an extended timestamp is read back");

        Bytes c0c1 = rtmp::HandshakeC0C1();
        Check(c0c1.size() == 1 + rtmp::HandshakeSize && c0c1[0] == 3, "C0 and C1");

        Bytes bad{ 0x02, 0, 0, 0, 0xFF, 0xFF, 0xFF, 0x01, 0, 0, 0, 0 };
        rtmp::ChunkReader badReader;
        messages.clear();
        Check(!badReader.Feed(bad.data(), bad.size(), messages), "a message larger than allowed is refused");
    }
}

int main()
{
    Urls();
    Amf();
    H264();
    Aac();
    Av1();
    Chunks();
    std::printf(s_failures == 0 ? "PASS\n" : "FAIL (%d)\n", s_failures);
    return s_failures == 0 ? 0 : 1;
}
