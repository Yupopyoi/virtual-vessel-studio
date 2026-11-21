// A source the mix reads from a player instead of a device (BGM and SE detailed design 4): the BGM deck and the SE
// voices. The mix calls it once per block on its thread, under the engine's lock.
#pragma once

namespace vv::audio
{
    class BlockProvider
    {
    public:
        virtual ~BlockProvider() = default;

        // Writes one block of BlockFrames stereo frames; false when it has nothing to play, and the block is ignored.
        virtual bool ReadBlock(float* block) = 0;
    };
}
