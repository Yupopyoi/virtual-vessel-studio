#include "bgm_deck.h"

#include "clip_convert.h"
#include "dsp.h"
#include "mixer.h"

#include <chrono>
#include <cstring>

namespace vv::audio
{
    namespace
    {
        // About 2.7 s of ring, kept about 1.5 s full: the deck's thread may be late by a second without a gap.
        constexpr size_t RingFrames = 131072;
        constexpr size_t AheadFrames = 72000;

        // How long a switch waits for the mix to fade out what plays; the mix stops only with the engine.
        constexpr auto FadeWait = std::chrono::seconds(2);
    }

    BgmDeck::BgmDeck(Opener opener)
        : _opener(std::move(opener)), _ring(RingFrames)
    {
        _thread = std::thread([this] { Run(); });
    }

    BgmDeck::~BgmDeck()
    {
        {
            std::lock_guard<std::mutex> guard(_lock);
            _stop = true;
        }

        _wake.notify_all();
        if (_thread.joinable())
        {
            _thread.join();
        }
    }

    int64_t BgmDeck::Play(const std::wstring& path, bool loop, float gainDb)
    {
        SetGain(gainDb);
        int64_t generation = 0;
        {
            std::lock_guard<std::mutex> guard(_lock);
            generation = ++_nextGeneration;
            _request = Request{ true, path, loop, generation };
            _pending = true;
        }

        // Starting at once, so a status read right after the call does not still say idle.
        _state = static_cast<int32_t>(BgmState::Starting);
        _wake.notify_all();
        return generation;
    }

    void BgmDeck::Stop()
    {
        {
            std::lock_guard<std::mutex> guard(_lock);
            _request = Request{};
            _pending = true;
        }

        _wake.notify_all();
    }

    void BgmDeck::SetGain(float gainDb)
    {
        _trackGain = DbToLinear(ClampGainDb(gainDb));
    }

    BgmStatus BgmDeck::Status() const
    {
        BgmStatus status;
        status.state = static_cast<BgmState>(_state.load());
        status.error = _error.load();
        status.generation = _generation.load();
        status.finishedGeneration = _finishedGeneration.load();
        status.playedFrames = _playedFrames.load();
        status.lengthFrames = _lengthFrames.load();
        status.underruns = _underruns.load();
        return status;
    }

    bool BgmDeck::ReadBlock(float* block)
    {
        int32_t phase = _phase.load(std::memory_order_acquire);
        if (phase != _lastPhase)
        {
            // A track that starts comes up from silence.
            if (phase == static_cast<int32_t>(Phase::Playing))
            {
                _gain = 0.0f;
            }

            _lastPhase = phase;
        }

        if (phase == static_cast<int32_t>(Phase::Idle))
        {
            return false;
        }

        size_t read = _ring.Read(block, BlockFrames);
        if (read < static_cast<size_t>(BlockFrames))
        {
            std::memset(block + (read * Channels), 0, (static_cast<size_t>(BlockFrames) - read) * Channels * sizeof(float));
        }

        bool fading = phase == static_cast<int32_t>(Phase::FadingOut);
        float target = fading ? 0.0f : _trackGain.load();
        float step = 1.0f / static_cast<float>(fading ? FadeOutFrames : FadeInFrames);
        for (int i = 0; i < BlockFrames; i++)
        {
            _gain = _gain < target ? (_gain + step < target ? _gain + step : target) : (_gain - step > target ? _gain - step : target);
            block[i * Channels] *= _gain;
            block[(i * Channels) + 1] *= _gain;
        }

        if (fading)
        {
            if (_gain <= 0.0f)
            {
                // Faded out: what was read ahead is let go, and the deck's thread may go on.
                _ring.Read(nullptr, _ring.Available());
                _phase.store(static_cast<int32_t>(Phase::Idle), std::memory_order_release);
                _lastPhase = static_cast<int32_t>(Phase::Idle);
                _drained.store(true, std::memory_order_release);
            }

            return true;
        }

        _playedFrames.fetch_add(static_cast<int64_t>(read));
        if (read < static_cast<size_t>(BlockFrames))
        {
            if (_endWritten.load(std::memory_order_acquire) && _ring.Available() == 0)
            {
                // The track played to its end.
                _finishedGeneration = _generation.load();
                _phase.store(static_cast<int32_t>(Phase::Idle), std::memory_order_release);
                _lastPhase = static_cast<int32_t>(Phase::Idle);
                int32_t playing = static_cast<int32_t>(BgmState::Playing);
                _state.compare_exchange_strong(playing, static_cast<int32_t>(BgmState::Idle));
            }
            else
            {
                _underruns++;
            }
        }

        return read > 0;
    }

