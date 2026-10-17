# Setup Screen

## 1. Purpose

The screen for preparing devices and settings before streaming.

This first version has the capture Cards and the app settings Card (UI foundation detailed design 7.7). Other settings are added with their features.

## 2. Layout

Cards stacked from the top:

1. Capture (game screen)
2. Capture (sub monitor)
3. App settings

## 3. Capture Card

One per capture source, showing `ICaptureDeviceSource` (Capture) directly.

| Row | Component | Content |
|---|---|---|
| State | Status Indicator (`components.md` 3.13) | The state, and an explanation when there is a problem (chapter 4) |
| Capture | Toggle | On starts, off stops. Whether it was started is saved and restored at the next start |
| Device | Dropdown | The devices found. The game screen starts with "Choose a camera", the sub monitor with "Automatic (not the main monitor)" |
| Device list | Button (Secondary) "Search again" | Lists the devices again |
| Flip left and right | Toggle | Corrects the video's orientation |
| Flip top and bottom | Toggle | Corrects the video's orientation |
| Show the mouse cursor (sub monitor only) | Toggle | On by default |
| Actual video | Description (monospace, Text 2) | Resolution and frame rate (for example `1920×1080 · 59.9 fps`); "—" while not capturing |

- Changes apply at once and are saved (Capture detailed design chapter 10).
- The Capture toggle follows the state. It stays on while waiting for a missing device (`Unavailable`).

## 4. State Display

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
| Failed | Error | Could not capture. Explanation: "Another app may be using the device." |

- A missing plugin (`PluginMissing`) is shown to users as a failure (Capture detailed design 6.2).

## 5. Strings

Keys are `setup.capture.*`. The texts are those in the tables of chapters 3 and 4 (Japanese with the same meaning).
