# Setup Screen

## 1. Purpose

The screen for preparing devices and settings before streaming. It also shows the credits of the software in use.

It has the capture Cards, the audio Card (chapter 11), the voice conversion Cards (chapter 13, being designed), the tracking Card, the avatar Card, the face tuning Card (chapter 9), the recording Card (chapter 10), the app settings Card (UI foundation detailed design 7.7), and the two credits Cards (chapter 8). Other settings are added with their features.

- The screen is named "Settings" (Japanese 設定) in the navigation and its heading, since it holds the app settings and the credits as well as device setup.
- Its identifier (`setup`), its string keys (`nav.setup`, `setup.*`), and its name in the documents (the setup screen) stay. The identifier is saved as `lastScreen` in `ui-settings.json`, so changing it would lose the screen to return to.

## 2. Layout

The Cards are divided into pages by subject, switched by Tabs at the top of the screen (`components.md` 3.8). With more Cards, a subject had become hard to find, so they are no longer one column (user decision, 2026-10-31).

| Page | Key | Cards |
|---|---|---|
| Video | `setup.page.video` | Capture (game screen, sub monitor) |
| Audio | `setup.page.audio` | Audio (chapter 11; without audio, the line "Audio is not available."), BGM, sound effects (chapter 14), voice conversion (chapter 13) |
| Avatar | `setup.page.avatar` | Avatar (chapter 5), tracking (chapter 4), face tuning (chapter 9; only when avatars are available), lighting (chapter 15; only with Stage) |
| Streaming and recording | `setup.page.output` | Recording (chapter 10; without recording, the line "Recording is not available."), streaming (chapter 12) |
| App | `setup.page.app` | App settings, credits and licenses (chapter 8) |

