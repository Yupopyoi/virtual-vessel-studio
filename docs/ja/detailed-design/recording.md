# エンコードと録画 詳細設計

## 1. 目的

配信の映像と音声をエンコードし、動画ファイルに録画する（方式設計10.7〜10.9、10.13）。配信（10.10〜10.12）は、同じエンコードの上に、別の出力先として後で加える。

- 録画を配信より先に作る（利用者の決定、2026-10-27）。ネットワークもストリームキーも要らず、手元で確かめられるためである。
- 既定は、1920×1080・30fps、音声はAAC、MP4とする（利用者の決定）。映像は、GPUにAV1のエンコーダーがあればAV1、なければH.264（High）とし、利用者はH.264を選べる（利用者の決定、2026-10-28。理由は[decisions/recording.md](../decisions/recording.md)）。
- 音声は、Audio Moduleの配信用の音声（Audio詳細設計）とする：マイクとゲームの音声。
- 試作（2026-10-27。試作のプログラムは残していないため、数値をここに記録する）で、アプリの中でのエンコードが間に合うことを確かめた。開発PC（Core i7-13700F、RTX 4070）で、2Frameに1回読み出し、Media Foundation経由のハードウェアのエンコーダーでエンコードしても、Frameの時間は変わらず（60fpsで16.7ms）、メインスレッドの負荷は1Frame 0.35〜0.48ms、GPUのエンコーダーの使用率は約5%、CPU（24スレッド）は1.4%増え、Frameの取りこぼしはなかった。

この設計に含めないもの：配信（RTMPS）、外部への映像出力（Spout）、ほかのコーデック（H.265、VP9）。

## 2. 責務

| 責務 | 担当 |
|---|---|
| 配信の映像を描く（Stream Render Target） | Video（Video詳細設計） |
| 配信の映像を読み出し、Frameごとに時刻を付ける | Video（`StreamFrameReader`） |
| 映像（AV1かH.264）と音声（AAC）のエンコード、MP4への多重化 | Video、ネイティブの`VirtualVessel.Encode.Native` |
| 録画の開始・停止、ファイル名、状態 | Video（`RecordingService`） |
| 配信用の音声（取り込み、混ぜる） | Audio（Audio詳細設計） |
| 録画のボタン、ライブ画面の録画の状態、録画の設定 | UI |

## 3. Moduleの境界

- VideoはAudioのClassを参照しない。Audioは配信用のバスを、ネイティブの読み出し口（`vv_audio_stream_tap`、Audio詳細設計5.3）として渡す。Videoのマネージドのコードはそれをエンコーダーへ渡し、エンコーダーは自分のスレッドで音声を読む。配信用のバスからファイルまで、音声はネイティブのコードの中にとどまる。バスに何を混ぜるか、その前のRVC等の処理の段はAudioのものである（Audio詳細設計5.2）。
- Captureは参照しない。取り込んだ映像は、描いた配信の映像を通してだけファイルに届く（方式設計10.2）。
- エンコーダーのMedia Foundationの型は、ネイティブのPluginの中だけにとどめる。マネージドのコードからは`IRecordingService`と素朴な値だけが見える。

## 4. 主なClassとInterface

