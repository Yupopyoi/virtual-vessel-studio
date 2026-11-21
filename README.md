# Virtual Vessel Studio

[![Latest release](https://img.shields.io/github/v/release/Yupopyoi/virtual-vessel-studio?label=release)](https://github.com/Yupopyoi/virtual-vessel-studio/releases/latest)
[![Platform: Windows x64](https://img.shields.io/badge/platform-Windows%20x64-0078D6)](#requirements)
[![Unity 6.6](https://img.shields.io/badge/Unity-6.6-000000?logo=unity)](unity/VirtualVesselStudio/ProjectSettings/ProjectVersion.txt)
[![License: Apache-2.0](https://img.shields.io/badge/license-Apache--2.0-blue)](LICENSE)
[![Repository checks](https://github.com/Yupopyoi/virtual-vessel-studio/actions/workflows/repository-checks.yml/badge.svg?branch=main)](https://github.com/Yupopyoi/virtual-vessel-studio/actions/workflows/repository-checks.yml)

**English** | [日本語](README.ja.md)

Virtual Vessel Studio is a Windows application for VTuber streaming, setup, and content creation. It brings 3D avatars, tracking, video capture, and stages together in one app, with no separate tools to set up.

> [!NOTE]
> **v0.3.0 is an early preview.** You can move a VRM avatar with a webcam, arrange captured videos on the stream screen, mix the audio, and stream to YouTube or record. You can make a voice of your own in Voice Lab and change your voice in real time while streaming. From v0.3.0, you can play BGM and sound effects, take in a game running on this PC from its window, and place the avatar in a frame (see [Known limitations](#known-limitations)).

## Contents

- [What you can do in v0.3.0](#what-you-can-do-in-v030)
- [Requirements](#requirements)
- [Install](#install)
- [Sample avatar: Otoha-chan](#sample-avatar-otoha-chan)
- [Getting started](#getting-started)
- [Tips for tracking](#tips-for-tracking)
- [Known limitations](#known-limitations)
- [Troubleshooting](#troubleshooting)
- [Uninstall](#uninstall)
- [For developers](#for-developers)
- [License](#license)

## What you can do in v0.3.0

- **Sample avatar:** Otoha-chan (音葉ちゃん) comes with the app, so you can try it without a model of your own.
- **Avatar:** register a VRM model (VRM 1.0, or VRM 0.x converted when loaded), and check the terms of use its author set.
- **Tracking with a webcam:** the face (expressions, eyes, and mouth), the upper body, arms, hands, and fingers move the avatar. You can tune the face, how far the fingers bend, and how fast or steady the movement is.
- **Video capture:**
  - a game screen through a capture board;
  - a game running on this PC, from its window, with only that game's sound (from v0.3.0);
  - a second monitor.
- **Live screen:**
  - see the stream video;
  - move and resize the videos on it;
  - place the avatar in a frame, such as on the right with the game on the left (from v0.3.0);
  - stop a video's capture with the Del key or its menu;
  - see which of your arms and hands the camera sees.
- **Audio:** mix your microphone and the game's audio, hear the game yourself, and delay the audio to match the avatar.
- **BGM and sound effects** (from v0.3.0): play BGM and sound effects on the stream and in recordings, from WAV or MP3 files. Seven BGM pieces and a set of sound effects come with the app, free to use in streams and videos (CC0). Sound effects can be given shortcut keys that work while you play a game in another app.
- **Voice conversion** (from v0.2.0): change your microphone's voice in real time into a voice made in Voice Lab, for the stream and recordings. The pitch matches your own automatically, and you can adjust the voice's finish (body, clarity, brightness, air). While it gets ready, or when it cannot convert, your microphone is kept out so your own voice does not go out.
- **Voice Lab** (from v0.2.0): make voice candidates from words or a mood, shape the timbre, build a voice model, and register it. You can try a voice with your own voice before registering it. The first time, the app prepares the environment for making voices on this PC (agreeing to its terms is required).
- **Streaming:** stream to YouTube, or another RTMP(S) server, with AV1 or H.264. The connection comes back by itself if it drops.
- **Recording:** record the stream video and audio to a file, while streaming or on its own.
- **Japanese and English UI**, dark and light themes, and a UI scale setting.

## Requirements

- Windows (64-bit), with a GPU that supports DirectX 11
- A webcam, for tracking
- A VRM model (`.vrm`) that its author allows you to use as an avatar, or the included sample avatar
- For streaming: an internet connection with upload speed to spare (more than 6 Mbit/s for the default bitrate)
- Optional: a microphone; a capture board, for a game screen; a second monitor
- Recommended for streaming and recording: a GPU with a hardware video encoder. AV1 needs one that encodes AV1.
- Voice conversion: runs on the GPU; checked on an RTX 4070.
- Making voices in Voice Lab:
  - an NVIDIA GPU (GeForce GTX 900 series or later) with driver 528.33 or later;
  - about 13 GB where the environment goes (up to about 30 GB free for a while as it is built);
  - an internet connection (about 30 to 60 minutes to build the environment, and about 20 minutes per voice; both on an RTX 4070).

## Install

1. Download `VirtualVesselStudio-vX.Y.Z-win-x64.zip` from the [latest release](https://github.com/Yupopyoi/virtual-vessel-studio/releases/latest).
2. (Optional) Check the download against `SHA256SUMS.txt` on the same page:

   ```powershell
   Get-FileHash .\VirtualVesselStudio-v0.3.0-win-x64.zip -Algorithm SHA256
   ```

3. Extract the ZIP to a folder you can write to, such as `Documents\VirtualVesselStudio`. Keep all files together.
4. Run `VirtualVesselStudio.exe`.

> [!IMPORTANT]
> The app is not code-signed yet ([#81](https://github.com/Yupopyoi/virtual-vessel-studio/issues/81)). On the first run, Windows may show "Windows protected your PC". Choose **More info**, then **Run anyway**. Only do this with a ZIP downloaded from this repository's Releases page.

## Sample avatar: Otoha-chan

Otoha-chan (音葉ちゃん, `otoha-chan.vrm`) is a sample VRM avatar made for Virtual Vessel Studio. Use her to try tracking and the live screen before you prepare your own model.

- **Where:** in the `Samples` folder of the app folder (`Samples\otoha-chan.vrm`), from v0.1.1.

  The v0.1.0 package does not include her. Download [`otoha-chan.vrm`](https://github.com/Yupopyoi/virtual-vessel-studio/raw/main/tests/samples/otoha-chan.vrm) from this repository instead.
- **How to use:** register `Samples\otoha-chan.vrm` as in [1. Register your avatar](#1-register-your-avatar), then choose **otoha-chan** in **Avatar to use**.
- **Terms of use:** VRM Public License 1.0, with these terms set in the model:
  - anyone may use her as an avatar, including for commercial use by corporations;
  - you may redistribute and modify her, and credit is not required;
  - excessively violent or sexual use, political or religious use, and antisocial or hateful use are not allowed.

  The full terms are in `licenses\otoha-chan-LICENSE.md` and in **Settings** → **Credits and licenses**.

## Getting started

The app has two main screens: **Settings**, for preparing devices before you stream, and **Live**, for watching and arranging the stream video. The Settings screen has five pages, chosen with the tabs at the top: **Video**, **Audio**, **Avatar**, **Streaming and recording**, and **App**. Each setting is in a card that opens when you click its title.

### 1. Register your avatar

1. Open the **Avatar** card on the **Avatar** page of **Settings**.
2. Click **Register an avatar…** and choose your `.vrm` file, or the sample `Samples\otoha-chan.vrm` in the app folder.
3. Check the model's terms of use, then click **Register**.

   The app keeps its own copy of the model, so your original file is not changed.
4. Choose it in **Avatar to use**.

### 2. Start tracking

1. Open the **Tracking** card on the **Avatar** page of **Settings**.
2. Choose your webcam in **Camera**.
3. Turn on **Track**.

   The camera image shows the points it tracks, and the line under it ("Face ✓   Body ✓   Left hand –   …") shows which parts it sees.
4. Adjust as you like:
   - **Mirror**: on by default; the avatar moves like a mirror image of you.
   - **Body movement**: 3D, or 2D for steadier arms. 2D uses only the movement seen from the front.
   - **Movement speed**: higher follows faster; lower is steadier.
5. Open **Face and finger tuning** to adjust how easily the eyes close, winks, brows, mouth, and fingers react, while watching the avatar.

Settings are saved automatically. Tracking starts again the next time you open the app.

### 3. Capture videos (optional)

1. Open **Capture (game screen)** or **Capture (sub monitor)** on the **Video** page of **Settings**.
2. Choose the device and turn on **Capture**. Use the flip switches if the video is mirrored or upside down.
3. For a game on this PC, choose **A window on this PC** in **Input** of **Capture (game screen)**, then the game's window. Only that game's sound comes with it. A minimized window cannot be taken in.

### 4. Arrange the stream screen

1. Open **Live**.
2. Drag a video to move it, or drag its corners to resize it. Right-click a video for more options.
3. Change the stacking order in **Stacking order**.
   The **Avatar** frame moves and resizes the same way, to set where the avatar appears and how much of it is shown. It is always in front.
4. **In view** shows whether the camera sees each arm and hand. A part it cannot see rests in its default pose.

### 5. Set up the audio (optional)

1. Open the **Audio** page of **Settings**.
2. Choose your microphone, and set the level of each source in the mixer.
3. The game's audio comes from the capture board. Its row is under **Capture (game screen)** on the **Video** page, which also picks a matching device for you.
4. BGM and sound effects are in the **BGM** and **Sound effects** cards on the **Audio** page. **Add a file…** adds your own WAV or MP3 files. On **Live**, choose and play BGM, and press sound effects, in **BGM and sound effects**.
5. To play a sound effect from another app, click its **Shortcut** button and press keys with Ctrl, Alt, or Shift.

### 6. Make a voice and use it (optional)

1. Open **Voice Lab** on the left. The first time, after a check of the PC and agreeing to the terms, it prepares the environment for making voices.
2. On the **Make a voice** tab, go through STEP 1 to 5.
   - STEP 1: pick words or a mood, make voice candidates, and pick one as the base.
   - STEP 2: shape the timbre with sliders such as pitch and age.
   - STEP 3: make the voice model. You can use the other screens meanwhile.
   - STEP 4: try it on your own voice, from a recorded file or the microphone. The voice's finish is adjusted here too.
   - STEP 5: name it and register it.
3. On the **Audio** page of **Settings**, turn on **Convert my voice** under **Voice conversion** and choose the voice.

The **Made voices** tab lists the voices and how they were made. Voice Lab cannot be used while streaming or recording.

### 7. Stream or record

1. Open the **Streaming and recording** page of **Settings**.
2. To stream, click **Register…** in **Streaming** and paste the stream key from YouTube Studio. The key is kept in Windows Credential Manager, and is not shown again.
3. To record, choose a **Folder** in **Recording** if you want to change it.
4. On **Live**, click **● Go live** or **● Record**.

Depending on your YouTube settings, a stream may be public at once.

## Tips for tracking

- Sit so that your face and upper body, including both elbows and hands, are in the camera image.
- Use even, bright lighting, and avoid a strong light behind you.
- Close other apps that use the same webcam.
- If the avatar shakes, lower **Movement speed**, or try **Body movement: 2D**.
- Put the camera in front of you, such as on top of the monitor. A camera to one side sees too little of your body, and the avatar turns or leans.
- To lower the load, turn off the **Parts tracked** you do not use, or choose **Body detection: Light** (from v0.1.2).

## Known limitations

| Limitation | Issue |
| --- | --- |
| The avatar frame does not cut off the avatar: hands and other parts outside it are still drawn | — |
| The converted voice arrives about 0.4 s after your own. What you hear yourself cannot be chosen yet | — |
| Making voices in Voice Lab needs an NVIDIA GPU. Voice conversion is not checked on GPUs other than an RTX 4070 | — |
| Voice Lab's terms are a pre-release version (0.1) and are likely to change | — |
| Finger strength is one setting for each person; it is not kept per model | [#67](https://github.com/Yupopyoi/virtual-vessel-studio/issues/67) |
| The app is not code-signed, and it does not check for updates | [#81](https://github.com/Yupopyoi/virtual-vessel-studio/issues/81) |

See all [open issues](https://github.com/Yupopyoi/virtual-vessel-studio/issues).

## Troubleshooting

- **Tracking says it cannot open the camera:** another app may be using it. Close it, or choose another camera.
- **"No person in view":** sit in front of the camera with your face and upper body in view.
- **An avatar cannot be loaded:** the model's humanoid bones may be incomplete, or the file may be damaged. Check that it is a valid VRM file.
- **The stream stutters, or says the connection cannot keep up:** lower **Video bitrate** in **Streaming**. It needs to fit well inside your upload speed.
- **Reporting a problem:** open a [bug report](https://github.com/Yupopyoi/virtual-vessel-studio/issues/new/choose). Include:
  - the version;
  - what you did and what happened;
  - if you can, the latest log file from `%LOCALAPPDATA%\VirtualVesselStudio\Data\Logs`.

  Do not post stream keys or other private information.

## Uninstall

1. Delete the folder you extracted the app to.
2. To delete your settings, registered avatars, BGM and sound effects, made voices, and logs as well, delete `%LOCALAPPDATA%\VirtualVesselStudio`. Your original `.vrm` files are not inside it.
3. If you prepared Voice Lab's environment, delete **Voice making environment (Python)** on Voice Lab's **Made voices** tab first. After the app is gone, delete `VirtualVesselStudio` inside the folder you chose for the environment.

## For developers

```text
virtual-vessel-studio/
├─ docs/       Documentation (Japanese: docs/ja, English: docs/en)
├─ unity/      Unity application (unity/VirtualVesselStudio)
├─ native/     Native Windows code and plugins
├─ services/   Python services, adapters, and launchers
├─ tools/      Development, build, release, and maintenance tooling
└─ tests/      Tests and samples that do not belong to one component
```

- **Unity 6.6:** open `unity/VirtualVesselStudio` from Unity Hub. The exact version is in [`ProjectVersion.txt`](unity/VirtualVesselStudio/ProjectSettings/ProjectVersion.txt).
- **Git LFS:** clone with Git LFS installed, so you get the sample model in `tests/samples/`.
- **Documentation:**
  - [system design](docs/en/architecture/system-design.md);
  - [development workflow](docs/en/development/development-workflow.md), with the branch model and the release steps;
  - detailed designs in [`docs/en/detailed-design/`](docs/en/detailed-design/).
- **Before changing anything:** read [`CLAUDE.md`](CLAUDE.md), the project rules for contributors and development agents.

## License

Virtual Vessel Studio is licensed under the [Apache License 2.0](LICENSE). See [NOTICE](NOTICE) for attribution.

The name "Virtual Vessel Studio" and its logos are not covered by the license. See [TRADEMARKS.md](TRADEMARKS.md).

Third-party components, models, and runtimes distributed with the application are subject to their own licenses; the app shows them in **Settings** → **Credits and licenses**.
