# ライブ画面

## 1. 目的

配信中に使う画面。配信される映像そのものを見ながら、映像の配置を直す。

配信プレビュー、録画の開始・停止、入っている音、配置編集を置く。配信の開始・停止、音声、カメラの切り替え等は、それぞれの機能を作るときに加える。

## 2. 構成

```text
┌ ライブ ─────────────────────────────┐
│ ┌ 配信プレビュー（16:9） ─────────┐ │
│ │  映像（Stream Render Target）   │ │
│ │  └ 配置編集（Overlay）          │ │
│ └─────────────────────────────────┘ │
│ [● 録画中 00:12:34] [■ 録画を停止]  │
│ [🎤] マイク ▮▮▮▯  [🎤] ゲーム ▮▯▯▯  │
│ 映っている部分：[✓ 左腕] [○ 左手]… │
│ ┌ 重なり順 ───────┐ 操作の説明       │
│ │ Layer List      │                  │
│ └─────────────────┘                  │
└─────────────────────────────────────┘
```

| 部分 | 部品 | 内容 |
|---|---|---|
| 配信プレビュー | Preview（`components.md` 3.17） | `IStreamPreviewSource`（Video）。画面の幅いっぱい（最大960px）に16:9で置く |
| 配置編集 | Media Frame（3.19） | プレビューのOverlayに置く。映像は`IFrameSource`（Stage） |
| 録画 | Status Chip（3.13）とButton（3.1）。`RecordControl` | プレビューのすぐ下。`IRecordingService`（Video）。状態ごとの表示、停止の確認、保存したファイルの表示は録画詳細設計18.1。録画がないときは出さない |
| 配信 | Status Chip（3.13）とButton（3.1）。`StreamControl` | 録画の操作の下。`IStreamingService`（Streaming）。状態ごとの表示、開始と終了の確認は配信詳細設計18.1。配信がないときは出さない |
| 入っている音 | 音の元ごとのIconButton（3.2）、名前、Level Meter（3.5）。`AudioStrip` | 録画の操作の下。`IAudioService`（Audio）。ミュートのButtonは押すたびに切り替わり、ミュート中は斜線のマイク（形で示す）。メーターは-60〜0dB。音声がないときは出さない |
| 映っている部分 | 腕と手ごとのStatus Chip（3.13） | プレビューの下。トラッキングが動いている間、本人の腕と手のどれがカメラに映っているかを、本人の左右で、アバターが使うのと同じ判定で示す（`LimbStatusRow`、0.2秒ごとに更新）。腕は、肩、肘、手首が見えて画像の中にあれば「映っている」（チェック）、肘だけない場合も「映っている」、手首だけない場合は「手首が見えない」（警告）、それ以外は「映っていない」（輪郭）。手は、手のトラッキングが見つけていれば「映っている」。腕や手が休む理由が分かるようにしたいという利用者の要望（2026-10-25） |
| 重なり順 | Layer List（3.20）をCardに入れる | プレビューの下。手前の映像が上 |
| 操作の説明 | 説明文（Text 2） | 「映像はドラッグで移動、角のドラッグで大きさを変えられます。右クリックでメニューを開きます。」 |

## 3. 状態

| 状態 | 表示 |
|---|---|
| 映像がない（Videoがない） | Previewの「映像がありません」。配置編集は出さない |
| 取り込んでいる映像がない | プレビューは背景だけ。Layer Listでは各映像が「非表示」 |
| 映像を取り込み中 | 映像と枠を表示し、操作できる |
| トラッキングが止まっている、またはない | 映っている部分は出さない |
| 配信中 | 「LIVE」と経過時間と実際のビットレートと「■ 配信を終了」。開始と終了は確認の後。再接続中は警告の形と「再接続中」 |
| 録画中 | 「● 録画中」と経過時間（0.25秒ごとに更新）と「■ 録画を停止」。停止はDangerの確認の後 |

- 取り込みの開始・停止はセットアップ画面で行う。ライブ画面には置かない（Setupと配信中の操作を分ける、`ui-guidelines.md`）。

## 4. 操作

配置編集の操作は、UI基盤詳細設計9.3、9.4に従う。キーボードだけでも、Tabで枠を選び、矢印キーで動かせる。

## 5. 文字列

| Key | 日本語 | English |
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
