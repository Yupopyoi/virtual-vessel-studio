# Virtual Vessel Studio

[![Latest release](https://img.shields.io/github/v/release/Yupopyoi/virtual-vessel-studio?label=release)](https://github.com/Yupopyoi/virtual-vessel-studio/releases/latest)
[![Platform: Windows x64](https://img.shields.io/badge/platform-Windows%20x64-0078D6)](#requirements)
[![Unity 6.6](https://img.shields.io/badge/Unity-6.6-000000?logo=unity)](unity/VirtualVesselStudio/ProjectSettings/ProjectVersion.txt)
[![License: Apache-2.0](https://img.shields.io/badge/license-Apache--2.0-blue)](LICENSE)
[![Repository checks](https://github.com/Yupopyoi/virtual-vessel-studio/actions/workflows/repository-checks.yml/badge.svg?branch=main)](https://github.com/Yupopyoi/virtual-vessel-studio/actions/workflows/repository-checks.yml)

**English** | [日本語](README.ja.md)

Virtual Vessel Studio is a Windows application for VTuber streaming, setup, and content creation. It brings 3D avatars, tracking, video capture, and stages together in one app, with no separate tools to set up.

> [!NOTE]
> **v0.1.0 is an early preview.** You can move a VRM avatar with a webcam and arrange captured videos on the stream screen. Streaming, recording, audio, and voice conversion are not included yet (see [Known limitations](#known-limitations)).

## Contents

- [What you can do in v0.1.0](#what-you-can-do-in-v010)
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

## What you can do in v0.1.0

- **Sample avatar:** Otoha-chan (音葉ちゃん) comes with the app, so you can try it without a model of your own.
- **Avatar:** register a VRM model (VRM 1.0, or VRM 0.x converted when loaded), and check the terms of use its author set.
- **Tracking with a webcam:** the face (expressions, eyes, and mouth), the upper body, arms, hands, and fingers move the avatar. You can tune the face and how fast or steady the movement is.
- **Video capture:**
  - a game screen through a capture board;
  - a second monitor.
- **Live screen:**
  - see the stream video;
  - move and resize the videos on it;
  - see which of your arms and hands the camera sees.
- **Japanese and English UI**, dark and light themes, and a UI scale setting.

## Requirements

- Windows (64-bit), with a GPU that supports DirectX 11
- A webcam, for tracking
- A VRM model (`.vrm`) that its author allows you to use as an avatar, or the included sample avatar
- Optional: a capture board, for a game screen; a second monitor

## Install

1. Download `VirtualVesselStudio-vX.Y.Z-win-x64.zip` from the [latest release](https://github.com/Yupopyoi/virtual-vessel-studio/releases/latest).
2. (Optional) Check the download against `SHA256SUMS.txt` on the same page:

   ```powershell
   Get-FileHash .\VirtualVesselStudio-v0.1.0-win-x64.zip -Algorithm SHA256
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

The app has two main screens: **Settings**, for preparing devices before you stream, and **Live**, for watching and arranging the stream video. On the Settings screen, each setting is in a card that opens when you click its title.

### 1. Register your avatar

1. Open **Settings** → **Avatar**.
2. Click **Register an avatar…** and choose your `.vrm` file, or the sample `Samples\otoha-chan.vrm` in the app folder.
3. Check the model's terms of use, then click **Register**.

   The app keeps its own copy of the model, so your original file is not changed.
4. Choose it in **Avatar to use**.

### 2. Start tracking

1. Open **Settings** → **Tracking**.
2. Choose your webcam in **Camera**.
3. Turn on **Track**.

   The camera image shows the points it tracks, and the line under it ("Face ✓   Body ✓   Left hand –   …") shows which parts it sees.
4. Adjust as you like:
   - **Mirror**: on by default; the avatar moves like a mirror image of you.
   - **Body movement**: 3D, or 2D for steadier arms. 2D uses only the movement seen from the front.
   - **Movement speed**: higher follows faster; lower is steadier.
5. Open **Face tuning** to adjust how easily the eyes close, winks, brows, and mouth react, while watching the avatar.

Settings are saved automatically. Tracking starts again the next time you open the app.

### 3. Capture videos (optional)

1. Open **Settings** → **Capture (game screen)** or **Capture (sub monitor)**.
2. Choose the device and turn on **Capture**. Use the flip switches if the video is mirrored or upside down.

### 4. Arrange the stream screen

1. Open **Live**.
2. Drag a video to move it, or drag its corners to resize it. Right-click a video for more options.
3. Change the stacking order in **Stacking order**.
4. **In view** shows whether the camera sees each arm and hand. A part it cannot see rests in its default pose.

## Tips for tracking

- Sit so that your face and upper body, including both elbows and hands, are in the camera image.
- Use even, bright lighting, and avoid a strong light behind you.
- Close other apps that use the same webcam.
- If the avatar shakes, lower **Movement speed**, or try **Body movement: 2D**.
- If the camera is to one side and the avatar looks sideways, face your usual front and click **Face the front** (from v0.1.2).
- To lower the load, turn off the **Parts tracked** you do not use, or choose **Body detection: Light** (from v0.1.2).

## Known limitations

| Limitation | Issue |
| --- | --- |
| Streaming, recording, audio, and real-time voice conversion are not included yet | — |
| Text fields accept English letters, numbers, and symbols only, because of a Japanese input (IME) problem | [#74](https://github.com/Yupopyoi/virtual-vessel-studio/issues/74) |
| Finger movement cannot be tuned per person or per model | [#67](https://github.com/Yupopyoi/virtual-vessel-studio/issues/67) |
| The app is not code-signed, and it does not check for updates | [#81](https://github.com/Yupopyoi/virtual-vessel-studio/issues/81) |

See all [open issues](https://github.com/Yupopyoi/virtual-vessel-studio/issues).

## Troubleshooting

- **Tracking says it cannot open the camera:** another app may be using it. Close it, or choose another camera.
- **"No person in view":** sit in front of the camera with your face and upper body in view.
- **An avatar cannot be loaded:** the model's humanoid bones may be incomplete, or the file may be damaged. Check that it is a valid VRM file.
- **Reporting a problem:** open a [bug report](https://github.com/Yupopyoi/virtual-vessel-studio/issues/new/choose). Include:
  - the version;
  - what you did and what happened;
  - if you can, the latest log file from `%LOCALAPPDATA%\VirtualVesselStudio\Data\Logs`.

  Do not post stream keys or other private information.

## Uninstall

1. Delete the folder you extracted the app to.
2. To delete your settings, registered avatars, and logs as well, delete `%LOCALAPPDATA%\VirtualVesselStudio`. Your original `.vrm` files are not inside it.

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
