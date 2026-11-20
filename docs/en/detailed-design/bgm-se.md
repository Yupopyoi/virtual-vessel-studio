# BGM and SE Detailed Design

## 1. Purpose

Registers BGM and sound effects (SE) in the app and plays them on the stream (system design chapter 12). BGM and SE enter the mixer as Audio module sources, like the microphone and the game audio (audio detailed design chapter 17, stage 4). Streaming and recording get only the mixed audio and do not know BGM or SE (system design 12.8).

The first version reads WAV and MP3 (the user's decision, 2026-11-16). BGM plays one chosen track, looped or not. SE plays at once when pressed, and any number can overlap.

The app comes with BGM and SE ready to use (the user's request, 2026-11-17; 6.3).

To be added later (the user's decision, 2026-11-16). The first version does not build them, but is shaped so that adding them needs no rework (chapter 17):

- How BGM is ordered: playlists, shuffle, repeat, crossfades between tracks (system design 12.4).
- Keyboard shortcuts for SE, and later external devices and events (system design 12.5).
- More formats: M4A (AAC) and whatever else Windows can decode.
- BGM made inside the app (system design 16.32).

## 2. Responsibilities

| Responsibility | Owner |
|---|---|
| Registering BGM and SE files, their names, volumes, loops, and deleting them (CRUD) | Audio (`AudioClipLibrary`) |
| Decoding BGM a little at a time, playing, stopping, fading | Audio, the native `VirtualVessel.Audio.Native` (the BGM deck) |
| Decoding SE into memory and playing them overlapped | Audio, native (the SE voices) |
| Deciding what plays next (one track, later playlists) | Audio (`IBgmSequence`) |
| The BGM and SE sources' volume, mute, stream and monitor sends, level | Audio (the existing source mechanism) |
| The registration and settings screens, the live screen's controls | UI. Every action goes through a command |

## 3. Module Boundaries

- BGM and SE live in the Audio module (system design 12.15); no new module is made, so they share the mix, buses, limiter, and meters.
- Other modules see `IAudioClipLibrary`, `IBgmPlayer`, `ISoundEffectPlayer`, and the existing `IAudioService`. No Media Foundation or WASAPI types are shown.
- The UI asks `IBgmPlayer` and `ISoundEffectPlayer` for playback. When keyboard shortcuts or external devices are added, commands (such as `PlaySoundEffectCommand`) come with them as their way in, so the screen's buttons and the shortcuts go through the same command (chapter 17).
- BGM made in the app (system design 16.32) registers its audio in this library, using only its public API.

## 4. Main Classes and Interfaces

| Class | Kind | Role |
|---|---|---|
| `IAudioClipLibrary` | Interface | Registering, listing, renaming, changing volume and loop, deleting BGM and SE. Results are `CrudResult` (system design 6.31) |
| `AudioClipLibrary` | Class | Reads and writes `Audio/BGM/<id>/` and `Audio/SE/<id>/` |
| `AudioClipKind` | Enum | `Bgm`, `SoundEffect` |
| `BuiltInAudioClips` | Class (service) | Registers the BGM and SE that come with the app in the library on the first start (6.3) |
| `AudioClipRecord` | Record | One registration: ID, kind, name, volume, loop, length, original file name, when it was added |
| `IBgmPlayer` | Interface | Plays and stops BGM, and its current state |
| `IBgmSequence` | Interface | Decides the next track. The first version has `SingleTrack` (one track, looped or not) |
| `ISoundEffectPlayer` | Interface | Plays an SE, stops them all |
| `AudioSourceRole.Music`, `SoundEffect` | Enum values | Sources that become mixer rows (planned in audio detailed design chapter 4) |
| The native BGM deck | C++ | A decoding thread reads the file ahead into a ring; the mix thread reads from it |
| The native SE voices | C++ | Play SE held in memory, overlapped up to a fixed number |

## 5. Public API

```csharp
public interface IAudioClipLibrary
{
    CrudResult<IReadOnlyList<AudioClipRecord>> List(AudioClipKind kind);
    CrudResult<AudioClipRecord> Get(string id);
    // Whether a file can be registered, and if not, why (its format, unreadable, an SE too long): for the user.
    Task<AudioClipProblem> CheckAsync(AudioClipKind kind, string sourcePath, CancellationToken cancellationToken);
    // Copies the file and checks it can be read and how long it is. Heavy, so called off the main thread.
    Task<CrudResult<AudioClipRecord>> AddAsync(AudioClipKind kind, string sourcePath, string name, IReadOnlyDictionary<string, string> names, CancellationToken cancellationToken);
    CrudResult<AudioClipRecord> Update(string id, AudioClipChange change); // name, volume, loop
    CrudResult Delete(string id);                // Busy for the BGM that plays
    CrudResult Move(string id, int position);    // To a place in its kind's list (0 is first); past the end is last
    IReadOnlyList<string> FileExtensions { get; } // wav and mp3 in the first version
    event Action<AudioClipKind> Changed;          // after adding, changing, deleting; the players follow it
}

public interface IBgmPlayer
{
    IReadOnlyObservable<BgmState> State { get; } // the track's ID, Idle / Starting / Playing / Stopping / Failed, the problem
    void Play(IBgmSequence sequence);            // fades out what plays and replaces it
    void Stop();                                 // fades out
    float? Progress { get; }                     // 0 to 1; null when nothing plays
}

public interface IBgmSequence
{
    // The track to play first, and when the current one ends (a track not looped). Null ends.
    BgmCue Next(BgmCue finished);
}

public interface ISoundEffectPlayer
{
    void Play(string id);  // overlaps; one not loaded yet is not played, and is logged
    void StopAll();
    bool IsReady(string id);
}
```

- `CheckAsync` is there to tell the user why `AddAsync` returns `Invalid`, as a CRUD result (`CrudResult`) holds only the kind of failure (system design 6.31).
- The BGM that plays cannot be deleted (`Busy`): the deck's thread has its file open. Its Delete is disabled with the reason. An SE does not keep its file open once loaded, so it can always be deleted; the SE player then removes it from the engine.
- A `BgmCue` holds a track's ID and whether it loops. The first version's `SingleTrack` returns the chosen track once, following its loop setting. Playlists and shuffle are added as new `IBgmSequence` implementations (chapter 17); the player and the native deck do not change.
- Playback never rebuilds the audio pipeline during a stream (system design 12.11). The sources and buses exist from start-up.

## 6. Data Model

### 6.1 Registered files

In the Data Root's `Audio/` (system design 6.10, 12.6).

```text
Audio/
├─ BGM/
│  ├─ order.json            the order the user put them in (below)
│  └─ bgm-<id>/
│     ├─ audio.mp3          a copy of the original (its own extension)
│     └─ audio-profile.json
└─ SE/
   ├─ order.json
   └─ se-<id>/
      ├─ audio.wav
      └─ audio-profile.json
```

```json
{
  "schemaVersion": 1,
  "name": "Fluffy afternoon",
  "gainDb": 0,
  "loop": true,
  "durationSeconds": 31.0,
  "originalName": "fuwafuwa.mp3",
  "addedAtUtc": "2026-11-16T12:00:00Z"
}
```

- The original file is copied unchanged and decoded each time it plays. A converted copy would turn an MP3 into a WAV about ten times its size.
- Adding copies into a sibling folder (`.part`) and then renames it, so a half-made one is never listed (as VoiceModelStore does).
- Only BGM uses `loop`; SE keeps it `false` and does not use it (the settings files write nulls too, so it cannot be left out).
- Fields this version does not know are written back, not dropped (CLAUDE.md 25). Examples to come: an SE's `shortcut`, how a made BGM was made (system design 16.32).
- `name` is 1 to 60 characters. Equal names are allowed; the ID tells them apart.
- `names` (built-in files only): the name per language (`{ "ja": "サンプル１（ポップ）", "en": "Sample 1 (Pop)" }`). Kept until the user renames the file, then removed (changing the volume or the loop keeps it). The screens show `AudioClipRecord.NameIn(language)`: the name in the screen's language, else the English one, else `name`. As a field was only added, schemaVersion stays 1 (an earlier version keeps it as an unknown field).
- The list (`List`) is in the order the user put the files in (2026-11-20). `order.json` (`{ "schemaVersion": 1, "ids": ["se-…", …] }`) holds each kind's IDs from the first. Those not in it (added after the last reordering, or never reordered) come after, in the order they were added. IDs of deleted files are skipped and drop out at the next reordering. The order is one file so that reordering does not rewrite every `audio-profile.json`. The setup and the live screens both show this order.

### 6.2 Sources in the Audio Profile

The Audio Profile's `sources` (audio detailed design chapter 10) gain one `music` and one `soundEffect` source, with no device.

```json
{ "id": "...", "role": "music", "gainDb": -6, "muted": false, "sends": { "stream": 0, "monitor": 0 } }
```

- Older files without them get them with defaults: BGM -6 dB, SE 0 dB, sent to both the stream and the monitor (the defaults of audio detailed design 5.2).
- The last BGM played, and whether it was playing, are not saved, so BGM never starts by itself at start-up. When playlists are added, only the chosen playlist is saved.

### 6.3 Built-in BGM and SE

The app comes with BGM and SE ready to use. The project made them, and they are under the app's own license, Apache-2.0.

```text
StreamingAssets/Audio/BuiltIn/
├─ built-in-audio.json      the list: ID, kind, file, Japanese and English names
├─ bgm/<name>.wav
└─ se/<name>.wav
```

```json
{ "schemaVersion": 1, "clips": [ { "id": "bgm-cute-pop", "kind": "bgm", "file": "bgm/cute-pop.wav", "names": { "ja": "きらきらポップ", "en": "Sparkly Pop" } } ] }
```

- After start-up, `BuiltInAudioClips` registers those not registered yet, with `AddAsync`. Like any registration, they are copied into the Data Root. The user can change their names, volumes, and loops, and delete them. Registering runs in the background so start-up does not wait.
- What was registered is remembered in the Data Root's `Audio/built-in-audio.json` (`{ "schemaVersion": 1, "added": ["bgm-cute-pop", ...] }`), written after each one. What the user deleted is not registered again. Only those added in a later version are registered.
- One that cannot be registered (no Media Foundation, for example) is not remembered and is tried again on the next start. The others are still registered.
- A registration gets `name` (chosen by Windows' display language, `Localizer.ChooseInitialLocale`) and the names in every language (`names`). Until the user renames it, it is shown in the screen's language (on the setup and the live screens, shown again when the language is switched), so a piece's name is not Japanese on an English screen (the user's request, 2026-11-20). Once the user renames it, the name is the user's data and is shown in every language. Saving the shown name unchanged in renaming does not count as a rename.
- The files are WAV (mono, 48 kHz, 16-bit), not MP3. An MP3 has the encoder's silence at its start and end, which Media Foundation does not remove, so a loop would break at its seam. They are mono because the pieces' only difference between left and right was a few milliseconds of delay, which halves their size. About 20 MB in all.
- `tools/audio/build_builtin_audio.py` renders them (numpy and scipy; `tools/audio/requirements.txt`). Its random seeds are fixed, so it renders the same files every time. BGM is levelled by loudness (RMS -19 dBFS), SE by peak (-3 dBFS).

## 7. State and Lifecycle

- `AudioService` makes the BGM and SE sources in the mixer at start-up. The native deck and SE voices are made with the engine and stopped with it at exit.
- After start-up, all registered SE are decoded off the main thread and handed to the native side. An added SE is handed over alone; a deleted one is removed.
- BGM states: `Idle` → (Play) `Starting` (reading ahead) → `Playing` → (Stop, or the end with nothing next from the sequence) `Stopping` (fading) → `Idle`. `Failed` when it cannot be read.
- The BGM player has frame work only while it has a track, reading the deck's state every tenth of a second; with none, it costs the frames nothing.
- Deleting a BGM or SE that plays stops it first.

## 8. Main Flows

### 8.1 Playing BGM

1. The UI passes `IBgmPlayer.Play` a `SingleTrack` of the chosen track.
2. `BgmPlayer` takes the first track from `Next` and gives the native deck the file's path, the loop, and the track's volume.
3. The deck's decoding thread opens the file with Media Foundation, converts it to 48 kHz float stereo, and writes about 1.5 seconds ahead into the ring (about 2.7 seconds), topping it up before it runs short. Mono goes to both sides, and three or more channels keep their front left and right. The plugin converts the rate by 4-point Hermite interpolation (2026-11-16): Media Foundation is asked for the sample format only, and the rate and channels come as the file has them. The converter keeps its state through a track, so reading in pieces, or going back to the start for a loop, leaves no seam. A 1 kHz tone at 44.1 kHz came out within 0.0004.
4. Every 10 ms the mix thread reads one block from the ring and adds it to the buses as the BGM source, with its volume, mute, and sends. It never reads the file.
5. A looped track goes back to its start on the decoding thread and goes on writing, so no silence comes at the seam.
6. When a track not looped ends, the deck says so. `BgmPlayer` calls `Next` and plays the next track if there is one, or goes `Idle`.

- Switching and stopping bring what plays down over about 0.3 s, and a new track up from its start over about 0.05 s, so nothing clicks.
- An empty ring (decoding late) is played as silence and counted (`Audio/Music.Underruns`).

### 8.2 Playing an SE

1. `PlaySoundEffectCommand` calls `ISoundEffectPlayer.Play(id)`.
2. The SE goes into a free native voice from its start and sounds from the next 10 ms block.
3. With no free voice, the oldest one is brought down briefly and replaced.
4. There are 16 voices. Each holds only its position and volume; the samples are shared, one per SE.

- An SE's own volume (`gainDb`) is applied as it enters a voice. The SE source's volume and mute are applied in the mixer.

## 9. Threads and Asynchrony

- The mix thread (existing): only reads the deck's ring and the SE voices. No allocation, waiting locks, or file reads (audio detailed design chapter 9).
- The deck's decoding thread (new, native, normal priority): decodes with Media Foundation and writes the ring. The ring has one writer and one reader, with no lock.
- SE decoding: managed, off the main thread; the samples are handed to the native side under the engine's lock (a swap only, as for the preview; audio detailed design 5.5).
- Registration (copying and checking the file): managed, off the main thread.

## 10. Settings

- The sources' volume, mute, and sends (6.2).
- Each track's and SE's volume and loop are in its registration file (6.1), not the Project's Profile: they belong to the material and stay the same across Projects.

## 11. Storage

As in 6.1 and 6.2. BGM and SE files are the user's assets (CLAUDE.md 25).

## 12. Errors and Recovery

| What happened | Behavior | Shown to the user |
|---|---|---|
| The file to register cannot be read, or is another format | Not registered (`Invalid`) | The audio file cannot be read |
| An SE longer than 30 seconds | Not registered (`Invalid`) | Sound effects can be up to 30 seconds |
| The file to play is missing or damaged | `Failed`; other audio goes on | This BGM cannot be played |
| Decoding is late | The gap is silence and counted | Nothing; counted in diagnostics |
| No Media Foundation (N editions) | BGM and SE cannot be used | The audio file cannot be read, with the Media Feature Pack's guidance |

- A BGM or SE failure never stops the microphone, the stream, or a recording (system design 12.11).

## 13. Logging and Diagnostics

- Logs registering, deleting, starting and ending playback, and failures. Tracks are logged by ID, without file paths or names (the names of the user's files can be personal).
- Failures log the format, rate, channel count, and HRESULT (system design 12.14).
- Metrics: `Audio/Music.Underruns`, `Audio/SoundEffect.VoicesStolen`.
- The audio itself is never logged.

## 14. Testing Strategy

| Kind | Target |
|---|---|
| Native (ctest) | The deck with written WAV and MP3 files: no silence at a loop's seam, the end notice, fades, silence when the ring is empty. SE voices: overlapping, replacing with no free voice. Mixing in stereo |
| EditMode | `BuiltInAudioClips`: registering on the first start and the names' language, not adding again, not bringing back deleted ones, adding only new ones, leaving failed ones for the next start, the shipped list being well formed. Through the real plugin, every shipped file registers. `AudioClipLibrary`: add, list, change, delete, reorder (a new one comes last, a deleted one leaves no gap), a built-in file's names per language (kept until it is renamed), half-made folders, keeping unknown fields, an SE too long. `SingleTrack`, `BgmPlayer`'s states with a fake engine |
| PlayMode | The settings and live screens' controls (with their screen spec) |
| Hardware (manual) | BGM and SE reach the stream and recordings, and the monitor send works |

## 15. Performance

- The mix thread's work per block is a copy from the ring and the sum of the SE voices that sound.
- Decoding MP3 is expected well under 1% of a core. The deck's ring holds about 2 seconds (about 0.8 MB).
- SE memory: about 11.5 MB for 30 seconds of stereo. A limit for many registrations is measured and set during implementation (chapter 17).

## 16. Folder / Namespace

```text
native/audio/src/        bgm_deck (the decoding thread and ring), sound_effects (the voices), audio_file (adds stereo decoding)
native/audio/include/    vv_audio.h (API version 11)
Audio/Runtime/Clips/     IAudioClipLibrary, AudioClipLibrary, AudioClipRecord, BuiltInAudioClips
StreamingAssets/Audio/BuiltIn/  the built-in BGM and SE (6.3)
tools/audio/             the scripts that render them
Audio/Runtime/Playback/  IBgmPlayer, BgmPlayer, IBgmSequence, SingleTrack, ISoundEffectPlayer
UI/Runtime/Screens/      the BGM and SE Cards (settings screen), the live screen's controls
```

## 17. Stages and Open Issues

Stages:

1. Native: stereo decoding, the BGM deck (loop, end notice, fades), SE voices. Done (2026-11-17).
2. Managed: the sources, the library (CRUD), `BgmPlayer`, `SingleTrack`, `SoundEffectPlayer`. Done. The UI calls the players' methods directly (no UI `IUiCommand` yet); commands come with keyboard shortcuts, as their way in.
3. Settings screen: the mixer rows, the BGM and SE Cards (`setup.md` chapter 14). Done.
4. Live screen: choosing, playing, stopping BGM and its volume; SE buttons (`live.md`). Done.
5. The built-in BGM and SE (6.3). Done (2026-11-17).

An integration test through the real plugin (`AudioPlayersIntegrationTests`) registers a WAV, plays it as BGM to its end, is refused deleting it while it plays, and loads and plays it as an effect.

To be added later, and where (without rework):

- Playlists, shuffle, repeat: `IBgmSequence` implementations and saved playlists (`Audio/Playlists/`).
- Crossfades and gapless playback between tracks: a deck API to queue the next track, decoding two at once.
- SE shortcuts: a `shortcut` in `audio-profile.json`, and UI key bindings that call `PlaySoundEffectCommand`.
- M4A and others: widen `FileExtensions`. Decoding is Media Foundation's, so the change is expected to need only checking.
- BGM made in the app (system design 16.32): registers its audio with `AddAsync`.

Open issues:

- Limits on the number of SE and their memory.
- Lowering BGM under the voice (ducking), if the user asks for it.