    void BgmDeck::Run()
    {
        while (true)
        {
            Request request;
            bool requested = false;
            {
                std::unique_lock<std::mutex> guard(_lock);
                _wake.wait_for(guard, std::chrono::milliseconds(20), [this] { return _stop || _pending; });
                if (_stop)
                {
                    break;
                }

                if (_pending)
                {
                    request = _request;
                    _pending = false;
                    requested = true;
                }
            }

            if (requested)
            {
                FadeOutAndWait();
                _source.reset();
                if (request.play)
                {
                    Start(request);
                }
                else
                {
                    _state = static_cast<int32_t>(BgmState::Idle);
                }
            }

            if (_source || !_converted.empty())
            {
                Fill();
            }
        }

        // The file is closed on this thread, which started Media Foundation for it.
        _source.reset();
    }

    void BgmDeck::FadeOutAndWait()
    {
        if (_phase.load(std::memory_order_acquire) == static_cast<int32_t>(Phase::Idle))
        {
            return;
        }

        _state = static_cast<int32_t>(BgmState::Stopping);
        _drained.store(false, std::memory_order_release);
        _phase.store(static_cast<int32_t>(Phase::FadingOut), std::memory_order_release);
        auto until = std::chrono::steady_clock::now() + FadeWait;
        while (!_drained.load(std::memory_order_acquire) && std::chrono::steady_clock::now() < until)
        {
            std::this_thread::sleep_for(std::chrono::milliseconds(5));
        }

        // The mix did not run (it stops only with the engine): nothing more is heard anyway.
        _phase.store(static_cast<int32_t>(Phase::Idle), std::memory_order_release);
        _converted.clear();
    }

    void BgmDeck::Start(const Request& request)
    {
        _endWritten = false;
        _playedFrames = 0;
        _lengthFrames = 0;
        _error = 0;
        _generation = request.generation;
        _converted.clear();
        _state = static_cast<int32_t>(BgmState::Starting);

        HRESULT hr = S_OK;
        _source = _opener(request.path, hr);
        if (!_source)
        {
            _error = static_cast<int32_t>(hr);
            _state = static_cast<int32_t>(BgmState::Failed);
            return;
        }

        ClipFormat format = _source->Format();
        _resampler = std::make_unique<StereoResampler>(format.rate, SampleRate);
        _loop = request.loop;
        _lengthFrames = format.rate > 0 ? (format.frames * SampleRate) / format.rate : 0;

        // Read ahead before it plays, so the first blocks never run short.
        Fill();
        if (_state.load() == static_cast<int32_t>(BgmState::Failed))
        {
            return;
        }

        _state = static_cast<int32_t>(BgmState::Playing);
        _phase.store(static_cast<int32_t>(Phase::Playing), std::memory_order_release);
    }

    void BgmDeck::Fill()
    {
        bool rewound = false;
        while (_ring.Available() < AheadFrames)
        {
            // What did not fit last time goes first.
            if (!_converted.empty())
            {
                size_t frames = _converted.size() / Channels;
                size_t written = _ring.Write(_converted.data(), frames);
                _converted.erase(_converted.begin(), _converted.begin() + static_cast<std::ptrdiff_t>(written * Channels));
                if (!_converted.empty())
                {
                    return;
                }

                continue;
            }

            if (!_source)
            {
                // The source is closed and everything it gave is in the ring: that is the end.
                _endWritten.store(true, std::memory_order_release);
                return;
            }

            HRESULT hr = _source->Read(_read);
            if (SUCCEEDED(hr) && _read.empty() && _loop && !rewound)
            {
                // Back to the start, through the same converter, so the loop has no seam. A file that gives nothing
                // even from its start ends instead of turning in place.
                hr = _source->Rewind();
                rewound = true;
                continue;
            }

            if (FAILED(hr) || _read.empty())
            {
                if (FAILED(hr))
                {
                    _error = static_cast<int32_t>(hr);
                    _state = static_cast<int32_t>(BgmState::Failed);
                }
                else
                {
                    // The last frames through the converter, then the end.
                    float tail[4] = {};
                    if (_resampler->From() != SampleRate)
                    {
                        _resampler->Process(tail, 2, _converted);
                    }
                }

                _source.reset();
                size_t frames = _converted.size() / Channels;
                size_t written = _ring.Write(_converted.data(), frames);
                _converted.erase(_converted.begin(), _converted.begin() + static_cast<std::ptrdiff_t>(written * Channels));

                // The end is marked once its last frame is in the ring; the rest waits for room in Run.
                if (_converted.empty())
                {
                    _endWritten.store(true, std::memory_order_release);
                }

                return;
            }

            rewound = false;
            int channels = _source->Format().channels;
            size_t count = _read.size() / static_cast<size_t>(channels);
            _pair.resize(count * Channels);
            ToStereo(_read.data(), count, channels, _pair.data());
            _resampler->Process(_pair.data(), count, _converted);
        }

    }
}