| Class | 種類 | 役割 |
|---|---|---|
| `IRecordingService` | Interface（UI） | UIが使う：状態、経過時間、開始、停止、最後のファイル、フォルダーと画質（5章） |
| `RecordingService` | Class（Video） | Application Service：録画の開始と停止、フォルダーと空き容量の確認、ファイル名 |
| `StreamFrameReader` | Class（Video） | Stream Render Targetを出力のFrame Rateで読み出し（`AsyncGPUReadback`）、Frameごとに共通の時計の時刻を付ける |
| `NativeEncoder` | Class（Video） | `VirtualVessel.Encode.Native`を包む（`IVideoEncoder`） |
| `RecordingEnvironment` | Class（Video） | サービスの外とのつなぎ：エンコーダーを開く、Frameを読む、空き容量、現在の時刻、ビデオのフォルダー。Testでは偽物に替え、GPUとPluginなしで状態を確かめる |
| `RecordingSettings` | Class | Streaming Profileのエンコードの値と、録画のフォルダー（10章） |
| `RecordingNames` | Static Class | ファイル名、フォルダー、空き容量の確認、画質とビットレートの対応。純粋（8.2） |
| `FramePacing` | Static Class | 出力のFrame Rateで、どのFrameを読むかを決める。純粋（8.1） |
| `VirtualVessel.Encode.Native` | ネイティブのDLL | Media FoundationのSink WriterでのAV1かH.264とAAC、MP4 |

## 5. 公開API

```csharp
public interface IRecordingService
{
    IReadOnlyObservable<RecordingState> State { get; }    // Idle、Starting、Recording、Stopping、Failed
    IReadOnlyObservable<RecordingProblem> Problem { get; } // None、FolderUnavailable、DiskFull、EncoderFailed、AudioUnavailable
    TimeSpan Elapsed { get; }                              // 録画中
    string LastFile { get; }                               // 最後に仕上げたファイル（「フォルダーを開く」用）
    IReadOnlyObservable<RecordingOptions> Options { get; } // フォルダー、画質、大きさ、Frame Rate
    void SetOptions(RecordingOptions options);             // 次の録画から使う
    long? FreeBytes();                                     // 保存先の空き容量。読めなければnull
    void Start();
    void Stop();
}
```

- `IRecordingService`と`RecordingOptions`、状態と問題の列挙は、UIの`VirtualVessel.UI.Recording`に置き、Videoが実装する。VideoはUIを参照する（`IStreamPreviewSource`と同じ向き）ため、UIはVideoを参照できない。
- `RecordingQuality`：`Standard`（6Mbps）、`High`（12Mbps）。ファイルのビットレートは、近いほうの画質として読む。画質を変えないかぎり、ファイルで直したビットレートは保つ。
- `RecordingCodec`：`Av1`、`H264`。`RecordingOptions.Codec`は次の録画の形式、`Av1Available`はGPUにAV1のエンコーダーがあるか（起動時に1度だけ`vv_encode_hardware_available`で調べる）。ない場合、形式は常にH.264になる。

### 5.1 ネイティブのAPI（`vv_encode.h`）

```c
int32_t vv_encode_api_version(void);                                           // 3
int32_t vv_encode_hardware_available(int32_t codec);                           // VV_ENCODE_CODEC_H264か_AV1のエンコーダーがGPUにあれば1
int32_t vv_encode_open(const vv_encode_config* config, vv_encoder** encoder);  // パス、幅、高さ、fps、ビットレート、形式、
                                                                               // 音声のビットレート、音声の読み出しとContext（nullなら映像だけ）
int32_t vv_encode_push_video(vv_encoder*, const uint8_t* bgra, int32_t length, int64_t qpc);  // 受け皿がなければVV_ENCODE_DROPPED
int32_t vv_encode_get_stats(vv_encoder*, vv_encode_stats* stats);               // 渡した数、書いた数、捨てた数、最後のエラー、ハードウェアか、
                                                                               // 書いた音声のFrame数、映像に対する音声の位置
int32_t vv_encode_close(vv_encoder*);                                          // 残りを書き、ファイルを仕上げる。Frameが1つもなければファイルを消す
```

