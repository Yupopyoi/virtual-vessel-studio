# ライブ画面

## 1. 目的

配信中に使う画面。配信される映像そのものを見ながら、映像の配置を直す。

本書は最初の版で、配信プレビューと配置編集だけを置く。配信の開始・停止、音声、カメラの切り替え等は、それぞれの機能を作るときに加える。

## 2. 構成

```text
┌ ライブ ─────────────────────────────┐
│ ┌ 配信プレビュー（16:9） ─────────┐ │
│ │  映像（Stream Render Target）   │ │
│ │  └ 配置編集（Overlay）          │ │
│ └─────────────────────────────────┘ │
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
| 映っている部分 | 腕と手ごとのStatus Chip（3.13） | プレビューの下。トラッキングが動いている間、本人の腕と手のどれがカメラに映っているかを、本人の左右で、アバターが使うのと同じ判定で示す（`LimbStatusRow`、0.2秒ごとに更新）。腕は、肩、肘、手首が見えて画像の中にあれば「映っている」（チェック）、手首だけない場合は「手首が見えない」（警告）、それ以外は「映っていない」（輪郭）。手は、手のトラッキングが見つけていれば「映っている」。腕や手が休む理由が分かるようにしたいという利用者の要望（2026-10-25） |
| 重なり順 | Layer List（3.20）をCardに入れる | プレビューの下。手前の映像が上 |
| 操作の説明 | 説明文（Text 2） | 「映像はドラッグで移動、角のドラッグで大きさを変えられます。右クリックでメニューを開きます。」 |

## 3. 状態

| 状態 | 表示 |
|---|---|
| 映像がない（Videoがない） | Previewの「映像がありません」。配置編集は出さない |
| 取り込んでいる映像がない | プレビューは背景だけ。Layer Listでは各映像が「非表示」 |
| 映像を取り込み中 | 映像と枠を表示し、操作できる |
| トラッキングが止まっている、またはない | 映っている部分は出さない |

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
| `stage.screen.game` | ゲーム画面 | Game screen |
| `stage.screen.subscreen` | サブモニター | Sub monitor |
