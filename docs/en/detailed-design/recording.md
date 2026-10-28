# Encoding and Recording Detailed Design

## 1. Purpose

Encode the stream video and the stream audio, and record them to a video file (system design 10.7–10.9, 10.13). Streaming (10.10–10.12) is added on the same encoding later, as another output.

- Recording comes before streaming (the user's decision, 2026-10-27): it is checked locally, without a network or a stream key.
- The defaults are 1920×1080 at 30 fps, with AAC audio, in MP4 (the user's decision). The video is AV1 where the GPU has an AV1 encoder and H.264 (High) elsewhere; the user can choose H.264 (the user's decision, 2026-10-28; why: [decisions/recording.md](../decisions/recording.md)).
- The audio is the stream mix of the Audio module (audio detailed design): the microphone and the game's audio.
- A spike (2026-10-27, branch `spike/encoder`) showed that encoding in the application keeps up: on the development PC (Core i7-13700F, RTX 4070), reading back every second frame and encoding it with the hardware encoder through Media Foundation left the frame time unchanged (16.7 ms at 60 fps), cost 0.35–0.48 ms of main thread a frame, used about 5% of the GPU's encoder and 1.4% of the CPU (24 threads), and dropped no frame.

Not in this design: streaming (RTMPS), external video output (Spout), other codecs (H.265, VP9).

## 2. Responsibilities

| Responsibility | Owner |
|---|---|
| Rendering the stream video (Stream Render Target) | Video (video detailed design) |
| Reading the stream video back and timing each frame | Video (`StreamFrameReader`) |
| Encoding video (AV1 or H.264) and audio (AAC), and muxing them into MP4 | Video, native `VirtualVessel.Encode.Native` |
| Starting, stopping, and naming a recording; its state | Video (`RecordingService`) |
| The stream audio (capture, mixing) | Audio (audio detailed design) |
| The record button, the recording state on the live screen, and the recording settings | UI |

## 3. Module Boundaries

- Video does not reference Audio's classes. Audio offers its stream bus as a native reader (`vv_audio_stream_tap`, audio detailed design 5.3); Video's managed code passes it to the encoder, and the encoder reads audio on its own thread. From the stream bus to the file, the audio stays in native code. What is mixed into the bus, and stages such as RVC before it, are Audio's (audio detailed design 5.2).
- Capture is not referenced: captured video reaches the file only through the rendered stream video (system design 10.2).
- The encoder's Media Foundation types stay in the native plugin. Managed code sees `IRecordingService` and plain values.

## 4. Main Classes and Interfaces

| Class | Kind | Role |
|---|---|---|
| `IRecordingService` | Interface (UI) | What UI uses: state, elapsed time, start, stop, the last file, the folder and quality (5) |
| `RecordingService` | Class (Video) | An application service: starts and stops recordings, checks the folder and free space, names files |
| `StreamFrameReader` | Class (Video) | Reads the Stream Render Target back (`AsyncGPUReadback`) at the output frame rate and stamps each frame with the shared clock |
| `NativeEncoder` | Class (Video) | Wraps `VirtualVessel.Encode.Native` (`IVideoEncoder`) |
| `RecordingEnvironment` | Class (Video) | What the service needs from outside: opening an encoder, reading frames, free space, the local time, the Videos folder. Tests give fakes, so the states are checked without a GPU or the plugin |
| `RecordingSettings` | Class | The streaming profile's encoding values and the recording folder (10) |
| `RecordingNames` | Static class | File names, folders, the free-space check, quality and bitrate; pure (8.2) |
| `FramePacing` | Static class | Which frames are read at the output frame rate; pure (8.1) |
| `VirtualVessel.Encode.Native` | Native DLL | AV1 or H.264, and AAC, through Media Foundation's Sink Writer, MP4 |

## 5. Public API

```csharp
public interface IRecordingService
{
    IReadOnlyObservable<RecordingState> State { get; }    // Idle, Starting, Recording, Stopping, Failed
    IReadOnlyObservable<RecordingProblem> Problem { get; } // None, FolderUnavailable, DiskFull, EncoderFailed, AudioUnavailable
    TimeSpan Elapsed { get; }                              // While recording
    string LastFile { get; }                               // The last finished file, for "Open folder"
    IReadOnlyObservable<RecordingOptions> Options { get; } // Folder, quality, size, frame rate
    void SetOptions(RecordingOptions options);             // Used from the next recording
    long? FreeBytes();                                     // Free space where recordings go; null when unreadable
    void Start();
    void Stop();
}
```

- `IRecordingService`, `RecordingOptions`, and the state and problem enums are in UI's `VirtualVessel.UI.Recording`, and Video implements them. Video references UI (as for `IStreamPreviewSource`), so UI cannot reference Video.
- `RecordingQuality`: `Standard` (6 Mbit/s), `High` (12 Mbit/s). A bitrate in the file reads as the nearer quality; an edited bitrate is kept until the quality is changed.
- `RecordingCodec`: `Av1`, `H264`. `RecordingOptions.Codec` is the format the next recording uses, and `Av1Available` whether the GPU has an AV1 encoder (read once at startup, `vv_encode_hardware_available`). Without one the codec is always H.264.

### 5.1 The Native API (`vv_encode.h`)

```c
int32_t vv_encode_api_version(void);                                           // 2
int32_t vv_encode_hardware_available(int32_t codec);                           // 1 when the GPU has an encoder for VV_ENCODE_CODEC_H264 or _AV1
int32_t vv_encode_open(const vv_encode_config* config, vv_encoder** encoder);  // path, width, height, fps, bitrate, codec
int32_t vv_encode_push_video(vv_encoder*, const uint8_t* bgra, int32_t length, int64_t qpc);  // VV_ENCODE_DROPPED with no free slot
int32_t vv_encode_get_stats(vv_encoder*, vv_encode_stats* stats);               // Pushed, written, dropped, last error, hardware or not
int32_t vv_encode_close(vv_encoder*);                                          // Drains, finishes the file; a file with no frame is deleted
```

- This version (API version 2) is video only. With audio, `vv_encode_config` gains the audio bitrate and the audio tap (or none), and the API version goes up.
- H.264 is written in the High profile (without it the encoder chose Constrained Baseline), and AV1 in its Main profile.
- The video is tagged BT.709, limited range (matrix, primaries, transfer, range), and the input is marked full range, so players convert the colors back as they were rendered. Untagged files looked darker in some players.

- Video frames are copied into four slots and encoded on the encoder's thread, as in the spike; a frame with no free slot is dropped and counted.
- Audio is pulled from the tap on the same thread, by time, so audio and video are written in order.

## 6. Data Models

- `RecordingState`: `Idle`, `Starting`, `Recording`, `Stopping`, `Failed`.
- `RecordingProblem`: `None`, `FolderUnavailable` (cannot create or write the folder), `DiskFull` (less than the reserve, 8.2), `EncoderFailed` (the encoder could not open or failed while writing), `AudioUnavailable` (recording goes on without audio).
- Files: `<folder>/VirtualVesselStudio_2026-10-27_22-30-05.mp4` (local time), AV1 Main or H.264 High, BT.709 limited range, AAC-LC 48 kHz stereo.

## 7. State and Lifecycle

```text
Idle ──Start──▶ Starting ──opened──▶ Recording ──Stop──▶ Stopping ──finished──▶ Idle
                   │                    │
                   └──failed──▶ Failed ◀┘ (encoder error, disk full)
```

- `Start` creates the folder if needed, checks the free space, names the file, and opens the encoder on a worker thread (starting Media Foundation can take a moment). Once it is open, the next frame turns it `Recording` and starts reading frames back.
- `Stop` stops reading, waits up to a second for readbacks on their way, then drains the encoder and finishes the file on a worker thread. The main thread never waits for the encoder.
- `Stop` while starting stops once the encoder is open. `Start` while starting, recording, or stopping does nothing.
- Recording goes on whatever screen is shown, and while tracking, capture, or the avatar change.
- At shutdown a recording in progress is stopped and finished, so the file is playable.
- `Failed` keeps the problem until the next `Start`. The part already written is finished and kept.

## 8. Important Sequences

### 8.1 A Frame

1. Video renders the Stream Render Target (every frame, as now).
2. At the output rate (30 fps from a 60 fps render: every second frame), `StreamFrameReader` requests a readback and notes the shared clock's time (QPC) of the frame. At most two readbacks wait; a frame past that is skipped and counted.
3. When the pixels arrive (a frame later), they are pushed to the encoder with that time.
4. The encoder's thread converts and encodes the frame, reads the audio for up to that time from the tap, and writes both.

- The stream bus is behind the moment it was heard by Audio's `StreamLatency` (audio detailed design 8.4): about 20–30 ms without stages, and more with RVC. The video frames are held that long before encoding, so the mouth and the voice match in the file (system design 10.9). Below one frame (33 ms) nothing is held.

### 8.2 Starting

- File names come from the local time (`RecordingNames.FileName`, pure); an existing name gets `_2`, `_3`.
- At least 2 GB must be free in the folder (`RecordingNames.HasRoom`, pure); about 45 minutes at the default bitrates. While recording, free space is checked every 10 s, and the recording stops with `DiskFull` below 500 MB.

## 9. Threading

- Main thread: readback requests and pushing the pixels (a copy into a slot, about 0.5 ms a frame).
- Encoder thread (native): conversion, AV1 or H.264 and AAC through Media Foundation, muxing, writing. Nothing waits for it on the main thread; a full queue drops a frame.
- The audio tap is read on the encoder thread; the Audio module fills it on its own thread (audio detailed design 9).

## 10. Configuration

`Profiles/Streaming/<id>.json`, the project's streaming profile (project data detailed design 6; system design 6.3), shared with streaming later:

```json
{
  "schemaVersion": 1,
  "width": 1920, "height": 1080, "frameRate": 30,
  "videoBitrate": 6000000, "audioBitrate": 160000,
  "recordingFolder": null,
  "videoCodec": null
}
```

- `videoCodec` is "av1" or "h264" once the user has chosen, and null until then: null records AV1 where the GPU has an AV1 encoder and H.264 elsewhere, so a profile shared by PCs follows each one. A chosen "av1" on a PC without an AV1 encoder records H.264 and keeps the choice. Added in schema version 1 without a step: a file without it reads as null.

- `width` and `height` are also the Stream Render Target's size; they are read at startup, before Video starts. A value out of range (width 320–3840 and height 180–2160, even; frame rate 1–60; bitrate 0.5–50 Mbit/s) falls back to its default (`RecordingSettings.Usable`), so a mistake in the file cannot stop the stream video.
- `recordingFolder` null means `Videos/Virtual Vessel Studio` in the user's own Videos folder, where users look for videos. Choosing the default folder again saves null too, so it follows the Videos folder. The folder is not in the Data Root, so moving the Data Root does not move recordings.
- The size and frame rate cannot change while recording (system design 10.15).
- `ProfileKind` gains `Streaming`; the project gets a streaming profile ID like the others, made on first use.

## 11. Storage

The recordings are the user's own files and are never deleted by the application.

## 12. Error Handling and Recovery

- The encoder fails to open (no encoder for the format, the file cannot be created): `Failed` with `EncoderFailed` or `FolderUnavailable`; nothing else stops.
- A hardware encoder is used when there is one; otherwise Media Foundation's software encoder (H.264 only; AV1 is used only with a hardware encoder, since software AV1 is too slow for real time). The format and whether the encoder is hardware are logged (system design 5.15).
- A write error while recording: the encoder stops, the part written is finished, and the state is `Failed`.
- No audio (the Audio module unavailable or no device): the recording goes on with video only and `AudioUnavailable` is shown.
- Recording never stops the avatar, tracking, or capture (system design 10.16).

## 13. Logging and Diagnostics

- Start and stop with the file name, size, encoder (hardware or software), and settings; problems at Warning or Error. Nothing per frame.
- Metrics (Diagnostics screen): `Recording/FramesWritten`, `Recording/FramesDropped` (no slot), `Recording/FramesSkipped` (readbacks waiting), `Recording/ReadbackLatency` (frames), `Recording/AvOffset` (ms, audio against video at the writer; added with audio).

## 14. Testing Strategy

| Kind | Target |
|---|---|
| EditMode | `RecordingNames` (names, collisions, free space), the state machine with a fake encoder, settings and migration |
| Native (ctest) | Encoding synthetic video and a synthetic tone; the file's duration, frame count, and audio length (the spike's test, extended) |
| PlayMode | Recording the stream video for a few seconds; the file plays with the expected size and duration (`ffprobe` when present) |
| Performance (Explicit) | The spike's measurement as a test: frame time with and without recording, main-thread cost, drops, CPU and GPU (`EncodeSpikeProbe`, moved to Video) |
| Manual | A/V sync: a clap seen and heard at the same moment in the file |