- API version 3で音声のトラックを加えた：AAC-LC、48kHzステレオ、96・128・160・192kbps（Media FoundationのAACのエンコーダーはこれだけを受け付けるため、ビットレートは近いものにする）。バスのfloatのサンプルを16bitのPCMに変えて渡す。
- `vv_encode_audio_read`は、音声エンジンの読み出し口（`vv_audio_stream_tap.read`）と同じ形である。マネージドの側が読み出し口の2つの値をそのまま渡すため、どちらのPluginも相手にリンクしない。
- H.264はHighプロファイルで書く（指定しないとエンコーダーはConstrained Baselineを選んでいた）。AV1はMainプロファイルである。
- 映像にBT.709・limited rangeの色の情報（行列、原色、伝達特性、範囲）を付け、入力はfull rangeと示す。プレーヤーが、描いたときの色に戻せるようにするためである。情報がないファイルは、プレーヤーによって暗く見えた。

- 映像のFrameは、試作と同じく4つの受け皿に写し、エンコーダーのスレッドでエンコードする。受け皿が空いていないFrameは捨てて数える。
- 音声は同じスレッドで、時刻に沿って読み出し口から取り出す。そのため、音声と映像は時刻の順に書かれる。

## 6. Data Model

- `RecordingState`：`Idle`、`Starting`、`Recording`、`Stopping`、`Failed`。
- `RecordingProblem`：`None`、`FolderUnavailable`（フォルダーを作れない・書けない）、`DiskFull`（空きが予備を下回る、8.2）、`EncoderFailed`（エンコーダーを開けない、書き込み中に失敗した）、`AudioUnavailable`（音声なしで録画を続ける）。
- ファイル：`<フォルダー>/VirtualVesselStudio_2026-10-27_22-30-05.mp4`（現地時刻）、AV1 MainかH.264 High、BT.709・limited range、AAC-LC 48kHzステレオ。

## 7. 状態とライフサイクル

```text
Idle ──Start──▶ Starting ──開いた──▶ Recording ──Stop──▶ Stopping ──仕上げた──▶ Idle
                   │                    │
                   └──失敗──▶ Failed ◀──┘（エンコーダーのエラー、空き容量の不足）
```

- `Start`は、フォルダーを作り（なければ）、空き容量を確かめ、ファイル名を決めて、エンコーダーを作業用のスレッドで開く（Media Foundationの起動に時間がかかることがあるため）。開けたら次のFrameで`Recording`になり、Frameの読み出しを始める。
- `Stop`は、読み出しを止め、途中の読み出しが届くのを最大1秒待ってから、作業用のスレッドでエンコーダーの残りを書き、ファイルを仕上げる。メインスレッドはエンコーダーを待たない。
- 開始中の`Stop`は、開けた時点で停止に移る。開始中、録画中、停止中の`Start`は何もしない。
- どの画面を表示していても、トラッキング、取り込み、アバターが変わっても、録画は続く。
- 終了時に録画中なら、停止して仕上げる。ファイルは再生できる状態になる。
- `Failed`は、次の`Start`まで問題を保つ。それまでに書いた部分は仕上げて残す。

## 8. 主な流れ

### 8.1 1つのFrame

1. VideoがStream Render Targetを描く（今と同じく毎Frame）。
2. 出力のFrame Rate（60fpsの描画から30fps：2Frameに1回）で、`StreamFrameReader`が読み出しを依頼し、そのFrameの共通の時計の時刻（QPC）を記録する。待っている読み出しは2つまでとし、それを超えるFrameは飛ばして数える。
3. 画素が届いたら（1Frame後）、その時刻とともにエンコーダーへ渡す。
4. エンコーダーのスレッドは、Frameを変換してエンコードし、その時刻までの音声を読み出し口から取り出して、両方を書く。

