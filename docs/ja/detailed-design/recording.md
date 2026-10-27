# エンコードと録画 詳細設計

## 1. 目的

配信の映像と音声をエンコードし、動画ファイルに録画する（方式設計10.7〜10.9、10.13）。配信（10.10〜10.12）は、同じエンコードの上に、別の出力先として後で加える。

- 録画を配信より先に作る（利用者の決定、2026-10-27）。ネットワークもストリームキーも要らず、手元で確かめられるためである。
- 既定は、1920×1080・30fps、H.264、音声はAAC、MP4とする（利用者の決定）。
- 音声は、Audio Moduleの配信用の音声（Audio詳細設計）とする：マイクとゲームの音声。
- 試作（2026-10-27、`spike/encoder`ブランチ）で、アプリの中でのエンコードが間に合うことを確かめた。開発PC（Core i7-13700F、RTX 4070）で、2Frameに1回読み出し、Media Foundation経由のハードウェアのエンコーダーでエンコードしても、Frameの時間は変わらず（60fpsで16.7ms）、メインスレッドの負荷は1Frame 0.35〜0.48ms、GPUのエンコーダーの使用率は約5%、CPU（24スレッド）は1.4%増え、Frameの取りこぼしはなかった。

この設計に含めないもの：配信（RTMPS）、外部への映像出力（Spout）、ほかのコーデック。

## 2. 責務

| 責務 | 担当 |
|---|---|
| 配信の映像を描く（Stream Render Target） | Video（Video詳細設計） |
| 配信の映像を読み出し、Frameごとに時刻を付ける | Video（`StreamFrameReader`） |
| 映像（H.264）と音声（AAC）のエンコード、MP4への多重化 | Video、ネイティブの`VirtualVessel.Encode.Native` |
| 録画の開始・停止、ファイル名、状態 | Video（`RecordingService`） |
| 配信用の音声（取り込み、混ぜる） | Audio（Audio詳細設計） |
| 録画のボタン、ライブ画面の録画の状態、録画の設定 | UI |

## 3. Moduleの境界

- VideoはAudioのClassを参照しない。Audioは配信用の音声を、ネイティブの読み出し口（`vv_audio_stream_tap`、Audio詳細設計5.2）として渡す。Videoのマネージドのコードはそれをエンコーダーへ渡し、エンコーダーは自分のスレッドで音声を読む。ファイルに届くまで、音声はマネージドのコードを通らない。
- Captureは参照しない。取り込んだ映像は、描いた配信の映像を通してだけファイルに届く（方式設計10.2）。
- エンコーダーのMedia Foundationの型は、ネイティブのPluginの中だけにとどめる。マネージドのコードからは`IRecordingService`と素朴な値だけが見える。

## 4. 主なClassとInterface

| Class | 種類 | 役割 |
|---|---|---|
| `IRecordingService` | Interface | UIが使う：状態、経過時間、開始、停止、最後のファイル（5章） |
| `RecordingService` | Class（Video） | Application Service：録画の開始と停止、フォルダーと空き容量の確認、ファイル名 |
| `StreamFrameReader` | Class（Video） | Stream Render Targetを出力のFrame Rateで読み出し（`AsyncGPUReadback`）、Frameごとに共通の時計の時刻を付ける |
| `NativeEncoder` | Class（Video） | `VirtualVessel.Encode.Native`を包む（`IDisposable`のHandle） |
| `RecordingSettings` | Class | Streaming Profileのエンコードの値と、録画のフォルダー（10章） |
| `RecordingNames` | Static Class | ファイル名と空き容量の確認。純粋（8.2） |
| `VirtualVessel.Encode.Native` | ネイティブのDLL | Media FoundationのSink WriterでのH.264とAAC、MP4 |

## 5. 公開API

```csharp
public interface IRecordingService
{
    IReadOnlyObservable<RecordingState> State { get; }    // Idle、Starting、Recording、Stopping、Failed
    IReadOnlyObservable<RecordingProblem> Problem { get; } // None、FolderUnavailable、DiskFull、EncoderFailed、AudioUnavailable
    TimeSpan Elapsed { get; }                              // 録画中
    string LastFile { get; }                               // 最後に仕上げたファイル（「フォルダーを開く」用）
    void Start();
    void Stop();
}
```

### 5.1 ネイティブのAPI（`vv_encode.h`）

```c
int32_t vv_encode_open(const vv_encode_config* config, vv_encoder** encoder);  // パス、大きさ、fps、ビットレート、音声の読み出し口（なしも可）
int32_t vv_encode_push_video(vv_encoder*, const uint8_t* bgra, int32_t length, int64_t qpc);
int32_t vv_encode_get_stats(vv_encoder*, vv_encode_stats* stats);
int32_t vv_encode_close(vv_encoder*);                                          // 残りを書き、ファイルを仕上げる
```

