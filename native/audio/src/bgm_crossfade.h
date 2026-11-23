// The crossfade when switching tracks (BGM and SE detailed design 8.5): two BGM decks taken in turn. Switching while
// a track plays brings the old deck down while the other brings the new track up, and the mix hears the two added
// as one source.
#pragma once

#include "bgm_deck.h"
#include "mixer.h"

#include <array>
#include <atomic>
#include <memory>
#include <mutex>
#include <vector>

namespace vv::audio
{
    class BgmCrossfader final : public BlockProvider
    {
    public:
        explicit BgmCrossfader(BgmDeck::Opener opener = OpenMediaFile);

        BgmCrossfader(const BgmCrossfader&) = delete;
        BgmCrossfader& operator=(const BgmCrossfader&) = delete;

        // Any thread but the mix's, like a deck's.
        int64_t Play(const std::wstring& path, bool loop, float gainDb);
        void Stop();
        void SetGain(float gainDb);
        BgmStatus Status() const;

        // The length of the next switches, in frames at 48 kHz; 0 switches as one deck does.
        void SetCrossfade(int frames);

        static constexpr int MaxCrossfadeFrames = SampleRate * 5;

        // The mix thread.
        bool ReadBlock(float* block) override;

        // Whether a switch crossfades: something plays, and the length is not 0. Pure.
        static bool Crossfades(bool playing, int frames)
        {
            return playing && frames > 0;
        }

    private:
        std::array<std::unique_ptr<BgmDeck>, 2> _decks;

        // The API's side: which deck was asked last.
        mutable std::mutex _lock;
        int _current = 0;
        std::atomic<int> _crossfadeFrames{ 0 };

        // The mix thread only: the other deck's block, added to the first.
        std::vector<float> _other;
    };
}