- ファイルの0は最初の映像のFrameとする。読み出し口がそれより前の音声を渡しても（バスは直近の数秒を持っている）、それは書かない。そのため、音声は映像と同時に始まる。
- 読み出し口から受け取った音声のかたまりは、その時刻が示すファイルの時間軸の位置に置く（`FrameAt`、`Place`）。隙間は無音で埋め、ファイルより前の分と、書いた分に重なる分は書かない。1ms以内のずれは続きとして扱うため、途切れないバスはサンプルごとにそのまま書かれ、エンコーダーの処理の時刻によって映像とずれることはない。
- 音声が映像より1秒以上遅れないよう、無音で追いつく。Media Foundationのファイルの書き手は遅れている側の流れを待つため、バスが止まると映像まで待たされ、Frameが捨てられてしまうからである。映像より1秒以上先のかたまりは書かない（このファイルのものではありえない）。
- 映像のFrameとバスは、同じ時計で時刻を付ける必要がある。アプリの単調な時計はQPCを直接読む（Application基盤詳細設計）。別の0から数えるUnityの`Stopwatch`のままでは、最初の録画で音声のトラックが空になり、Frameの大半が捨てられた。
- 最後のFrameの音声は、少しの間（バスの遅れと混ぜ合わせの周期）まだ混ぜ合わせの途中である。エンコーダーは、ファイルを仕上げる前に最大200msそれを待つ。
- Frameを書く時点で、その音声がまだ混ぜ合わされていないことがある。その分は次のFrameとともに書く。無音で埋めた量はログに書く（`audio_silence_frames`）。録画中の`Recording/AvOffset`は、そのため音声が約1Frame遅れて見える。仕上げたファイルでは、両方とも最後まで入っている。

- 配信用のバスは、Audioの`StreamLatency`（Audio詳細設計8.4）だけ、聞こえた時刻より遅れている：処理の段がなければ約20〜30ms、RVCがあればそれ以上。映像のFrameはその分だけ保ってからエンコードし、ファイルの中で口の動きと声を合わせる（方式設計10.9）。1Frame（33ms）未満なら保たない。

### 8.2 開始

- ファイル名は現地時刻から作る（`RecordingNames.FileName`、純粋）。同じ名前があれば`_2`、`_3`を付ける。
- フォルダーに2GB以上の空きが必要である（`RecordingNames.HasRoom`、純粋）。既定のビットレートで約45分にあたる。録画中は10秒ごとに空きを確かめ、500MBを下回ったら`DiskFull`で停止する。

## 9. Thread

- メインスレッド：読み出しの依頼と、画素を渡すこと（受け皿への写し、1Frame約0.5ms）。
- エンコーダーのスレッド（ネイティブ）：変換、Media FoundationでのAV1かH.264とAAC、多重化、書き込み。メインスレッドはこれを待たない。受け皿がいっぱいならFrameを捨てる。
- 音声の読み出し口は、エンコーダーのスレッドで読む。Audio Moduleは自分のスレッドでそれを満たす（Audio詳細設計9章）。

## 10. 設定

`Profiles/Streaming/<id>.json`。ProjectのStreaming Profile（Project Data詳細設計6章、方式設計6.3）で、後で配信と共有する：

```json
{
  "schemaVersion": 1,
  "width": 1920, "height": 1080, "frameRate": 30,
  "videoBitrate": 6000000, "audioBitrate": 160000,
  "recordingFolder": null,
  "videoCodec": null
}
```

- `videoCodec`は、利用者が選ぶと"av1"か"h264"、選ぶまではnullである。nullなら、GPUにAV1のエンコーダーがあればAV1、なければH.264で録画する。そのため、複数のPCで共有したProfileは、それぞれのPCに従う。"av1"を選んだProfileをAV1のエンコーダーがないPCで使うと、H.264で録画し、選んだことは保つ。Schema version 1のまま、手順なしで加えた。ない古いファイルはnullとして読む。

- `width`と`height`は、Stream Render Targetの大きさにもなる。起動時に読み、Videoより先に決める。範囲の外の値（幅320〜3840、高さ180〜2160の偶数、Frame Rate 1〜60、ビットレート0.5〜50Mbps）は既定に戻す（`RecordingSettings.Usable`）。ファイルの書き間違いで配信の映像が止まらないようにするためである。
- `recordingFolder`がnullのときは、利用者自身のビデオのフォルダーの`Virtual Vessel Studio`とする。利用者が既定のフォルダーを選び直したときもnullで保存し、ビデオのフォルダーの場所に従う。利用者が動画を探す場所だからである。Data Rootの中ではないため、Data Rootを移しても録画は移らない。
- 大きさとFrame Rateは、録画中は変えられない（方式設計10.15）。
- `ProfileKind`に`Streaming`を加える。ほかと同じく、ProjectにStreaming ProfileのIDを持たせ、初めて使うときに作る。

