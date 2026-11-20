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
| Avatar | `setup.page.avatar` | Avatar (chapter 5), tracking (chapter 4), face tuning (chapter 9; only when avatars are available) |
| Streaming and recording | `setup.page.output` | Recording (chapter 10; without recording, the line "Recording is not available."), streaming (chapter 12) |
| App | `setup.page.app` | App settings, credits and licenses (chapter 8) |

- The page last opened is saved as `lastSetupPage` in `ui-settings.json` and opens next time (UI foundation detailed design 8.1). A value not known opens the first page.
- All pages are built, and only the open one shows. Switching pages loses no Card's state.
- The tracking camera has no capture Card; it is chosen in the tracking Card.
- Cards mostly do not collapse. With pages, closing them to keep the screen short is no longer needed. A Card of settings used seldom (such as detailed ones) may, on any page, be a collapsible Card (`components.md` 3.10) that starts closed (user decision, 2026-11-03).
- The state (Status Indicator) sits at the right end of the Card's title band. The explanation of a problem comes first in the body.

## 3. Capture Card

One per capture source, showing `ICaptureDeviceSource` (Capture) directly.

| Row | Component | Content |
|---|---|---|
| State | Status Indicator (`components.md` 3.13) | The state, and an explanation when there is a problem (chapter 6) |
| Video | Image (256×144, fitted to 16:9) | The captured video, small, to check that the chosen device is the right one; "No video" when there is none |
| Capture | Toggle | On starts, off stops. Whether it was started is saved and restored at the next start |
| Input (game screen only) | Segmented (Capture board, A window on this PC) | What the game screen is taken from (Capture detailed design 8.6). With "A window on this PC", Device lists the windows open on this PC, one per program (its largest). A chosen device shows its kind. Changing the kind lets go of a device of the other kind. Explanation: "A game running on this PC is taken from its window under "A window on this PC". Only that game's sound is taken with it." |
| Device | Dropdown | The devices found. The game screen starts with "Choose a camera" ("Choose the game's window" for windows), the sub monitor with "Automatic (not the main monitor)". A window shows as "Title (program)". With none found, "No device found" ("No window found" for windows) |
| Audio (game screen only) | Dropdown | The audio device the game's sound comes from (`GameAudioRow`). The same setting as the game audio's device on the Audio Card (section 11); either place changes it. Choosing a video device chooses the audio device whose name matches. Choosing a window chooses only that game's sound ("Sound of game.exe", Audio detailed design 8.7) and does not send it to the monitor: the PC plays the game's sound already, so it would be heard twice. Back on a capture board it is sent to the monitor again. Left out when audio is not available |
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
| What moves | Segmented (Upper body, Full body (soon)) | The upper body. Full body cannot be chosen yet; it is shown in the disabled color so users know it is coming (Tracking detailed design 6.5, `components.md` 3.18). Description: "For streaming seated. Full body is coming later." Japanese: 上半身, 全身（準備中） |
| Parts tracked | Toggles "Face", "Body", "Hands", one under another | All on by default. A part turned off is not inferred, which lowers the load, and does not move (Tracking detailed design 6.5). "Hands" covers both hands and the fingers. Description: "Turning off parts you do not use lowers the load. A part turned off does not move." One under another so they fit a row's control (at most 260px) in every language. A change applies at once and is saved |
| Body movement | Segmented (3D, 2D) | 3D by default. 2D moves the avatar only by the movement seen from the front, without the body's depth (Tracking detailed design 8.6). Description: "2D leaves out moving toward and away from the camera and moves the avatar only as seen from the front. The arms point more steadily." A change applies at once and is saved |
| Movement speed | Slider (1 to 10, steps of 1) | The smoothing level (Tracking detailed design 8.2). Description: "Higher follows your movement faster; lower keeps the avatar steadier." 5 by default (on 2026-10-24 the levels moved two steps and then four more, making a level steady within about 300 ms lag the new 5). A change applies at once and is saved |
| Body detection | Segmented (Light, Standard) | The Pose model (Tracking detailed design chapter 10), Standard by default. Description: "Light uses less of the computer but tracks the body more roughly. A change takes a few seconds to get ready again." A change while tracking restarts it with the new model, and is saved. Heavy ("Precise") is not offered: on the user's camera it tracked worse than standard (2026-10-26) |
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
| Register | Button (Primary) "Register an avatar…" | Opens the file-picking screen (VRM files only) and registers after the confirmation dialog (5.2) |
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
| Light brightness | Slider (10 to 150%, steps of 5%), under the zoom | The brightness of the stage's light (Stage detailed design 7.1). 100% is the default. Caption: "This also changes the light on the stream, and is saved." Lowered when the face is washed out (user's request, 2026-10-21). Beside the picture, so it is changed while watching the face (user's decision, 2026-10-21). Left out without Stage |
| Winks | Toggle | Whether one eye may close alone. Description: "When on, one eye can close alone. Turn it off if one eye closes by mistake, such as when you turn your head." Off by default. While off, "Winking" is disabled |
| Per part | Slider (50 to 300%, steps of 10%) with a bar under it | Eye closing, winking, brows (surprise, anger, sadness), mouth, gaze, and smile. 100% is the default. The bar is how far the avatar moves that part now (0 to 100%), redrawn about every 50 ms |
| Finger bend, thumb bend | Slider (50–200%, in 10% steps) | How strongly the fingers bend (avatar detailed design 7.5). 100% is the default. Caption: "Raise these if the fingers do not bend far enough, and lower them if they bend too far. An open hand stays open at any value." The hands are seen with the picture's height and zoom |
| Reset everything | Button (Secondary) "Reset" | Puts every part and the fingers back to 100% and turns winks off |

- The video is on the left and the sliders on the right, so the avatar stays in view whichever slider moves. On a narrow window the sliders wrap below the video.
- The bars are progress bars (`components.md` 3.15) without a title; the introduction says what they show.
- A change applies to the avatar on the stream at once and is saved (`avatar-settings.json`).
- When avatars are unavailable (no Avatar), this Card is not placed. While no avatar is chosen, the bars stay still.
- The light's brightness reaches the stream at once and is saved in `stage-settings.json` (Stage detailed design 6.3). "Reset" puts back only the face tuning, not the light.
- The crop is made by placing the whole stream enlarged inside a frame, shifting it, and hiding what is outside the frame. Changing the shown part of Unity's Image (its UV rectangle) was not redrawn (2026-10-21).

## 10. Recording Card

Shows `IRecordingService` (Video) directly (`RecordingCard`); details in recording detailed design 18.2.

| Row | Content |
|---|---|
| Folder | On the right, "Change…" (Windows' folder screen) and "Open" (Explorer). The folder's path is under the description, where it wraps (`components.md` 3.9) |
| Quality | Segmented: Standard, High. The bitrates (6 Mbps, 12 Mbps) are in the description |
| Format | Segmented: AV1, H.264. The description says AV1 is recommended and when to choose H.264. Without an AV1 encoder on the GPU, AV1 cannot be chosen and the description says why (recording detailed design 18.2) |
| Free space | Free space on the folder's drive, read again every 10 seconds while the Card is shown |
| Video | Size, frame rate, and container (1920×1080, 30 fps, MP4); fixed for now |

- Changes are used from the next recording; a recording in progress keeps the settings it started with.
- Recording is started and stopped on the live screen (`live.md`).

| Key | 日本語 | English |
|---|---|---|
| `setup.recording.title` | 録画 | Recording |
| `setup.recording.unavailable` | 録画は使えません。 | Recording is not available. |
| `setup.recording.folder` | 保存先 | Folder |
| `setup.recording.folder.description` | 録画したファイルを保存するフォルダーです。次の録画から使われます。 | Where recordings are saved. Used from the next recording. |
| `setup.recording.folder.pick` | 変更… | Change… |
| `setup.recording.folder.pick-title` | 録画の保存先を選んでください | Choose where to save recordings |
| `setup.recording.folder.open` | 開く | Open |
| `setup.recording.quality` | 画質 | Quality |
| `setup.recording.quality.description` | 標準は6 Mbps、高画質は12 Mbpsです。高画質はきれいですが、ファイルが約2倍の大きさになります。 | Standard is 6 Mbps and High 12 Mbps. High looks better, and its files are about twice as large. |
| `setup.recording.quality.standard` | 標準 | Standard |
| `setup.recording.quality.high` | 高画質 | High |
| `setup.recording.codec` | 形式 | Format |
| `setup.recording.codec.description` | AV1がおすすめです。同じ画質でファイルが小さくなります。古い編集ソフトやスマートフォンで開けないときは、H.264を選んでください。 | AV1 is recommended: smaller files at the same quality. Choose H.264 if an older editor or phone cannot open the files. |
| `setup.recording.codec.no-av1` | このPCのGPUはAV1に対応していないため、H.264で録画します。 | This PC's GPU does not support AV1, so recordings use H.264. |
| `setup.recording.codec.av1` | AV1 | AV1 |
| `setup.recording.codec.h264` | H.264 | H.264 |
| `setup.recording.free` | 空き容量 | Free space |
| `setup.recording.free.value` | {size} GB | {size} GB |
| `setup.recording.free.unknown` | わかりません | Unknown |
| `setup.recording.format` | 映像 | Video |
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

Under a row, only when there is a problem, what happened and what to do; when microphone access is denied, "Open microphone privacy settings". Hear and Noise removal are explained in Help Hints after their column headings (components.md 3.28), not in lines under the table (the user's request, 2026-11-11). That the game audio's device is chosen on the Video page is the tooltip of its disabled device choice. Under the table, the stream and recording audio delay (Slider, 0 to 1000 ms, 300 ms by default; audio detailed design 8.5).

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
| `setup.capture.audio` | 音声 | Audio |
| `setup.capture.audio.description` | ゲームの音を取り込む機器です。映像の機器を選択すると、同じ機器の音声が自動で選択されます。 | The device the game's sound comes from. Choosing a video device chooses its audio too. |
| `setup.audio.device.none` | なし | None |
| `setup.audio.device.missing` | 接続されていないデバイス | A device not connected now |
| `setup.audio.device.application` | {name} の音 | Sound of {name} |
| `setup.audio.gain` | 音量 | Volume |
| `setup.audio.mute` | ミュート | Mute |
| `setup.audio.level` | レベル | Level |
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
| `setup.audio.monitored.description` | 「モニター」の出力先から聞こえます。配信と録画には影響しません。スピーカーではマイクに音が入るため、ヘッドホンの使用を推奨します。 | Plays the source on the monitor output, without changing the stream or recordings. Headphones are recommended: speakers leak into the microphone. |
| `setup.audio.denoise` | ノイズ除去 | Noise removal |
| `setup.audio.denoise.description` | 話していない間の、キーボードやマウスなどの音を抑えます。声の変換を使わないときも効きます。 | Reduces keyboard, mouse, and other sounds while you are not speaking, with or without voice conversion. |
| `setup.audio.game-device` | ゲームの音声の機器は、「映像」のページで選びます。 | The game audio's device is chosen on the Video page. |
| `setup.audio.monitor` | モニター | Monitor |
| `setup.audio.monitor.device` | 出力先 | Output |
| `setup.audio.delay` | 配信・録画の音声の遅延 | Stream and recording audio delay |
| `setup.audio.delay.description` | アバターの動きは、カメラから読み取るぶん声より遅れます。配信と録画の音声をこの分だけ遅らせて合わせます。自分に聞こえる音は遅れません。 | The avatar moves later than your voice, since its motion is read from the camera. Streaming and recording get the sound this much later to match. What you hear is not delayed. |

## 12. The Streaming Card

`IStreamingService` (Streaming) is shown directly (`StreamingCard`). The rows and their behavior are in the streaming detailed design 18.2. The stream key's value is never shown, and the dialog that registers it uses a masked field. While streaming no row can change, and the Card says why.

| Key | 日本語 | English |
|---|---|---|
| `setup.streaming.title` | 配信 | Streaming |
| `setup.streaming.unavailable` | 配信は使えません。 | Streaming is not available. |
| `setup.streaming.locked` | 配信中は変更できません。 | These cannot change while streaming. |
| `setup.streaming.destination` | 配信先 | Destination |
| `setup.streaming.destination.youtube` | YouTube | YouTube |
| `setup.streaming.destination.custom` | 他のサーバー | Other server |
| `setup.streaming.url` | サーバーのURL | Server URL |
| `setup.streaming.url.description` | YouTubeは既定のままで使えます。rtmp:// か rtmps:// で始まるURLです。 | YouTube works with the default. The URL starts with rtmp:// or rtmps://. |
| `setup.streaming.url.placeholder` | rtmps://example.com/live | rtmps://example.com/live |
| `setup.streaming.url.invalid` | rtmp:// または rtmps:// で始まるURLを入力してください。 | Enter a URL that starts with rtmp:// or rtmps://. |
| `setup.streaming.url.unencrypted` | rtmp:// は暗号化されません。ストリームキーも暗号化されずに送られます。rtmps:// を使用することを推奨します。 | rtmp:// is not encrypted, and neither is the stream key sent over it. rtmps:// is recommended. |
| `setup.streaming.key` | ストリームキー | Stream key |
| `setup.streaming.key.description` | Windowsの資格情報マネージャーに保存します。 | Saved in Windows Credential Manager. |
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
| `setup.streaming.codec.description` | AV1を推奨します。同じビットレートできれいに映ります。配信が正常に行えない場合は、H.264を選択してください。 | AV1 is recommended: it looks better at the same bitrate. If the stream does not work properly, choose H.264. |
| `setup.streaming.codec.no-av1` | このPCのGPU、または配信先がAV1に対応していないため、H.264で配信を行います。 | This PC's GPU or the destination does not support AV1, so the stream uses H.264. |
| `setup.streaming.codec.av1` | AV1 | AV1 |
| `setup.streaming.codec.h264` | H.264 | H.264 |
| `setup.streaming.bitrate` | 映像のビットレート | Video bitrate |
| `setup.streaming.bitrate.description` | 1080p 30 fpsならば、6 Mbpsを推奨します。回線の上り速度より低くしてください。 | 6 Mbps is recommended for 1080p at 30 fps. Keep it below your connection's upload speed. |
| `setup.streaming.test` | 接続テスト | Connection test |
| `setup.streaming.test.description` | サーバーに接続可能であるかを確かめます。配信は開始しません。 | Checks that the server can be reached. The stream does not start. |
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
| Match the pitch automatically | Toggle, Measure again | Under the description, "Your voice 200 Hz → this voice 285 Hz (+6)"; "not measured" before measuring |
| Pitch adjustment | Slider | -24 to +24 semitones, added to the automatic shift when that is on; the description changes with automatic on or off. Under it, "Converted pitch 317 Hz (total +8)" (user's check, 2026-11-10) |
| Finish | Four sliders, Reset the finish | The chosen voice's body, clarity, brightness, and air (-6 to +6 dB). A change is kept with the voice and heard on the stream at once. Disabled without a voice (Voice detailed design 8.6; the user's decision, 2026-11-15) |

A Help Hint after the name "Convert my voice" holds "Voice conversion uses the GPU." (the user's request, 2026-11-11: keep the screen's text short).

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
| `setup.voice.enabled.body` | オフの場合、元の声が配信と録画に入ります。オンにしてから声の準備ができるまでと、変換できないときは、マイクの音を入れません。起動時は前回の状態に戻ります。 | Off, your own voice goes to the stream and recordings. On, the microphone is kept out until the voice is ready, and whenever it cannot convert. It starts as it was last time. |
| `setup.voice.voice` | 声 | Voice |
| `setup.voice.voice.body` | Voice Labで作成した声から選択します。 | Choose from the voices made in Voice Lab. |
| `setup.voice.auto-pitch` | 高さを自動で合わせる | Match the pitch automatically |
| `setup.voice.auto-pitch.body` | 自分の声の高さを測定し、選択した声の高さに合わせます。 | Measures your voice and moves it to the chosen voice's pitch. |
| `setup.voice.pitch-match` | 自分の声 {user} → この声 {voice}（{shift}） | Your voice {user} → this voice {voice} ({shift}) |
| `setup.voice.unmeasured` | 測定前 | not measured |
| `setup.voice.remeasure` | 測り直す | Measure again |
| `setup.voice.adjust` | 高さの調整 | Pitch adjustment |
| `setup.voice.adjust.body-auto` | 半音単位で調整します。自動で合わせた高さに加算されます。 | In semitones, added to the automatic match. |
| `setup.voice.adjust.body-manual` | 半音単位で調整します。男声を女声にする場合は、+12前後が目安です。 | In semitones. From a man's voice to a woman's, about +12. |
| `setup.voice.final` | 変換後の高さ {hz}（合計 {total}） | Converted pitch {hz} (total {total}) |
| `setup.voice.gpu` | 声の変換にはGPUを使用します。 | Voice conversion uses the GPU. |

## 14. The BGM and Sound Effects Cards

Under the audio Card (chapter 11) on the Audio page are the BGM Card and the sound effects Card (BGM and SE detailed design; 2026-11-17). The registered files are listed a row each, as a table (`components.md` 3.24).

| Column | What it holds |
|---|---|
| Play | Icon Button. For BGM it plays the file on the stream and in recordings, and turns into stop (■) while it plays. For a sound effect each press plays it from its start, overlapping. Disabled until it is loaded, with a tooltip saying it is loading |
| Name | Takes the rest of the width, cut with an ellipsis, its tooltip holding it whole. For BGM a Status Chip after the name says Playing, or Cannot play (by shape and text) |
| Length | Minutes and seconds, monospaced, right-aligned. Blank when the file does not say |
| Loop | BGM only. Toggle, on by default; a change applies from the next play. Explained by a Help Hint after its heading |
| Volume | Slider, -60 to +12 dB: the file's own, apart from the BGM and sound effects rows of the mixer. The BGM that plays takes it at once |
| Actions | Rename (the name turns into a field where it is, and the button into Save; 1 to 60 characters), Delete (after a confirmation; the original file stays). Delete is disabled for the BGM that plays, with the reason in its tooltip |

- Add a file… is under the rows. The file dialog offers WAV and MP3. The chosen file is checked, and when it cannot be registered the Card says why (another format, unreadable, a sound effect over 30 seconds, cannot be saved). It is registered under its file name, which can be changed later.
- The sound effects Card has Stop sound effects at the right of its title, stopping every sound effect that sounds.
- With nothing registered, a line says what to add.
- The mixer (chapter 11) gains BGM and sound effects rows. They have no device, so the Device column is blank, as is Noise removal. Hear is on by default.
- The live screen's audio (`live.md`) gains BGM and sound effects, with a note and a speaker as their icons, crossed out when muted.

| Key | 日本語 | English |
|---|---|---|
| `setup.bgm.title` | BGM | BGM |
| `setup.se.title` | 効果音 | Sound effects |
| `setup.clips.name` | 名前 | Name |
| `setup.clips.length` | 長さ | Length |
| `setup.clips.loop` | ループ | Loop |
| `setup.clips.loop.description` | 曲の終わりで先頭に戻り、繰り返し再生します。変更は次の再生から反映されます。 | Plays again from the start at the end. A change applies from the next play. |
| `setup.clips.gain` | 音量 | Volume |
| `setup.clips.add` | ファイルを追加… | Add a file… |
| `setup.clips.rename` | 名前を変更 | Rename |
| `setup.clips.save` | 保存 | Save |
| `setup.clips.delete` | 削除 | Delete |
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
| `setup.se.delete.title` | 効果音の削除 | Delete the sound effect |
| `setup.se.delete.body` | この効果音を削除します。追加した元のファイルは削除されません。 | This sound effect is deleted. The file it was added from is not. |
| `setup.clips.delete-in-use` | 再生中は削除できません。停止してから削除してください。 | It cannot be deleted while it plays. Stop it first. |
| `setup.clips.problem.unsupported` | WAVかMP3のファイルを選択してください。 | Choose a WAV or MP3 file. |
| `setup.clips.problem.unreadable` | 音声ファイルを読み込めませんでした。ファイルが壊れていないか確認してください。 | The audio file could not be read. Check that it is not damaged. |
| `setup.clips.problem.too-long` | 効果音は30秒までです。短いファイルを選択してください。 | Sound effects can be up to 30 seconds. Choose a shorter file. |
| `setup.clips.problem.storage` | ファイルを保存できませんでした。保存先の空き容量を確認してください。 | The file could not be saved. Check the free space where it is kept. |
