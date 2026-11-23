#include "bgm_crossfade.h"

#include "mixer.h"

#include <algorithm>
#include <cstring>

namespace vv::audio
{
    BgmCrossfader::BgmCrossfader(BgmDeck::Opener opener)
        : _other(static_cast<size_t>(BlockFrames) * Channels)
    {
        // One count for both decks, so a play's generation names it whichever deck has it.
        auto generations = std::make_shared<std::atomic<int64_t>>(0);
        _decks[0] = std::make_unique<BgmDeck>(opener, generations);
        _decks[1] = std::make_unique<BgmDeck>(opener, generations);
    }

    int64_t BgmCrossfader::Play(const std::wstring& path, bool loop, float gainDb)
    {
        std::lock_guard<std::mutex> guard(_lock);
        BgmState state = _decks[_current]->Status().state;
        bool playing = state == BgmState::Starting || state == BgmState::Playing;
        int frames = _crossfadeFrames.load();
        if (!Crossfades(playing, frames))
        {
            return _decks[_current]->Play(path, loop, gainDb);
        }

        // The old track comes down while the new one comes up over the same length, on the other deck.
        _decks[_current]->Stop(frames);
        _current = 1 - _current;
        return _decks[_current]->Play(path, loop, gainDb, frames);
    }

    void BgmCrossfader::Stop()
    {
        std::lock_guard<std::mutex> guard(_lock);
        _decks[0]->Stop();
        _decks[1]->Stop();
    }

    void BgmCrossfader::SetGain(float gainDb)
    {
        std::lock_guard<std::mutex> guard(_lock);
        _decks[_current]->SetGain(gainDb);
    }

    BgmStatus BgmCrossfader::Status() const
    {
        std::lock_guard<std::mutex> guard(_lock);
        BgmStatus status = _decks[_current]->Status();
        BgmStatus other = _decks[1 - _current]->Status();
        status.underruns += other.underruns;

        // A track not looped that ends on either deck is named, whichever was asked last.
        status.finishedGeneration = std::max(status.finishedGeneration, other.finishedGeneration);
        return status;
    }

    void BgmCrossfader::SetCrossfade(int frames)
    {
        _crossfadeFrames = std::clamp(frames, 0, MaxCrossfadeFrames);
    }

    bool BgmCrossfader::ReadBlock(float* block)
    {
        bool first = _decks[0]->ReadBlock(block);
        if (!first)
        {
            std::memset(block, 0, _other.size() * sizeof(float));
        }

        bool second = _decks[1]->ReadBlock(_other.data());
        if (second)
        {
            for (size_t i = 0; i < _other.size(); i++)
            {
                block[i] += _other[i];
            }
        }

        return first || second;
    }
}
