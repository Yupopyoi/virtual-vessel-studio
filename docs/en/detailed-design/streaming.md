# Streaming Detailed Design

## 1. Purpose

Send the stream video and audio straight from the application to a streaming service, without an external streaming application (system design 10.10–10.12).

- The first target is YouTube Live over RTMPS (system design 10.10). It starts with the user registering the server URL and the stream key; the YouTube API comes later (system design 10.11).
- "Another RTMP(S) server" can be chosen too, for other services that take RTMP(S) (Twitch and others). What is particular to a service stays inside its destination.
- The video and audio are the same rendering and stream bus as recording (recording detailed design, audio detailed design), and the audio delay (audio detailed design 8.5) applies the same way.
- Encoding is Windows' own Media Foundation, with a hardware encoder first (system design 10.7, the user's decision 2026-10-22). RTMP, FLV, and TLS are also built without external libraries (TLS through Windows' `SChannel`).

Not in this design: the YouTube API (OAuth, creating broadcasts), streaming to several destinations at once, other protocols such as SRT or WHIP, external video output (Spout).

## 2. Responsibilities

| Responsibility | Owner |
|---|---|
| Rendering the stream video (Stream Render Target) | Video (video detailed design) |
| Reading the video back and timing it | Video (`StreamFrameReader`; as NV12 for streaming, 8.1) |
| The stream bus and the audio delay | Audio (audio detailed design) |
| Encoding for the stream (H.264 or AV1, AAC), muxing into FLV, sending over RTMP(S), reconnecting | Streaming, native `VirtualVessel.Stream.Native` |
| Starting and stopping the stream, its state and problems, the destination's settings | Streaming (`StreamingService`) |
| Keeping the stream key | ProjectData (Windows Credential Manager, system design 6.13) |
| The stream controls (live screen), the destination and key (setup screen) | UI |

## 3. Module Boundaries

- A new module `VirtualVessel.Streaming` (system design 2.4, "Streaming"). It uses Video's `IStreamOutput` and `StreamFrameReader`, and takes Audio's tap as plain values, as recording does (recording detailed design 3).
- Media Foundation, RTMP, FLV, and TLS types stay in the native plugin. Managed code sees `IStreamingService` and plain values.
- What differs per destination (the URL's form, the codecs it takes, the recommended bitrate) is in `StreamDestination`, and is not carried into encoding or sending (system design 10.10: Encode, Multiplex, and Publish are kept apart).
- The stream key is not written to the streaming profile. The profile holds only a credential ID (`credentialId`); the value is in Windows Credential Manager (system design 6.13).

## 4. Main Classes and Interfaces

| Class | Kind | Role |
|---|---|---|
| `IStreamingService` | Interface (UI) | What UI uses: state, problem, elapsed time, start, stop, the destination's settings, registering the key (5) |
| `StreamingService` | Class (Streaming) | An application service: starts and stops the stream, watches reconnection, statistics |
| `StreamDestination` | Class (Streaming) | Per kind of destination: the default URL, the codecs it takes, the recommended bitrate; pure |
| `StreamPublisher` | Class (Streaming) | Wraps `VirtualVessel.Stream.Native` |
| `ICredentialStore` | Interface (ProjectData) | Keeps and reads secrets; `WindowsCredentialStore` implements it |
| `VirtualVessel.Stream.Native` | Native DLL | Encoding (Media Foundation MFTs), FLV, RTMP, TLS, the send queue, reconnection |

## 5. Public API

```csharp
public interface IStreamingService
{
    IReadOnlyObservable<StreamingState> State { get; }     // Idle, Connecting, Live, Reconnecting, Stopping, Failed
    IReadOnlyObservable<StreamingProblem> Problem { get; } // 6
    TimeSpan Elapsed { get; }                              // Since Live
    IReadOnlyObservable<StreamingOptions> Options { get; } // Destination, server URL, whether a key is registered, codec, bitrate
    void SetOptions(StreamingOptions options);             // Not while streaming (system design 10.15)
    void SetStreamKey(string key);                         // To Credential Manager; empty removes it; never read back
    StreamingStats Stats { get; }                          // Actual bitrate, queue, dropped frames, reconnects
    void Start();
    void Stop();
}
```

- `SetStreamKey` only writes; no API returns the key. UI knows only whether one is registered.

### 5.1 The Native API (`vv_stream.h`)

```c
int32_t vv_stream_open(const vv_stream_config* config, vv_stream** stream);
    // URL, key, width, height, fps, codec, video and audio bitrates, keyframe interval,
    // the audio tap (the same shape as vv_encode_audio_read)
int32_t vv_stream_push_video(vv_stream*, const uint8_t* nv12, int32_t length, int64_t qpc);
int32_t vv_stream_get_stats(vv_stream*, vv_stream_stats* stats);  // State, problem, bytes sent, queue, dropped frames, reconnects
int32_t vv_stream_close(vv_stream*);                             // Sends what is queued and disconnects
```

- Connecting, encoding, and sending run on the plugin's threads, and `open` does not wait for them; the state is read through `get_stats`.
- After `open`, the plugin uses the key only to connect, and never puts it in a log or an error string.

## 6. Data Models

- `StreamingState`: `Idle`, `Connecting` (until RTMP `publish` is accepted), `Live`, `Reconnecting`, `Stopping`, `Failed`.
- `StreamingProblem`: `None`, `NoStreamKey`, `ServerUnreachable` (name resolution, TCP, or TLS failed), `Rejected` (the service refused `publish`, usually a wrong key), `ConnectionLost` (reconnecting), `EncoderFailed`, `CodecNotSupported` (the destination does not take the codec), `AudioUnavailable` (streaming goes on without audio).
- `StreamDestinationKind`: `YouTube`, `CustomRtmp`.
- `StreamingStats`: the actual bitrate (last 5 s), the queue length (ms), dropped frames, reconnects, encoding time.

## 7. State and Lifecycle

```text
Idle ──Start──▶ Connecting ──publish accepted──▶ Live ──Stop──▶ Stopping ──▶ Idle
                    │                              │ ▲
                    │                              ▼ │ reconnected
                    │                         Reconnecting ──gave up──▶ Failed
                    └──failed (no key, refused, ...)──▶ Failed
```

- `Start` checks that a key is registered and opens the plugin. Connecting and encoding start on the plugin's threads; when `publish` is accepted, the state is `Live`.
- A lost connection is `Reconnecting`, tried again after 1, 2, 4, 8, 15, 30 s, ... (at most 30 s apart). After 10 minutes of failures it is `Failed` and the user is told. Each reconnection starts again from a keyframe.
- A failed stream never stops recording, the avatar, tracking, or capture (system design 10.16).
- At shutdown a stream in progress sends what is queued and disconnects.
- The resolution, codec, destination, URL, and key cannot change while streaming (system design 10.15). The setup screen disables them then and says why.

## 8. Important Sequences

### 8.1 A Frame

1. Video renders the Stream Render Target.
2. At the stream's output frame rate, `StreamFrameReader` reads it back. For streaming, a compute shader first turns it into NV12, BT.709 limited range (three quarters of the RGB data). It goes to the encoder as it is, with no color conversion in Media Foundation (on the CPU).
3. The plugin's encoding thread gives the NV12 to an MFT (the hardware H.264 or AV1 encoder) and takes the compressed frames it gives back.
4. On the same thread, the audio up to that frame's time is read from the tap and encoded by the AAC MFT, placed by its time as for recording (recording detailed design 8.1).
5. Both are made into FLV tags and put in the send queue. The sending thread makes them into RTMP chunks and sends them over TLS.

- Recording and streaming at the same time each encode on their own (NVENC handles several sessions). Sharing the encoded output is in 17.
- With recording and streaming both reading the stream bus, Audio's tap gets a read position per reader (audio API version 3: `vv_audio_get_stream_tap` takes `VV_AUDIO_READER_RECORDING` or `VV_AUDIO_READER_STREAMING`, one fixed tap for each).

### 8.2 A Congested Connection

- When the send queue holds more than 3 s, video frames are dropped until the next keyframe (audio is not dropped), so a slow connection does not keep adding delay. Dropped frames are counted, and the user is told the connection cannot keep up.
- If the queue stays full (30 s), the user is advised to lower the bitrate. Lowering it automatically is in 17.

### 8.3 Destinations

| Destination | Default URL | Video codec | Recommended video bitrate (1080p30) |
|---|---|---|---|
| YouTube | `rtmps://a.rtmps.youtube.com/live2` | AV1 where the GPU has an encoder, otherwise H.264 | 6 Mbit/s (3–10) |
| Another RTMP(S) server | Entered by the user (`rtmp://` or `rtmps://`) | H.264 (many services do not take AV1) | 6 Mbit/s |

- AV1 is sent with Enhanced RTMP (FLV `av01`). Whether YouTube takes AV1 over RTMPS is checked first in the implementation (a spike); if not, it is H.264.
- Keyframes every 2 s (YouTube's recommendation); video at a constant bitrate (CBR).

## 9. Threading

- Main thread: readback requests, and copying the pixels into a slot (as for recording).
- Encoding thread (native): video and audio encoding, FLV muxing.
- Sending thread (native): connecting, TLS, RTMP, sending, reconnecting; it never makes encoding wait.
- None of them makes the main thread wait.

## 10. Configuration

Added to `Profiles/Streaming/<id>.json`, the streaming profile shared with recording (recording detailed design 10):

```json
{
  "schemaVersion": 1,
  "width": 1920, "height": 1080, "frameRate": 30,
  "videoBitrate": 6000000, "audioBitrate": 160000,
  "recordingFolder": null, "videoCodec": null,
  "stream": {
    "destination": "youtube",
    "serverUrl": null,
    "credentialId": "<32 hex digits>",
    "videoCodec": null,
    "videoBitrate": 6000000
  }
}
```

- `serverUrl` null uses the destination's default URL.
- `stream.videoBitrate` is kept apart from recording's `videoBitrate` (recording is set by quality, streaming by the connection).
- A file without `stream` reads as the defaults above (still schema version 1, without a step).

## 11. Storage

- The stream key is kept in Windows Credential Manager as a generic credential (`CredWrite`, target `VirtualVesselStudio/stream/<credentialId>`, for that user and that PC only (`CRED_PERSIST_LOCAL_MACHINE`)). The profile holds only the ID that makes the target name. The ID is 32 hexadecimal digits; profiles can be edited or come from someone else, so its form is checked before use, and it cannot name a target outside this application's own.
- A profile exported and shared never contains the key; another PC needs the key registered again.
- Compared (the user agreed, 2026-10-30): written as it is in a settings file, the key leaks through sharing, Issue attachments, and backups. Encrypted with DPAPI in a file and Credential Manager both keep it from other users and other PCs; Credential Manager also keeps it out of files, and the user can see and remove it in Windows' own screen.
- A program running as the same user (malware) can read it whichever way it is kept. Not holding a key at all (the YouTube API's OAuth, with revocable tokens) is a later step of system design 10.11.

## 12. Error Handling and Recovery

| Situation | State and problem | What the user is told |
|---|---|---|
| No key registered | Does not start, `NoStreamKey` | Register the stream key under Streaming on the setup screen |
| The server cannot be reached | `Failed`, `ServerUnreachable` | Check the internet connection and the URL |
| The stream was refused | `Failed`, `Rejected` | Check the stream key in YouTube Studio |
| Lost while streaming | `Reconnecting`, `ConnectionLost` | Reconnecting automatically; recording goes on |
| The encoder cannot be used | `Failed`, `EncoderFailed` | Try again, or check the log on the Diagnostics screen |
| The destination does not take the codec | `Failed`, `CodecNotSupported` | Choose H.264 as the format on the setup screen |
| No audio | Stays `Live`, `AudioUnavailable` | Streaming without audio |

- TLS certificates are checked as Windows normally checks them; there is no setting to skip it.
- With `rtmp://` (unencrypted) the key travels unencrypted. The setup screen warns, and recommends `rtmps://` where the destination offers it.

## 13. Logging and Diagnostics

- Start and stop, the kind of destination, the server's host name, codec, bitrate, encoder (hardware or not), connection state changes, reconnects, and problems. Nothing per frame.
- **The key never appears in logs, diagnostics, error strings, or diagnostics exports** (system design 10.12). Only the URL's host name is logged, since some destinations put the key in the path. The key's state is logged only as registered or not.
- Metrics (Diagnostics screen, system design 5.15): `Streaming/Bitrate`, `Streaming/QueueMs`, `Streaming/FramesDropped`, `Streaming/Reconnects`, `Streaming/EncodeTime`, `Streaming/AvOffset`.

## 14. Testing Strategy

| Kind | Target |
|---|---|
| Native (ctest) | FLV tags (AVC, AAC, Enhanced RTMP AV1), RTMP chunks and AMF0, the handshake, the queue and how frames are dropped when it is full, the reconnection intervals; that the key never appears in logs or error strings |
| Native (integration) | On this PC, a few seconds sent to a local RTMP server (ffmpeg in listen mode, where it is on the developer's PATH), and the video and audio it received read back |
| EditMode | `StreamingService`'s states (with a fake plugin), `StreamDestination`, settings and migration, `ICredentialStore` (a fake, and writing to and removing from Windows Credential Manager) |
| PlayMode | The stream controls (start, the end confirmation, states, reconnecting) |
| Manual | An unlisted YouTube stream: video, audio, sync, disconnecting and reconnecting |

## 15. Performance

- As for recording, at most 1 ms of main thread a frame. Read back as NV12, it is three quarters of recording's data.
- Recording and streaming at once at 1080p30 without dropping frames (two encodes; NVENC around 10% in all, expected).
- The send queue holds at most 3 s (about 2.3 MB at 6 Mbit/s).

## 16. Directory / Namespace

```text
native/stream/                          VirtualVessel.Stream.Native (encoding, FLV, RTMP, TLS, tests)
Streaming/Runtime/                      StreamingService, StreamDestination, StreamPublisher
Streaming/Tests/EditMode/
ProjectData/Runtime/Credentials/        ICredentialStore, WindowsCredentialStore
UI/Runtime/Streaming/IStreamingService.cs
UI/Runtime/Screens/                     StreamControl (live screen), StreamingCard (setup screen)
Streaming/Plugins/x86_64/               VirtualVessel.Stream.Native.dll (kept out of Git)
Video/Shaders/                          The NV12 conversion
```

The DLL is built by `tools/native/build-stream.ps1` and kept out of Git.

## 17. Open Issues

| Item | Plan |
|---|---|
| Sharing the encoded output between recording and streaming | When two encodes prove too heavy; moving recording to MFTs and NV12 too makes it possible |
| Lowering the bitrate automatically to suit the connection | When congestion proves a problem |
| The YouTube API (creating broadcasts, titles, start and end) | A later step of system design 10.11 |
| Streaming to several destinations at once | When asked for |

## 18. UI

### 18.1 The Live Screen

The stream controls sit next to the record control.

| State | Shown |
|---|---|
| Idle | Button (Primary) "● Go live". Without a registered key it cannot be pressed, and says why |
| Connecting | The button disabled, "Connecting…" |
| Live | A status chip "LIVE 00:12:34" (the live shape and text) and a Danger button "■ End stream" |
| Reconnecting | A status chip "Reconnecting" (the warning shape) and "■ End stream" |
| Failed | A status chip with the problem and what to do, and "● Go live" again |

- Going live asks first: "Go live? Depending on your YouTube settings, it may be public at once." with "Go live" and "Cancel". A stray start would be public, so unlike recording it is confirmed.
- Ending is a Danger action, run only after a confirmation: "End the stream?" with "End stream" (Danger) and "Cancel".
- While live, the actual bitrate is shown small, with a warning when the connection cannot keep up.

### 18.2 The Setup Screen

A "Streaming" Card:

| Row | Content |
|---|---|
| Destination | Segmented: YouTube, Another RTMP(S) server |
| Server URL | For YouTube, the default URL (can be changed); otherwise entered. With `rtmp://`, a warning that it is not encrypted |
| Stream key | "Registered" or "Not registered". "Register…" opens a dialog with a masked field to paste it into; "Remove". The key's value is never shown |
| Format | Segmented: AV1, H.264 (one the destination or the GPU does not support cannot be chosen) |
| Video bitrate | Slider, 2 to 12 Mbit/s, with the destination's recommendation in the description |
| Connection test | Button: connects to the server and checks up to RTMP `connect` (no `publish`, so no stream starts) |

- While streaming, none of the rows can change (and they say why).
