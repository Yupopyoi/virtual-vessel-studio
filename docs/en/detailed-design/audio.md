# Audio Detailed Design

## 1. Purpose

The Audio module gathers the sound that goes out with the stream video and mixes it for recording and, later, streaming, and for the user's own monitoring (system design 9.2–9.4, 10.8, 10.9).

More sources and processing will join over time: real-time voice conversion (RVC) on the microphone, background music (BGM), and sound effects (SE) (the user's note, 2026-10-27). So the module is built as **sources**, each with a chain of **stages**, sent to **buses**; a new kind of sound is a new source or stage, and the mix, the buses, and the encoder do not change (system design 9.3).

This first version has two sources, the microphone and the game's audio (both audio input devices; a capture board's audio is an input device), with gain and mute, and the stream bus (the user's decision, 2026-10-27). The rest comes in the steps of chapter 17.

Not in this design: a virtual microphone (system design 9.4).

## 2. Responsibilities

| Responsibility | Owner |
|---|---|
| Listing audio devices, capturing from input devices, playing to the monitor device | Audio, native `VirtualVessel.Audio.Native` (WASAPI) |
| Sources, their stages, the buses, and the mix; levels for the meters; aligning sources in time | Audio (native engine, managed control) |
| Stages that run in Unity, such as RVC on Inference Engine | Voice (later), through the managed stage bridge (8.3) |
| Saving the sources, devices, gains, and sends | Audio (the project's audio profile) |
| Encoding the stream bus | Video (recording detailed design) |
| The Audio Card and the meters | UI |

## 3. Module Boundaries

- The engine runs in native code: capture, the stages that are native (gain, mute, limiter, later EQ), the mix, the buses. Managed code adds and removes sources, sets their stages and sends, and reads levels a few times a second.
- A stage that must run in Unity (RVC on Inference Engine, system design 9.5) is not moved into native code. It reads its source's audio from the engine and writes the result back through the managed stage bridge (8.3); its latency is declared, and the mix aligns the other sources to it (8.4).
- Other modules see `IAudioService`, the stage bridge, and the stream tap (5.3), never WASAPI types.
- Capture's capture board source stays video only; its audio device is chosen here as the "game audio" source (system design 10.21: capture audio is routed through Audio).
- Voice (RVC) depends on Audio, not the other way round.

## 4. Main Classes and Interfaces

| Class | Kind | Role |
|---|---|---|
| `IAudioService` | Interface | Sources, devices, buses, the stream tap (5) |
| `IAudioSource` | Interface | One source: state, gain, mute, sends, levels, its stages |
| `AudioSourceRole` | Enum | What a source is for: `Microphone`, `GameAudio`, later `Music`, `SoundEffect` |
| `AudioSourceInput` | Kind | Where its sound comes from: an input device, later a clip player (files, for BGM and SE) |
| `AudioBus` | Enum | `Stream` (recording, streaming), `Monitor` (the user's headphones) |
| `IManagedAudioStage` | Interface | A stage that runs in Unity, with its declared latency (8.3) |
| `AudioService` | Class | The application service: builds the sources from the profile, opens devices, reports problems |
| `AudioSettings` | Class | The audio profile (10) |
| `NativeAudioEngine` | Class | Wraps `VirtualVessel.Audio.Native` |
| `VirtualVessel.Audio.Native` | Native DLL | Capture, native stages, the mix, the buses, ring buffers, the stream tap |

## 5. Public API

```csharp
public interface IAudioService
{
    IReadOnlyList<IAudioSource> Sources { get; }                          // The microphone, then the game audio; BGM and SE add more
    IReadOnlyObservable<IReadOnlyList<AudioDevice>> InputDevices { get; } // ID, name, and whether it is Windows' default
    void RefreshDevices();
    AudioStreamTap StreamTap { get; }                   // For the encoder (5.3): the context and the function pointer
    TimeSpan StreamLatency { get; }                     // How far the stream bus is behind (8.4)
}

public interface IAudioSource
{
    string Id { get; }
    AudioSourceRole Role { get; }
    IReadOnlyObservable<AudioSourceState> State { get; } // Off, Starting, Running, Unavailable, Error
    IReadOnlyObservable<AudioProblem> Problem { get; }   // None, InUse, AccessDenied, Failed
    IReadOnlyObservable<AudioSourceOptions> Options { get; } // Device, gain (-60 to +12 dB), mute, the stream send (-inf for none)
    void SetOptions(AudioSourceOptions options);        // Applied at once and saved in the profile
    float PeakDb { get; }                               // The highest of the last 0.1 s, after the gain and before mute, for the meter
}
```

- The user's choices are handled together as `Options` and `SetOptions`, as for tracking and recording, so a change on the setup screen or the live screen shows on both.
- Adding and removing sources (`AddSource`, `RemoveSource`), the monitor bus and output devices, and managed stages (`Attach`) come with their steps (17). The first version has its two sources, the microphone and the game audio, from the start.
- `IAudioEngine` stands between `AudioService` and the native engine (`NativeAudioEngine`); tests give a fake one, so `AudioService` is checked without devices or the plugin.

### 5.2 Sources, Stages, Buses

```text
Microphone ─ capture ─▶ [RVC (managed, later)] ─▶ gain/mute ─┬─▶ Stream bus ─▶ limiter ─▶ stream tap ─▶ encoder
Game audio ─ capture ─────────────────────────▶ gain/mute ─┼─▶ Monitor bus ─▶ limiter ─▶ output device (step 2)
BGM / SE   ─ clip player (later) ─────────────▶ gain/mute ─┘
```

- Each source's sends set what reaches each bus, so the microphone can go to the stream only, while BGM goes to both, and game audio may go to the monitor too (step 2).
- Defaults: microphone to the stream only; game audio to the stream (and the monitor in step 2); BGM and SE to both.

### 5.3 The Stream Tap

```c
typedef struct vv_audio_stream_tap
{
    void* context;
    // Copies up to `frames` stereo float frames of the stream bus, ending no later than `until_qpc`,
    // and returns how many; writes the QPC of the first one.
    int32_t (*read)(void* context, float* out, int32_t frames, int64_t until_qpc, int64_t* first_qpc);
} vv_audio_stream_tap;
```

- Managed code gets it from `vv_audio_get_stream_tap` and passes it to the encoder as plain values; Video never links to the audio DLL.
- One reader at a time (recording; streaming shares the same encoded audio).

## 6. Data Models

- Format inside: 48 kHz, 32-bit float, stereo. A mono source is copied to both channels.
- Time: each block carries the QPC of its first sample, the same clock as the video frames (recording detailed design 8.1). For a device the QPC comes from WASAPI per packet; a clip player stamps what it plays.

## 7. State and Lifecycle

- `AudioService` starts after Logging and before Video's recording. It builds the sources of the profile and opens their devices; a missing device is `Unavailable`, and the source's thread tries it again every second. Windows' device notifications are not used: a second's delay is enough, and it saves their COM callbacks and threading.
- A source with no device is `Off`; the buses then carry the other sources, or silence.
- Sources can be added and removed while running; the mix picks them up at its next block.
- At shutdown the engine's threads stop and the devices are released.

## 8. Important Sequences

### 8.1 A Block

1. Each device source's capture thread (WASAPI shared mode, event driven, 10 ms) receives packets with their QPC, converts them to 48 kHz float stereo (`AUTOCONVERTPCM`), and writes them to the source's input ring buffer.
2. The mix thread, every 10 ms, takes each source's block for the same time (after its stages), applies gain, mute, and the sends, and adds it to the buses.
3. Each bus is limited to −1 dBFS and written to its ring buffer: the stream bus for the tap, the monitor bus for the output device (step 2).
4. The encoder reads the stream bus through the tap (5.3).

### 8.2 Clocks

Devices run on clocks of their own; their drift (a few samples a second) is absorbed at each source's input by dropping or repeating one sample now and then, so no ring buffer runs over or under.

### 8.3 Stages That Run in Unity (RVC)

- A managed stage declares its latency and block size. The engine gives it a ring buffer of its source's input and takes its output from another, both 48 kHz float.
- The stage's work (for RVC, Inference Engine on the GPU) runs on its own schedule in Unity; the engine only copies blocks in and out, so a slow stage never stalls the mix: if its output is late, the engine plays silence for that source and counts it.
- Removing a stage connects the source straight through again, without reopening the device (system design 9.3, 9.8).

### 8.4 Aligning Sources in Time

- Each source's latency is the sum of its stages' latencies (0 for gain and mute). The mix delays every other source to the largest one, so a converted voice and the game's audio stay together.
- `StreamLatency` reports it; Video delays the stream video by the same amount before encoding, so the mouth and the voice match (system design 10.9). Without RVC it is 30 ms (the 20 ms a source gathers before it plays, and a device period of about 10 ms), and the video is not delayed.
- The stream bus's times come from its sample count since the first block. The mix thread mixes the blocks the QPC clock says are due, not one per wake, so a late wake does not shift the times.

## 9. Threading

- One capture thread per device source and one mix thread, native, at above-normal priority (MMCSS "Audio"). No allocation, no lock held while waiting, and no logging on them; counters only.
- Managed stages run in Unity, never on those threads (8.3).
- Managed reads levels and states from the main thread every 100 ms.

## 10. Configuration

`Profiles/Audio/<id>.json`, the project's audio profile (project data detailed design 6; system design 6.3):

```json
{
  "schemaVersion": 1,
  "sources": [
    { "id": "...", "role": "microphone", "deviceId": "...", "gainDb": 0, "muted": false, "sends": { "stream": 0 } },
    { "id": "...", "role": "gameAudio", "deviceId": null, "gainDb": -6, "muted": false, "sends": { "stream": 0 } }
  ]
}
```

- Sources are a list, so BGM, SE, and more microphones are added without changing the format. A source of a role this version does not know is kept and not played; unknown fields at the top of the file are kept (CLAUDE.md 25). Unknown fields inside a source are not kept yet.
- JSON cannot hold -inf, so a send of -120 dB or below is none.
- The microphone defaults to Windows' default input device on first use; the game audio has no default (a capture board must be chosen).
- `ProfileKind` gains `Audio`.

## 11. Storage

Settings only (10). No audio is stored by this module; recordings are written by Video. Clip files for BGM and SE (later) are imported into the Data Root's `Audio/BGM` and `Audio/SE` (system design 6.10).

## 12. Error Handling and Recovery

- A device unplugged: `Unavailable`; silence in its place; it opens again when plugged in.
- A device in exclusive use by another application: `Error` with "Another application is using this device".
- Microphone access denied by Windows privacy settings: `Error` with "Allow microphone access in Windows Settings > Privacy" and a button that opens that page.
- A managed stage that fails or falls behind: its source plays silence (counted), and the source can be switched back to pass-through (system design 9.13).
- A failure in Audio never stops video or recording; recording notes `AudioUnavailable`.

## 13. Logging and Diagnostics

- Devices opened (name, format), lost, and found again; sources added and removed; stage latencies; problems. Nothing per block.
- Metrics: per source `Audio/<role>.Overruns`, `Underruns`, `DriftCorrections` (roles `Microphone`, `GameAudio`). `StageLate` comes with stages; `StreamLatency` is logged at start.
- Devices are logged by name, never by ID.
- Audio is never written to logs or diagnostics (CLAUDE.md 20).

## 14. Testing Strategy

| Kind | Target |
|---|---|
| Native (ctest) | The mix with synthetic signals: gains, mute, sends, limiting, drift correction, ring buffers, the tap's timing, a delayed stage and the alignment of the others |
| EditMode | `AudioService` with a fake engine: sources from the profile, adding and removing, device loss and return, settings and migration |
| Hardware (manual) | A real microphone and capture board: levels, unplugging, the recording's sync (recording detailed design 14) |

## 15. Performance

- Latency from a device to the stream bus: about 20–30 ms (two 10 ms periods and the mix), plus any stage's own latency.
- CPU: well under 1% of one core for two device sources.

## 16. Directory / Namespace

```text
native/audio/include/vv_audio.h        The C API (version 1)
native/audio/src/                      mixer (the mix, the bus, the tap; no threads or devices), frame_ring, dsp (gains, limiter),
                                       device_source (WASAPI capture), exports (the engine and the mix thread)
native/audio/tests/                    The mix, drift, and timing with synthetic signals; this PC's devices (skipped on CI)
Audio/Runtime/                         IAudioService, AudioService, AudioSettings, NativeAudioEngine (IAudioEngine)
Audio/Tests/EditMode/
UI/Runtime/Screens/                    AudioCard (setup screen), AudioStrip (live screen)
```

A new module `VirtualVessel.Audio` (system design 2.4); UI references Audio, as it does Capture and Tracking. The DLL is built by `tools/native/build-audio.ps1` into `Audio/Plugins/x86_64` and kept out of Git; the release build and CI build it too.

## 17. Steps

1. The microphone and the game's audio, gain and mute, the stream bus, levels, settings (this version).
2. The monitor bus: game audio and later BGM and SE on the user's headphones, with their own sends (system design 9.4).
3. Voice conversion as a managed stage on the microphone (8.3), and the stream video delayed to match (8.4).
4. Clip players for BGM and SE, with their files in the Data Root.

## 18. UI

An "Audio" Card on the setup screen, one row group per source:

| Row | Content |
|---|---|
| Heading | The source's name and a Status Chip of its state (a shape and text; "Muted" with the warning shape when muted) |
| Problem | What happened and what to do, as for capture. When Windows denies microphone access, an "Open microphone privacy settings" button (`ms-settings:privacy-microphone`) |
| Device | Select ("None" first, for both sources). A chosen device that is unplugged stays as "A device not connected now" |
| Volume | Slider, -60 to +12 dB in 1 dB steps |
| Mute | Toggle |
| Input level | Level Meter (components 3.5), -60 dB (empty) to 0 dB (full) |

- Devices are listed again every 5 seconds while the Card is shown, so a device plugged in can be chosen.
- Sends to the monitor come with step 2.
- On the live screen, under the record control, each source's mute IconButton (the crossed-out microphone when muted), its name, and a compact meter, so the user sees that sound is going in while recording (`live.md`).