- 映像のFrameは、試作と同じく4つの受け皿に写し、エンコーダーのスレッドでエンコードする。受け皿が空いていないFrameは捨てて数える。
- 音声は同じスレッドで、時刻に沿って読み出し口から取り出す。そのため、音声と映像は時刻の順に書かれる。

## 6. Data Model

- `RecordingState`：`Idle`、`Starting`、`Recording`、`Stopping`、`Failed`。
- `RecordingProblem`：`None`、`FolderUnavailable`（フォルダーを作れない・書けない）、`DiskFull`（空きが予備を下回る、8.2）、`EncoderFailed`（エンコーダーを開けない、書き込み中に失敗した）、`AudioUnavailable`（音声なしで録画を続ける）。
- ファイル：`<フォルダー>/VirtualVesselStudio_2026-10-27_22-30-05.mp4`（現地時刻）、H.264 High、AAC-LC 48kHzステレオ。

## 7. 状態とライフサイクル

```text
Idle ──Start──▶ Starting ──開いた──▶ Recording ──Stop──▶ Stopping ──仕上げた──▶ Idle
                   │                    │
                   └──失敗──▶ Failed ◀──┘（エンコーダーのエラー、空き容量の不足）
```

- `Start`は、フォルダーと空き容量を確かめ、エンコーダーを開き、Frameの読み出しを始める。`Stop`は、読み出しを止め、エンコーダーの残りを書き、ファイルを仕上げる。最大1秒ほどかかる。
- どの画面を表示していても、トラッキング、取り込み、アバターが変わっても、録画は続く。
- 終了時に録画中なら、停止して仕上げる。ファイルは再生できる状態になる。
- `Failed`は、次の`Start`まで問題を保つ。それまでに書いた部分は仕上げて残す。

## 8. 主な流れ

### 8.1 1つのFrame

1. VideoがStream Render Targetを描く（今と同じく毎Frame）。
2. 出力のFrame Rate（60fpsの描画から30fps：2Frameに1回）で、`StreamFrameReader`が読み出しを依頼し、そのFrameの共通の時計の時刻（QPC）を記録する。待っている読み出しは2つまでとし、それを超えるFrameは飛ばして数える。
3. 画素が届いたら（1Frame後）、その時刻とともにエンコーダーへ渡す。
4. エンコーダーのスレッドは、Frameを変換してエンコードし、その時刻までの音声を読み出し口から取り出して、両方を書く。

### 8.2 開始

- ファイル名は現地時刻から作る（`RecordingNames.FileName`、純粋）。同じ名前があれば`_2`、`_3`を付ける。
- フォルダーに2GB以上の空きが必要である（`RecordingNames.HasRoom`、純粋）。既定のビットレートで約45分にあたる。録画中は10秒ごとに空きを確かめ、500MBを下回ったら`DiskFull`で停止する。

## 9. Thread

- メインスレッド：読み出しの依頼と、画素を渡すこと（受け皿への写し、1Frame約0.5ms）。
- エンコーダーのスレッド（ネイティブ）：変換、Media FoundationでのH.264とAAC、多重化、書き込み。メインスレッドはこれを待たない。受け皿がいっぱいならFrameを捨てる。
- 音声の読み出し口は、エンコーダーのスレッドで読む。Audio Moduleは自分のスレッドでそれを満たす（Audio詳細設計9章）。

## 10. 設定

`Profiles/Streaming/<id>.json`。ProjectのStreaming Profile（Project Data詳細設計6章、方式設計6.3）で、後で配信と共有する：

```json
{
  "schemaVersion": 1,
  "width": 1920, "height": 1080, "frameRate": 30,
  "videoBitrate": 6000000, "audioBitrate": 160000,
  "recordingFolder": null
}
```

- `recordingFolder`がnullのときは、利用者自身のビデオのフォルダーの`Virtual Vessel Studio`とする。利用者が動画を探す場所だからである。Data Rootの中ではないため、Data Rootを移しても録画は移らない。
- 大きさとFrame Rateは、録画中は変えられない（方式設計10.15）。
- `ProfileKind`に`Streaming`を加える。ほかと同じく、ProjectにStreaming ProfileのIDを持たせ、初めて使うときに作る。

## 11. 保存

録画は利用者自身のファイルであり、アプリは消さない。

## 12. エラーと回復

