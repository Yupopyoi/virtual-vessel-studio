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
│ ┌ Stacking order ─┐ How to edit      │
│ │ Layer List      │                  │
│ └─────────────────┘                  │
└─────────────────────────────────────┘
```

| Part | Component | Content |
|---|---|---|
| Stream preview | Preview (`components.md` 3.17) | `IStreamPreviewSource` (Video), at 16:9 across the screen's width (at most 960 px) |
| Placement editing | Media Frame (3.19) | On the preview's overlay; the videos are `IFrameSource` (Stage) |
| Stacking order | Layer List (3.20) in a Card | Below the preview; the video in front is at the top |
| How to edit | Description (Text 2) | "Drag a video to move it and drag its corners to resize it. Right-click for more." |

## 3. States

| State | Display |
|---|---|
| No video (no Video module) | The Preview's "No video". No placement editing |
| No video being captured | The preview shows only the background; the Layer List marks each video as hidden |
| Videos being captured | Videos and frames are shown and can be edited |

- Capturing is started and stopped on the setup screen, not on the live screen (setup and live operations are kept apart, `ui-guidelines.md`).

## 4. Operation

Placement editing follows UI foundation detailed design 9.3 and 9.4. With the keyboard alone, Tab selects a frame and the arrow keys move it.

## 5. Strings

| Key | Japanese | English |
|---|---|---|
| `live.layers` | 重なり順 | Stacking order |
| `live.hint` | 映像はドラッグで移動、角のドラッグで大きさを変えられます。右クリックでメニューを開きます。 | Drag a video to move it and drag its corners to resize it. Right-click for more. |
| `stage.screen.game` | ゲーム画面 | Game screen |
| `stage.screen.subscreen` | サブモニター | Sub monitor |
