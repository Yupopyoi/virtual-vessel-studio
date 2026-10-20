# Setup Screen

## 1. Purpose

The screen for preparing devices and settings before streaming.

It has the capture Cards, the tracking Card, the avatar Card, and the app settings Card (UI foundation detailed design 7.7). Other settings are added with their features.

## 2. Layout

Cards stacked from the top:

1. Capture (game screen)
2. Capture (sub monitor)
3. Tracking (chapter 4)
4. Avatar (chapter 5)
5. App settings

- The tracking camera has no capture Card; it is chosen in the tracking Card.

## 3. Capture Card

One per capture source, showing `ICaptureDeviceSource` (Capture) directly.

| Row | Component | Content |
|---|---|---|
| State | Status Indicator (`components.md` 3.13) | The state, and an explanation when there is a problem (chapter 6) |
| Video | Image (256×144, fitted to 16:9) | The captured video, small, to check that the chosen device is the right one; "No video" when there is none |
| Capture | Toggle | On starts, off stops. Whether it was started is saved and restored at the next start |
| Device | Dropdown | The devices found. The game screen starts with "Choose a camera", the sub monitor with "Automatic (not the main monitor)" |
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

Keys are `setup.capture.*` for the capture Cards, `setup.tracking.*` for the tracking Card, and `setup.avatar.*` for the avatar Card. The texts are those in the tables of chapters 3–6 (Japanese with the same meaning).
