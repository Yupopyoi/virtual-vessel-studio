# Setup Screen

## 1. Purpose

The screen for preparing devices and settings before streaming. It also shows the credits of the software in use.

It has the capture Cards, the audio Card (chapter 11), the tracking Card, the avatar Card, the face tuning Card (chapter 9), the recording Card (chapter 10), the app settings Card (UI foundation detailed design 7.7), and the credits Card (chapter 8). Other settings are added with their features.

- The screen is named "Settings" (Japanese 設定) in the navigation and its heading, since it holds the app settings and the credits as well as device setup.
- Its identifier (`setup`), its string keys (`nav.setup`, `setup.*`), and its name in the documents (the setup screen) stay. The identifier is saved as `lastScreen` in `ui-settings.json`, so changing it would lose the screen to return to.

## 2. Layout

Cards stacked from the top:

1. Capture (game screen)
2. Capture (sub monitor)
3. Audio (chapter 11; without audio, the line "Audio is not available.")
4. Tracking (chapter 4)
5. Avatar (chapter 5)
6. Face tuning (chapter 9; only when avatars are available)
7. Recording (chapter 10; without recording, the line "Recording is not available.")
8. App settings
9. Credits and licenses (chapter 8)

- The tracking camera has no capture Card; it is chosen in the tracking Card.
- Every Card is collapsible (`components.md` 3.10). So that the screen does not grow long as settings are added, all Cards are closed when the screen is built. Whether a Card is open is not saved; each start begins with them closed.
- The state (Status Indicator) sits at the right end of the Card's title band, so it shows while the Card is closed. The explanation of a problem comes first in the body.

## 3. Capture Card

One per capture source, showing `ICaptureDeviceSource` (Capture) directly.

| Row | Component | Content |
|---|---|---|
| State | Status Indicator (`components.md` 3.13) | The state, and an explanation when there is a problem (chapter 6) |
| Video | Image (256×144, fitted to 16:9) | The captured video, small, to check that the chosen device is the right one; "No video" when there is none |
| Capture | Toggle | On starts, off stops. Whether it was started is saved and restored at the next start |
| Device | Dropdown | The devices found. The game screen starts with "Choose a camera", the sub monitor with "Automatic (not the main monitor)" |
| Audio (game screen only) | Dropdown | The audio device the game's sound comes from (`GameAudioRow`). The same setting as the game audio's device on the Audio Card (section 11); either place changes it. Choosing a video device chooses the audio device whose name matches. Left out when audio is not available |
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
| Camera not found | Warning | Waiting for the camera. Explanation: "The camera is not found. It is found again when you connect it." |
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
| Starting | Idle | Starting |
| Waiting for video | Idle | Waiting for video |
| No signal | Warning | Waiting for video. Explanation: "No video is arriving. Check the game device's power and cable." |
| Capturing | Success | Capturing |
| Device not found | Warning | Device not found; still looking. Explanation: "It is found again by itself when connected." |
| The tracking camera | Error | Could not capture. Explanation: "Tracking uses this camera. Choose another device." |
| Failed | Error | Could not capture. Explanation: "Another app may be using the device." |

- A missing plugin (`PluginMissing`) is shown to users as a failure (Capture detailed design 6.2).

## 7. Strings

Keys are `setup.capture.*` for the capture Cards, `setup.tracking.*` for the tracking Card, `setup.avatar.*` for the avatar Card, `setup.face.*` for the face tuning Card, and `setup.credits.*` for the credits Card. The texts are those in the tables of chapters 3–6, 8, and 9 (Japanese with the same meaning).

## 8. Credits and Licenses Card

Lists the software and assets in use from the SBOM shipped with the application (`sbom/virtual-vessel-studio.cdx.json`). The full license texts are read from the files shipped with the application (development workflow 18.1.2).

