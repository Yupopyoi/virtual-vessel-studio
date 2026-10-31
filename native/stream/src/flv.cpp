#include "flv.h"

#include "amf0.h"

namespace vv::stream::flv
{
    namespace
    {
        constexpr uint8_t AvcKeyframe = 0x17;
        constexpr uint8_t AvcInterframe = 0x27;
        constexpr uint8_t AvcSequence = 0x00;
        constexpr uint8_t AvcNalu = 0x01;

        // Sound format 10 (AAC), 44 kHz flag, 16-bit, stereo: the fixed first byte of AAC in FLV.
        constexpr uint8_t AacHeader = 0xAF;

        // The extended video tag header: IsExHeader, the frame type, and the packet type.
        constexpr uint8_t ExHeader = 0x80;
        constexpr uint8_t ExKeyframe = 1 << 4;
        constexpr uint8_t ExInterframe = 2 << 4;
        constexpr uint8_t PacketSequenceStart = 0;
        constexpr uint8_t PacketCodedFrames = 1;

        // AV1 as an AMF number in onMetaData: the FourCC read as a big-endian integer.
        constexpr double Av1FourCc = static_cast<double>(('a' << 24) | ('v' << 16) | ('0' << 8) | '1');

        void PutFourCc(Bytes& out)
        {
            PutText(out, "av01");
        }

        // Reads AV1's leb128 sizes.
        bool ReadLeb128(const uint8_t* data, size_t length, size_t& at, uint64_t& value)
        {
            value = 0;
            for (int i = 0; i < 8; i++)
            {
                if (at >= length)
                {
                    return false;
                }

                uint8_t byte = data[at++];
                value |= static_cast<uint64_t>(byte & 0x7F) << (i * 7);
                if ((byte & 0x80) == 0)
                {
                    return true;
                }
            }

            return false;
        }

        class BitReader
        {
        public:
            BitReader(const uint8_t* data, size_t length)
                : _data(data), _length(length)
            {
            }

            bool Ok() const
            {
                return _ok;
            }

            uint32_t Read(int bits)
            {
                uint32_t value = 0;
                for (int i = 0; i < bits; i++)
                {
                    if (_bit >= _length * 8)
                    {
                        _ok = false;
                        return 0;
                    }

                    value = (value << 1) | ((_data[_bit / 8] >> (7 - (_bit % 8))) & 1);
                    _bit++;
                }

                return value;
            }

            // uvlc(): leading zeros, then that many bits.
            void SkipUvlc()
            {
                int zeros = 0;
                while (_ok && Read(1) == 0)
                {
                    zeros++;
                    if (zeros >= 32)
                    {
                        _ok = false;
                        return;
                    }
                }

                Read(zeros);
            }

        private:
            const uint8_t* _data;
            size_t _length;
            size_t _bit = 0;
            bool _ok = true;
        };
    }

    std::vector<Span> SplitAnnexB(const uint8_t* data, size_t length)
    {
        std::vector<Span> nals;
        size_t start = SIZE_MAX;
        size_t i = 0;
        while (i + 2 < length)
        {
            if (data[i] == 0 && data[i + 1] == 0 && data[i + 2] == 1)
            {
                if (start != SIZE_MAX)
                {
                    size_t end = i;
                    while (end > start && data[end - 1] == 0)
                    {
                        end--; // The extra zero of a 4-byte start code belongs to no NAL unit.
                    }

                    nals.push_back({ data + start, end - start });
                }

                i += 3;
                start = i;
            }
            else
            {
                i++;
            }
        }

        if (start != SIZE_MAX && start < length)
        {
            nals.push_back({ data + start, length - start });
        }

        return nals;
    }

    Bytes AvcSequenceHeader(const Span& sps, const Span& pps)
    {
        Bytes out;
        PutU8(out, AvcKeyframe);
        PutU8(out, AvcSequence);
        PutU24(out, 0);
        PutU8(out, 1);                                      // configurationVersion
        PutU8(out, sps.length > 1 ? sps.data[1] : 0x64);    // AVCProfileIndication
        PutU8(out, sps.length > 2 ? sps.data[2] : 0x00);    // profile_compatibility
        PutU8(out, sps.length > 3 ? sps.data[3] : 0x28);    // AVCLevelIndication
        PutU8(out, 0xFF);                                   // 4-byte NAL unit lengths
        PutU8(out, 0xE1);                                   // one SPS
        PutU16(out, static_cast<uint32_t>(sps.length));
        PutBytes(out, sps.data, sps.length);
        PutU8(out, 1);                                      // one PPS
        PutU16(out, static_cast<uint32_t>(pps.length));
        PutBytes(out, pps.data, pps.length);
        return out;
    }

    Bytes AvcFrame(const std::vector<Span>& nals, bool keyframe)
    {
        Bytes out;
        PutU8(out, keyframe ? AvcKeyframe : AvcInterframe);
        PutU8(out, AvcNalu);
        PutU24(out, 0);
        for (const Span& nal : nals)
        {
            int type = NalType(nal);
            if (type == 7 || type == 8 || type == 9)
            {
                continue;
            }

            PutU32(out, static_cast<uint32_t>(nal.length));
            PutBytes(out, nal.data, nal.length);
        }

        return out;
    }