- The page last opened is saved as `lastSetupPage` in `ui-settings.json` and opens next time (UI foundation detailed design 8.1). A value not known opens the first page.
- Each page starts with links to its Cards (Page Index, `components.md` 3.29) (user's request, 2026-11-22).
- All pages are built, and only the open one shows. Switching pages loses no Card's state.
- The tracking camera has no capture Card; it is chosen in the tracking Card.
- Cards mostly do not collapse. With pages, closing them to keep the screen short is no longer needed. A Card of settings used seldom (such as detailed ones) may, on any page, be a collapsible Card (`components.md` 3.10) that starts closed (user decision, 2026-11-03). As an exception, the BGM and the sound effects Cards (section 14) are collapsible Cards that start open, as they are used often but their lists grow long (user decision, 2026-11-20).
- The state (Status Indicator) sits at the right end of the Card's title band. The explanation of a problem comes first in the body.
- Every setting is explained in a Help Hint after its name (`components.md` 3.28), not in lines under the row; with no first-run guide, these hints are where the settings are explained (decision record `setup-guidance.md`). Only a note for a situation goes under a row, such as "rtmp:// is not encrypted" or "this PC's GPU has no AV1". A table's columns have the hints after their headings. The texts are in the string tables (keys ending in `.help`); the Japanese is proofread by the user (2026-11-22).

## 3. Capture Card

One per capture source, showing `ICaptureDeviceSource` (Capture) directly.

| Row | Component | Content |
|---|---|---|
| State | Status Indicator (`components.md` 3.13) | The state, and an explanation when there is a problem (chapter 6) |
| Video | Image (256×144, fitted to 16:9) | The captured video, small, to check that the chosen device is the right one; "No video" when there is none |
| Capture | Toggle | On starts, off stops. Whether it was started is saved and restored at the next start |
| Input (game screen only) | Dropdown (Capture board, A window on this PC) | What the game screen is taken from (Capture detailed design 8.6). With "A window on this PC", Device lists the windows open on this PC, one per program (its largest). A chosen device shows its kind. Changing the kind lets go of a device of the other kind. |
| Device | Dropdown | The devices found. The game screen starts with "Choose a camera" ("Choose the game's window" for windows), the sub monitor with "Automatic (not the main monitor)". A window shows as "Title (program)". With none found, "No device found" ("No window found" for windows) |
| Audio (game screen only) | Dropdown | The audio device the game's sound comes from (`GameAudioRow`). The same setting as the game audio's device on the Audio Card (section 11); either place changes it. Choosing a video device chooses the audio device whose name matches. Choosing a window chooses only that game's sound ("Sound of game.exe", Audio detailed design 8.7) whatever audio device was chosen before, and lists it among the choices while a window is chosen, and does not send it to the monitor: the PC plays the game's sound already, so it would be heard twice. Back on a capture board it is sent to the monitor again. Left out when audio is not available |
| Device list | Button (Secondary) "Search again" | Lists the devices again |
| Flip left and right | Toggle | Corrects the video's orientation |
| Flip top and bottom | Toggle | Corrects the video's orientation |
| Show the mouse cursor (sub monitor only) | Toggle | On by default |
| Actual video | Description (monospace, Text 2) | Resolution and frame rate (for example `1920×1080 · 59.9 fps`); "—" while not capturing |

- Changes apply at once and are saved (Capture detailed design chapter 10).
- The Capture toggle follows the state. It stays on while waiting for a missing device (`Unavailable`).

## 4. Tracking Card

Shows `ITrackingSource` (Tracking) and the tracking camera (Capture's `camera`) directly (Tracking detailed design 5.4, chapter 11).

| Row | Component | Content |
|---|---|---|
| State | Status Indicator | The state and, if there is a problem, its explanation (4.1) |
| Video | An image (256×144, fit to 16:9) with points and bones over it | The camera image with the tracked body points and bones and hand points and fingers drawn over it. Points in `Primary` and bones in `Accent Line`, each with an `On Accent` outline so they show on bright and dark images alike. Redrawn about every 33 ms. Never on the stream |
| Detected | Description (Text 2) | A mark and a word per part: "Face ✓   Body ✓   Left hand –   Right hand ✓". ✓ tracked, – lost, × disabled; never color alone. Hidden while stopped |
| Load | Description (monospace, Text 2) | Every second: "App 60 fps · Tracking 30/s · CPU 12% · GPU 8%". CPU is this application's share of all cores; GPU is the share of a frame the GPU spent on this application's work (Unity's FrameTimingManager; "—" where unknown). System-wide GPU usage cannot be read from Unity and is not shown |
| Track | Toggle | On starts tracking (opening the camera), off stops it. Whether it was on is saved and restored at the next start |
| Mirror | Toggle | On by default. Flips the camera image and points left to right, like a mirror. The avatar follows the same setting (Tracking detailed design chapter 10) |
| What moves | Segmented (Upper body, Full body (soon)) | The upper body. Full body cannot be chosen yet; it is shown in the disabled color so users know it is coming (Tracking detailed design 6.5, `components.md` 3.18). Japanese: 上半身, 全身（準備中） |
| Parts tracked | Toggles "Face", "Body", "Hands", one under another | All on by default. A part turned off is not inferred, which lowers the load, and does not move (Tracking detailed design 6.5). "Hands" covers both hands and the fingers. One under another so they fit a row's control (at most 260px) in every language. A change applies at once and is saved |
| Body movement | Segmented (3D, 2D) | 3D by default. 2D moves the avatar only by the movement seen from the front, without the body's depth (Tracking detailed design 8.6). A change applies at once and is saved |
| Movement speed | Slider (1 to 10, steps of 1) | The smoothing level (Tracking detailed design 8.2). 5 by default (on 2026-10-24 the levels moved two steps and then four more, making a level steady within about 300 ms lag the new 5). A change applies at once and is saved |
| Body detection | Segmented (Light, Standard) | The Pose model (Tracking detailed design chapter 10), Standard by default. A change while tracking restarts it with the new model, and is saved. Heavy ("Precise") is not offered: on the user's camera it tracked worse than standard (2026-10-26) |
| Camera | Dropdown | The devices found, with "Choose a camera" first. The camera is not opened until one is chosen |
| Device list | Button (Secondary) "Search again" | Lists the devices again |

### 4.1 Tracking State Display

| State | Chip | Text |
|---|---|---|
| Stopped | Idle | Stopped |
| Preparing | Idle | Preparing |
| Tracking | Success | Tracking |
| No one in view | Warning | No one in view. Explanation: "Sit in front of the camera so your face and upper body are in view." |
| A camera must be chosen | Warning | Waiting for the camera. Explanation: "Choose the webcam to track with in Camera." |
| Camera not found | Warning | Waiting for the camera. Explanation: "The camera is not found. Connect it and it is found again automatically." |
| The game screen's camera | Error | Cannot track. Explanation: "The game screen capture uses this camera. Choose another camera." |
| Failed | Error | Cannot track. Explanation: "The camera could not open or tracking could not start. Check that no other app is using the camera." |

- Developer problems such as a missing MediaPipe plugin are shown to users as a failure.
- The toggle stays on while waiting for the camera or MediaPipe.

## 5. Avatar Card

Shows `IAvatarService` (Avatar) directly (Avatar detailed design 5.1, 6.2, 6.4).

| Row | Part | Content |
|---|---|---|
| State | Status Indicator | The state, and an explanation when there is a problem (5.1) |
| Image | Image (96×96, fitted in a square) | The image in the model of the avatar in use; "No image" when there is none |
| Avatar to use | Dropdown | "None" first, then the registered avatars' names. One that cannot be loaded is "Avatar that cannot be loaded". Choosing loads and swaps at once |
| Format | Description (Text 2) | "VRM 1.0". VRM 0.x is "VRM 0.x (converted to 1.0 when loaded)". "—" when none |
| Register | Button (Primary) "Register an avatar…", Button (Secondary) "Use the sample avatar" | "Register an avatar…" opens the file-picking screen (VRM files only) and registers after the confirmation dialog (5.2). "Use the sample avatar" registers the sample shipped with the application (`Samples\otoha-chan.vrm`; `tests/samples/` in the Editor) without picking a file, after the same confirmation. Not shown when there is no sample (user's decision, 2026-11-22; decision record `setup-guidance.md`). The two buttons are one above the other: side by side they overflowed the 260px control (user's report, 2026-11-22) |
| Unregister | Button (Danger) "Unregister" | Unregisters the avatar in use, after a Danger confirmation dialog ("The copy of the model in this app is deleted. Your original file is kept.") |
| Terms of use (set by the model's author) | Rows of name and value (values in Text 2) | Model name, authors, who may use it as an avatar, commercial use, excessive violence, excessive sexual content, political or religious use, antisocial or hateful content, credit, redistribution, modification, (VRM 0.x only) the license name, the license URL, and other terms' URLs. Conditions the file does not state say "Not stated". Only `https` URLs get an "Open" button beside them, which opens the browser only when pressed |

- During an operation (picking a file, registering, unregistering, loading) the buttons are disabled; one operation runs at a time.
- A failed registration (not VRM, too large, missing bones, cannot load, already registered, cannot save) shows the explanation of 5.1 in a failure notice (`components.md` Notice).
- Text written by the author is shown as plain text (never read as rich text).

### 5.1 Avatar State Display

| State | Chip | Text |
|---|---|---|
| Not chosen | Idle | Not chosen |
| Loading | Idle | Loading |
| Shown | Success | Shown. When a swap failed, its explanation is added |
| Cannot load | Error | Cannot load. The explanation depends on the problem: "This model cannot be used because its humanoid bones are incomplete." "The avatar could not be loaded. Check that the file is not damaged." "Could not save. Check the free space at the save location." |

Explanations of registration problems:

| Problem | Explanation |
|---|---|
| Not VRM | This is not a VRM file. Choose a file ending in .vrm. |
| Too large | The file is too large. Choose a file of 1 GB or less. |
| Already registered | This model is already registered. |

### 5.2 Registration Confirmation Dialog

- Title "Register this avatar?", action button "Register" (Primary).
- The body is "Check the terms of use set by the author before registering." When who may use it as an avatar is not "everyone", it is "This model is not set to be usable as an avatar by everyone. Check the terms of use to see whether you may use it."
- Under the body, the terms rows of chapter 5 are shown (read only: no "Open" buttons, and Tab moves only between the buttons). When long, only that part scrolls (up to 280px high).

## 6. Capture State Display

Shape, color, and text are always combined (`design-system.md` 3.4).

| State | Chip | Text |
|---|---|---|
| Stopped | Idle | Stopped |
| A device must be chosen | Warning | Stopped. Explanation: "Choose the capture board under Device. A camera is never opened by itself, so a webcam does not show you by mistake." |
| A window must be chosen | Warning | Stopped. Explanation: "Choose the game's window under "Device"." |
| Starting | Idle | Starting |
| Waiting for video | Idle | Waiting for video |
| No signal | Warning | Waiting for video. Explanation: "No video is arriving. Check the game device's power and cable connection." |
| Capturing | Success | Capturing |
| Device not found | Warning | Device not found; still looking. Explanation: "Connect the device and it is found again automatically." |
| Window not found | Warning | Device not found; still looking. Explanation: "It looks again when the game starts. A minimized window cannot be found." Closing the game also shows this, and starting the game again starts capturing by itself |
| The tracking camera | Error | Could not capture. Explanation: "Tracking uses this camera. Choose another device." |
| Failed | Error | Could not capture. Explanation: "Another app may be using the device." |

- A missing plugin (`PluginMissing`) is shown to users as a failure (Capture detailed design 6.2).

## 7. Strings

Keys are `setup.capture.*` for the capture Cards, `setup.tracking.*` for the tracking Card, `setup.avatar.*` for the avatar Card, `setup.face.*` for the face tuning Card, and `setup.credits.*` for the credits Card. The texts are those in the tables of chapters 3–6, 8, and 9 (Japanese with the same meaning).

## 8. Credits and Licenses Card

Lists the software and assets in use from the SBOM shipped with the application (`sbom/virtual-vessel-studio.cdx.json`). The full license texts are read from the files shipped with the application (development workflow 18.1.2).

There are two Cards, placed at the end of the app settings page in this order. Voice Lab's training environment alone has dozens of Python packages, and in one Card they would bury the application's own components.

Both are collapsible Cards (`components.md` 3.10) that start closed. Few people read them, and open they would fill most of the page with long lists (user decision, 2026-11-13). When the SBOM cannot be read, the Card does not collapse, so its problem shows.

| Card | Title | Components listed |
|---|---|---|
| The app | "Credits and licenses: the app" | The application itself first, then the distributed components (`required`) in the SBOM's order |
| Voice Lab's Python environment | "Credits and licenses: Voice Lab's Python environment" | The components not distributed but installed on the user's PC when Voice Lab's training environment is set up (`optional`), in the SBOM's order. The SBOM puts the main ones first, such as Python, RVC, its pretrained models, VoxCPM, its model, PyTorch, and uv, followed by the other packages by name (development workflow 18.1.2). Not shown when the SBOM has no `optional` components |

| Row | Component | Content |
|---|---|---|
| Introduction | Description (Text 2) | The app: "Virtual Vessel Studio uses the following software and assets. Where the full license text is included, open it with "Show full text"." Voice Lab's Python environment: "When the training environment for making voices in Voice Lab is set up, the following software and models are installed on this PC. They are not part of the app, so each one's full license text is published by its provider." |
| A row per component | Looks like a Setting Row | The name on the left, and below it "version · supplier · license" (only what the SBOM gives; Caption, Text 3). The ones built into another are indented by spacing L (24px) under it. Development-only components (`excluded`) are in neither Card |
| Full text | Button (Secondary) "Show full text" / "Hide full text" | Only on components whose SBOM entry names license files. Opens the full text below the row. With several files, each starts with its file name (monospace) |
| Full text box | A scrolling box (up to 320px high, Surface 3 background, Radius S) | The full text (Caption, Text 2). It scrolls on its own so that a long notices file does not stretch the screen |

- Names, versions, license names, and full texts are shown as written in the files, as text (never parsed as Rich Text), and the same in every language.
- A Markdown file (`.md`) is shown without its marks, as headings, paragraphs, items, and table rows, with links as their text and address (the user's review, 2026-11-15; `MarkdownText`). Other files are shown line by line.
- The SBOM is read when the screen is built; a full text when it is first opened.
- When the SBOM cannot be read, only the app Card is shown, and it says "The credits could not be read. Try restarting the app." instead of the rows. A missing file shows "The full license text is missing." in its box. The developer log records the file's path and the reason.
- Where the files are read: in a built application, the executable's folder (`licenses/` and so on); in the Editor, the repository files that `tools/release/license-files.json` names as their sources.
- Paths written in the SBOM are read only inside that folder (paths with `..`, absolute paths, and `\` are not read). The SBOM ships with the application, so no trust boundary is crossed, but even an edited SBOM cannot make the screen read other files.

## 9. Face and Finger Tuning Card

Tunes, part by part and while watching the avatar, how strongly the user's face shows on the avatar's (Avatar detailed design 7.4). The tuning is the user's and stays when the avatar changes.

| Row | Component | Content |
|---|---|---|
| Introduction | Description (Text 2) | "With tracking running, watch the avatar's movement while you adjust. Higher values show an expression with less movement. Under each slider is how far the avatar moves now." |
| Video | Image (384×216, 16:9) | The middle of the stream, enlarged so the expression can be seen. "No video" when there is none |
| Picture height | Slider (0 to 100%, steps of 5%), under the video | Which height of the avatar frame (Stage detailed design 7.2) is shown (0% the frame's bottom, 100% its top); of the whole stream without an avatar. 85% at first (where the camera's default position puts the face of an avatar of average height). The face's height depends on the avatar's, so the user sets it. Not saved |
| Zoom | Slider (100 to 400%, steps of 10%), under the picture height | How much the picture is enlarged. 100% shows the avatar frame's whole height (the whole stream without an avatar); 220% at first. Across, it shows the frame's middle. Because both follow the frame, the face stays in view however small or far to the side the avatar is placed (user's report, 2026-11-19). The picture keeps the stream's shape and never shows past the stream's edges. The face's size depends on the avatar, so the user sets it (user's request, 2026-10-21). Not saved |
| Winks | Toggle | Whether one eye may close alone. Off by default. While off, "Winking" is disabled |
| Per part | Slider (50 to 300%, steps of 10%) with a bar under it | Eye closing, winking, brows (surprise, anger, sadness), mouth, gaze, and smile. 100% is the default. The bar is how far the avatar moves that part now (0 to 100%), redrawn about every 50 ms |
| Finger bend, thumb bend | Slider (50–200%, in 10% steps) | How strongly the fingers bend (avatar detailed design 7.5). 100% is the default. Both sliders have the same Help Hint. The hands are seen with the picture's height and zoom |
| Reset everything | Button (Secondary) "Reset" | Puts every part and the fingers back to 100% and turns winks off |

- The video is on the left and the sliders on the right, so the avatar stays in view whichever slider moves. On a narrow window the sliders wrap below the video.
- The bars are progress bars (`components.md` 3.15) without a title; the introduction says what they show.
- A change applies to the avatar on the stream at once and is saved (`avatar-settings.json`).
- When avatars are unavailable (no Avatar), this Card is not placed. While no avatar is chosen, the bars stay still.
- The crop is made by placing the whole stream enlarged inside a frame, shifting it, and hiding what is outside the frame. Changing the shown part of Unity's Image (its UV rectangle) was not redrawn (2026-10-21).

## 10. Recording Card

Shows `IRecordingService` (Video) directly (`RecordingCard`); details in recording detailed design 18.2.

| Row | Content |
|---|---|
| Folder | On the right, "Change…" (Windows' folder screen) and "Open" (Explorer). The folder's path is under the description, where it wraps (`components.md` 3.9) |
| Quality | Segmented: Standard, High. The bitrates (6 Mbps, 12 Mbps) are in the Help Hint |
| Format | Segmented: AV1, H.264. The Help Hint says AV1 is recommended and when to choose H.264. Without an AV1 encoder on the GPU, AV1 cannot be chosen and the line under the row says why (recording detailed design 18.2) |
| Free space | Free space on the folder's drive, read again every 10 seconds while the Card is shown |
| Video | Size, frame rate, and container (1920×1080, 30 fps, MP4); fixed for now |

- Changes are used from the next recording; a recording in progress keeps the settings it started with.
- Recording is started and stopped on the live screen (`live.md`).

| Key | 日本語 | English |
|---|---|---|
| `setup.recording.title` | 録画 | Recording |
| `setup.recording.unavailable` | 録画は使えません。 | Recording is not available. |
| `setup.recording.folder` | 保存先 | Folder |
| `setup.recording.folder.help` | 録画したファイルを保存するフォルダーです。変更は次の録画から反映されます。 | Where recordings are saved. A change applies from the next recording. |
| `setup.recording.folder.pick` | 変更… | Change… |
| `setup.recording.folder.pick-title` | 録画の保存先を選んでください | Choose where to save recordings |
| `setup.recording.folder.open` | 開く | Open |
| `setup.recording.quality` | 画質 | Quality |
| `setup.recording.quality.help` | 標準は6 Mbps、高画質は12 Mbpsです。高画質ではきれいに映りますが、ファイルの大きさが約2倍になります。1時間の録画で、標準は約2.7 GB、高画質は約5.4 GBです。 | Standard is 6 Mbps and High 12 Mbps. High looks better, and its files are about twice as large. An hour of recording is about 2.7 GB in Standard and about 5.4 GB in High. |
| `setup.recording.quality.standard` | 標準 | Standard |
| `setup.recording.quality.high` | 高画質 | High |
| `setup.recording.codec` | 形式 | Format |
| `setup.recording.codec.help` | AV1を推奨します。同じ画質で、ファイルが小さくなります。古い編集ソフトを使用するなどで録画ファイルを開けない場合は、H.264を選択してください。 | AV1 is recommended: smaller files at the same quality. If the files cannot be opened, for example in an older editor, choose H.264. |
| `setup.recording.codec.no-av1` | このPCのGPUはAV1に対応していないため、H.264で録画します。 | This PC's GPU does not support AV1, so recordings use H.264. |
| `setup.recording.codec.av1` | AV1 | AV1 |
| `setup.recording.codec.h264` | H.264 | H.264 |
| `setup.recording.free` | 空き容量 | Free space |
| `setup.recording.free.help` | 保存先のドライブの空き容量です。10秒ごとに取得します。 | The free space on the folder's drive, read every 10 seconds. |
| `setup.recording.free.value` | {size} GB | {size} GB |
| `setup.recording.free.unknown` | わかりません | Unknown |
| `setup.recording.format` | 映像 | Video |
| `setup.recording.format.help` | 録画する映像の解像度とフレームレートです。 | The resolution and frame rate of the recorded video. |
| `setup.recording.format.value` | {width}×{height}・{rate} fps・MP4 | {width}×{height}, {rate} fps, MP4 |

## 11. Audio Card

Shows `IAudioService` (Audio) as a mixer (`AudioCard`, `components.md` 3.25): a row each for the sources (the microphone, the game audio, BGM, sound effects) and the monitor, in shared columns. BGM and sound effects have no device, so their Device column is blank (chapter 14). Another source adds only a row. Details in audio detailed design 18.

| Row | Content |
|---|---|
| Source | The name and a Status Chip of its state (Off, Starting, Running, Muted, Not found, Unavailable; a shape and text) |
| Device | Select, "None" first; an unplugged device stays as "A device not connected now". The sound of a game on this PC shows as "Sound of game.exe". The game audio's is chosen on the capture Card (chapter 3), so here it is disabled and only shows the device's name. The monitor chooses its output |
| Volume | Slider, -60 to +12 dB |
| Hear | Toggle; not on the monitor's row |
| Noise removal | Toggle, on the microphone's row only. On by default, and it works without voice conversion too (the user's decision, 2026-11-11; audio detailed design 8.6). Saved in the project's Audio Profile |
| Mute | Toggle |
| Level | Level Meter, -60 to 0 dB |

Under a row, only when there is a problem, what happened and what to do; when microphone access is denied, "Open microphone privacy settings". Each column is explained in a Help Hint after its heading (components.md 3.28), not in lines under the table (the user's request, 2026-11-11). The monitor's row explains its output in a Help Hint after the row's name. That the game audio's device is chosen on the Video page is the tooltip of its disabled device choice. Under the table, the stream and recording audio delay (Slider, 0 to 1000 ms, 300 ms by default; audio detailed design 8.5).

- When the Card is narrower than 870 px, the column headings hide, rows wrap, and each control carries its name.
- Devices are listed again every 5 seconds while the Card is shown.
- The microphone starts on Windows' default input device on first use; the game audio starts on "None" (choose the capture board's audio; choosing its video device does this, section 3).

| Key | 日本語 | English |
|---|---|---|
| `setup.audio.title` | 音声 | Audio |
| `setup.audio.unavailable` | 音声は使えません。 | Audio is not available. |
| `setup.audio.microphone` | マイク | Microphone |
| `setup.audio.game` | ゲームの音声 | Game audio |
| `setup.audio.music` | BGM | BGM |
| `setup.audio.sound-effect` | 効果音 | Sound effects |
| `setup.audio.source` | 音の元 | Source |
| `setup.audio.device` | デバイス | Device |
| `setup.audio.device.help` | 音声入力に使う機器です。なお、ゲームの音声の機器は「映像」ページで選択してください。 | The device the sound comes in from. The game audio's device is chosen on the Video page. |
| `setup.capture.audio` | 音声 | Audio |
| `setup.capture.audio.help` | ゲームの音を取り込む機器です。映像の機器を選択すると、同じ機器の音声が自動で選択されます。「音声」ページの「ゲームの音声」と同じ設定です。 | The device the game's sound comes from. Choosing a video device chooses its audio too. It is the same setting as Game audio on the Audio page. |
| `setup.audio.device.none` | なし | None |
| `setup.audio.device.missing` | 接続されていないデバイス | A device not connected now |
| `setup.audio.device.application` | {name} の音 | Sound of {name} |
| `setup.audio.gain` | 音量 | Volume |
| `setup.audio.gain.help` | 配信と録画に入れる音量です。0 dBが元の大きさです。 | The volume in the stream and recordings. 0 dB is the original level. |
| `setup.audio.mute` | ミュート | Mute |
| `setup.audio.mute.help` | オンの間、その音を配信と録画に入れないようにします。 | While on, the sound is kept out of the stream and recordings. |
| `setup.audio.level` | レベル | Level |
| `setup.audio.level.help` | 現在の音の大きさです。メーターが右端に届く場合は、音が割れる可能性があるため、音量を下げてください。 | How loud the sound is now. If the meter reaches the right end, the sound may distort, so lower the volume. |
| `setup.audio.state.off` | オフ | Off |
| `setup.audio.state.starting` | 開始中 | Starting |
| `setup.audio.state.running` | 動作中 | Running |
| `setup.audio.state.not-found` | 未発見 | Not found |
| `setup.audio.state.failed` | 使えません | Unavailable |
| `setup.audio.state.muted` | ミュート中 | Muted |
| `setup.audio.problem.not-found` | デバイスが接続されていません。接続すると自動で使い始めます。 | The device is not connected. It is used again as soon as it is connected. |
| `setup.audio.problem.in-use` | ほかのアプリがこのデバイスを専有しています。そのアプリを閉じるか、そのアプリの設定で専有を解除してください。 | Another application is using this device exclusively. Close it, or turn off exclusive use in its settings. |
| `setup.audio.problem.access-denied` | Windowsの設定で、マイクへのアクセスが許可されていません。「マイクのプライバシー設定を開く」から、デスクトップアプリのアクセスを許可してください。 | Windows does not allow access to the microphone. Open the microphone privacy settings and allow desktop apps to access it. |
| `setup.audio.problem.failed` | デバイスを開けませんでした。もう一度選び直してください。続く場合は診断画面のログを確認してください。 | The device could not be opened. Choose it again; if it keeps happening, check the log on the Diagnostics screen. |
| `setup.audio.open-privacy` | マイクのプライバシー設定を開く | Open microphone privacy settings |
| `setup.audio.monitored` | 聞く | Hear |
| `setup.audio.monitored.help` | 「モニター」の出力先から聞くことができます。配信と録画には影響しません。スピーカーではマイクに音が入るため、ヘッドホンの使用を推奨します。 | Lets you hear the source on the Monitor output. It does not change the stream or recordings. Headphones are recommended: speakers leak into the microphone. |
| `setup.audio.denoise` | ノイズ除去 | Noise removal |
| `setup.audio.denoise.help` | 話していない間の、キーボードやマウスなどの音を除去します。声の変換を使わない場合にも有効です。 | Removes keyboard, mouse, and other sounds while you are not speaking. It works without voice conversion too. |
| `setup.audio.game-device` | ゲームの音声の機器は、「映像」のページで選びます。 | The game audio's device is chosen on the Video page. |
| `setup.audio.monitor` | モニター | Monitor |
| `setup.audio.monitor.device` | 出力先 | Output |
| `setup.audio.monitor.help` | 自分で聞く音を出す機器です。ヘッドホンを選択することを推奨します。配信と録画には影響しません。 | The device that plays what you hear yourself. Headphones are recommended. It does not change the stream or recordings. |
| `setup.audio.delay` | 配信・録画の音声の遅延 | Stream and recording audio delay |
| `setup.audio.delay.help` | アバターの動きは、カメラの映像から読み取る分だけ、声より遅れます。配信と録画の音声をこの時間だけ遅らせて、動きに合わせます。自分に聞こえる音は遅れません。 | The avatar moves later than your voice, by the time it takes to read the motion from the camera. Streaming and recording get the sound this much later to match the motion. What you hear is not delayed. |

## 12. The Streaming Card

`IStreamingService` (Streaming) is shown directly (`StreamingCard`). The rows and their behavior are in the streaming detailed design 18.2. The stream key's value is never shown, and the dialog that registers it uses a masked field. While streaming no row can change, and the Card says why.

| Key | 日本語 | English |
|---|---|---|
| `setup.streaming.title` | 配信 | Streaming |
| `setup.streaming.unavailable` | 配信は使えません。 | Streaming is not available. |
| `setup.streaming.locked` | 配信中は変更できません。 | These cannot change while streaming. |
| `setup.streaming.destination` | 配信先 | Destination |
| `setup.streaming.destination.help` | YouTubeか、ほかのRTMP(S)のサーバーを選択します。 | Choose YouTube or another RTMP(S) server. |
| `setup.streaming.destination.youtube` | YouTube | YouTube |
| `setup.streaming.destination.custom` | 他のサーバー | Other server |
| `setup.streaming.url` | サーバーのURL | Server URL |
| `setup.streaming.url.help` | YouTubeは既定のままで使用できます。ほかのサーバーでは、rtmp:// か rtmps:// で始まるURLを入力してください。 | YouTube works with the default. For another server, enter a URL that starts with rtmp:// or rtmps://. |
| `setup.streaming.url.placeholder` | rtmps://example.com/live | rtmps://example.com/live |
| `setup.streaming.url.invalid` | rtmp:// または rtmps:// で始まるURLを入力してください。 | Enter a URL that starts with rtmp:// or rtmps://. |
| `setup.streaming.url.unencrypted` | rtmp:// は暗号化されません。ストリームキーも暗号化されずに送られます。rtmps:// を使用することを推奨します。 | rtmp:// is not encrypted, and neither is the stream key sent over it. rtmps:// is recommended. |
| `setup.streaming.key` | ストリームキー | Stream key |
| `setup.streaming.key.help` | YouTube Studioの「ライブ配信」に表示されるキーです。キーを知っている人は、このチャンネルで配信できるため、ほかの人に見せないでください。アプリはWindowsの資格情報マネージャーに保存し、登録したあとは表示しません。 | The key shown under Go live in YouTube Studio. Anyone who knows it can stream on your channel, so do not show it to others. The app saves it in Windows Credential Manager and does not show it after it is registered. |
| `setup.streaming.key.registered` | 登録済み | Registered |
| `setup.streaming.key.not-registered` | 未登録 | Not registered |
| `setup.streaming.key.register` | 登録… | Register… |
| `setup.streaming.key.remove` | 削除 | Remove |
| `setup.streaming.key.dialog.title` | ストリームキーの登録 | Register the stream key |
| `setup.streaming.key.dialog.body` | YouTube Studioのストリームキーを入力してください。 | Enter the stream key from YouTube Studio. |
| `setup.streaming.key.dialog.field` | ストリームキー | Stream key |
| `setup.streaming.key.dialog.confirm` | 登録 | Register |
| `setup.streaming.key.failed` | 登録できませんでした。もう一度試してください。 | It could not be registered. Please try again. |
| `setup.streaming.key.remove.title` | ストリームキーを削除しますか？ | Remove the stream key? |
| `setup.streaming.key.remove.body` | 配信するには、もう一度登録が必要です。 | You will need to register it again to stream. |
| `setup.streaming.key.remove.confirm` | 削除 | Remove |
| `setup.streaming.codec` | 形式 | Format |
| `setup.streaming.codec.help` | AV1を推奨します。同じビットレートできれいに映ります。配信が正常に行えない場合は、H.264を選択してください。 | AV1 is recommended: it looks better at the same bitrate. If the stream does not work properly, choose H.264. |
| `setup.streaming.codec.no-av1` | このPCのGPU、または配信先がAV1に対応していないため、H.264で配信を行います。 | This PC's GPU or the destination does not support AV1, so the stream uses H.264. |
| `setup.streaming.codec.av1` | AV1 | AV1 |
| `setup.streaming.codec.h264` | H.264 | H.264 |
| `setup.streaming.bitrate` | 映像のビットレート | Video bitrate |
| `setup.streaming.bitrate.help` | 1080p 30 fpsの場合は、6 Mbpsを推奨します。回線の上りの速度より低くしてください。高くするほどきれいに映りますが、回線に余裕がないと配信が途切れます。 | 6 Mbps is recommended for 1080p at 30 fps. Keep it below your connection's upload speed. Higher looks better, but without room on the connection the stream breaks up. |
| `setup.streaming.test` | 接続テスト | Connection test |
| `setup.streaming.test.help` | サーバーに接続できるかを確認します。配信は開始しません。 | Checks that the server can be reached. The stream does not start. |
| `setup.streaming.test.run` | テスト | Test |
| `setup.streaming.test.ok` | 接続できました。 | Connected. |

## 13. The Voice Conversion Card

Shows `IVoiceConversion` (Voice) (`VoiceCard`). It goes on the Audio page, under the Audio Card: it is processing on the microphone, so it sits on the microphone's page. Mock-up: https://claude.ai/artifact/4XQ54BFpSmHzcqtrfq6G4R (reviewed by the user, 2026-11-10)

It should be usable with simple settings alone (the user's request, 2026-11-03). With the window fixed at 1 s, noise removal was the only detailed setting left, and it moved to the Audio Card (chapter 11), so it is one Card (13.2).

### 13.1 The Voice Conversion Card

A status chip at the right end of the title (Running ✓, Loading ○, Off ○, Unavailable ×, Stopped ×).

| Row | Control | What it does |
|---|---|---|
| Convert my voice | Toggle | Off, your own voice goes to the stream and recordings as it is. On, the microphone is kept out until the voice is ready and whenever it cannot convert (Voice detailed design 12); it shows on meanwhile and can be pressed even without a voice or a GPU, so turning it off always brings your own voice back. Saved and restored at start: for someone streaming with a changed voice, starting off and sending their own voice is the worse failure (user's decision, 2026-11-10) |
| Voice | Select | The usable voices made in Voice Lab. The prototype's voice is for developers and not listed (only on the Diagnostics screen). Choosing saves it in the Voice Profile (Voice detailed design 6.2) |
| Match the pitch automatically | Toggle, Measure again | Under the name, "Your voice 200 Hz → this voice 285 Hz (+6)"; "not measured" before measuring |
| Pitch adjustment | Slider | -24 to +24 semitones, added to the automatic shift when that is on; the Help Hint's text changes with automatic on or off. Under the name, "Converted pitch 317 Hz (total +8)" (user's check, 2026-11-10) |
| Finish | Four sliders, Reset the finish | The chosen voice's body, clarity, brightness, and air (-6 to +6 dB). A change is kept with the voice and heard on the stream at once. Disabled without a voice (Voice detailed design 8.6; the user's decision, 2026-11-15) |

The Help Hint of "Convert my voice" also says that voice conversion uses the GPU (the user's request, 2026-11-11: keep the screen's text short).

- Without a usable GPU: the Card starts with "Voice conversion cannot be used on this PC" and why, and every row is disabled (13.3).
- Without a voice: the Card starts with "No voice" and "Open Voice Lab", and the other rows are disabled.
- Stopped, or the voice cannot load: the Card starts with what stopped, that the microphone is kept out of the stream and recordings, that turning it off and on tries again, and that left off your own voice goes out (2026-11-15). It comes before "No voice", as it says what the stream gets. With `onFailure` set to `PassThrough`, it says your own voice goes out.
- What you hear (13.4), If conversion stops, and Read the notes on use come later: they need a change to the audio stage, the failure behavior, and the terms (Voice Lab detailed design 8.2).

### 13.2 Fixed Values

The window (how much HuBERT and the generator see at once) is fixed at 1 s and is not a choice (user decision, 2026-11-03). With the user's speech before the window, the user heard no difference with 2.5 s or 5.4 s, and 1 s is the lightest on the GPU (Voice detailed design chapter 17). There is no simple choice by what comes first (Lighter GPU, Smoothness) either: no combination has a reason to be chosen.

The overlap at joins is 50 ms only, not a choice (user decision, 2026-11-03). A choice would need two generators and two templates for every voice.

### 13.3 PCs Without Enough GPU

A PC without enough GPU should not use this feature (the user's remark, 2026-11-03). Which GPUs are enough is not known, so voice conversion cannot be turned on only when there is no usable GPU (user decision, 2026-11-03): when the GPU cannot compute (no compute shaders, or software rendering).

There is no Expected delay row. With the window and the overlap fixed, the delay is always about 0.38 s, so it does not help choose (the user's judgment, 2026-11-03).

### 13.4 What You Hear

The converted voice is heard about 0.38 s late, which makes rhythm hard when singing (Voice detailed design chapter 17). With My own, the monitor gets the microphone as it is, and the stream and recordings get the converted voice. It needs a change to Audio's processing stage, so it comes in a separate step (audio detailed design 8.3).

### 13.5 Strings

| Key | 日本語 | English |
|---|---|---|
| `setup.voice.title` | 声の変換 | Voice conversion |
| `setup.voice.state.running` | 動作中 | Running |
| `setup.voice.state.loading` | 準備中 | Loading |
| `setup.voice.state.off` | オフ | Off |
| `setup.voice.state.unavailable` | 使用不可 | Unavailable |
| `setup.voice.state.stopped` | 停止 | Stopped |
| `setup.voice.no-voice` | 声がありません | No voice |
| `setup.voice.no-voice.body` | Voice Labで声を作成すると、変換先の声を選択できるようになります。 | Make a voice in Voice Lab to choose the voice to convert to. |
| `setup.voice.open-lab` | Voice Labを開く | Open Voice Lab |
| `setup.voice.no-gpu` | このPCでは声の変換を使用できません | Voice conversion cannot be used on this PC |
| `setup.voice.no-gpu.body` | 声の変換にはGPUが必要です。使用できるGPUが見つかりませんでした。 | It needs a GPU, and no usable GPU was found. |
| `setup.voice.failed` | 声の変換が停止しました | Voice conversion stopped |
| `setup.voice.failed.body` | 元の声が流れないよう、配信と録画にマイクの音を入れていません。やり直すには、「声を変換する」をオフにしてからオンにしてください。オフのままにすると、元の声が入ります。解決しない場合は、診断画面のログを確認してください。 | Your microphone is kept out of the stream and recordings, so your own voice does not go out. To try again, turn Convert my voice off and on. Left off, your own voice goes out. If it keeps happening, check the log on the Diagnostics screen. |
| `setup.voice.failed.body-pass-through` | 配信と録画には元の声が流れています。もう一度オンにしてください。解決しない場合は、診断画面のログを確認してください。 | The stream and recordings get your own voice. Turn it on again; if it keeps happening, check the log on the Diagnostics screen. |
| `setup.voice.enabled` | 声を変換する | Convert my voice |
| `setup.voice.enabled.help` | オフの場合、元の声が配信と録画に入ります。オンの場合、声の準備を進めている間と、変換に失敗したときは、マイクの音を入れません。声の変換にはGPUを使用します。 | Off, your own voice goes to the stream and recordings. On, the microphone is kept out while the voice gets ready and whenever conversion fails. Voice conversion uses the GPU. |
| `setup.voice.voice` | 声 | Voice |
| `setup.voice.voice.help` | Voice Labで作成した声から選択します。 | Choose from the voices made in Voice Lab. |
| `setup.voice.auto-pitch` | 高さを自動で合わせる | Match the pitch automatically |
| `setup.voice.auto-pitch.help` | 自分の声の高さを測定し、選択した声の高さに合わせます。「測り直す」で測定をやり直せます。 | Measures your voice and moves it to the chosen voice's pitch. Measure again measures it once more. |
| `setup.voice.pitch-match` | 自分の声 {user} → この声 {voice}（{shift}） | Your voice {user} → this voice {voice} ({shift}) |
| `setup.voice.unmeasured` | 測定前 | not measured |
| `setup.voice.remeasure` | 測り直す | Measure again |
| `setup.voice.adjust` | 高さの調整 | Pitch adjustment |
| `setup.voice.adjust.help-auto` | 半音単位で調整します。自動で合わせた高さに加算されます。 | In semitones, added to the automatic match. |
| `setup.voice.adjust.help-manual` | 半音単位で調整します。男声を女声にする場合は、+12前後が目安です。 | In semitones. From a man's voice to a woman's, about +12. |
| `setup.voice.final` | 変換後の高さ {hz}（合計 {total}） | Converted pitch {hz} (total {total}) |

## 14. The BGM and Sound Effects Cards

Under the audio Card (chapter 11) on the Audio page are the BGM Card and the sound effects Card (BGM and SE detailed design; 2026-11-17). The registered files are listed a row each, as a table (`components.md` 3.24). Both are collapsible Cards (`components.md` 3.10) that start open. As their lists grow long, the one not in use can be closed (an exception to the screen-wide rule; user decision, 2026-11-20). Whether they are open is not remembered.

| Column | What it holds |
|---|---|
| Reorder | A "≡" mark at the row's start (`text-3`; not a button, the same mark as the Layer List's grip). Dragging it up or down moves its row; while dragging, the row is dimmed and an Accent Line marks where it lands (as the Layer List, `components.md` 3.20). The mark takes no focus. By keyboard, with the focus on one of the row's buttons, `Ctrl`+Up and Down move it by one, and the focus stays on the same control of the moved row (its Rename button if that one cannot take it) (not while typing a name). The live screen's BGM choice and sound effect buttons follow the same order |
| Play | Icon Button. For BGM it plays the file on the stream and in recordings, and turns into stop (■) while it plays. For a sound effect each press plays it from its start, overlapping. Disabled until it is loaded, with a tooltip saying it is loading |
| Name | A built-in file shows its name in the screen's language until renamed (`bgm-se.md` 6.3). Takes the rest of the width, cut with an ellipsis, its tooltip holding it whole. For BGM a Status Chip after the name says Playing, or Cannot play (by shape and text) |
| Length | Minutes and seconds, monospaced, right-aligned. Blank when the file does not say |
| Loop | BGM only. Toggle, on by default; a change applies from the next play. Explained by a Help Hint after its heading |
| Volume | Slider, -60 to +12 dB: the file's own, apart from the BGM and sound effects rows of the mixer. The BGM that plays takes it at once |
| Shortcut | Sound effects only. Pressed, it says "Press keys" and waits with an Accent Line border. Keys pressed with `Ctrl`, `Alt`, or `Shift` are assigned (they play it whichever app is in use; `bgm-se.md` 8.3). A key alone is not assigned, and the tooltip says a modifier is needed. `Esc` cancels; `Delete` or `Backspace` removes them. While it waits it takes every key (Tab does not leave it; `Esc` does). The keys are shown monospaced, as "Ctrl+Alt+1", or "None". Keys another sound effect has are not assigned, and the Card says why under the rows. When another app holds the keys, a Status Chip "Keys used by another app" (a warning shape and text) follows the name. Explained by a Help Hint after its heading |
| Actions | Rename (the name turns into a field where it is, and the button into Save; 1 to 60 characters), Delete (after a confirmation; the original file stays). Delete is disabled for the BGM that plays, with the reason in its tooltip |

- Add a file… is under the rows. The file dialog offers WAV and MP3. The chosen file is checked, and when it cannot be registered the Card says why (another format, unreadable, a sound effect over 30 seconds, cannot be saved). It is registered under its file name, which can be changed later.
- The sound effects Card has Stop sound effects at the right of its title, stopping every sound effect that sounds.
- With nothing registered, a line says what to add.
- The mixer (chapter 11) gains BGM and sound effects rows. They have no device, so the Device column is blank, as is Noise removal. Hear is on by default.
- The live screen's audio (`live.md`) gains BGM and sound effects, with a note and a speaker as their icons, crossed out when muted.

| Key | 日本語 | English |
|---|---|---|
| `setup.bgm.title` | BGM | BGM |
| `setup.bgm.add.help` | WAVまたはMP3のファイルを追加します。アプリはファイルの複製をローカルに保存します。 | Adds a WAV or MP3 file. The app keeps a copy of the file on this PC. |
| `setup.bgm.gain.help` | このBGMの音量です。再生している間も変更できます。 | This BGM's volume. It can be changed while it plays. |
| `setup.se.title` | 効果音 | Sound effects |
| `setup.se.add.help` | 30秒までのWAVまたはMP3のファイルを追加します。アプリはファイルの複製をローカルに保存します。 | Adds a WAV or MP3 file of up to 30 seconds. The app keeps a copy of the file on this PC. |
| `setup.se.gain.help` | この効果音の音量です。鳴らしている間も変更できます。 | This sound effect's volume. It can be changed while it plays. |
| `setup.clips.name` | 名前 | Name |
| `setup.clips.length` | 長さ | Length |
| `setup.clips.loop` | ループ | Loop |
| `setup.clips.loop.help` | 曲の終わりで先頭に戻り、繰り返し再生します。変更は次の再生から反映されます。 | Plays again from the start at the end. A change applies from the next play. |
| `setup.clips.gain` | 音量 | Volume |
| `setup.clips.add` | ファイルを追加… | Add a file… |
| `setup.clips.rename` | 名前を変更 | Rename |
| `setup.clips.save` | 保存 | Save |
| `setup.clips.delete` | 削除 | Delete |
| `setup.clips.move` | ドラッグで並べ替えます。行のボタンを選択したうえで、Ctrl+↑↓を押下しても移動可能です。 | Drag to reorder, or select one of the row's buttons and press Ctrl+Up or Ctrl+Down. |
| `setup.bgm.move` | ドラッグで並べ替えます。行のボタンを選択したうえで、Ctrl+↑↓を押下しても移動可能です。BGMの並び順は、ライブ画面の一覧にも反映されます。 | Drag to reorder, or select one of the row's buttons and press Ctrl+Up or Ctrl+Down. The Live screen's list follows the same order. |
| `setup.clips.name.required` | 名前を入力してください | Enter a name |
| `setup.clips.filter` | 音声ファイル | Audio files |
| `setup.bgm.play` | このBGMを配信と録画で再生します。 | Plays this BGM on the stream and in recordings. |
| `setup.bgm.stop` | このBGMを停止します。 | Stops this BGM. |
| `setup.bgm.playing` | 再生中 | Playing |
| `setup.bgm.failed` | 再生できません | Cannot play |
| `setup.bgm.empty` | BGMはまだ登録されていません。WAVかMP3のファイルを追加してください。 | No BGM yet. Add a WAV or MP3 file. |
| `setup.bgm.delete.title` | BGMの削除 | Delete the BGM |
| `setup.bgm.delete.body` | このBGMを削除します。追加した元のファイルは削除されません。 | This BGM is deleted. The file it was added from is not. |
| `setup.se.play` | この効果音を配信と録画で再生します。 | Plays this sound effect on the stream and in recordings. |
| `setup.se.stop-all` | 効果音を停止 | Stop sound effects |
| `setup.se.empty` | 効果音はまだ登録されていません。30秒までのWAVかMP3のファイルを追加してください。 | No sound effects yet. Add a WAV or MP3 file of up to 30 seconds. |
| `setup.se.loading` | 読み込み中です。 | Loading. |
| `setup.se.shortcut` | ショートカット | Shortcut |
| `setup.se.shortcut.help` | クリックしてから、割り当てるキーを押してください。<br>Ctrl、Alt、Shiftのいずれかとの組み合わせのみ登録可能です。<br>Deleteキーでショートカットキーを解除できます。<br>ほかのアプリを使用している間も鳴らせます。 | Click this, then press the keys to assign.<br>Only combinations with Ctrl, Alt, or Shift can be registered.<br>Press Delete to remove the shortcut keys.<br>It plays while you use other apps too. |
| `setup.se.shortcut.none` | なし | None |
| `setup.se.shortcut.press` | キーを押してください | Press keys |
| `setup.se.shortcut.need-modifier` | Ctrl、Alt、Shiftのどれかと一緒に押してください。 | Press it with Ctrl, Alt, or Shift. |
| `setup.se.shortcut.taken` | このキーはほかの効果音で使っています。別のキーを選んでください。 | Another sound effect uses these keys. Choose other keys. |
| `setup.se.shortcut.held-elsewhere` | キーをほかのアプリが使用中 | Keys used by another app |
| `setup.se.delete.title` | 効果音の削除 | Delete the sound effect |
| `setup.se.delete.body` | この効果音を削除します。追加した元のファイルは削除されません。 | This sound effect is deleted. The file it was added from is not. |
| `setup.clips.delete-in-use` | 再生中は削除できません。停止してから削除してください。 | It cannot be deleted while it plays. Stop it first. |
| `setup.clips.problem.unsupported` | WAVかMP3のファイルを選択してください。 | Choose a WAV or MP3 file. |
| `setup.clips.problem.unreadable` | 音声ファイルを読み込めませんでした。ファイルが壊れていないか確認してください。 | The audio file could not be read. Check that it is not damaged. |
| `setup.clips.problem.too-long` | 効果音は30秒までです。短いファイルを選択してください。 | Sound effects can be up to 30 seconds. Choose a shorter file. |
| `setup.clips.problem.storage` | ファイルを保存できませんでした。保存先の空き容量を確認してください。 | The file could not be saved. Check the free space where it is kept. |

## 15. Lighting Card

Builds the stage's lights (Stage detailed design 7.3). It replaces the provisional brightness slider, which was in the face tuning Card (2026-10-21) (user's request, 2026-11-22). On the avatar page, under the face tuning, since both are judged by looking at the face on the stream. Left out without Stage.

| Row | Component | Content |
|---|---|---|
| Video | "Placement" and "Stream" Tabs over an image (448×252, 16:9), on the left | "Placement" is the stage view (15.2). "Stream" is the whole stream, "No video" when there is none. So the face stays in view while a light moves |
| Right of the title band | Button (Secondary) "Add a light", "Remove this light" | An added light is a spot from the right (left and right 45°, up and down 15°, distance 2 m), and becomes the chosen one. With four lights adding is disabled, with one removing is, and their tooltips say why |
| Light | Segmented (1 to 4) | The light the rows below change. Lights not made yet are disabled |
| Kind | Segmented (Whole stage, Spot) | A directional light, or a spot aimed at the face |
| Left and right | Slider (-180 to 180°, steps of 5°) | 0° is the front (the camera's side), plus the screen's right |
| Up and down | Slider (-30 to 90°, steps of 5°) | 0° is the face's height, 90° straight above |
| Distance | Slider (0.5 to 5 m, steps of 0.1 m) | Spot only. Disabled for a whole-stage light, with the reason in its tooltip |
| Spread | Slider (10 to 120°, steps of 5°) | Spot only. Disabled for a whole-stage light |
| Brightness | Slider (10 to 200%, steps of 5%) | 70% by default |
| Color | A swatch and a Dropdown (White, Pink, Orange, Yellow, Green, Sky blue, Blue, Purple, Custom). Custom starts from the color the light had | The color is also told by its name (not by color alone) |
| Color temperature | Slider (2700 to 9000 K, steps of 100 K) | White only. Disabled for the other colors |
| Color code | Text input (`#RRGGBB`) | Custom color only. Applies on Enter or on leaving the field; a code that cannot be read says why under the field. For the other colors it is disabled and shows that color's code |
| Color by | Dropdown (RGB, HSV) | Whether the three sliders below set the color as RGB or HSV (user's request, 2026-11-22). Not saved. Custom color only |
| R, G, B / H, S, V | Sliders: RGB 0 to 255 each; HSV H 0 to 360°, S and V 0 to 100% | Custom color only. Disabled for the other colors. All three have the same Help Hint |
| Ambient light | Slider (0 to 200%, steps of 5%) | Shared by all lights |
| Reset the lighting | Button (Secondary) "Reset" | Back to one light as it started, and the ambient light at 100% |

- The video is on the left and the settings on the right, at most 480 px tall and scrolled inside, so the avatar stays in view whichever light moves (user's report, 2026-11-22: with the settings under the video, the video went off the screen). On a narrow window the settings wrap below the video.
- Sliders are not Setting Rows: as in the face tuning Card, the name is above with its Help Hint. As rows, each was tall and the list grew too long.
- A change reaches the stream at once and is saved in `stage-settings.json` (Stage detailed design 6.3).
- Disabled rows stay in view, so the user sees what a spot or white would add.
- The Japanese text is provisional, for the user to proofread (`ui-guidelines.md`).

### 15.1 Strings

| Key | 日本語 | English |
|---|---|---|
| `setup.lighting.title` | 照明 | Lighting |
| `setup.lighting.light` | ライト | Light |
| `setup.lighting.light.help` | 下の設定を変えるライトを選択します。ライトは4つまで追加できます。 | Choose the light the settings below change. Up to four lights can be added. |
| `setup.lighting.light.1` | 1 | 1 |
| `setup.lighting.light.2` | 2 | 2 |
| `setup.lighting.light.3` | 3 | 3 |
| `setup.lighting.light.4` | 4 | 4 |
| `setup.lighting.add` | ライトを追加 | Add a light |
| `setup.lighting.add.full` | ライトは4つまでです。 | There can be up to four lights. |
| `setup.lighting.remove` | このライトを削除 | Remove this light |
| `setup.lighting.remove.last` | ライトは1つ以上必要です。 | At least one light is needed. |
| `setup.lighting.kind` | 種類 | Kind |
| `setup.lighting.kind.help` | 「全体」は、ステージ全体を同じ向きから照らします。「スポット」は、アバターの顔に向けた光で、当たる範囲を絞れます。 | Whole stage lights everything from one direction. Spot is aimed at the avatar's face, and its cone can be narrowed. |
| `setup.lighting.kind.directional` | 全体 | Whole stage |
| `setup.lighting.kind.spot` | スポット | Spot |
| `setup.lighting.yaw` | 左右の向き | Left and right |
| `setup.lighting.yaw.help` | アバターから見て、光が来る方向です。0°が正面（カメラの側）で、プラスにすると画面の右から照らします。 | Where the light comes from around the avatar. 0° is the front (the camera's side); a plus value lights from the screen's right. |
| `setup.lighting.pitch` | 上下の向き | Up and down |
| `setup.lighting.pitch.help` | 光が来る高さです。0°が顔と同じ高さで、90°が真上です。マイナスにすると下から照らします。 | How high the light comes from. 0° is the face's height and 90° straight above; a minus value lights from below. |
| `setup.lighting.distance` | 距離 | Distance |
| `setup.lighting.distance.help` | スポットライトから顔までの距離です。遠くするほど、光の当たる範囲が広がります。明るさは変わりません。 | How far the spot is from the face. Farther lights a wider area; the brightness stays the same. |
| `setup.lighting.spot-only` | スポットライトで使います。 | Used by a spot. |
| `setup.lighting.spot-angle` | 光の広がり | Spread |
| `setup.lighting.spot-angle.help` | スポットライトの光が広がる角度です。小さくするほど、狭い範囲だけを照らします。 | The angle of the spot's cone. Smaller lights a narrower area. |
| `setup.lighting.intensity` | 明るさ | Brightness |
| `setup.lighting.intensity.help` | このライトの明るさです。既定は70%です。顔が白くなりすぎる場合は下げてください。 | This light's brightness, 70% by default. Lower it if the face looks washed out. |
| `setup.lighting.color` | 色 | Color |
| `setup.lighting.color.help` | 光の色です。「白」では、下の色温度で暖かい色から冷たい色まで変えられます。 | The light's color. With White, the color temperature below makes it warmer or cooler. |
| `setup.lighting.color.white` | 白 | White |
| `setup.lighting.color.pink` | ピンク | Pink |
| `setup.lighting.color.orange` | オレンジ | Orange |
| `setup.lighting.color.yellow` | 黄色 | Yellow |
| `setup.lighting.color.green` | 緑 | Green |
| `setup.lighting.color.sky` | 水色 | Sky blue |
| `setup.lighting.color.blue` | 青 | Blue |
| `setup.lighting.color.purple` | 紫 | Purple |
| `setup.lighting.color.custom` | カスタム | Custom |
| `setup.lighting.temperature` | 色温度 | Color temperature |
| `setup.lighting.temperature.help` | 白い光の色合いです。小さくするほど暖かい色（電球の色）に、大きくするほど冷たい色になります。6500 Kが昼の光です。 | The shade of the white light. Lower is warmer, like a light bulb; higher is cooler. 6500 K is daylight. |
| `setup.lighting.white-only` | 色が「白」のときに使います。 | Used when the color is White. |
| `setup.lighting.code` | カラーコード | Color code |
| `setup.lighting.code.help` | 光の色を「#FF88CC」のような形で入力します。入力したあと、Enterキーを押すか、ほかの場所をクリックすると反映されます。 | Enter the light's color like "#FF88CC". It applies when you press Enter or click elsewhere. |
| `setup.lighting.code.invalid` | 「#」と6桁の英数字（0〜9、A〜F）で入力してください。 | Enter # and six characters from 0–9 and A–F. |
| `setup.lighting.channels` | 色の指定 | Color by |
| `setup.lighting.channels.help` | 下のスライダーで、色をRGBとHSVのどちらで決めるかを選びます。 | Choose whether the sliders below set the color as RGB or HSV. |
| `setup.lighting.channels.rgb` | RGB | RGB |
| `setup.lighting.channels.hsv` | HSV | HSV |
| `setup.lighting.hsv.h` | H | H |
| `setup.lighting.hsv.s` | S | S |
| `setup.lighting.hsv.v` | V | V |
| `setup.lighting.hsv.help` | 光の色を、色相（H、0〜360°）、彩度（S、0〜100%）、明度（V、0〜100%）で決めます。 | Sets the light's color by hue (H, 0–360°), saturation (S, 0–100%), and value (V, 0–100%). |
| `setup.lighting.rgb.help` | 光の色を、赤（R）、緑（G）、青（B）の強さ（0〜255）で決めます。 | Sets the light's color by how much red (R), green (G), and blue (B) it has, from 0 to 255. |
| `setup.lighting.rgb.r` | R | R |
| `setup.lighting.rgb.g` | G | G |
| `setup.lighting.rgb.b` | B | B |
| `setup.lighting.custom-only` | 色が「カスタム」のときに使います。 | Used when the color is Custom. |
| `setup.lighting.ambient` | 環境光 | Ambient light |
| `setup.lighting.ambient.help` | ライトが当たらない影の側の明るさです。下げると影が濃くなり、スポットライトが目立ちます。すべてのライトに共通です。 | How bright the shade is where no light reaches. Lower makes the shadows deeper and a spot stand out. It is shared by all lights. |
| `setup.lighting.reset` | 照明を既定に戻す | Reset the lighting |
| `setup.lighting.reset.help` | ライトを1つにし、向き、明るさ、色と環境光を初めの状態に戻します。 | Goes back to one light, with its direction, brightness, and color, and the ambient light, as they started. |
| `setup.lighting.reset.button` | 既定に戻す | Reset |
| `setup.lighting.tab.placement` | 配置 | Placement |
| `setup.lighting.tab.stream` | 配信の映像 | Stream |
| `setup.lighting.view.help` | 番号の印をドラッグすると、ライトが顔を向いたまま動きます。何もない所をドラッグすると視点が回り、ホイールで寄ったり引いたりできます。Shiftを押しながらドラッグすると、見る中心が動きます。ダブルクリックで視点が戻ります。 | Drag a numbered marker to move its light; it keeps facing the face. Drag elsewhere to turn the view, and use the wheel to zoom. Drag with Shift to move the center of the view. Double-click to reset the view. |
| `setup.lighting.invert-x` | 左右を反転 | Reverse left and right |
| `setup.lighting.invert-y` | 上下を反転 | Reverse up and down |
| `setup.lighting.invert.help` | 何もない所をドラッグしたときに、視点が回る向きを反転します。 | Reverses which way the view turns when you drag on empty space. |

### 15.2 The Placement View (Stage View)

The picture's place switches between "Placement" and "Stream" Tabs (`components.md` 3.8), "Placement" by default. "Placement" shows the lights' positions and directions in 3D and moves them by dragging (Stage detailed design 7.4, system design 11.15); "Stream" is the preview as before. User's request (2026-11-22): from numbers alone it was unclear where a light is and which way its light goes.

| Element | Content |
|---|---|
| View | The stage view's image (16:9, 448 px wide): the stage, the backdrop quad, the avatar, guides (light directions, spot cones), and the stream camera's position |
| Markers | A circle with the light's number (1 to 4) at each light. Filled with the light's color, with text and frame in a readable color. The chosen light's is larger with a white frame. Hidden when not in view (behind the camera and the like) |
| Help Hint beside the Tabs | How to operate it (the table below, shortened) |
| Under the view | Toggles "Reverse left and right" and "Reverse up and down" with a Help Hint. They reverse which way the view turns when dragging on empty space; people find different directions natural (user's request, 2026-11-22). Saved in `ui-settings.json` (UI foundation detailed design 8.1). Shown only with "Placement" |

| Input | Result |
|---|---|
| Click a marker | Chooses that light (the Light segmented control follows) |
| Drag a marker | The light moves on a sphere, still facing the face. The left-right and up-down sliders follow |
| Drag on empty space | Orbits the view. Left and right, the stage turns with the pointer (dragging right turns it right, as in Unity's Scene View). Up and down, the viewpoint follows the pointer (dragging down raises it; user decision, 2026-11-22). Both can be reversed |
| Middle-button drag, or drag with Shift | Moves the pivot |
| Wheel | Zooms in and out |
| Double-click on empty space | Resets the view |
| Arrow keys with the view focused | Move the chosen light by 5° (left-right, up-down); with Shift they orbit the view |
| F | Puts the chosen light at the pivot |

- The stage view is open (`IStageView.Open`) only while "Placement" is shown. It closes when the Tab changes, the Card is out of view, another page opens, or the Setup screen is left.
- While dragging, each change reaches the stream; it is saved 0.5 s after the last change (Stage detailed design 6.3).
- The sliders stay, for exact values and keyboard-only use.
- When the stage view cannot be used (no render target, for example), the "Placement" Tab is disabled with the reason in its tooltip, and "Stream" is shown.
- The text is provisional, for the user to proofread.
