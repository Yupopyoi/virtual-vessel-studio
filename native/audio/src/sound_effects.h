// SE voices (BGM and SE detailed design 8.2): sound effects held whole in memory, played from their start into a
// free voice, overlapping. Everything here runs under the engine's lock, which the mix also holds, so it needs no
// atomics; what is slow (decoding, freeing a large effect) happens outside the lock.
#pragma once

#include "block_provider.h"

#include <cstdint>
#include <memory>
#include <vector>

namespace vv::audio
{
    class SoundEffects final : public BlockProvider
    {
    public:
        static constexpr int MaxEffects = 64;
        static constexpr int MaxVoices = 16;

        // Holds an effect's 48 kHz stereo frames; false when all MaxEffects places are taken.
        bool Load(std::shared_ptr<const std::vector<float>> frames, int* effect);

        // Stops the effect's voices and gives its frames back, to be freed by the caller outside the lock.
        std::shared_ptr<const std::vector<float>> Unload(int effect);

        // Plays the effect from its start. With every voice busy, the one that started first fades out over a block
        // and gives its place. False for an effect not loaded.
        bool Play(int effect, float gainDb);
        void StopAll();

        // How many voices were taken from an effect still playing, for diagnostics.
        int64_t Stolen() const
        {
            return _stolen;
        }

        bool ReadBlock(float* block) override;

    private:
        struct Voice
        {
            int effect = -1; // -1 when free.
            size_t position = 0;
            float gain = 1.0f;
            bool releasing = false; // Fading out over this block to free its place.
            uint64_t started = 0;
        };

        std::shared_ptr<const std::vector<float>> _effects[MaxEffects];

        // MaxVoices play at once; the extra places hold voices fading out after being taken.
        Voice _voices[MaxVoices + 4];
        uint64_t _plays = 0;
        int64_t _stolen = 0;
    };
}