- エンコーダーを開けない（H.264のエンコーダーがない、ファイルを作れない）：`EncoderFailed`か`FolderUnavailable`で`Failed`にする。ほかは止めない。
- ハードウェアのエンコーダーがあればそれを使い、なければMedia Foundationのソフトウェアのエンコーダーを使う。どちらを使ったかを記録する（方式設計5.15）。
- 録画中の書き込みのエラー：エンコーダーを止め、書いた部分を仕上げ、`Failed`にする。
- 音声がない（Audio Moduleが使えない、機器がない）：映像だけで録画を続け、`AudioUnavailable`を表示する。
- 録画は、アバター、トラッキング、取り込みを止めない（方式設計10.16）。

## 13. ログと診断

- 開始と停止を、ファイル名、大きさ、エンコーダー（ハードウェアかソフトウェアか）、設定とともに記録する。問題はWarningかError。毎Frameの記録はしない。
- 計測（Diagnostics画面）：`Recording/FramesWritten`、`Recording/FramesDropped`（受け皿がない）、`Recording/FramesSkipped`（読み出しが待っている）、`Recording/ReadbackLatency`（Frame）、`Recording/AvOffset`（ms、書き込み時の映像に対する音声）。

## 14. Testの方針

| 種類 | 対象 |
|---|---|
| EditMode | `RecordingNames`（名前、重なり、空き容量）、偽のエンコーダーでの状態の移り変わり、設定と移行 |
| ネイティブ（ctest） | 合成の映像と合成の音をエンコードし、ファイルの長さ、Frame数、音声の長さを確かめる（試作のTestを広げる） |
| PlayMode | 配信の映像を数秒録画し、ファイルが期待の大きさと長さで再生できる（`ffprobe`があるとき） |
| 性能（Explicit） | 試作の計測をTestにする：録画の有無でのFrameの時間、メインスレッドの負荷、取りこぼし、CPUとGPU（`EncodeSpikeProbe`をVideoへ移す） |
| 手動 | 音声と映像の同期：手をたたいた瞬間が、ファイルの中で見た目と音で一致する |

## 15. 性能

- 録画のためのメインスレッドの負荷は1Frame 1ms以下（計測で0.35〜0.48ms）。
- ハードウェアのエンコーダーで、1080p30でFrameを取りこぼさない。
- 毎FrameのAllocationをしない：固定の4つの受け皿。読み出しのBufferはUnityのものである。

## 16. Folder / Namespace

```text
native/encode/                         VirtualVessel.Encode.Native（CMake、Test）
Video/Runtime/Recording/               RecordingService、StreamFrameReader、NativeEncoder、RecordingNames
Video/Runtime/Recording/IRecordingService.cs
```

エンコーダーのDLLは`tools/native/build-encode.ps1`で作り、取り込みのPluginと同じくGitには入れない。リリースのビルドで作る（開発の進め方21章）。

## 17. 未解決の課題

| 項目 | 方針 |
|---|---|
| NVIDIA以外のGPU（AMD、Intel）と、ソフトウェアのエンコーダーの負荷 | リリース前にほかのPCで測る |
| GPUのTextureを直接エンコーダーへ渡す（読み出しなし） | ほかのPCで読み出しの負荷が大きすぎるときだけ |
| H.265、AV1を選べるようにする | 配信の後 |
| 録画と配信で別の設定にする | 配信とともに |

## 18. UI

### 18.1 ライブ画面

配信のプレビューの下に録画の操作を置き、ライブ画面から録画を開始・停止できるようにする（利用者の要望、2026-10-27）。

| 状態 | 表示 |
|---|---|
| Idle | Button（Secondary）「● 録画」と録画のIcon |
| Starting、Stopping | Buttonを無効にし、「準備中…」／「仕上げ中…」 |
| Recording | Status Chip「録画中 00:12:34」（塗りつぶした丸とText、色だけにしない）と、Dangerの Button「■ 録画を停止」 |
| Failed | 問題と対処を示すStatus Chip（5章）と、もう一度「● 録画」 |

- **誤操作を防ぐ**（利用者の要望。components 3.1、3.11）：
  - 停止はDangerの操作とし、確認のダイアログの後でだけ行う：「録画を停止しますか？ここまでのファイルは保存されます。」に、「録画を停止」（Danger）と「キャンセル」。
  - 開始は確認しない。誤って始めても失うものはなく、止めるときは上の確認を通る。
  - 開始中・停止中は、2度目の押下に反応しない。SpaceやEnterは、Buttonにフォーカスがあるときだけ効く（全体のShortcutはまだ設けない）。
- 停止の後、ファイルの場所を通知で知らせ、「フォルダーを開く」を付ける。

### 18.2 設定画面

「録画」のCard：フォルダー（「変更…」と「開く」）、画質（標準 6Mbps、高画質 12Mbps）、空き容量、大きさとFrame Rate（1920×1080、30fps。当面は固定）。音声の元はAudioのCardにある（Audio詳細設計18章）。
