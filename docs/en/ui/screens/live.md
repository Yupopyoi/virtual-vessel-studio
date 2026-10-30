# Live Screen

## 1. Purpose

The screen used while streaming. The user adjusts where videos are placed while looking at the stream video itself.

It has the stream preview, starting and stopping a recording, the sound going in, and placement editing. Starting and stopping the stream, audio, camera switching, and so on are added with their features.

## 2. Layout

```text
┌ Live ───────────────────────────────┐
│ ┌ Stream preview (16:9) ──────────┐ │
│ │  Video (Stream Render Target)   │ │
│ │  └ Placement editing (overlay)  │ │
│ └─────────────────────────────────┘ │
│ [● REC 00:12:34] [■ Stop recording] │
│ [🎤] Mic ▮▮▮▯  [🎤] Game ▮▯▯▯       │
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
| Streaming | Status Chip (3.13) and Buttons (3.1); `StreamControl` | Under the record control; `IStreamingService` (Streaming). What each state shows and the confirmations to start and end: streaming detailed design 18.1. Left out without streaming |
| Recording | Status Chip (3.13) and Buttons (3.1); `RecordControl` | Right under the preview; `IRecordingService` (Video). What each state shows, the confirmation to stop, and the saved file: recording detailed design 18.1. Left out without recording |
| Sound going in | Per source an IconButton (3.2), its name, and a Level Meter (3.5); `AudioStrip` | Under the record control; `IAudioService` (Audio). The mute button toggles, and muted shows the crossed-out microphone (by shape). The meter is -60 to 0 dB. Left out without audio |
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
| Streaming | "LIVE" with the elapsed time, the actual bitrate, and "■ End stream"; starting and ending come after a confirmation. While reconnecting, the warning shape and "Reconnecting" |
| Recording | "● REC" with the elapsed time (redrawn every 0.25 s) and "■ Stop recording"; stopping comes after a Danger confirmation |

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
| `live.stream.start` | ● 配信を開始 | ● Go live |
| `live.stream.connecting` | 接続しています… | Connecting… |
| `live.stream.ending` | 配信を終了しています… | Ending the stream… |
| `live.stream.stop` | ■ 配信を終了 | ■ End stream |
| `live.stream.busy` | 配信の開始か終了の途中です。終わるまでお待ちください。 | The stream is starting or ending. Please wait until it finishes. |
| `live.stream.no-key` | セットアップ画面の「配信」で、ストリームキーを登録してください。 | Register the stream key under Streaming on the setup screen. |
| `live.stream.live` | LIVE | LIVE |
| `live.stream.reconnecting` | 再接続中 | Reconnecting |
| `live.stream.failed` | 配信の問題 | Stream problem |
| `live.stream.confirm-start.title` | 配信を開始しますか？ | Go live? |
| `live.stream.confirm-start.body` | YouTubeの設定によっては、すぐに公開されます。 | Depending on your YouTube settings, it may be public at once. |
| `live.stream.confirm-start.start` | 配信を開始 | Go live |
| `live.stream.confirm-stop.title` | 配信を終了しますか？ | End the stream? |
| `live.stream.confirm-stop.body` | 配信が止まります。録画は続きます。 | The stream stops. Recording goes on. |
| `live.stream.confirm-stop.stop` | 配信を終了 | End stream |
| `live.stream.bitrate` | 実際のビットレート：{rate} Mbps | Actual bitrate: {rate} Mbps |
| `live.stream.slow` | 回線が追いついていません。設定のビットレートを下げてください。 | The connection cannot keep up. Lower the bitrate in the settings. |
| `live.stream.problem.no-key` | ストリームキーが登録されていません。セットアップ画面の「配信」で登録してください。 | No stream key is registered. Register it under Streaming on the setup screen. |
| `live.stream.problem.unreachable` | サーバーに接続できません。インターネット接続とURLを確認してください。 | The server cannot be reached. Check the internet connection and the URL. |
| `live.stream.problem.rejected` | 配信が拒否されました。YouTube Studioでストリームキーを確認してください。 | The stream was refused. Check the stream key in YouTube Studio. |
| `live.stream.problem.lost` | 接続が切れました。自動で再接続しています。録画は続きます。 | The connection was lost. Reconnecting automatically. Recording goes on. |
| `live.stream.problem.encoder` | エンコーダーを使えません。もう一度試すか、診断画面のログを確認してください。 | The encoder cannot be used. Try again, or check the log on the Diagnostics screen. |
| `live.stream.problem.audio` | 音声なしで配信しています。 | Streaming without audio. |
| `live.record.start` | ● 録画 | ● Record |
| `live.record.starting` | 録画を開始しています… | Starting recording… |
| `live.record.finishing` | ファイルを保存しています… | Saving the file… |
| `live.record.stop` | ■ 録画を停止 | ■ Stop recording |
| `live.record.busy` | 録画の開始か停止の途中です。終わるまでお待ちください。 | Recording is starting or stopping. Please wait until it finishes. |
| `live.record.recording` | 録画中 | REC |
| `live.record.failed` | 録画の問題 | Recording problem |
| `live.record.confirm.title` | 録画を停止しますか？ | Stop recording? |
| `live.record.confirm.body` | ここまでのファイルは保存されます。 | The file so far is saved. |
| `live.record.confirm.stop` | 録画を停止 | Stop recording |
| `live.record.saved` | 保存しました: {name} | Saved: {name} |
| `live.record.open-folder` | フォルダーを開く | Open folder |
| `live.record.problem.folder` | 録画フォルダーに書き込めません。設定画面の「録画」で別のフォルダーを選んでください。 | The recording folder cannot be written. Choose another folder under Recording on the setup screen. |
| `live.record.problem.disk` | 空き容量が足りないため録画できません（または途中で止めました）。不要なファイルを消すか、設定画面の「録画」で別のドライブのフォルダーを選んでください。 | There is not enough free space to record (or the recording was stopped). Delete files you do not need, or choose a folder on another drive under Recording on the setup screen. |
| `live.record.problem.encoder` | 動画を書き出せませんでした。もう一度録画してください。続く場合は診断画面のログを確認してください。 | The video could not be written. Record again; if it keeps happening, check the log on the Diagnostics screen. |
| `live.record.problem.audio` | 音声なしで録画しています。 | Recording without audio. |
| `live.audio.microphone` | マイク | Mic |
| `live.audio.game` | ゲーム | Game |
| `live.audio.mute` | ミュート | Mute |
| `live.audio.unmute` | ミュートを解除 | Unmute |
| `stage.screen.game` | ゲーム画面 | Game screen |
| `stage.screen.subscreen` | サブモニター | Sub monitor |
