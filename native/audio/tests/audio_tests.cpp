// The mix with synthetic signals (audio detailed design 14): gains, mute, sends, the limiter, drift
// correction, running dry, and the stream tap's timing; then the engine and the device list on this PC.

#include "vv_audio.h"

#include "audio_file.h"
#include "denoiser.h"
#include "device_source.h"
#include "dsp.h"
#include "mixer.h"

#include <windows.h>

#include <audioclient.h>
#include <mferror.h>

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

    // A steady tone: amplitude on both channels, one block at a time.
    std::vector<float> Tone(float amplitude, size_t frames, size_t& phase)
    {
        std::vector<float> data(frames * Channels);
        for (size_t i = 0; i < frames; i++, phase++)
        {
            float value = amplitude * std::sin(static_cast<float>(phase) * 2.0f * 3.14159265f * 440.0f / SampleRate);
            data[i * Channels] = value;
            data[(i * Channels) + 1] = value;
        }

        return data;
    }

    // With the QPC frequency at the sample rate, a frame's QPC is its index: timing is easy to check.
    constexpr int64_t Frequency = SampleRate;

    // Feeds each source one block per mix block for `blocks`, and returns the bus's peak over the last half.
    float Run(Mixer& mixer, const std::vector<std::pair<int, float>>& sources, int blocks, uint64_t& mixed)
    {
        std::vector<size_t> phases(sources.size(), 0);
        float peak = 0.0f;
        std::vector<float> out(BlockFrames * Channels);
        for (int b = 0; b < blocks; b++)
        {
            for (size_t s = 0; s < sources.size(); s++)
            {
                std::vector<float> tone = Tone(sources[s].second, BlockFrames, phases[s]);
                mixer.Write(sources[s].first, tone.data(), BlockFrames);
            }

            mixer.MixBlock(static_cast<int64_t>(mixed * BlockFrames));
            mixed++;
            int64_t first = 0;
            int32_t read = mixer.ReadStream(out.data(), BlockFrames, INT64_MAX, &first);
            if (b >= blocks / 2)
            {
                peak = std::max(peak, Peak(out.data(), read, Channels));
            }
        }

        return peak;
    }

    void GainMuteAndSends()
    {
        Mixer mixer(Frequency);
        int a = -1;
        mixer.Add(&a);
        mixer.SetGain(a, -6.0206f, false);
        uint64_t mixed = 0;
        float peak = Run(mixer, { { a, 0.5f } }, 100, mixed);
        Check(std::fabs(peak - 0.25f) < 0.01f, "-6 dB halves a tone");

        mixer.SetGain(a, -6.0206f, true);
        Run(mixer, { { a, 0.5f } }, 10, mixed);
        mixer.TakeCounters(a);
        peak = Run(mixer, { { a, 0.5f } }, 100, mixed);
        Check(peak < 1e-6f, "a muted source is silent on the bus");
        Check(std::fabs(mixer.TakeCounters(a).peak - 0.25f) < 0.01f, "a muted source still shows its level");

        mixer.SetGain(a, 0.0f, false);
        mixer.SetSend(a, 0, -INFINITY);
        Run(mixer, { { a, 0.5f } }, 10, mixed);
        peak = Run(mixer, { { a, 0.5f } }, 100, mixed);
        Check(peak < 1e-6f, "a source without a send is silent on the bus");
    }

    void SumAndLimit()
    {
        Mixer mixer(Frequency);
        int a = -1;
        int b = -1;
        mixer.Add(&a);
        mixer.Add(&b);
        uint64_t mixed = 0;
        float peak = Run(mixer, { { a, 0.3f }, { b, 0.2f } }, 100, mixed);
        Check(std::fabs(peak - 0.5f) < 0.01f, "two sources add");

        peak = Run(mixer, { { a, 0.9f }, { b, 0.9f } }, 200, mixed);
        Check(peak <= LimiterCeiling + 1e-4f && peak > 0.85f, "the bus is limited to -1 dBFS");
    }

    // A device running fast or slow against the mix: one sample now and then keeps the fill steady.
    void Drift(double ratio, const char* what)
    {
        Mixer mixer(Frequency);
        int a = -1;
        mixer.Add(&a);
        size_t phase = 0;
        double owed = 0.0;
        std::vector<float> out(BlockFrames * Channels);
        for (int block = 0; block < 6000; block++) // A minute.
        {
            owed += BlockFrames * ratio;
            size_t frames = static_cast<size_t>(owed);
            owed -= static_cast<double>(frames);
            std::vector<float> tone = Tone(0.5f, frames, phase);
            mixer.Write(a, tone.data(), frames);
            mixer.MixBlock(static_cast<int64_t>(block) * BlockFrames);
            int64_t first = 0;
            mixer.ReadStream(out.data(), BlockFrames, INT64_MAX, &first);
        }

        SourceCounters counters = mixer.TakeCounters(a);
        std::printf("drift %s: corrections %lld, underruns %lld, overruns %lld\n", what, static_cast<long long>(counters.driftCorrections),
            static_cast<long long>(counters.underruns), static_cast<long long>(counters.overruns));
        Check(counters.underruns == 0 && counters.overruns == 0, what);
        Check(counters.driftCorrections > 1000 && counters.driftCorrections < 4000, "about one correction per 0.1% of the samples");
    }

    void RunningDry()
    {
        Mixer mixer(Frequency);
        int a = -1;
        mixer.Add(&a);
        uint64_t mixed = 0;
        Run(mixer, { { a, 0.5f } }, 50, mixed);
        float peak = Run(mixer, {}, 50, mixed);
        Check(peak < 1e-6f, "a source that stops sending is silent");
        Check(mixer.TakeCounters(a).underruns >= 1, "running dry is counted");
    }

    void TapTiming()
    {
        Mixer mixer(Frequency);
        std::vector<float> out(4000 * Channels);
        int64_t first = -1;
        Check(mixer.ReadStream(out.data(), 100, INT64_MAX, &first) == 0, "nothing before the first block");

        const int64_t anchor = 1000;
        for (int b = 0; b < 10; b++)
        {
            mixer.MixBlock(anchor + (static_cast<int64_t>(b) * BlockFrames));
        }

        int32_t read = mixer.ReadStream(out.data(), 4000, anchor + 999, &first);
        Check(read == 1000 && first == anchor, "the tap stops at the time asked for");
        read = mixer.ReadStream(out.data(), 4000, INT64_MAX, &first);
        Check(read == 3800 && first == anchor + 1000, "the next read follows on");
        Check(mixer.ReadStream(out.data(), 4000, INT64_MAX, &first) == 0, "and stops at what was mixed");

        // Each reader keeps its own place, so recording and streaming both get every frame.
        read = mixer.ReadStream(out.data(), 4000, anchor + 999, &first, 1);
        Check(read == 1000 && first == anchor, "a second reader starts from the same frames");
        Check(mixer.ReadStream(out.data(), 100, INT64_MAX, &first, MaxReaders) == 0, "a reader that does not exist reads nothing");

        // A delayed bus gives each frame as heard that much later: the same frames, later times, and fewer by a time.
        Mixer delayed(Frequency);
        delayed.SetStreamDelay(300);
        for (int b = 0; b < 10; b++)
        {
            delayed.MixBlock(anchor + (static_cast<int64_t>(b) * BlockFrames));
        }

        read = delayed.ReadStream(out.data(), 4000, anchor + 999, &first);
        Check(read == 700 && first == anchor + 300, "a delay moves the bus later for its readers");
        delayed.SetStreamDelay(-5);
        read = delayed.ReadStream(out.data(), 4000, anchor + 999, &first);
        Check(read == 300 && first == anchor + 700, "a negative delay is none");

        // With the real QPC frequency, a frame's time is its index over the rate.
        Check(Mixer::FrameQpc(0, 48000ULL * 3600, 10'000'000) == 36'000'000'000LL, "an hour in, frame times stay exact");
    }

    void Ramps()
    {
        std::vector<float> data(BlockFrames * Channels, 1.0f);
        float peak = ApplyRamp(data.data(), BlockFrames, Channels, 0.0f, 1.0f);
        bool rising = true;
        for (int i = 1; i < BlockFrames; i++)
        {
            rising = rising && data[static_cast<size_t>(i) * Channels] >= data[static_cast<size_t>(i - 1) * Channels];
        }

        Check(rising && data[0] < 0.01f && std::fabs(peak - 1.0f) < 1e-6f, "a gain change ramps over the block");
        Check(ClampGainDb(40.0f) == MaxGainDb && ClampGainDb(-100.0f) == MinGainDb && ClampGainDb(NAN) == 0.0f, "gains are kept in range");
        Check(DbToLinear(-INFINITY) == 0.0f && std::isinf(LinearToDb(0.0f)), "-inf is silence");
    }

    // Mixes blocks of a tone from `source` and returns the monitor bus's peak over the last half.
    float RunMonitor(Mixer& mixer, int source, float amplitude, int blocks, uint64_t& mixed)
    {
        size_t phase = 0;
        float peak = 0.0f;
        std::vector<float> out(BlockFrames * Channels);
        for (int b = 0; b < blocks; b++)
        {
            std::vector<float> tone = Tone(amplitude, BlockFrames, phase);
            mixer.Write(source, tone.data(), BlockFrames);
            mixer.MixBlock(static_cast<int64_t>(mixed * BlockFrames));
            mixed++;
            size_t read = mixer.ReadMonitor(out.data(), BlockFrames);
            if (b >= blocks / 2)
            {
                peak = std::max(peak, Peak(out.data(), static_cast<int>(read), Channels));
            }
        }

        return peak;
    }

    // With a stage on, capture waits for the stage, the mix hears only what the stage writes, and bursts
    // of many blocks are played evenly without drift corrections.
    void Stage()
    {
        Mixer mixer(Frequency);
        int id = -1;
        Check(mixer.Add(&id), "a source is added");
        mixer.SetSend(id, 1, 0.0f);
        mixer.SetStage(id, true);

        size_t phase = 0;
        std::vector<float> loud = Tone(0.5f, BlockFrames, phase);
        mixer.Write(id, loud.data(), BlockFrames);
        std::vector<float> read(static_cast<size_t>(BlockFrames) * 2 * Channels);
        Check(mixer.ReadStage(id, read.data(), BlockFrames * 2) == BlockFrames, "capture reaches the stage");

        const int BurstBlocks = 10;
        std::vector<float> out(static_cast<size_t>(BlockFrames) * Channels);
        uint64_t mixed = 0;
        for (int burst = 0; burst < 100; burst++)
        {
            std::vector<float> quiet = Tone(0.25f, static_cast<size_t>(BurstBlocks) * BlockFrames, phase);
            mixer.WriteStage(id, quiet.data(), static_cast<size_t>(BurstBlocks) * BlockFrames);
            for (int block = 0; block < BurstBlocks; block++)
            {
                mixer.MixBlock(static_cast<int64_t>(mixed * BlockFrames));
                mixed++;
                size_t got = mixer.ReadMonitor(out.data(), BlockFrames);
                if (burst > 0)
                {
                    Check(std::abs(Peak(out.data(), static_cast<int>(got), Channels) - 0.25f) < 0.01f, "the stage's audio plays, not the capture");
                }
            }
        }

        // A burst of 100 ms played evenly waits 50 ms on average; the average swings with the bursts.
        StageDelay delay = mixer.GetStageDelay(id);
        Check(std::abs(delay.stageOutput - BurstBlocks * BlockFrames / 2.0f) < 300.0f, "a burst waits half its length for the mix");
        Check(delay.stageInput == 0.0f, "nothing waits for a stage that reads");
        Check(std::abs(delay.monitor - BlockFrames / 2.0f) < 1.0f, "the monitor read at once waits half a block");

        SourceCounters counters = mixer.TakeCounters(id);
        Check(counters.underruns == 0, "bursts do not run dry");
        Check(counters.driftCorrections == 0, "a burst's swing is not taken for drift");

        mixer.SetStage(id, false);
        for (int i = 0; i < 20; i++)
        {
            std::vector<float> tone = Tone(0.5f, BlockFrames, phase);
            mixer.Write(id, tone.data(), BlockFrames);
            mixer.MixBlock(static_cast<int64_t>(mixed * BlockFrames));
            mixed++;
            mixer.ReadMonitor(out.data(), BlockFrames);
        }

        Check(std::abs(Peak(out.data(), BlockFrames, Channels) - 0.5f) < 0.01f, "off again, the capture plays directly");
        Check(mixer.ReadStage(id, read.data(), BlockFrames) == 0, "nothing waits for a stage that is off");
    }

    double Rms(const std::vector<float>& samples, size_t first)
    {
        double sum = 0.0;
        for (size_t i = first; i < samples.size(); i++)
        {
            sum += samples[i] * samples[i];
        }

        return std::sqrt(sum / static_cast<double>(samples.size() - first));
    }

    // Steady noise, the kind RNNoise learns to take out, falls by far once it has settled.
    void Denoise()
    {
        Denoiser denoiser;
        std::vector<float> noise(static_cast<size_t>(48000) * 2);
        unsigned int state = 12345;
        for (float& sample : noise)
        {
            state = state * 1103515245u + 12345u;
            sample = (static_cast<float>((state >> 16) & 0x7fff) / 16384.0f - 1.0f) * 0.05f;
        }

        double before = Rms(noise, 48000);
        Check(denoiser.Process(noise.data(), static_cast<int>(noise.size())), "the denoiser runs");
        Check(Rms(noise, 48000) < before * 0.25, "steady noise falls by more than 12 dB");

        // A held note: harmonics of 150 Hz for 8 s. RNNoise alone silenced one like it within two seconds.
        Denoiser voice;
        std::vector<float> note(static_cast<size_t>(48000) * 8);
        for (size_t i = 0; i < note.size(); i++)
        {
            float t = static_cast<float>(i) / 48000.0f;
            for (int h = 1; h <= 8; h++)
            {
                note[i] += 0.3f / static_cast<float>(h) * std::sin(2.0f * 3.14159265f * 150.0f * static_cast<float>(h) * t);
            }
        }

        Check(Denoiser::Periodicity(note.data(), 1920) > 0.9f, "a note is periodic");
        double held = Rms(note, 48000 * 6);
        voice.Process(note.data(), static_cast<int>(note.size()));
        Check(Rms(note, 48000 * 6) > held * 0.7, "a held note keeps its level after 6 s");

        Check(Denoiser::Periodicity(noise.data(), 1920) < 0.6f, "noise is not periodic");
        Denoiser::VoiceGate gate = Denoiser::NextVoiceGate({}, 0.9f, 0.1f);
        Check(gate.Weight > 0.4f, "a voice blends the original back quickly");
        Check(Denoiser::NextVoiceGate({}, 0.9f, 0.0001f).Weight == 0.0f, "a quiet periodic hum is not a voice");

        gate = { 1.0f, 0 };
        gate = Denoiser::NextVoiceGate(gate, 0.9f, 0.1f);
        for (int frame = 0; frame < 10; frame++)
        {
            gate = Denoiser::NextVoiceGate(gate, 0.1f, 0.1f);
        }

        Check(gate.Weight == 1.0f, "a voice is held for 100 ms after its last voiced frame");
        for (int frame = 0; frame < 3; frame++)
        {
            gate = Denoiser::NextVoiceGate(gate, 0.1f, 0.1f);
        }

        Check(gate.Weight < 0.05f, "and then let go within a few frames");

        // A held note with 30 ms of breath noise at the same level in the middle, as when a sung note turns rough.
        Denoiser rough;
        std::vector<float> roughNote(static_cast<size_t>(48000) * 4);
        uint32_t breath = 7u;
        for (size_t i = 0; i < roughNote.size(); i++)
        {
            float t = static_cast<float>(i) / 48000.0f;
            if (i >= 96000 && i < 96000 + 1440)
            {
                breath = breath * 1103515245u + 12345u;
                roughNote[i] = (static_cast<float>((breath >> 16) & 0x7fff) / 16384.0f - 1.0f) * 0.4f;
                continue;
            }

            for (int h = 1; h <= 8; h++)
            {
                roughNote[i] += 0.3f / static_cast<float>(h) * std::sin(2.0f * 3.14159265f * 150.0f * static_cast<float>(h) * t);
            }
        }

        std::vector<float> roughOriginal = roughNote;
        rough.Process(roughNote.data(), static_cast<int>(roughNote.size()));
        double lowest = 1.0;
        for (size_t at = 48000; at + 960 <= roughNote.size(); at += 480)
        {
            double out = 0.0;
            double in = 0.0;
            for (size_t i = at; i < at + 960; i++)
            {
                out += static_cast<double>(roughNote[i]) * roughNote[i];
                in += static_cast<double>(roughOriginal[i - Denoiser::DelaySamples]) * roughOriginal[i - Denoiser::DelaySamples];
            }

            lowest = std::min(lowest, std::sqrt(out / in));
        }

        Check(lowest > 0.7, "a rough moment inside a note does not cut a gap into it");
    }

    void MonitorBus()
    {
        Mixer mixer(Frequency);
        int a = -1;
        mixer.Add(&a);
        uint64_t mixed = 0;
        Check(RunMonitor(mixer, a, 0.5f, 100, mixed) == 0.0f, "a source reaches the monitor only when asked to");

        mixer.SetSend(a, 1, 0.0f);
        Check(std::fabs(RunMonitor(mixer, a, 0.5f, 100, mixed) - 0.5f) < 0.01f, "a source sent to the monitor is heard there");

        mixer.SetSend(a, 0, -200.0f);
        Check(std::fabs(RunMonitor(mixer, a, 0.5f, 100, mixed) - 0.5f) < 0.01f, "the sends are apart: none to the stream, still heard");

        mixer.SetMonitor(-6.0206f, false);
        Check(std::fabs(RunMonitor(mixer, a, 0.5f, 100, mixed) - 0.25f) < 0.01f, "the monitor has its own volume");

        mixer.SetMonitor(0.0f, true);
        Check(RunMonitor(mixer, a, 0.5f, 100, mixed) == 0.0f, "muting the monitor silences it");

        mixer.SetMonitor(0.0f, false);
        mixer.SetGain(a, 0.0f, true);
        Check(RunMonitor(mixer, a, 0.5f, 100, mixed) == 0.0f, "a muted source is silent on the monitor too");

        // Ring: nobody plays, so it fills, and a new reader starts from now rather than from long ago.
        mixer.SetGain(a, 0.0f, false);
        for (int b = 0; b < 200; b++)
        {
            mixer.MixBlock(static_cast<int64_t>(mixed * BlockFrames));
            mixed++;
        }

        Check(mixer.MonitorAvailable() < 20000, "the monitor bus does not grow without a reader");
        mixer.DiscardMonitor();
        Check(mixer.MonitorAvailable() == 0, "what waited is let go");

        // The stream delay is for the stream: the monitor is heard at once.
        Mixer delayed(Frequency);
        int d = -1;
        delayed.Add(&d);
        delayed.SetSend(d, 1, 0.0f);
        delayed.SetStreamDelay(SampleRate);
        uint64_t delayedMixed = 0;
        Check(RunMonitor(delayed, d, 0.5f, 100, delayedMixed) > 0.4f, "the stream delay does not delay the monitor");
    }

    void Fill()
    {
        vv::audio::FillTracker fill;
        Check(fill.Next(vv::audio::PrimeFrames - 1, 480).take == 0, "it waits to fill up");
        Check(fill.Next(vv::audio::PrimeFrames, 480).take == 480, "then it plays");
        Check(fill.Next(100, 480).ranDry, "it says when it ran dry");
        Check(fill.Next(100, 480).take == 0, "and waits to fill up again");

        vv::audio::FillTracker settled;
        for (int i = 0; i < 60; i++)
        {
            settled.Next(1000, 480);
        }

        int corrected = 0;
        int more = 0;
        for (int i = 0; i < 200; i++)
        {
            vv::audio::FillStep step = settled.Next(1300, 480);
            corrected += step.corrected ? 1 : 0;
            more += step.take == 481 ? 1 : 0;
        }

        Check(more > 0 && more == corrected, "a fill running high takes a frame more");
    }

    void Problems()
    {
        int state = 0;
        int problem = 0;
        DeviceLoop::Classify(AUDCLNT_E_DEVICE_INVALIDATED, state, problem);
        Check(state == VV_AUDIO_SOURCE_UNAVAILABLE, "an unplugged device is unavailable");
        DeviceLoop::Classify(AUDCLNT_E_DEVICE_IN_USE, state, problem);
        Check(state == VV_AUDIO_SOURCE_ERROR && problem == VV_AUDIO_PROBLEM_IN_USE, "a device in exclusive use");
        DeviceLoop::Classify(E_ACCESSDENIED, state, problem);
        Check(problem == VV_AUDIO_PROBLEM_ACCESS_DENIED, "microphone access denied by Windows");
    }

    // A file through Media Foundation: a 16-bit stereo WAV at 44.1 kHz, its channels opposite in sign but for
    // an offset, so the mono mix is the offset alone and the file's rate is kept.
    void AudioFiles()
    {
        float pair[] = { 0.5f, -0.25f, 0.0f, 1.0f };
        std::vector<float> mono = ToMono(pair, 2, 2);
        Check(mono.size() == 2 && mono[0] == 0.125f && mono[1] == 0.5f, "channels are averaged");

        constexpr int Rate = 44100;
        constexpr int Frames = Rate / 2;
        std::vector<int16_t> pcm(Frames * 2);
        for (int i = 0; i < Frames; i++)
        {
            int16_t tone = static_cast<int16_t>(8000.0f * std::sin(static_cast<float>(i) * 2.0f * 3.14159265f * 440.0f / Rate));
            pcm[i * 2] = static_cast<int16_t>(tone + 3277);
            pcm[(i * 2) + 1] = static_cast<int16_t>(-tone + 3277);
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
        put16(22, 2);
        put32(24, Rate);
        put32(28, Rate * 4);
        put16(32, 4);
        put16(34, 16);
        std::memcpy(&wav[36], "data", 4);
        put32(40, dataBytes);
        std::memcpy(&wav[44], pcm.data(), dataBytes);

        wchar_t folder[MAX_PATH];
        GetTempPathW(MAX_PATH, folder);
        std::wstring path = std::wstring(folder) + L"vv_audio_tests.wav";
        HANDLE file = CreateFileW(path.c_str(), GENERIC_WRITE, 0, nullptr, CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
        DWORD written = 0;
        bool saved = file != INVALID_HANDLE_VALUE && WriteFile(file, wav.data(), static_cast<DWORD>(wav.size()), &written, nullptr) && written == wav.size();
        if (file != INVALID_HANDLE_VALUE)
        {
            CloseHandle(file);
        }

        Check(saved, "the test file is written");
        vv_audio_clip* clip = nullptr;
        int32_t rate = 0;
        int64_t count = 0;
        int32_t error = 0;
        int32_t result = vv_audio_decode_file(path.c_str(), &clip, &rate, &count, &error);
        if (result != VV_AUDIO_OK && error == static_cast<int32_t>(NoMediaFoundation))
        {
            // Windows N editions have no Media Foundation unless its pack is installed.
            std::printf("SKIP files: no Media Foundation\n");
        }
        else
        {
            Check(result == VV_AUDIO_OK, "a WAV file is decoded");
            Check(rate == Rate, "at its own rate");
            Check(count == Frames, "every frame");
            std::vector<float> samples(static_cast<size_t>(count));
            Check(vv_audio_clip_read(clip, samples.data(), count) == VV_AUDIO_OK, "the samples are read");
            float worst = 0.0f;
            for (float s : samples)
            {
                worst = std::max(worst, std::fabs(s - (3277.0f / 32768.0f)));
            }

            Check(worst < 0.001f, "the channels are mixed to mono");
        }

        vv_audio_clip_free(clip);
        DeleteFileW(path.c_str());

        clip = nullptr;
        result = vv_audio_decode_file(L"C:\\no such folder\\none.m4a", &clip, &rate, &count, &error);
        Check(result == VV_AUDIO_UNREADABLE && clip == nullptr && error != 0, "a missing file is unreadable");
    }

    // A preview reaches the monitor only, plays to its end, and is replaced or stopped (audio detailed design 5.5).
    void Preview()
    {
        Mixer mixer(Frequency);
        uint64_t mixed = 0;
        mixer.PlayPreview(std::vector<float>(BlockFrames * 2 + 100, 0.25f));
        Check(mixer.GetPreviewPosition().length == BlockFrames * 2 + 100, "the preview's length");

        std::vector<float> monitor(BlockFrames * Channels);
        float heard = 0.0f;
        for (int b = 0; b < 4; b++)
        {
            mixer.MixBlock(Mixer::FrameQpc(0, mixed * BlockFrames, Frequency));
            mixed++;
            size_t got = mixer.ReadMonitor(monitor.data(), BlockFrames);
            for (size_t i = 0; i < got * Channels; i++)
            {
                heard = std::max(heard, monitor[i]);
            }
        }

        Check(heard > 0.2f, "the preview is heard on the monitor");
        Check(mixer.GetPreviewPosition().length == 0, "it stops at its end");
        std::vector<float> stream(BlockFrames * Channels);
        int64_t first = 0;
        int32_t read = mixer.ReadStream(stream.data(), BlockFrames, Mixer::FrameQpc(0, mixed * BlockFrames, Frequency), &first, 0);
        float onStream = 0.0f;
        for (int32_t i = 0; i < read * Channels; i++)
        {
            onStream = std::max(onStream, std::fabs(stream[i]));
        }

        Check(onStream == 0.0f, "the preview never reaches the stream");

        mixer.PlayPreview(std::vector<float>(SampleRate, 0.1f));
        mixer.PlayPreview(std::vector<float>(100, 0.1f));
        Check(mixer.GetPreviewPosition().length == 100, "playing replaces the preview");
        mixer.StopPreview();
        Check(mixer.GetPreviewPosition().length == 0, "stop ends it");

        float block[8] = {};
        std::vector<float> two = { 0.5f, -0.5f };
        Check(Mixer::AddPreview(block, 4, 2, two, 1) == 1 && block[0] == -0.5f && block[1] == -0.5f && block[2] == 0.0f, "mono goes to every channel from the position");

        float one[] = { 0.0f, 1.0f };
        std::vector<float> up = ResampleLinear(one, 2, 24000, 48000);
        Check(up.size() == 4 && up[1] == 0.5f, "resampling interpolates");
        Check(ResampleLinear(one, 2, 48000, 48000).size() == 2, "the same rate is a copy");
    }

    // Where each export starts in the DLL, so a crash's offset can be placed in a function.
    void PrintExports()
    {
        HMODULE module = GetModuleHandleW(L"VirtualVessel.Audio.Native.dll");
        const char* names[] = { "vv_audio_list_inputs", "vv_audio_create", "vv_audio_destroy", "vv_audio_add_source", "vv_audio_remove_source",
            "vv_audio_set_device", "vv_audio_get_source_status", "vv_audio_get_stream_tap", "vv_audio_api_version" };
        for (const char* name : names)
        {
            FARPROC address = module != nullptr ? GetProcAddress(module, name) : nullptr;
            std::printf("export %s at +0x%llx\n", name, address != nullptr ? static_cast<unsigned long long>(reinterpret_cast<const char*>(address) - reinterpret_cast<const char*>(module)) : 0ULL);
        }
    }

    void EngineOnThisPc()
    {
        PrintExports();
        vv_audio_device devices[32];
        int32_t count = -1;
        std::printf("listing inputs\n");
        bool audio = vv_audio_list_inputs(devices, 32, &count) == VV_AUDIO_OK;
        std::printf("listed: %s, %d\n", audio ? "ok" : "failed", count);
        if (!audio)
        {
            // CI's hosted runners (Windows Server) have no audio service; the mix is tested above.
            std::printf("SKIP devices: no audio service\n");
            count = 0;
        }

        for (int32_t i = 0; i < count && i < 32; i++)
        {
            std::wprintf(L"input %d: %s%s\n", i, devices[i].name, devices[i].is_default ? L" (default)" : L"");
        }

        std::printf("creating the engine\n");
        vv_audio_engine* engine = nullptr;
        Check(vv_audio_create(&engine) == VV_AUDIO_OK, "the engine starts");
        std::printf("adding a source\n");
        int32_t source = -1;
        Check(vv_audio_add_source(engine, &source) == VV_AUDIO_OK, "a source is added");
        vv_audio_source_status status = {};
        vv_audio_get_source_status(engine, source, &status);
        Check(status.state == VV_AUDIO_SOURCE_OFF, "a source without a device is off");

        std::printf("choosing a missing device\n");
        vv_audio_set_device(engine, source, L"{0.0.1.00000000}.{00000000-0000-0000-0000-000000000000}");
        auto start = std::chrono::steady_clock::now();
        while (status.state != VV_AUDIO_SOURCE_UNAVAILABLE && std::chrono::steady_clock::now() - start < std::chrono::seconds(3))
        {
            std::this_thread::sleep_for(std::chrono::milliseconds(20));
            vv_audio_get_source_status(engine, source, &status);
        }

        Check(!audio || status.state == VV_AUDIO_SOURCE_UNAVAILABLE, "a missing device is unavailable");

        // With a real default microphone, it runs and the bus keeps time.
        for (int32_t i = 0; i < count && i < 32; i++)
        {
            if (devices[i].is_default)
            {
                vv_audio_set_device(engine, source, devices[i].id);
                start = std::chrono::steady_clock::now();
                while (status.state != VV_AUDIO_SOURCE_RUNNING && std::chrono::steady_clock::now() - start < std::chrono::seconds(3))
                {
                    std::this_thread::sleep_for(std::chrono::milliseconds(20));
                    vv_audio_get_source_status(engine, source, &status);
                }

                std::printf("default input: state %d, error 0x%08x\n", status.state, static_cast<unsigned>(status.last_error));
                Check(status.state == VV_AUDIO_SOURCE_RUNNING, "the default microphone runs");
                std::this_thread::sleep_for(std::chrono::seconds(2));
                vv_audio_get_source_status(engine, source, &status);
                std::printf("after 2 s: peak %.1f dB, underruns %lld, overruns %lld\n", status.peak_db, static_cast<long long>(status.underruns), static_cast<long long>(status.overruns));
                Check(status.overruns == 0, "nothing is lost from the microphone");
                break;
            }
        }

        std::printf("reading the tap\n");
        vv_audio_stream_tap tap = {};
        vv_audio_get_stream_tap(engine, VV_AUDIO_READER_RECORDING, &tap);
        std::vector<float> out(48000 * Channels);
        LARGE_INTEGER now;
        QueryPerformanceCounter(&now);
        int64_t first = 0;
        int32_t read = tap.read(tap.context, out.data(), 48000, now.QuadPart, &first);
        Check(read > 0 && first < now.QuadPart, "the tap gives the stream bus up to now");

        std::printf("removing the source\n");
        Check(vv_audio_remove_source(engine, source) == VV_AUDIO_OK, "a source is removed");

        // The monitor on Windows' default output, playing silence: no tone on the developer's speakers.
        vv_audio_device outputs[32];
        int32_t outputCount = 0;
        vv_audio_list_outputs(outputs, 32, &outputCount);
        std::printf("output devices: %d\n", outputCount);
        vv_audio_monitor_status monitor = {};
        vv_audio_get_monitor_status(engine, &monitor);
        Check(monitor.state == VV_AUDIO_SOURCE_OFF, "the monitor is off with no device");
        for (int32_t i = 0; i < outputCount && i < 32; i++)
        {
            if (outputs[i].is_default)
            {
                vv_audio_set_monitor_device(engine, outputs[i].id);
                auto opened = std::chrono::steady_clock::now();
                while (monitor.state != VV_AUDIO_SOURCE_RUNNING && std::chrono::steady_clock::now() - opened < std::chrono::seconds(3))
                {
                    std::this_thread::sleep_for(std::chrono::milliseconds(20));
                    vv_audio_get_monitor_status(engine, &monitor);
                }

                Check(monitor.state == VV_AUDIO_SOURCE_RUNNING, "the monitor runs on the default output");
                std::this_thread::sleep_for(std::chrono::milliseconds(300));
                vv_audio_get_monitor_status(engine, &monitor);
                std::printf("monitor: underruns %lld, corrections %lld\n", monitor.underruns, monitor.drift_corrections);
                vv_audio_set_monitor_device(engine, nullptr);
            }
        }

        std::printf("destroying the engine\n");
        vv_audio_destroy(engine);
    }
}

namespace
{
    // A crash would otherwise lose the buffered output and say nothing of where it was: print the
    // exception and the module and offset it happened at, so a CI failure can be found.
    LONG WINAPI ReportCrash(EXCEPTION_POINTERS* exception)
    {
        void* address = exception->ExceptionRecord->ExceptionAddress;
        HMODULE module = nullptr;
        char name[MAX_PATH] = "?";
        if (GetModuleHandleExA(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT, static_cast<LPCSTR>(address), &module))
        {
            GetModuleFileNameA(module, name, MAX_PATH);
        }

        std::printf("CRASH: exception 0x%08lx at %s+0x%llx\n", exception->ExceptionRecord->ExceptionCode, name,
            static_cast<unsigned long long>(static_cast<const char*>(address) - reinterpret_cast<const char*>(module)));
        return EXCEPTION_EXECUTE_HANDLER;
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
    SetUnhandledExceptionFilter(ReportCrash);
    Step("gain, mute, and sends", GainMuteAndSends);
    Step("sum and limit", SumAndLimit);
    Step("drift", [] { Drift(1.001, "a device 0.1% fast"); Drift(0.999, "a device 0.1% slow"); });
    Step("running dry", RunningDry);
    Step("tap timing", TapTiming);
    Step("stage", Stage);
    Step("denoise", Denoise);
    Step("monitor bus", MonitorBus);
    Step("fill", Fill);
    Step("ramps", Ramps);
    Step("problems", Problems);
    Step("audio files", AudioFiles);
    Step("preview", Preview);
    Step("the engine on this PC", EngineOnThisPc);
    std::printf(s_failures == 0 ? "PASS\n" : "FAIL (%d)\n", s_failures);
    return s_failures == 0 ? 0 : 1;
}
