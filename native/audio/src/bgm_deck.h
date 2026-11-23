// The BGM deck (BGM and SE detailed design 8.1): its own thread decodes the track ahead into a ring, and the mix
// thread reads a block from it every 10 ms, so the mix never touches a file. Fades happen on the mix's side, over
// what is already in the ring, so a stop fades at once rather than after the seconds read ahead.
#pragma once

#include "block_provider.h"
#include "clip_source.h"
#include "frame_ring.h"

#include <atomic>
#include <condition_variable>
#include <cstdint>
#include <functional>
#include <memory>
#include <mutex>
#include <string>
#include <thread>

namespace vv::audio
{
    enum class BgmState : int32_t
    {
        Idle = 0,
        Starting = 1, // Opening and reading ahead.
        Playing = 2,
        Stopping = 3, // Fading out.
        Failed = 4,   // The last track could not be opened or read; error has the HRESULT.
    };

    struct BgmStatus
    {
        BgmState state = BgmState::Idle;
        int32_t error = 0;
        int64_t generation = 0;          // Of the last Play.
        int64_t finishedGeneration = 0;  // Of the last track that played to its end (not looped).
        int64_t playedFrames = 0;        // Of the current track, at 48 kHz.
        int64_t lengthFrames = 0;        // Of the current track, at 48 kHz; 0 when the file does not say.
        int64_t underruns = 0;
    };

    class BgmDeck final : public BlockProvider
    {
    public:
        // Opens a file on the deck's thread; Media Foundation's by default, synthetic sound in the tests.
        using Opener = std::function<std::unique_ptr<ClipSource>(const std::wstring&, HRESULT&)>;

        // generations: a count shared with another deck, so plays on either have their own numbers (8.5); null
        // gives this deck its own.
        explicit BgmDeck(Opener opener = OpenMediaFile, std::shared_ptr<std::atomic<int64_t>> generations = nullptr);
        ~BgmDeck() override;

        BgmDeck(const BgmDeck&) = delete;
        BgmDeck& operator=(const BgmDeck&) = delete;

        // Any thread but the mix's. Play fades out what plays, then starts the track from its beginning, coming up
        // over fadeInFrames; it returns the play's generation, which the status names when it ends. Stop comes down
        // over fadeOutFrames; a fade already under way takes the new length from where it is.
        int64_t Play(const std::wstring& path, bool loop, float gainDb, int fadeInFrames = FadeInFrames);
        void Stop(int fadeOutFrames = FadeOutFrames);
        void SetGain(float gainDb);
        BgmStatus Status() const;

        // The mix thread.
        bool ReadBlock(float* block) override;

        // Whether a track is on: starting, playing, or coming down.
        bool Busy() const;

        // The fades' lengths, in frames at 48 kHz. Pure values for the tests.
        static constexpr int FadeOutFrames = 14400; // 0.3 s
        static constexpr int FadeInFrames = 2400;   // 0.05 s

    private:
        enum class Phase : int32_t
        {
            Idle = 0,
            Playing = 1,
            FadingOut = 2,
        };

        struct Request
        {
            bool play = false;
            std::wstring path;
            bool loop = false;
            int64_t generation = 0;
        };

        void Run();
        void FadeOutAndWait();
        void Start(const Request& request);
        void Fill();

        Opener _opener;
        FrameRing _ring;

        // Requests from the API, taken by the deck's thread.
        std::mutex _lock;
        std::condition_variable _wake;
        bool _pending = false;
        Request _request;
        bool _stop = false;
        std::shared_ptr<std::atomic<int64_t>> _generations;

        // The deck's thread only.
        std::unique_ptr<ClipSource> _source;
        std::unique_ptr<class StereoResampler> _resampler;
        bool _loop = false;
        std::vector<float> _read;
        std::vector<float> _pair;
        std::vector<float> _converted;

        // Between the deck's thread, the mix thread, and the API.
        std::atomic<int32_t> _phase{ static_cast<int32_t>(Phase::Idle) };
        std::atomic<int32_t> _state{ static_cast<int32_t>(BgmState::Idle) };
        std::atomic<int32_t> _error{ 0 };
        std::atomic<float> _trackGain{ 1.0f };
        std::atomic<int> _fadeInFrames{ FadeInFrames };
        std::atomic<int> _fadeOutFrames{ FadeOutFrames };
        std::atomic<bool> _endWritten{ false };   // The deck wrote the last frame of a track that does not loop.
        std::atomic<bool> _drained{ true };       // The mix finished a fade out and emptied the ring.
        std::atomic<int64_t> _generation{ 0 };
        std::atomic<int64_t> _finishedGeneration{ 0 };
        std::atomic<int64_t> _playedFrames{ 0 };
        std::atomic<int64_t> _lengthFrames{ 0 };
        std::atomic<int64_t> _underruns{ 0 };

        // The mix thread only.
        float _gain = 0.0f;
        int32_t _lastPhase = static_cast<int32_t>(Phase::Idle);

        std::thread _thread;
    };
}