## 15. Performance

- Main thread at most 1 ms a frame for recording (measured 0.35–0.48 ms).
- No dropped frame at 1080p30 with the hardware encoder.
- No allocation per frame: four fixed slots; the readback's own buffers belong to Unity.

## 16. Directory / Namespace

```text
native/encode/                         VirtualVessel.Encode.Native (CMake, tests)
Video/Runtime/Recording/               RecordingService, RecordingEnvironment, StreamFrameReader, FramePacing, NativeEncoder, RecordingNames, RecordingSettings
UI/Runtime/Recording/IRecordingService.cs   IRecordingService, RecordingOptions, the state and problem enums
UI/Runtime/Screens/                    RecordControl (live screen), RecordingCard (setup screen)
```

- `StreamFrameReader` hands the readback's NativeArray straight to the encoder, so the Video assembly enables `allowUnsafeCode`.

The encoder DLL is built by `tools/native/build-encode.ps1` and kept out of Git like the capture plugin; the release build builds it (development workflow 21).

## 17. Open Issues

| Item | Plan |
|---|---|
| Giving the encoder the GPU texture directly (no readback) | Only if the readback costs too much on other PCs |
| Whether AV1 plays on PCs without the AV1 Video Extension | Check on another PC; until then the setup screen says to choose H.264 if files do not open |
| The format for streaming (YouTube takes AV1, other services may need H.264) | With streaming |
| Recording and streaming with different settings | With streaming |

