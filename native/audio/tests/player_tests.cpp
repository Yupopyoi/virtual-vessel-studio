// BGM and SE players (BGM and SE detailed design 14): the rate and channel conversion, the BGM deck with synthetic
// sound (end, loop seam, fades, switching, failure), the SE voices, a player as a mixer source, and real WAV files
// through Media Foundation.

#include "vv_audio.h"

#include "audio_file.h"
#include "bgm_deck.h"
#include "clip_convert.h"
#include "clip_source.h"
#include "mixer.h"
#include "sound_effects.h"

#include <windows.h>

#include <algorithm>
#include <chrono>
#include <cmath>
#include <cstdio>
#include <cstring>
#include <string>
#include <thread>
#include <vector>

using namespace vv::audio;

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

    constexpr float Pi = 3.14159265f;

    // Synthetic sound at 48 kHz stereo: `frames` of one value, given in pieces as a decoder would.
    class SteadySource final : public ClipSource
    {
    public:
        SteadySource(float value, int64_t frames, int channels = 2, int rate = SampleRate)
            : _value(value), _frames(frames), _channels(channels), _rate(rate)
        {
        }

        ClipFormat Format() const override
        {
            return ClipFormat{ _rate, _channels, _frames };
        }

        HRESULT Read(std::vector<float>& frames) override
        {
            int64_t count = std::min<int64_t>(1152, _frames - _at);
            frames.assign(static_cast<size_t>(count * _channels), _value);
            _at += count;
            return S_OK;
        }

        HRESULT Rewind() override
        {
            _at = 0;
            Rewinds++;
            return S_OK;
        }

        int Rewinds = 0;

    private:
        float _value;
        int64_t _frames;
        int _channels;
        int _rate;
        int64_t _at = 0;
    };

    BgmDeck::Opener Steady(float value, int64_t frames)
    {
        return [value, frames](const std::wstring&, HRESULT& result) -> std::unique_ptr<ClipSource>
        {
            result = S_OK;
            return std::make_unique<SteadySource>(value, frames);
        };
    }

    // Plays as the mix would: a block at a time, a little ahead of real time, collecting both channels' left.
    std::vector<float> ReadBlocks(BgmDeck& deck, int blocks)
    {
        std::vector<float> out;
        std::vector<float> block(static_cast<size_t>(BlockFrames) * Channels);
        for (int b = 0; b < blocks; b++)
        {
            if (!deck.ReadBlock(block.data()))
            {
                std::fill(block.begin(), block.end(), 0.0f);
            }

            for (int i = 0; i < BlockFrames; i++)
            {
                out.push_back(block[static_cast<size_t>(i) * Channels]);
            }

            // The deck's thread tops the ring up every 20 ms.
            if (b % 20 == 19)
            {
                std::this_thread::sleep_for(std::chrono::milliseconds(5));
            }
        }

        return out;
    }

    bool WaitFor(BgmDeck& deck, BgmState state)
    {
        auto until = std::chrono::steady_clock::now() + std::chrono::seconds(3);
        while (deck.Status().state != state && std::chrono::steady_clock::now() < until)
        {
            std::this_thread::sleep_for(std::chrono::milliseconds(2));
        }

        return deck.Status().state == state;
    }

    // The largest step between neighbouring samples: a click shows as a jump.
    float LargestStep(const std::vector<float>& samples, size_t from, size_t to)
    {
        float largest = 0.0f;
        for (size_t i = from + 1; i < to && i < samples.size(); i++)
        {
            largest = std::max(largest, std::fabs(samples[i] - samples[i - 1]));
        }

        return largest;
    }

    void Conversion()
    {
        float mono[] = { 0.5f, -0.25f };
        float stereo[4] = {};
        ToStereo(mono, 2, 1, stereo);
        Check(stereo[0] == 0.5f && stereo[1] == 0.5f && stereo[3] == -0.25f, "mono goes to both sides");
        float six[] = { 0.1f, 0.2f, 0.3f, 0.4f, 0.5f, 0.6f };
        ToStereo(six, 1, 6, stereo);
        Check(stereo[0] == 0.1f && stereo[1] == 0.2f, "more channels keep their front left and right");

        // A 1 kHz tone from 44.1 kHz to 48 kHz stays the tone: the curve's error is small.
        constexpr int From = 44100;
        std::vector<float> tone(From * 2);
        for (int i = 0; i < From; i++)
        {
            tone[static_cast<size_t>(i) * 2] = tone[(static_cast<size_t>(i) * 2) + 1] = 0.5f * std::sin(2.0f * Pi * 1000.0f * i / From);
        }

        std::vector<float> whole = ResampleStereo(tone.data(), From, From, SampleRate);
        Check(std::abs(static_cast<int>(whole.size() / 2) - SampleRate) <= 4, "a second stays a second");
        double error = 0.0;
        for (int i = 100; i < SampleRate - 100; i++)
        {
            double expected = 0.5 * std::sin(2.0 * 3.14159265358979 * 1000.0 * i / SampleRate);
            error = std::max(error, std::fabs(whole[static_cast<size_t>(i) * 2] - expected));
        }

        std::printf("resampling error %.5f\n", error);
        Check(error < 0.005, "the tone is kept closely");

        // In pieces, through one converter, the same as whole: no seam where the pieces meet.
        StereoResampler pieces(From, SampleRate);
        std::vector<float> joined;
        for (int at = 0; at < From; at += 1000)
        {
            int count = std::min(1000, From - at);
            pieces.Process(&tone[static_cast<size_t>(at) * 2], static_cast<size_t>(count), joined);
        }

        bool same = joined.size() + 8 >= whole.size();
        for (size_t i = 0; same && i < joined.size(); i++)
        {
            same = std::fabs(joined[i] - whole[i]) < 1e-6f;
        }

        Check(same, "pieces join without a seam");
        std::vector<float> unchanged = ResampleStereo(tone.data(), 10, SampleRate, SampleRate);
        Check(unchanged.size() == 20 && unchanged[2] == tone[2], "the mix's own rate is copied as it is");
    }

    void TrackEnds()
    {
        constexpr int64_t Frames = SampleRate / 2;
        BgmDeck deck(Steady(0.5f, Frames));
        int64_t generation = deck.Play(L"steady", false, 0.0f);
        Check(WaitFor(deck, BgmState::Playing), "a track starts");
        std::vector<float> out = ReadBlocks(deck, 80);
        BgmStatus status = deck.Status();
        Check(status.finishedGeneration == generation, "a track not looped says it ended");
        Check(status.state == BgmState::Idle, "and the deck is idle");
        Check(status.playedFrames == Frames, "every frame was played");
        Check(status.lengthFrames == Frames, "its length is known");
        Check(out[0] < 0.001f && out[static_cast<size_t>(BgmDeck::FadeInFrames) + 10] == 0.5f, "it comes up from silence to its level");
        Check(out[static_cast<size_t>(Frames) + 10] == 0.0f, "and is silent after its end");
        Check(status.underruns == 0, "with nothing missing");
    }

    void LoopSeam()
    {
        // A tenth of a second, looped many times: every frame after the fade in is at its level, with no gap.
        BgmDeck deck(Steady(0.5f, SampleRate / 10));
        deck.Play(L"steady", true, 0.0f);
        Check(WaitFor(deck, BgmState::Playing), "a looped track starts");
        std::vector<float> out = ReadBlocks(deck, 150);
        float lowest = 1.0f;
        for (size_t i = static_cast<size_t>(BgmDeck::FadeInFrames) + 1; i < out.size(); i++)
        {
            lowest = std::min(lowest, out[i]);
        }

        Check(lowest == 0.5f, "a loop has no silence at its seam");
        Check(deck.Status().state == BgmState::Playing && deck.Status().finishedGeneration == 0, "and never ends");
        Check(deck.Status().underruns == 0, "and nothing runs short");
    }

    void FadeAndSwitch()
    {
        // Stop fades out over 0.3 s, without a click.
        BgmDeck deck(Steady(0.5f, SampleRate * 10));
        deck.Play(L"a", false, 0.0f);
        WaitFor(deck, BgmState::Playing);
        ReadBlocks(deck, 20);
        deck.Stop();
        std::vector<float> fading;
        auto until = std::chrono::steady_clock::now() + std::chrono::seconds(3);
        while (deck.Status().state != BgmState::Idle && std::chrono::steady_clock::now() < until)
        {
            std::vector<float> more = ReadBlocks(deck, 1);
            fading.insert(fading.end(), more.begin(), more.end());
        }

        Check(deck.Status().state == BgmState::Idle, "stopping ends idle");
        Check(fading.size() >= static_cast<size_t>(BgmDeck::FadeOutFrames) - BlockFrames, "after fading out");
        Check(LargestStep(fading, 0, fading.size()) < 0.001f, "without a click");
        std::vector<float> after = ReadBlocks(deck, 5);
        Check(*std::max_element(after.begin(), after.end()) == 0.0f, "and silence after");

        // Switching: the first fades out, then the next comes in from its start.
        int64_t opened = 0;
        BgmDeck switching([&opened](const std::wstring& path, HRESULT& result) -> std::unique_ptr<ClipSource>
        {
            result = S_OK;
            opened++;
            return std::make_unique<SteadySource>(path == L"a" ? 0.5f : -0.5f, SampleRate * 10);
        });
        switching.Play(L"a", true, 0.0f);
        WaitFor(switching, BgmState::Playing);
        ReadBlocks(switching, 20);
        switching.Play(L"b", true, 0.0f);
        std::vector<float> both;
        until = std::chrono::steady_clock::now() + std::chrono::seconds(3);
        while ((both.empty() || both.back() > -0.5f) && std::chrono::steady_clock::now() < until)
        {
            std::vector<float> more = ReadBlocks(switching, 1);
            both.insert(both.end(), more.begin(), more.end());
        }

        Check(opened == 2 && !both.empty() && both.back() == -0.5f, "the next track takes over");
        Check(LargestStep(both, 0, both.size()) < 0.001f, "and the switch does not click");
        bool crossed = false;
        for (float v : both)
        {
            crossed = crossed || (v < 0.0f);
            Check(!(crossed && v > 0.0f), "the two tracks are never heard together");
            if (crossed && v > 0.0f)
            {
                break;
            }
        }
    }

    void Failure()
    {
        BgmDeck deck([](const std::wstring&, HRESULT& result) -> std::unique_ptr<ClipSource>
        {
            result = E_ACCESSDENIED;
            return nullptr;
        });
        deck.Play(L"missing", false, 0.0f);
        Check(WaitFor(deck, BgmState::Failed), "a track that cannot open fails");
        Check(deck.Status().error == static_cast<int32_t>(E_ACCESSDENIED), "and says why");
        std::vector<float> block(static_cast<size_t>(BlockFrames) * Channels, 1.0f);
        Check(!deck.ReadBlock(block.data()), "and plays nothing");
    }

    void Effects()
    {
        SoundEffects effects;
        auto make = [](float value, size_t frames) { return std::make_shared<std::vector<float>>(frames * Channels, value); };
        int a = -1;
        int b = -1;
        Check(effects.Load(make(0.25f, 1000), &a) && effects.Load(make(0.5f, 200), &b), "effects load");
        std::vector<float> block(static_cast<size_t>(BlockFrames) * Channels);
        Check(!effects.ReadBlock(block.data()), "nothing plays before a press");

        effects.Play(a, 0.0f);
        effects.Play(b, 0.0f);
        effects.ReadBlock(block.data());
        Check(std::fabs(block[0] - 0.75f) < 1e-6f, "two effects overlap");
        Check(std::fabs(block[static_cast<size_t>(300) * Channels] - 0.25f) < 1e-6f, "and each ends at its own length");
        effects.ReadBlock(block.data());
        effects.ReadBlock(block.data());
        Check(!effects.ReadBlock(block.data()), "and they free their voices at the end");

        // More presses than voices: the oldest gives way, fading out over a block.
        for (int i = 0; i < SoundEffects::MaxVoices + 1; i++)
        {
            effects.Play(a, -6.0f);
        }

        Check(effects.Stolen() == 1, "the oldest voice is taken");
        Check(!effects.Play(63, 0.0f), "an effect not loaded does not play");
        std::shared_ptr<const std::vector<float>> released = effects.Unload(a);
        Check(released != nullptr, "an effect is given back when unloaded");
        Check(!effects.ReadBlock(block.data()), "and its voices stop");
    }

    // A player in place of a device: the mix reads it, with the source's gain and sends.
    class Half final : public BlockProvider
    {
    public:
        bool ReadBlock(float* block) override
        {
            std::fill(block, block + (static_cast<size_t>(BlockFrames) * Channels), 0.25f);
            return true;
        }
    };

    void MixerSource()
    {
        Mixer mixer(SampleRate);
        int id = -1;
        mixer.Add(&id);
        Half half;
        mixer.SetProvider(id, &half);
        mixer.SetGain(id, 0.0f, false);
        for (int i = 0; i < 10; i++)
        {
            mixer.MixBlock(static_cast<int64_t>(i) * BlockFrames);
        }

        std::vector<float> out(static_cast<size_t>(BlockFrames) * Channels);
        int64_t first = 0;
        int32_t read = mixer.ReadStream(out.data(), BlockFrames, static_cast<int64_t>(5) * BlockFrames, &first);
        Check(read == BlockFrames && std::fabs(out[100] - 0.25f) < 1e-4f, "a player reaches the stream bus");
    }

    std::wstring WriteWav(const wchar_t* name, int rate, int channels, int frames, float frequency)
    {
        std::vector<int16_t> pcm(static_cast<size_t>(frames) * channels);
        for (int i = 0; i < frames; i++)
        {
            for (int c = 0; c < channels; c++)
            {
                pcm[(static_cast<size_t>(i) * channels) + c] = static_cast<int16_t>(8000.0f * std::sin(static_cast<float>(i) * 2.0f * Pi * frequency / rate));
            }
        }

        const uint32_t dataBytes = static_cast<uint32_t>(pcm.size() * sizeof(int16_t));
        std::vector<char> wav(44 + dataBytes);
        auto put32 = [&](size_t at, uint32_t v) { std::memcpy(&wav[at], &v, 4); };
        auto put16 = [&](size_t at, uint16_t v) { std::memcpy(&wav[at], &v, 2); };
        std::memcpy(&wav[0], "RIFF", 4);
        put32(4, 36 + dataBytes);
        std::memcpy(&wav[8], "WAVEfmt ", 8);
        put32(16, 16);
        put16(20, 1);
        put16(22, static_cast<uint16_t>(channels));
        put32(24, static_cast<uint32_t>(rate));
        put32(28, static_cast<uint32_t>(rate * channels * 2));
        put16(32, static_cast<uint16_t>(channels * 2));
        put16(34, 16);
        std::memcpy(&wav[36], "data", 4);
        put32(40, dataBytes);
        std::memcpy(&wav[44], pcm.data(), dataBytes);

        wchar_t folder[MAX_PATH];
        GetTempPathW(MAX_PATH, folder);
        std::wstring path = std::wstring(folder) + name;
        HANDLE file = CreateFileW(path.c_str(), GENERIC_WRITE, 0, nullptr, CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
        DWORD written = 0;
        bool saved = file != INVALID_HANDLE_VALUE && WriteFile(file, wav.data(), static_cast<DWORD>(wav.size()), &written, nullptr) && written == wav.size();
        if (file != INVALID_HANDLE_VALUE)
        {
            CloseHandle(file);
        }

        Check(saved, "the test file is written");
        return path;
    }

    void Files()
    {
        std::wstring path = WriteWav(L"vv_player_tests.wav", 44100, 1, 44100, 440.0f);
        vv_audio_file_info info = {};
        int32_t error = 0;
        int32_t result = vv_audio_probe_file(path.c_str(), &info, &error);
        if (result != VV_AUDIO_OK && error == static_cast<int32_t>(NoMediaFoundation))
        {
            std::printf("SKIP files: no Media Foundation\n");
            DeleteFileW(path.c_str());
            return;
        }

        Check(result == VV_AUDIO_OK, "a WAV file is probed");
        Check(info.rate == 44100 && info.channels == 1, "with its own rate and channels");
        Check(std::abs(info.frames - SampleRate) <= 2, "and its length at the mix's rate");
        Check(vv_audio_probe_file(L"C:\\no\\such\\file.mp3", &info, &error) == VV_AUDIO_UNREADABLE, "a missing file is unreadable");

        // SE: decoded whole to 48 kHz stereo; one too long is refused.
        std::vector<float> stereo;
        Check(SUCCEEDED(DecodeStereo(path, 30, stereo)), "an effect is decoded");
        Check(std::abs(static_cast<int>(stereo.size() / 2) - SampleRate) <= 4 && stereo[1000] == stereo[1001], "to the mix's rate, the mono on both sides");
        Check(DecodeStereo(path, 0, stereo) == FileTooLong, "an effect too long is refused");

        // BGM: the deck reads the real file to its end.
        BgmDeck deck;
        int64_t generation = deck.Play(path, false, 0.0f);
        Check(WaitFor(deck, BgmState::Playing), "a WAV track plays");
        std::vector<float> out = ReadBlocks(deck, 110);
        BgmStatus status = deck.Status();
        Check(status.finishedGeneration == generation, "to its end");
        Check(std::abs(status.playedFrames - SampleRate) <= 4, "a second of it");
        float loudest = 0.0f;
        for (float v : out)
        {
            loudest = std::max(loudest, std::fabs(v));
        }

        Check(loudest > 0.2f && loudest < 0.3f, "at its level");
        DeleteFileW(path.c_str());
    }

    void Step(const char* name, void (*test)())
    {
        std::printf("== %s\n", name);
        test();
    }
}

int main()
{
    std::setvbuf(stdout, nullptr, _IONBF, 0);
    Step("conversion", Conversion);
    Step("a track ends", TrackEnds);
    Step("a loop's seam", LoopSeam);
    Step("fades and switching", FadeAndSwitch);
    Step("failure", Failure);
    Step("effects", Effects);
    Step("a player in the mixer", MixerSource);
    Step("files", Files);
    std::printf(s_failures == 0 ? "PASS\n" : "FAIL (%d)\n", s_failures);
    return s_failures == 0 ? 0 : 1;
}
