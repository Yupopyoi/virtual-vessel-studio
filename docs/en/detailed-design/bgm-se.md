# BGM and SE Detailed Design

## 1. Purpose

Registers BGM and sound effects (SE) in the app and plays them on the stream (system design chapter 12). BGM and SE enter the mixer as Audio module sources, like the microphone and the game audio (audio detailed design chapter 17, stage 4). Streaming and recording get only the mixed audio and do not know BGM or SE (system design 12.8).

The first version reads WAV and MP3 (the user's decision, 2026-11-16). BGM plays one chosen track, looped or not. SE plays at once when pressed, and any number can overlap.

The app comes with BGM and SE ready to use (the user's request, 2026-11-17; 6.3).

To be added later (the user's decision, 2026-11-16). The first version does not build them, but is shaped so that adding them needs no rework (chapter 17):

- How BGM is ordered: playlists, shuffle, repeat, crossfades between tracks (system design 12.4).
- Playing SE from external devices and events (system design 12.5). Keyboard shortcuts are built (2026-11-21; 8.3).
- More formats: M4A (AAC) and whatever else Windows can decode.
- BGM made inside the app (system design 16.32).

A crossfade when switching tracks (8.5) and ducking, lowering BGM only while the user talks (8.4), were added (the user's request, 2026-11-23).

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
- The UI asks `IBgmPlayer` and `ISoundEffectPlayer` for playback. For SE, the buttons of the setup and live screens and the shortcuts go through the same `PlaySoundEffectCommand` (system design 14.28; 8.3). External devices will come in through this command too.
- The UI takes the shortcut keys (`VirtualVessel.UI.Shortcuts`; system design 14.27). Audio keeps the keys' text as it is, without knowing what it means.
- BGM made in the app (system design 16.32) registers its audio in this library, using only its public API.

## 4. Main Classes and Interfaces

| Class | Kind | Role |
|---|---|---|
| `IAudioClipLibrary` | Interface | Registering, listing, renaming, changing volume and loop, deleting BGM and SE. Results are `CrudResult` (system design 6.31) |
| `AudioClipLibrary` | Class | Reads and writes `Audio/BGM/<id>/` and `Audio/SE/<id>/` |
| `AudioClipKind` | Enum | `Bgm`, `SoundEffect` |
| `BuiltInAudioClips` | Class (service) | Registers the BGM and SE that come with the app in the library on the first start (6.3) |
| `AudioClipRecord` | Record | One registration: ID, kind, name (and, for a built-in file, the names per language), `Settings` (`AudioClipSettings`: the volume and loop the user changes), and `File` (`AudioClipFile`: unchanged since it was registered: the file in the library, the original file name, the length, when it was added) |
| `IBgmPlayer` | Interface | Plays and stops BGM, and its current state |
| `IBgmSequence` | Interface | Decides the next track. The first version has `SingleTrack` (one track, looped or not) |
| `ISoundEffectPlayer` | Interface | Plays an SE, stops them all |
| `AudioSourceRole.Music`, `SoundEffect` | Enum values | Sources that become mixer rows (planned in audio detailed design chapter 4) |
| The native BGM deck | C++ | A decoding thread reads the file ahead into a ring; the mix thread reads from it |
| The native SE voices | C++ | Play SE held in memory, overlapped up to a fixed number |
| `BgmMixOptions` | Struct | Ducking (on or off, how far, how loud a voice it reacts to) and the crossfade's length. `IAudioService` holds it, saved in the Audio Profile (6.2) |
| The native crossfade (`BgmCrossfader`) | C++ | Holds two BGM decks, takes them in turn when switching, and adds the two into one source (8.5) |
| The native ducking | C++ | In the mixer, lowers the BGM source by how loud the microphone's block is (8.4) |

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
    CrudResult<AudioClipRecord> Update(string id, AudioClipChange change); // name, volume, loop, an SE's shortcut
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

`IAudioService` (audio detailed design 5) gains how BGM is mixed (2026-11-23):

```csharp
IReadOnlyObservable<BgmMixOptions> BgmMix { get; } // ducking and the crossfade, saved in the Audio Profile
void SetBgmMix(BgmMixOptions options);             // applies at once
IReadOnlyObservable<bool> Ducking { get; }          // whether BGM is lowered under the voice now (every 0.1 s)

public readonly struct BgmMixOptions
{
    bool Ducking { get; }            // off by default (the user's decision, 2026-11-23)
    float DuckDepthDb { get; }       // how far: -24 to -3 dB, default -12
    float DuckThresholdDb { get; }   // how loud a voice it reacts to: -60 to -20 dBFS, default -40
    float CrossfadeSeconds { get; }  // a switch's length: 0 to 5 s, default 2; 0 is the switch as before
}
```

- How BGM is mixed is the mixer's work, so it is on `IAudioService`, not `IBgmPlayer`. `BgmPlayer` does not change.

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
  "addedAtUtc": "2026-11-16T12:00:00Z",
  "shortcut": "Ctrl+Alt+1"
}
```

- The original file is copied unchanged and decoded each time it plays. A converted copy would turn an MP3 into a WAV about ten times its size.
- Adding copies into a sibling folder (`.part`) and then renames it, so a half-made one is never listed (as VoiceModelStore does).
- Only BGM uses `loop`; SE keeps it `false` and does not use it (the settings files write nulls too, so it cannot be left out).
- Fields this version does not know are written back, not dropped (CLAUDE.md 25). An example to come: how a made BGM was made (system design 16.32).
- `shortcut` (SE only): the keys that play the SE whichever app is in use (8.3). `Ctrl`, `Alt`, `Shift`, then the key, joined by `+` (`"Ctrl+Alt+1"`); the same text in every language. Null for none; up to 40 characters. Kept here, like the volume, as a property of the material. Only a field was added, so schemaVersion stays 1.
- `name` is 1 to 60 characters. Equal names are allowed; the ID tells them apart.
- `names` (built-in files only): the name per language (`{ "ja": "サンプル１（ポップ）", "en": "Sample 1 (Pop)" }`). Kept until the user renames the file, then removed (changing the volume or the loop keeps it). The screens show `AudioClipRecord.NameIn(language)`: the name in the screen's language, else the English one, else `name`. As a field was only added, schemaVersion stays 1 (an earlier version keeps it as an unknown field).
- The list (`List`) is in the order the user put the files in (2026-11-20). `order.json` (`{ "schemaVersion": 1, "ids": ["se-…", …] }`) holds each kind's IDs from the first. Those not in it (added after the last reordering, or never reordered) come after, in the order they were added. IDs of deleted files are skipped and drop out at the next reordering. The order is one file so that reordering does not rewrite every `audio-profile.json`. The setup and the live screens both show this order.

### 6.2 Sources in the Audio Profile

The Audio Profile's `sources` (audio detailed design chapter 10) gain one `music` and one `soundEffect` source, with no device.

```json
{ "id": "...", "role": "music", "gainDb": -6, "muted": false, "sends": { "stream": 0, "monitor": 0 } }
```

- Older files without them get them with defaults: BGM -6 dB, SE 0 dB, sent to both the stream and the monitor (the defaults of audio detailed design 5.2).
- How BGM is mixed (`BgmMixOptions`, chapter 5) is in the Audio Profile's `bgm` (2026-11-23): `"bgm": { "ducking": false, "duckDepthDb": -12, "duckThresholdDb": -40, "crossfadeSeconds": 2 }`. Only fields were added, so schemaVersion stays 1. A file without it reads the defaults, and values out of range are brought into range.
- The last BGM played, and whether it was playing, are not saved, so BGM never starts by itself at start-up. When playlists are added, only the chosen playlist is saved.

### 6.3 Built-in BGM and SE

The app comes with BGM and SE ready to use. The project made them, and they are under CC0 1.0 (the user's decision, 2026-11-20), so they can be used in streams and videos, monetized or not, without asking and without credit. The app and the scripts that make the sounds (`tools/audio/`) stay Apache-2.0. The folder's `LICENSE.txt` (a Japanese and English statement and the CC0 text) says so, and the SBOM component "Virtual Vessel Studio built-in BGM and sound effects" shows it in the settings screen's credits (settings screen 8).

```text
StreamingAssets/Audio/BuiltIn/
├─ built-in-audio.json      the list: ID, kind, file, Japanese and English names
├─ LICENSE.txt              CC0 1.0
├─ bgm/<name>.wav
└─ se/<name>.wav
```

```json
{ "schemaVersion": 1, "clips": [ { "id": "bgm-cute-pop", "kind": "bgm", "file": "bgm/cute-pop.wav", "names": { "ja": "サンプル１（ポップ）", "en": "Sample 1 (Pop)" } } ] }
```

- After start-up, `BuiltInAudioClips` registers those not registered yet, with `AddAsync`. Like any registration, they are copied into the Data Root. The user can change their names, volumes, and loops, and delete them. Registering runs in the background so start-up does not wait.
- What was registered is remembered in the Data Root's `Audio/built-in-audio.json` (`{ "schemaVersion": 1, "added": ["bgm-cute-pop", ...] }`), written after each one. What the user deleted is not registered again. Only those added in a later version are registered. One already registered is not replaced when a later version changes its sound or name, nor removed when it is dropped: it is the user's copy, which they may have renamed or adjusted. The sound effects were reviewed with the user before the first release (2026-11-20): the chime is higher, the buzzer brighter, longer and lower, the drumroll ends on a bigger crash, the applause has more people, "Correct" became "Sparkle 2" next to "Sparkle 1", "Pop" became "Bloop", and the fanfare and the rimshot were dropped. The BGM pieces are named by number and style, such as "Sample 1 (Pop)", and a lo-fi piece in 41-tone equal temperament ("Sample 6 (Lo-fi)") was added. The tuning is not shown to users: no "41" or the like in names or file names (the user's instruction, 2026-11-20). So that nothing in Sample 6 rubs under a stream, it is a bright major-key piece at 92 BPM (C–G–Am–F, C–G–F–G7), with added ninths rather than sevenths and a melody in short notes, and no neutral thirds, tape wobble, or noise; its 41-tone color is only the harmonic seventh of G7 (which the melody only passes through) and a slight slide into a phrase's first note (checked with the user, 2026-11-20). "Sample 7 (March)", with many instruments busy at once, was added (made as a toy box of a piece, but named by its style like the others) (12-tone, 132 BPM): xylophone, a tuba-and-pizzicato march, glockenspiel, woodblocks, a brass section, clarinet, and a slide whistle. The trumpet, horn, xylophone, and woodblock sounds were added to `tools/audio/synth.py`. The trumpet and the horn (and the tuba, built the same way) are made to sound blown by a player (the user's request): the note starts a little low and is lipped up to pitch, the tongue and the breath are heard, it is brighter the louder it is, a long note swells, the vibrato comes in late and drifts, and each note is a little off. The second trumpet plays a third below within C major, and horns hold the chords.
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

- Stopping brings what plays down over about 0.3 s, and a track started when nothing plays comes up from its start over about 0.05 s, so nothing clicks. Switching while a track plays is a crossfade (8.5). With a length of 0 s, what plays comes down over about 0.3 s and the new track up over about 0.05 s.
- An empty ring (decoding late) is played as silence and counted (`Audio/Music.Underruns`).

### 8.2 Playing an SE

1. `PlaySoundEffectCommand` calls `ISoundEffectPlayer.Play(id)`.
2. The SE goes into a free native voice from its start and sounds from the next 10 ms block.
3. With no free voice, the oldest one is brought down briefly and replaced.
4. There are 16 voices. Each holds only its position and volume; the samples are shared, one per SE.

- An SE's own volume (`gainDb`) is applied as it enters a voice. The SE source's volume and mute are applied in the mixer.

### 8.3 Playing an SE by Shortcut

During a stream another app, such as a game, is usually in front. So the SE shortcuts work while Virtual Vessel Studio is not in front (user's decision, 2026-11-21), with Windows' RegisterHotKey.

1. `SoundEffectShortcuts` (a service that lives from the app's start to its end) makes the keys-to-SE map from the library's SE list. When two SE have the same keys, the one first in the list is used. It is made again when the library changes.
2. `WindowsGlobalShortcuts` registers the keys on a thread of its own and waits for WM_HOTKEY in that thread's message loop. A hot key goes to the thread that registered it, hence the thread of its own. It does not depend on Unity's frames, so it is not delayed when frames slow down behind a game.
3. When keys are pressed, that thread runs `PlaySoundEffectCommand`. `SoundEffectPlayer.Play` can be called from any thread (it takes a lock inside), so the press is not handed to the main thread.
4. Keys need `Ctrl`, `Alt`, or `Shift`, so the keys a game uses alone are not taken from it. Holding keys down does not repeat (`MOD_NOREPEAT`). The keys offered are letters, digits, the number pad's digits, and F1 to F12. Punctuation keys are left out, as their place moves with the keyboard layout.
5. Windows refuses keys another app holds. Refused keys are in `Refused`, and the setup screen shows them on their row (`setup.md` chapter 14). They are tried again each time the keys change.
6. While the user assigns keys on the setup screen, every key is unregistered (`Paused`), so the keys pressed reach the screen and do not play a sound effect.

- RegisterHotKey only tells that the registered keys were pressed. No other key input is read, and key input is not recorded.
- On exit the thread unregisters every key before it ends; otherwise other apps could not use those keys.

### 8.4 Lowering BGM Under the Voice (Ducking)

BGM is lowered only while the user talks and comes back after (the user's request, 2026-11-23). It is off by default; the setup screen's BGM Card turns it on and off and sets how far and how loud a voice it reacts to. The live screen has its on and off too (`setup.md` chapter 14, `live.md`).

1. The mix thread measures the level (peak) of the microphone source's block, after its gain and mute. It is after the voice conversion stage (audio detailed design 8.3), so the converted voice is measured. A muted microphone has no voice, so nothing is lowered.
2. At the start of the next block, BGM is lowered if that level is at or above "how loud a voice it reacts to". It stays lowered for about 0.4 s after the level falls below (so it does not move between words), then comes back. It goes down over about 30 ms and back over about 0.5 s, smoothly (`Duck`, a pure function in dsp.h).
3. The lowering multiplies the BGM source's gain. It works on both the stream and the monitor, and the level meter shows the lowered level. SE and the game audio are not lowered.

- The sources are mixed in a fixed order, so the microphone's level is that of the block before (10 ms earlier). It is not a delay one can hear.
- Native API: `vv_audio_set_ducking(engine, key_source, ducked_source, enabled, depth_db, threshold_db)`, and `vv_audio_get_ducking(engine, &gain_db)` for how far it is lowered now (API version 13). Settings are passed as atomics; the mix thread takes no lock.
- `IAudioService.Ducking` is true while it is lowered by more than 1 dB. The setup screen shows "Lowering" from it, as a guide for setting how loud a voice it reacts to.

### 8.5 A Crossfade When Switching Tracks

Playing another track while one plays brings the old one down while the new one comes up from its start (the user's request, 2026-11-23). The length is 0 to 5 s, 2 by default. It applies to switching only; stopping still fades over about 0.3 s (the user's decision, 2026-11-23).

1. The native `BgmCrossfader` holds two BGM decks (8.1). The mix thread adds their two blocks and reads them as one BGM source.
2. `Play` while a track plays asks the current deck to come down over the length, and the other deck to bring the new track up over the same length. A deck that has come down drops what it read ahead and is free.
3. When nothing plays, when the length is 0, and when a track not looped ends and the next follows, one deck plays as before.
4. Switching again to a deck still coming down (two switches within the length) shortens its fade to about 0.3 s before the next track starts, so the new track does not wait long.

- The two decks share one count for the plays' generations. The status is that of the deck asked last, and underruns are the two added. `BgmPlayer` does not change.
- The gains move in straight lines. With the built-in BGM, whose loudness is matched (6.3), the overlap does not swell much.
- Native API: `vv_audio_bgm_set_crossfade(engine, milliseconds)` (API version 13). It applies from the next switch.
- The second deck adds a ring (about 1.6 MB) and a decoding thread. While unused, the thread only waits.

## 9. Threads and Asynchrony

- The mix thread (existing): only reads the deck's ring and the SE voices. Ducking (8.4) is worked out here too. No allocation, waiting locks, or file reads (audio detailed design chapter 9).
- The deck's decoding thread (new, native, normal priority): decodes with Media Foundation and writes the ring. The ring has one writer and one reader, with no lock.
- SE decoding: managed, off the main thread; the samples are handed to the native side under the engine's lock (a swap only, as for the preview; audio detailed design 5.5).
- Registration (copying and checking the file): managed, off the main thread.
- The shortcuts' thread (new, managed): registers the keys, and plays the SE when they are pressed (8.3). It does not wait for the main thread.

## 10. Settings

- The sources' volume, mute, and sends (6.2).
- How BGM is mixed (ducking, the crossfade): the Audio Profile's `bgm` (6.2).
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
| Another app holds an SE's shortcut keys | Those keys do not play it; its button does | "Keys used by another app" on its row (a warning shape and text) |
| Another SE has the same keys | Not assigned | Another sound effect uses these keys |

- A BGM or SE failure never stops the microphone, the stream, or a recording (system design 12.11).

## 13. Logging and Diagnostics

- Logs registering, deleting, starting and ending playback, and failures. Tracks are logged by ID, without file paths or names (the names of the user's files can be personal).
- Failures log the format, rate, channel count, and HRESULT (system design 12.14).
- The shortcuts log the keys another app refused, when they change. Presses are not logged.
- Changes to how BGM is mixed are logged with their values. Lowering and coming back are not, as they happen often.
- Metrics: `Audio/Music.Underruns`, `Audio/SoundEffect.VoicesStolen`.
- The audio itself is never logged.

## 14. Testing Strategy

| Kind | Target |
|---|---|
| Native (ctest) | The deck with written WAV and MP3 files: no silence at a loop's seam, the end notice, fades, silence when the ring is empty. SE voices: overlapping, replacing with no free voice. Mixing in stereo. Ducking: `Duck`'s steps (down, holding, back, off); in the mixer, BGM lowered only while there is voice, SE not lowered, a muted microphone not lowering it. Crossfade: the two tracks overlap, the old one goes, the new one comes up; 0 s as before; two switches in a row |
| EditMode | `BuiltInAudioClips`: registering on the first start and the names' language, not adding again, not bringing back deleted ones, adding only new ones, leaving failed ones for the next start, the shipped list being well formed. Through the real plugin, every shipped file registers. `AudioClipLibrary`: add, list, change, delete, reorder (a new one comes last, a deleted one leaves no gap), a built-in file's names per language (kept until it is renamed), half-made folders, keeping unknown fields, an SE too long. `SingleTrack`, `BgmPlayer`'s states with a fake engine. `BgmMixOptions`' ranges and saving, an older file's defaults, passing them to the engine, `Ducking` following it. Saving and removing an SE's shortcut. `KeyChord` read and written, the keys' map (the first for the same keys), playing the pressed SE and following the list, and with Windows itself, keys another holds are refused and taken once let go |
| PlayMode | The settings and live screens' controls (with their screen spec). Assigning, cancelling, and removing shortcuts, refusing keys another SE has, showing keys another app holds, the live screen's tooltips |
| Hardware (manual) | BGM and SE reach the stream and recordings, and the monitor send works |

## 15. Performance

- The mix thread's work per block is a copy from the ring and the sum of the SE voices that sound.
- Decoding MP3 is expected well under 1% of a core. The deck's ring holds about 2 seconds (about 0.8 MB).
- SE memory: about 11.5 MB for 30 seconds of stereo. A limit for many registrations is measured and set during implementation (chapter 17).

## 16. Folder / Namespace

```text
native/audio/src/        bgm_deck (the decoding thread and ring), bgm_crossfade (the two decks), mixer (ducking), sound_effects (the voices), audio_file (adds stereo decoding)
native/audio/include/    vv_audio.h (API version 13)
Audio/Runtime/Clips/     IAudioClipLibrary, AudioClipLibrary, AudioClipRecord, BuiltInAudioClips
StreamingAssets/Audio/BuiltIn/  the built-in BGM and SE (6.3)
tools/audio/             the scripts that render them
Audio/Runtime/Playback/  IBgmPlayer, BgmPlayer, IBgmSequence, SingleTrack, ISoundEffectPlayer
UI/Runtime/Shortcuts/    KeyChord, IGlobalShortcuts, WindowsGlobalShortcuts, SoundEffectShortcuts, ShortcutButton
UI/Runtime/Commands/     PlaySoundEffectCommand
UI/Runtime/Screens/      the BGM and SE Cards (settings screen), the live screen's controls
```

## 17. Stages and Open Issues

Stages:

1. Native: stereo decoding, the BGM deck (loop, end notice, fades), SE voices. Done (2026-11-17).
2. Managed: the sources, the library (CRUD), `BgmPlayer`, `SingleTrack`, `SoundEffectPlayer`. Done. For BGM, the UI calls the players' methods directly.
3. Settings screen: the mixer rows, the BGM and SE Cards (`setup.md` chapter 14). Done.
4. Live screen: choosing, playing, stopping BGM and its volume; SE buttons (`live.md`). Done.
5. The built-in BGM and SE (6.3). Done (2026-11-17).
6. SE shortcuts (8.3). Done (2026-11-21).
7. Ducking (8.4) and the crossfade when switching tracks (8.5). The live screen can switch tracks while one plays.

An integration test through the real plugin (`AudioPlayersIntegrationTests`) registers a WAV, plays it as BGM to its end, is refused deleting it while it plays, and loads and plays it as an effect.

To be added later, and where (without rework):

- Playlists, shuffle, repeat: `IBgmSequence` implementations and saved playlists (`Audio/Playlists/`).
- Crossfades and gapless playback between a playlist's tracks: with the two decks (8.5), the next track starts before the current one ends.
- M4A and others: widen `FileExtensions`. Decoding is Media Foundation's, so the change is expected to need only checking.
- BGM made in the app (system design 16.32): registers its audio with `AddAsync`.

Open issues:

- Limits on the number of SE and their memory.