## 11. 保存

録画は利用者自身のファイルであり、アプリは消さない。

## 12. エラーと回復

- エンコーダーを開けない（その形式のエンコーダーがない、ファイルを作れない）：`EncoderFailed`か`FolderUnavailable`で`Failed`にする。ほかは止めない。
- ハードウェアのエンコーダーがあればそれを使い、なければMedia Foundationのソフトウェアのエンコーダーを使う（H.264だけ。ソフトウェアのAV1は実時間に間に合わないため、AV1はハードウェアのエンコーダーがあるときだけ使う）。形式と、ハードウェアかどうかを記録する（方式設計5.15）。
- 録画中の書き込みのエラー：エンコーダーを止め、書いた部分を仕上げ、`Failed`にする。
- 音声がない（Audio Moduleが始まらず、配信用のバスがない）：映像だけで録画を続け、録画中に`AudioUnavailable`（「音声なしで録画しています。」）を表示する。機器のない音の元はバスの上で無音になるだけで、バスはある。そのときのファイルは無音のトラックを持つ。
- 録画は、アバター、トラッキング、取り込みを止めない（方式設計10.16）。

## 13. ログと診断

- 開始と停止を、ファイル名、大きさ、エンコーダー（ハードウェアかソフトウェアか）、設定とともに記録する。問題はWarningかError。毎Frameの記録はしない。
- 計測（Diagnostics画面）：`Recording/FramesWritten`、`Recording/FramesDropped`（受け皿がない）、`Recording/FramesSkipped`（読み出しが待っている）、`Recording/ReadbackLatency`（Frame）、`Recording/AvOffset`（ms、最後に書いたときの、映像に対する音声の終わりの位置。負は遅れ）。

## 14. Testの方針

| 種類 | 対象 |
|---|---|
| EditMode | `RecordingNames`（名前、重なり、空き容量）、偽のエンコーダーでの状態の移り変わり、設定と移行 |
| ネイティブ（ctest） | 合成の映像と合成の音をエンコードし、ファイルの長さ、Frame数、音声の長さを確かめる（試作のTestを広げる） |
| PlayMode | 配信の映像を数秒録画し、ファイルが期待の大きさと長さで再生できる（`ffprobe`があるとき） |
| 性能（Explicit） | 試作の計測をTestにする：録画の有無でのFrameの時間、メインスレッドの負荷、取りこぼし、CPUとGPU（Videoに新しく書く。試作のプログラムは残していない） |
| 手動 | 音声と映像の同期：手をたたいた瞬間が、ファイルの中で見た目と音で一致する |

## 15. 性能

- 録画のためのメインスレッドの負荷は1Frame 1ms以下（計測で0.35〜0.48ms）。
- ハードウェアのエンコーダーで、1080p30でFrameを取りこぼさない。
- 毎FrameのAllocationをしない：固定の4つの受け皿。読み出しのBufferはUnityのものである。

## 16. Folder / Namespace

```text
native/encode/                         VirtualVessel.Encode.Native（CMake、Test）
Video/Runtime/Recording/               RecordingService、RecordingEnvironment、StreamFrameReader、FramePacing、NativeEncoder、RecordingNames、RecordingSettings
UI/Runtime/Recording/IRecordingService.cs   IRecordingService、RecordingOptions、状態と問題の列挙
UI/Runtime/Screens/                    RecordControl（ライブ画面）、RecordingCard（設定画面）
```