## 18. UI

### 18.1 The Live Screen

A record control sits under the stream preview, so recording starts and stops from the live screen (the user's request, 2026-10-27).

| State | Shown |
|---|---|
| Idle | Button (Secondary) "● Record" (the ● is part of the text) |
| Starting, Stopping | The button disabled, "Starting recording…" / "Saving the file…" |
| Recording | A status chip "REC 00:12:34" (a filled circle and text, never color alone) and a Danger button "■ Stop recording" |
| Failed | A status chip with the problem and what to do (5), and "● Record" again |

- **Against mistakes** (the user's request; components 3.1, 3.11):
  - Stopping is a Danger action and runs only after a confirmation dialog: "Stop recording? The file so far is saved." with "Stop recording" (Danger) and "Cancel".
  - Starting needs no confirmation: starting by mistake loses nothing, and a stray start is stopped as above.
  - The button does not answer a second press while starting or stopping, and Space or Enter must be pressed on the button itself (no global shortcut yet).
- After stopping, "Saved: <file name>" and "Open folder" appear under the record control. "Open folder" opens Explorer with the file selected. Notifications (`INotificationService`) have no buttons, so this is on the live screen.
- A problem shows under the chip with what happened and what to do (for example, with too little space: delete files, or choose a folder on another drive on the setup screen).

### 18.2 The Setup Screen

A "Recording" Card: the folder (with "Change…" and "Open"), the quality (Standard 6 Mbit/s, High 12 Mbit/s), the free space, and the size and frame rate (1920×1080, 30 fps, fixed for now).

- The format: Segmented "AV1 (recommended)" / "H.264 (most compatible)". Without an AV1 encoder, AV1 is shown but cannot be chosen, and the description says the GPU does not support it.
- "Change…" opens Windows' folder screen (`SHBrowseForFolderW`) on a thread of its own, as the file screen does; rendering and the stream go on.
- The free space is read again every 10 seconds while the Card is shown.
- Changes are used from the next recording; a recording in progress keeps the settings it started with. The audio sources are on the Audio Card (audio detailed design 18).
