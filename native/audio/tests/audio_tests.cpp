// The mix with synthetic signals (audio detailed design 14): gains, mute, sends, the limiter, drift
// correction, running dry, and the stream tap's timing; then the engine and the device list on this PC.

#include "vv_audio.h"

#include "device_source.h"
#include "dsp.h"
#include "mixer.h"

#include <windows.h>

#include <audioclient.h>

#include <chrono>
#include <cmath>
#include <cstdio>
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

    void Problems()
    {
        int state = 0;
        int problem = 0;
        DeviceSource::Classify(AUDCLNT_E_DEVICE_INVALIDATED, state, problem);
        Check(state == VV_AUDIO_SOURCE_UNAVAILABLE, "an unplugged device is unavailable");
        DeviceSource::Classify(AUDCLNT_E_DEVICE_IN_USE, state, problem);
        Check(state == VV_AUDIO_SOURCE_ERROR && problem == VV_AUDIO_PROBLEM_IN_USE, "a device in exclusive use");
        DeviceSource::Classify(E_ACCESSDENIED, state, problem);
        Check(problem == VV_AUDIO_PROBLEM_ACCESS_DENIED, "microphone access denied by Windows");
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
        vv_audio_get_stream_tap(engine, &tap);
        std::vector<float> out(48000 * Channels);
        LARGE_INTEGER now;
        QueryPerformanceCounter(&now);
        int64_t first = 0;
        int32_t read = tap.read(tap.context, out.data(), 48000, now.QuadPart, &first);
        Check(read > 0 && first < now.QuadPart, "the tap gives the stream bus up to now");

        std::printf("removing the source\n");
        Check(vv_audio_remove_source(engine, source) == VV_AUDIO_OK, "a source is removed");
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
    Step("ramps", Ramps);
    Step("problems", Problems);
    Step("the engine on this PC", EngineOnThisPc);
    std::printf(s_failures == 0 ? "PASS\n" : "FAIL (%d)\n", s_failures);
    return s_failures == 0 ? 0 : 1;
}