| Row | Component | Content |
|---|---|---|
| Introduction | Description (Text 2) | "Virtual Vessel Studio uses the following software and assets. Where the full license text is included, open it with "Show full text"." |
| A row per component | Looks like a Setting Row | The name on the left, and below it "version · supplier · license" (only what the SBOM gives; Caption, Text 3). The application itself comes first, then the distributed components in the SBOM's order, with the ones built into another indented by spacing L (24px) under it. Development-only components (`excluded`) are left out |
| Full text | Button (Secondary) "Show full text" / "Hide full text" | Only on components whose SBOM entry names license files. Opens the full text below the row. With several files, each starts with its file name (monospace) |
| Full text box | A scrolling box (up to 320px high, Surface 3 background, Radius S) | The full text (Caption, Text 2). It scrolls on its own so that a long notices file does not stretch the screen |

- Names, versions, license names, and full texts are shown as written in the files, as text (never parsed as Rich Text), and the same in every language.
- The SBOM is read when the screen is built; a full text when it is first opened.
- When the SBOM cannot be read, the Card says "The credits could not be read. Reinstalling the app may fix this." instead of the rows. A missing file shows "The full license text is missing." in its box. The developer log records the file's path and the reason.
- Where the files are read: in a built application, the executable's folder (`licenses/` and so on); in the Editor, the repository files that `tools/release/license-files.json` names as their sources.
- Paths written in the SBOM are read only inside that folder (paths with `..`, absolute paths, and `\` are not read). The SBOM ships with the application, so no trust boundary is crossed, but even an edited SBOM cannot make the screen read other files.

## 9. Face Tuning Card

Tunes, part by part and while watching the avatar, how strongly the user's face shows on the avatar's (Avatar detailed design 7.4). The tuning is the user's and stays when the avatar changes.

| Row | Component | Content |
|---|---|---|
| Introduction | Description (Text 2) | "With tracking running, watch the avatar while you adjust. Higher values show an expression with less movement. Under each slider is how far the avatar moves now." |
| Video | Image (384×216, 16:9) | The middle of the stream, enlarged so the expression can be seen. "No video" when there is none |
| Picture height | Slider (0 to 100%, steps of 5%), under the video | Which height of the stream is shown (0% the bottom, 100% the top). 85% at first (where the camera's default position puts the face of an avatar of average height). The face's height depends on the avatar's, so the user sets it. Not saved |
| Zoom | Slider (100 to 400%, steps of 10%), under the picture height | How much the stream is enlarged. 100% shows the whole stream; 220% at first. It zooms about the stream's horizontal middle and never shows past the stream's edges. The face's size depends on the avatar, so the user sets it (user's request, 2026-10-21). Not saved |
| Light brightness | Slider (10 to 150%, steps of 5%), under the zoom | The brightness of the stage's light (Stage detailed design 7.1). 100% is the default. Caption: "This also changes the light on the stream, and is saved." Lowered when the face is washed out (user's request, 2026-10-21). Beside the picture, so it is changed while watching the face (user's decision, 2026-10-21). Left out without Stage |
| Winks | Toggle | Whether one eye may close alone. Description: "When on, one eye can close alone. Turn it off if one eye closes by mistake, such as when you turn your head." Off by default. While off, "Winking" is disabled |
| Per part | Slider (50 to 300%, steps of 10%) with a bar under it | Eye closing, winking, brows (surprise, anger, sadness), mouth, gaze, and smile. 100% is the default. The bar is how far the avatar moves that part now (0 to 100%), redrawn about every 50 ms |
| Reset everything | Button (Secondary) "Reset" | Puts every part back to 100% and turns winks off |

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

Shows `IAudioService` (Audio) directly (`AudioCard`), a group of rows per source (the microphone, the game audio); details in audio detailed design 18.

| Row | Content |
|---|---|
| Heading | The source's name and a Status Chip of its state (Off, Starting, Running, Muted, Not found, Unavailable; a shape and text) |
| Problem | What happened and what to do; when microphone access is denied, "Open microphone privacy settings" |
| Device | Select, "None" first; an unplugged device stays as "A device not connected now" |
| Volume | Slider, -60 to +12 dB |
| Mute | Toggle |
| Input level | Level Meter, -60 to 0 dB |
| Stream and recording audio delay | Slider, 0 to 1000 ms, 300 ms by default; once, after the sources (audio detailed design 8.5) |

- Devices are listed again every 5 seconds while the Card is shown.
- The microphone starts on Windows' default input device on first use; the game audio starts on "None" (choose the capture board's audio; choosing its video device does this, section 3).

| Key | 日本語 | English |
|---|---|---|
| `setup.audio.title` | 音声 | Audio |
| `setup.audio.unavailable` | 音声は使えません。 | Audio is not available. |
| `setup.audio.microphone` | マイク | Microphone |
| `setup.audio.game` | ゲームの音声 | Game audio |
| `setup.audio.device` | デバイス | Device |
| `setup.audio.device.microphone` | 話す声を拾うマイクです。 | The microphone that picks up your voice. |
| `setup.capture.audio` | 音声 | Audio |
| `setup.capture.audio.description` | ゲームの音を取り込む機器です。映像の機器を選ぶと、同じ機器の音声を自動で選びます。 | The device the game's sound comes from. Choosing a video device chooses its audio too. |
| `setup.audio.device.game` | キャプチャーボードの音声などを選びます。 | Choose the capture board's audio, for example. |
| `setup.audio.device.none` | なし | None |
| `setup.audio.device.missing` | 接続されていないデバイス | A device not connected now |
| `setup.audio.gain` | 音量 | Volume |
| `setup.audio.mute` | ミュート | Mute |
| `setup.audio.level` | 入力レベル | Input level |
| `setup.audio.state.off` | オフ | Off |
| `setup.audio.state.starting` | 開始中 | Starting |
| `setup.audio.state.running` | 動作中 | Running |
| `setup.audio.state.not-found` | 見つかりません | Not found |
| `setup.audio.state.failed` | 使えません | Unavailable |
| `setup.audio.state.muted` | ミュート中 | Muted |
| `setup.audio.problem.not-found` | デバイスが接続されていません。接続すると自動で使い始めます。 | The device is not connected. It is used again as soon as it is connected. |
| `setup.audio.problem.in-use` | ほかのアプリがこのデバイスを専有しています。そのアプリを閉じるか、そのアプリの設定で専有をやめてください。 | Another application is using this device exclusively. Close it, or turn off exclusive use in its settings. |
| `setup.audio.problem.access-denied` | Windowsの設定で、マイクへのアクセスが許可されていません。「マイクのプライバシー設定を開く」から、デスクトップアプリのアクセスを許可してください。 | Windows does not allow access to the microphone. Open the microphone privacy settings and allow desktop apps to access it. |
| `setup.audio.problem.failed` | デバイスを開けませんでした。もう一度選び直してください。続く場合は診断画面のログを確認してください。 | The device could not be opened. Choose it again; if it keeps happening, check the log on the Diagnostics screen. |
| `setup.audio.open-privacy` | マイクのプライバシー設定を開く | Open microphone privacy settings |
| `setup.audio.monitored` | 自分で聞く | Hear it myself |
| `setup.audio.monitored.description` | オンにすると、下の「モニター」から聞こえます。配信と録画には関係しません。 | Plays on the monitor below. This does not change the stream or recordings. |
| `setup.audio.monitor` | モニター | Monitor |
| `setup.audio.monitor.device` | 出力先 | Output |
| `setup.audio.monitor.device.description` | 自分が聞く音を出す機器です。ヘッドホンをおすすめします。スピーカーだと、マイクに音が入ることがあります。 | The device you listen on. Headphones are recommended: speakers can leak into the microphone. |
| `setup.audio.monitor.gain` | 音量 | Volume |
| `setup.audio.monitor.mute` | ミュート | Mute |
| `setup.audio.delay` | 配信・録画の音声の遅延 | Stream and recording audio delay |
| `setup.audio.delay.description` | アバターの動きは、カメラから読み取るぶん声より遅れます。配信と録画の音声をこの分だけ遅らせて合わせます。自分に聞こえる音は遅れません。 | The avatar moves later than your voice, since its motion is read from the camera. Streaming and recording get the sound this much later to match. What you hear is not delayed. |
