# Audio Detailed Design

## 1. Purpose

The Audio module gathers the sound that goes out with the stream video: the microphone and the game's audio from the capture board, mixed into the stream mix for recording and, later, streaming (system design 9.2–9.4, 10.8). This first version is the smallest pipeline that recording needs. Real-time voice conversion (RVC) is added later as a stage of the microphone's path, without changing the rest (system design 9.3).

- Sources: the microphone and the game's audio, both audio input devices on Windows (a capture board's audio is an input device) (the user's decision, 2026-10-27).
- Output: the stream mix, read by the encoder (recording detailed design). Monitoring the game's audio on the user's headphones comes in step 2 of this design (17).

Not in this design: voice conversion, effects, a virtual microphone (system design 9.4).

## 2. Responsibilities

| Responsibility | Owner |
|---|---|
| Listing audio input devices, opening them, capturing | Audio, native `VirtualVessel.Audio.Native` (WASAPI) |
| Gain, mute, and the stream mix; levels for the meters | Audio (native mix, managed control) |
| Saving the chosen devices and gains | Audio (the project's audio profile) |
| Encoding the mix | Video (recording detailed design) |
| The Audio Card and the meters | UI |

## 3. Module Boundaries

- The real-time path (capture → mix → the encoder) stays in native code. Managed code sets devices, gains, and mutes, and reads levels, a few times a second.
- Other modules see `IAudioService` and the stream tap (5.2), never WASAPI types.
- Capture's capture board source stays video only; its audio device is chosen here, as the "game audio" source (system design 10.21: capture audio is routed through Audio).

## 4. Main Classes and Interfaces

| Class | Kind | Role |
|---|---|---|
| `IAudioService` | Interface | Sources, their state, devices, gain and mute, levels, the stream tap (5) |
| `AudioService` | Class | An application service: opens the chosen devices, restores settings, reports problems |
| `AudioSourceKind` | Enum | `Microphone`, `GameAudio` |
| `AudioSettings` | Class | The audio profile (10) |
| `NativeAudioEngine` | Class | Wraps `VirtualVessel.Audio.Native` |
| `VirtualVessel.Audio.Native` | Native DLL | WASAPI capture per source, conversion to 48 kHz float stereo, the mix, a ring buffer for the tap |

## 5. Public API

```csharp
public interface IAudioService
{
    IReadOnlyList<IAudioSource> Sources { get; }        // Microphone, GameAudio
    IReadOnlyList<AudioDevice> Devices { get; }         // Input devices: id and name
    void RefreshDevices();
    AudioStreamTap StreamTap { get; }                   // For the encoder (5.2)
}

public interface IAudioSource
{
    AudioSourceKind Kind { get; }
    IReadOnlyObservable<AudioSourceState> State { get; } // Off, Starting, Running, Unavailable (device missing), Error
    string DeviceId { get; }
    void SelectDevice(string deviceId);                 // null turns the source off
    float GainDb { get; set; }                          // -60 to +12
    bool Muted { get; set; }
    float PeakDb { get; }                               // Since the last read, for the meter
}
```

### 5.2 The Stream Tap

```c
typedef struct vv_audio_stream_tap
{
    void* context;
    // Copies up to `frames` stereo float frames of the mix, ending no later than `until_qpc`, and
    // returns how many; writes the QPC of the first one.
    int32_t (*read)(void* context, float* out, int32_t frames, int64_t until_qpc, int64_t* first_qpc);
} vv_audio_stream_tap;
```

- Managed code gets it from `vv_audio_get_stream_tap` and passes it to the encoder as plain values; Video never links to the audio DLL.
- One reader at a time (the recording; streaming will share the same encoded audio).

## 6. Data Models

- Format inside: 48 kHz, 32-bit float, stereo. A mono microphone is copied to both channels.
- Time: each block carries the QPC of its first sample (WASAPI gives it per packet), the same clock as the video frames (recording detailed design 8.1).

## 7. State and Lifecycle

- `AudioService` starts after Logging and before Video's recording. Each source opens its saved device; a missing device is `Unavailable` and opens again when it appears (device notifications).
- A source with no device is `Off`; the mix then has only the other source, or silence.
- At shutdown the capture threads stop and the devices are released.

## 8. Important Sequences

1. Each source's capture thread (WASAPI shared mode, event driven, 10 ms) receives packets with their QPC.
2. It converts them to 48 kHz float stereo (WASAPI's own conversion, `AUTOCONVERTPCM`), applies the gain or mute, and writes them to its ring buffer.
3. The mix thread adds the sources by time into the stream ring buffer, limiting peaks to −1 dBFS, and updates the peak levels.
4. The encoder reads the stream ring buffer through the tap (5.2).

- Microphones and capture boards run on different clocks; their drift (a few samples a second) is absorbed in the mix by dropping or repeating one sample now and then, so neither ring buffer runs over.

## 9. Threading

- One capture thread per source and one mix thread, native, at above-normal priority (MMCSS "Audio"). No allocation, no lock held while waiting, no logging on them; counters only.
- Managed reads levels and states from the main thread every 100 ms.

## 10. Configuration

`Profiles/Audio/<id>.json`, the project's audio profile (project data detailed design 6; system design 6.3):

```json
{
  "schemaVersion": 1,
  "microphone": { "deviceId": "...", "gainDb": 0, "muted": false },
  "gameAudio": { "deviceId": null, "gainDb": -6, "muted": false }
}
```

- The microphone defaults to Windows' default input device on first use; the game audio has no default (a capture board must be chosen).
- `ProfileKind` gains `Audio`.

## 11. Storage

Settings only (10). No audio is stored by this module; recordings are written by Video.

## 12. Error Handling and Recovery

- A device unplugged: `Unavailable`; silence in its place; it opens again when plugged in.
- A device in exclusive use by another application: `Error` with an explanation ("Another application is using this device").
- Microphone access denied by Windows privacy settings: `Error` with "Allow microphone access in Windows Settings > Privacy" and a button that opens that page.
- A failure in Audio never stops video or recording; recording notes `AudioUnavailable`.

## 13. Logging and Diagnostics

- Devices opened (name, format), lost, and found again; problems. Nothing per block.
- Metrics: `Audio/Overruns` and `Audio/Underruns` per source, `Audio/DriftCorrections`, `Audio/MixLatency`.
- Audio is never written to logs or diagnostics (CLAUDE.md 20).

## 14. Testing Strategy

| Kind | Target |
|---|---|
| Native (ctest) | The mix: gains, mute, peak limiting, drift correction, ring buffers, the tap's timing, with synthetic signals |
| EditMode | `AudioService` with a fake engine: states, device loss and return, settings and migration |
| Hardware (manual) | A real microphone and capture board: levels, unplugging, the recording's sync (recording detailed design 14) |

## 15. Performance

- Latency from the microphone to the mix: about 20–30 ms (two 10 ms periods and the mix). RVC adds its own later; the video is then delayed to match (system design 10.9).
- CPU: well under 1% of one core for two sources.

## 16. Directory / Namespace

```text
native/audio/                          VirtualVessel.Audio.Native (CMake, tests)
Audio/Runtime/                         IAudioService, AudioService, NativeAudioEngine, AudioSettings
Audio/Tests/EditMode/
```

A new module `VirtualVessel.Audio` (system design 2.4). The DLL is built by `tools/native/build-audio.ps1`.

## 17. Open Issues and Steps

1. Microphone and game audio into the stream mix, levels, settings (this design).
2. Monitoring the game's audio on an output device (the user's headphones), with its own volume (system design 9.4).
3. Voice conversion as a stage on the microphone's path, and the video delayed to match.

## 18. UI

An "Audio" Card on the setup screen:

| Row | Content |
|---|---|
| Microphone | Device (Dropdown), gain (Slider, dB), mute (Toggle), level meter (Level Meter, components 3.5) |
| Game audio | Device (Dropdown, "None" first), gain, mute, level meter |
| Problem | The source's state and what to do, as for capture |

On the live screen, next to the record control, a compact meter per source with a mute button, so the user sees that sound is going in while recording.