    Bytes AacSequenceHeader(const uint8_t* audioSpecificConfig, size_t length)
    {
        Bytes out{ AacHeader, 0x00 };
        PutBytes(out, audioSpecificConfig, length);
        return out;
    }

    Bytes AacFrame(const uint8_t* raw, size_t length)
    {
        Bytes out{ AacHeader, 0x01 };
        PutBytes(out, raw, length);
        return out;
    }

    std::vector<std::pair<int, Span>> SplitObus(const uint8_t* data, size_t length)
    {
        std::vector<std::pair<int, Span>> obus;
        size_t at = 0;
        while (at < length)
        {
            size_t begin = at;
            uint8_t header = data[at++];
            int type = (header >> 3) & 0x0F;
            bool extension = (header & 0x04) != 0;
            bool hasSize = (header & 0x02) != 0;
            if (extension)
            {
                at++;
            }

            uint64_t size = length - at;
            if (hasSize && !ReadLeb128(data, length, at, size))
            {
                break;
            }

            if (at + size > length)
            {
                break;
            }

            at += static_cast<size_t>(size);
            obus.push_back({ type, Span{ data + begin, at - begin } });
        }

        return obus;
    }

    bool ParseSequenceHeader(const uint8_t* payload, size_t length, Av1Info& info)
    {
        BitReader bits(payload, length);
        info.profile = static_cast<int>(bits.Read(3));
        bits.Read(1); // still_picture
        bool reduced = bits.Read(1) != 0;
        if (reduced)
        {
            info.level = static_cast<int>(bits.Read(5));
            info.tier = 0;
            return bits.Ok();
        }

        bool timingInfo = bits.Read(1) != 0;
        if (timingInfo)
        {
            bits.Read(32); // num_units_in_display_tick
            bits.Read(32); // time_scale
            if (bits.Read(1) != 0)
            {
                bits.SkipUvlc(); // num_ticks_per_picture_minus_1
            }

            if (bits.Read(1) != 0) // decoder_model_info_present_flag
            {
                bits.Read(5);  // buffer_delay_length_minus_1
                bits.Read(32); // num_units_in_decoding_tick
                bits.Read(5);  // buffer_removal_time_length_minus_1
                bits.Read(5);  // frame_presentation_time_length_minus_1
            }
        }

        bits.Read(1); // initial_display_delay_present_flag
        bits.Read(5); // operating_points_cnt_minus_1

        // Only the first operating point is needed: its level and tier.
        bits.Read(12); // operating_point_idc[0]
        info.level = static_cast<int>(bits.Read(5));
        info.tier = info.level > 7 ? static_cast<int>(bits.Read(1)) : 0;
        return bits.Ok();
    }

    Bytes Av1SequenceStart(const Av1Info& info, const Span& sequenceHeaderObu)
    {
        Bytes out;
        PutU8(out, ExHeader | ExKeyframe | PacketSequenceStart);
        PutFourCc(out);
        PutU8(out, 0x81); // marker, version 1
        PutU8(out, static_cast<uint32_t>(((info.profile & 0x07) << 5) | (info.level & 0x1F)));

        // tier, high_bitdepth 0, twelve_bit 0, monochrome 0, subsampling x 1, y 1, chroma position 0 (8-bit 4:2:0).
        PutU8(out, static_cast<uint32_t>(((info.tier & 1) << 7) | 0x0C));
        PutU8(out, 0); // no initial presentation delay
        PutBytes(out, sequenceHeaderObu.data, sequenceHeaderObu.length);
        return out;
    }

    Bytes Av1Frames(const uint8_t* temporalUnit, size_t length, bool keyframe)
    {
        Bytes out;
        PutU8(out, ExHeader | (keyframe ? ExKeyframe : ExInterframe) | PacketCodedFrames);
        PutFourCc(out);
        for (const auto& obu : SplitObus(temporalUnit, length))
        {
            if (obu.first != ObuTemporalDelimiter)
            {
                PutBytes(out, obu.second.data, obu.second.length);
            }
        }

        return out;
    }

    Bytes MetaData(const StreamInfo& info)
    {
        Bytes out;
        amf0::WriteString(out, "@setDataFrame");
        amf0::WriteString(out, "onMetaData");
        amf0::WriteEcmaArrayStart(out, 11);
        auto number = [&out](const char* key, double value)
        {
            amf0::WriteKey(out, key);
            amf0::WriteNumber(out, value);
        };

        number("width", info.width);
        number("height", info.height);
        number("framerate", info.frameRate);
        number("videocodecid", info.av1 ? Av1FourCc : 7.0);
        number("videodatarate", info.videoKbps);
        number("audiocodecid", 10.0);
        number("audiodatarate", info.audioKbps);
        number("audiosamplerate", 48000.0);
        number("audiosamplesize", 16.0);
        amf0::WriteKey(out, "stereo");
        amf0::WriteBoolean(out, true);
        amf0::WriteKey(out, "encoder");
        amf0::WriteString(out, "Virtual Vessel Studio");
        amf0::WriteObjectEnd(out);
        return out;
    }
}