- `StreamFrameReader`は`AsyncGPUReadback`のNativeArrayをそのままエンコーダーへ渡すため、Video Assemblyは`allowUnsafeCode`を有効にする。

エンコーダーのDLLは`tools/native/build-encode.ps1`で作り、取り込みのPluginと同じくGitには入れない。リリースのビルドで作る（開発の進め方21章）。

## 17. 未解決の課題

| 項目 | 方針 |
|---|---|
| 音声の遅れが1Frame以上のときに映像を保つ（8.1） | ボイスチェンジとともに（Audio詳細設計の段階3）。それまでは遅れは30msで、1Frame未満 |
| 音声と映像の同期を手で確かめる（手を叩いたところが、見えるのと聞こえるのが同時か） | 利用者が、この版で |
| GPUのTextureを直接エンコーダーへ渡す（読み出しなし） | ほかのPCで読み出しの負荷が大きすぎるときだけ |
| AV1 Video Extensionが入っていないPCでAV1を再生できるか | ほかのPCで確かめる。それまでは、設定画面で「開けないときはH.264を選ぶ」と案内する |
| 配信の形式（YouTubeはAV1を受け付けるが、ほかのサービスはH.264が要ることがある） | 配信とともに |
| 録画と配信で別の設定にする | 配信とともに |

## 18. UI

### 18.1 ライブ画面

配信のプレビューの下に録画の操作を置き、ライブ画面から録画を開始・停止できるようにする（利用者の要望、2026-10-27）。

| 状態 | 表示 |
|---|---|
| Idle | Button（Secondary）「● 録画」（記号の●を文字に含める） |
| Starting、Stopping | Buttonを無効にし、「録画を開始しています…」／「ファイルを保存しています…」 |
| Recording | Status Chip「録画中 00:12:34」（塗りつぶした丸とText、色だけにしない）と、Dangerの Button「■ 録画を停止」 |
| Failed | 問題と対処を示すStatus Chip（5章）と、もう一度「● 録画」 |

- **誤操作を防ぐ**（利用者の要望。components 3.1、3.11）：
  - 停止はDangerの操作とし、確認のダイアログの後でだけ行う：「録画を停止しますか？ここまでのファイルは保存されます。」に、「録画を停止」（Danger）と「キャンセル」。
  - 開始は確認しない。誤って始めても失うものはなく、止めるときは上の確認を通る。
  - 開始中・停止中は、2度目の押下に反応しない。SpaceやEnterは、Buttonにフォーカスがあるときだけ効く（全体のShortcutはまだ設けない）。
- 停止の後、録画の操作の下に「保存しました: <ファイル名>」と「フォルダーを開く」を出す。「フォルダーを開く」は、エクスプローラーでそのファイルを選んだ状態で開く。通知（`INotificationService`）はButtonを持たないため、ライブ画面に置く。
- 問題があるときは、Chipの下に何が起きたかと対処を出す（例：空き容量が足りないときは、ファイルを消すか、設定画面で別のドライブのフォルダーを選ぶ）。

### 18.2 設定画面

「録画」のCard：フォルダー（「変更…」と「開く」）、画質（標準 6Mbps、高画質 12Mbps）、空き容量、大きさとFrame Rate（1920×1080、30fps。当面は固定）。

- 形式：Segmented「AV1」／「H.264」。おすすめと選び方は説明に書く（項目名を短くし、260pxに収める）。AV1のエンコーダーがないときは、AV1を見せるが選べないようにし、説明でGPUが対応していないことを示す。
- 「変更…」はWindowsのフォルダーを選ぶ画面（`SHBrowseForFolderW`）を、ファイルを選ぶ画面と同じく専用のスレッドで開く。描画と配信は止まらない。
- 空き容量は、Cardを表示している間、10秒ごとに読み直す。
- 変更は次の録画から使う。録画中の録画は、始めたときの設定のまま続く。音声の元はAudioのCardにある（Audio詳細設計18章）。
