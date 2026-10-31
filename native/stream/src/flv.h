// FLV tag bodies, as RTMP carries them in its audio and video messages (streaming detailed design 8.1):
// H.264 (AVC), AAC, and AV1 in Enhanced RTMP. Pure.
#pragma once

#include "bytes.h"

#include <cstddef>
#include <cstdint>
#include <utility>
#include <vector>

namespace vv::stream::flv
{
    // A run of bytes inside a buffer.
    struct Span
    {
        const uint8_t* data = nullptr;
        size_t length = 0;
    };

    // ---- H.264

    // The NAL units of an Annex B stream (start codes 00 00 01 or 00 00 00 01), without the start codes.
    std::vector<Span> SplitAnnexB(const uint8_t* data, size_t length);

    inline int NalType(const Span& nal)
    {
        return nal.length > 0 ? (nal.data[0] & 0x1F) : -1;
    }

    // The sequence header (AVCDecoderConfigurationRecord) from an SPS and a PPS.
    Bytes AvcSequenceHeader(const Span& sps, const Span& pps);

    // A coded picture: its NAL units with 4-byte lengths, without SPS, PPS, and access unit delimiters, which
    // the sequence header carries or RTMP does not need. Composition time 0: the encoders run without
    // B-frames (low latency), so pictures come in display order.
    Bytes AvcFrame(const std::vector<Span>& nals, bool keyframe);

    // ---- AAC

    Bytes AacSequenceHeader(const uint8_t* audioSpecificConfig, size_t length);
    Bytes AacFrame(const uint8_t* raw, size_t length);

    // The AudioSpecificConfig of AAC-LC at 48 kHz, stereo (object type 2, frequency index 3, 2 channels).
    inline Bytes AacLc48kStereoConfig()
    {
        return Bytes{ 0x11, 0x90 };
    }

    // ---- AV1 (Enhanced RTMP: an extended video tag header with the FourCC "av01")

    enum ObuType : int
    {
        ObuSequenceHeader = 1,
        ObuTemporalDelimiter = 2,
    };

    // The OBUs of a temporal unit (low overhead format, each with its size field).
    std::vector<std::pair<int, Span>> SplitObus(const uint8_t* data, size_t length);

    // What the AV1CodecConfigurationRecord needs from a sequence header OBU's payload; false when it cannot
    // be read. The color config is taken as 8-bit 4:2:0, which is what the encoders are given (NV12).
    struct Av1Info
    {
        int profile = 0;
        int level = 0;
        int tier = 0;
    };

    bool ParseSequenceHeader(const uint8_t* payload, size_t length, Av1Info& info);

    // The sequence start: the AV1CodecConfigurationRecord with the sequence header OBU (whole, with its header).
    Bytes Av1SequenceStart(const Av1Info& info, const Span& sequenceHeaderObu);

    // Coded frames: the temporal unit's OBUs without temporal delimiters.
    Bytes Av1Frames(const uint8_t* temporalUnit, size_t length, bool keyframe);

    // ---- Metadata

    struct StreamInfo
    {
        int width = 0;
        int height = 0;
        double frameRate = 0;
        int videoKbps = 0;
        int audioKbps = 0;
        bool av1 = false;
    };

    // The "@setDataFrame" "onMetaData" data message.
    Bytes MetaData(const StreamInfo& info);
}
