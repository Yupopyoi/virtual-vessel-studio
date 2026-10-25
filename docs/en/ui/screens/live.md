# Live Screen

## 1. Purpose

The screen used while streaming. The user adjusts where videos are placed while looking at the stream video itself.

This first version has only the stream preview and placement editing. Starting and stopping the stream, audio, camera switching, and so on are added with their features.

## 2. Layout

```text
┌ Live ───────────────────────────────┐
│ ┌ Stream preview (16:9) ──────────┐ │
│ │  Video (Stream Render Target)   │ │
│ │  └ Placement editing (overlay)  │ │
│ └─────────────────────────────────┘ │
│ In view: [✓ Left arm] [○ Left hand]… │
│ ┌ Stacking order ─┐ How to edit      │
│ │ Layer List      │                  │
│ └─────────────────┘                  │
└─────────────────────────────────────┘
```

| Part | Component | Content |
|---|---|---|
| Stream preview | Preview (`components.md` 3.17) | `IStreamPreviewSource` (Video), at 16:9 across the screen's width (at most 960 px) |
| Placement editing | Media Frame (3.19) | On the preview's overlay; the videos are `IFrameSource` (Stage) |
| In view | Status Chips (3.13), one per arm and hand | Under the preview, while tracking runs: which of the person's arms and hands the camera sees, by the person's own sides, as the avatar uses them (`LimbStatusRow`, refreshed every 0.2 s). An arm is seen with its shoulder, elbow, and wrist seen and in the image (check); partly without the wrist (warning, "wrist not seen"); otherwise not in view (outline). A hand is seen when the hand tracking finds it. The user asked to see why an arm or a hand rests (2026-10-25) |
| Stacking order | Layer List (3.20) in a Card | Below the preview; the video in front is at the top |
| How to edit | Description (Text 2) | "Drag a video to move it and drag its corners to resize it. Right-click for more." |

## 3. States

| State | Display |
|---|---|
| No video (no Video module) | The Preview's "No video". No placement editing |
| No video being captured | The preview shows only the background; the Layer List marks each video as hidden |
| Videos being captured | Videos and frames are shown and can be edited |
| Tracking not running, or no tracking | The arms and hands in view are not shown |

- Capturing is started and stopped on the setup screen, not on the live screen (setup and live operations are kept apart, `ui-guidelines.md`).

## 4. Operation

Placement editing follows UI foundation detailed design 9.3 and 9.4. With the keyboard alone, Tab selects a frame and the arrow keys move it.

## 5. Strings

| Key | Japanese | English |
|---|---|---|
| `live.layers` | 重なり順 | Stacking order |
| `live.hint` | 映像はドラッグで移動、角のドラッグで大きさを変えられます。右クリックでメニューを開きます。 | Drag a video to move it and drag its corners to resize it. Right-click for more. |
| `live.limbs` | 映っている部分 | In view |
| `live.limbs.left-arm.seen` | 左腕 | Left arm |
| `live.limbs.left-arm.partly` | 左腕（手首が見えない） | Left arm (wrist not seen) |
| `live.limbs.left-arm.unseen` | 左腕（映っていない） | Left arm (not in view) |
| `live.limbs.right-arm.seen` | 右腕 | Right arm |
| `live.limbs.right-arm.partly` | 右腕（手首が見えない） | Right arm (wrist not seen) |
| `live.limbs.right-arm.unseen` | 右腕（映っていない） | Right arm (not in view) |
| `live.limbs.left-hand.seen` | 左手 | Left hand |
| `live.limbs.left-hand.unseen` | 左手（映っていない） | Left hand (not in view) |
| `live.limbs.right-hand.seen` | 右手 | Right hand |
| `live.limbs.right-hand.unseen` | 右手（映っていない） | Right hand (not in view) |
| `stage.screen.game` | ゲーム画面 | Game screen |
| `stage.screen.subscreen` | サブモニター | Sub monitor |
