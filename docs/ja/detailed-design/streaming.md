# 配信詳細設計

## 1. 目的

配信の映像と音声を、外部の配信アプリを通さずに、本アプリから配信サービスへ直接送る（方式設計10.10〜10.12）。

- 初めの対象はYouTube Liveとし、RTMPSで送る（方式設計10.10）。サーバーのURLとストリームキーを利用者が登録する方式から始め、YouTube APIとの連携は後にする（方式設計10.11）。
- 同じRTMP(S)を受け付けるほかのサービスにも送れるよう、「そのほかのRTMP(S)サーバー」も選べる（Twitch等）。サービス固有の処理は送信先（Destination）の中に閉じ込める。
- 映像と音声は、録画と同じ描画結果と配信用のバスを使う（録画詳細設計、Audio詳細設計）。音声の遅延（Audio詳細設計8.5）も同じく効く。
- エンコードはWindows標準のMedia Foundationで行い、ハードウェアのエンコーダーを優先する（方式設計10.7、利用者の決定2026-10-22）。RTMP、FLV、TLSも外部ライブラリを使わずに作る（Windowsの`SChannel`でTLS）。

この設計に含めないもの：YouTube APIとの連携（OAuth、配信枠の作成）、複数の配信先への同時送信、SRTやWHIP等の別の送り方、外部への映像出力（Spout）。

## 2. 責務

| 責務 | 担当 |
|---|---|
| 配信の映像（Stream Render Target）の描画 | Video（Video詳細設計） |
| 映像の読み出しと時刻付け | Video（`StreamFrameReader`。配信ではNV12で読む、8.1） |
| 配信用のバスと音声の遅延 | Audio（Audio詳細設計） |
| 配信用のエンコード（H.264またはAV1、AAC）、FLVへの多重化、RTMP(S)での送信、再接続 | Streaming、ネイティブの`VirtualVessel.Stream.Native` |
| 配信の開始・停止、状態、問題、送信先の設定 | Streaming（`StreamingService`） |
| ストリームキーの保存 | ProjectData（Windowsの資格情報マネージャー、方式設計6.13） |
| 配信の操作（ライブ画面）、配信先とキーの設定（設定画面） | UI |

## 3. Moduleの境界

- 新しいModule `VirtualVessel.Streaming`（方式設計2.4の「Streaming」）。Videoの`IStreamOutput`と`StreamFrameReader`を使い、Audioの読み出し口は録画と同じく値として受け取る（録画詳細設計3章）。
- Media Foundation、RTMP、FLV、TLSの型はネイティブのPluginの中だけに置く。マネージドの側は`IStreamingService`と単純な値だけを見る。
- 送信先ごとの違い（URLの形、使えるコーデック、推奨のビットレート）は`StreamDestination`にまとめ、エンコーダーと送信の処理には持ち込まない（方式設計10.10「Encode、Multiplex、Publishを分ける」）。
- 配信の設定は録画とStreaming Profileのファイルを共有するので、書き込みは`RecordingService.SaveStream`に集める。どちらかの保存が、もう一方の変更を巻き戻さないためである。
- ストリームキーはStreaming Profileに書かない。Profileは資格情報のID（`credentialId`）だけを持ち、値はWindowsの資格情報マネージャーにある（方式設計6.13）。

## 4. 主なClassとInterface

| Class | 種類 | 役割 |
|---|---|---|
| `IStreamingService` | Interface（UI） | UIが使う：状態、問題、経過時間、開始、停止、送信先の設定、キーの登録（5章） |
| `StreamingService` | Class（Streaming） | Application Service：配信の開始と停止、再接続の見張り、統計 |
| `StreamDestination` | Class（Streaming） | 送信先の種類ごとの値：既定のURL、使えるコーデック、推奨のビットレート。純粋 |
| `StreamPublisher` | Class（Streaming） | `VirtualVessel.Stream.Native`を包む |
| `ICredentialStore` | Interface（ProjectData） | 秘密情報の保存と読み出し。`WindowsCredentialStore`が実装する |
| `VirtualVessel.Stream.Native` | ネイティブのDLL | エンコード（Media FoundationのMFT）、FLV、RTMP、TLS、送信の待ち行列、再接続 |

## 5. 公開API

```csharp
public interface IStreamingService
{
    IReadOnlyObservable<StreamingState> State { get; }     // Idle、Connecting、Live、Reconnecting、Stopping、Failed
    IReadOnlyObservable<StreamingProblem> Problem { get; } // 6章
    TimeSpan Elapsed { get; }                              // Liveになってから
    IReadOnlyObservable<StreamingOptions> Options { get; } // 送信先、サーバーURL、キーが登録済みか、コーデック、ビットレート
    void SetOptions(StreamingOptions options);             // 配信中は変えられない（方式設計10.15）
    bool SetStreamKey(string key);                         // 資格情報マネージャーへ。空で消す。失敗はfalse。値は読み戻さない
    StreamingStats Stats { get; }                          // 実際のビットレート、待ち行列、捨てたFrame、再接続の回数
    Task<StreamingProblem> TestConnectionAsync();          // RTMPのconnectまで。配信は始まらない
    void Start();
    void Stop();
}
```

- `SetStreamKey`は書き込むだけで、キーの値を返すAPIは作らない。UIは「登録済み」かどうかだけを知る。

### 5.1 ネイティブのAPI（`vv_stream.h`）

```c
int32_t vv_stream_open(const vv_stream_config* config, vv_stream** stream);
    // URL、キー、幅、高さ、fps、コーデック、映像と音声のビットレート、キーフレームの間隔、
    // 音声の読み出し口（vv_encode_audio_readと同じ形）
int32_t vv_stream_push_video(vv_stream*, const uint8_t* nv12, int32_t length, int64_t qpc);
int32_t vv_stream_get_stats(vv_stream*, vv_stream_stats* stats);  // 状態、問題、送ったバイト、待ち行列、捨てたFrame、再接続の回数
int32_t vv_stream_close(vv_stream*);                             // 送り終えて切る
```

- 接続、エンコード、送信はPluginのスレッドで行い、`open`は待たない。状態は`get_stats`で知る。
- キーは`open`で渡したあと、Pluginの中で接続にだけ使い、ログやエラーの文字列に含めない。

## 6. Data Model

- `StreamingState`：`Idle`、`Connecting`（接続とRTMPの`publish`まで）、`Live`、`Reconnecting`、`Stopping`、`Failed`。
- `StreamingProblem`：`None`、`NoStreamKey`、`ServerUnreachable`（名前解決、TCP、TLSの失敗）、`Rejected`（サービスが`publish`を断った：多くはキーの誤り）、`ConnectionLost`（再接続中）、`EncoderFailed`、`AudioUnavailable`（音声なしで配信を続ける）。
- `StreamDestinationKind`：`YouTube`、`CustomRtmp`。
- `StreamingStats`：実際のビットレート（直近5秒）、待ち行列の長さ（ms）、捨てたFrame、再接続の回数、エンコードの時間。

## 7. 状態とライフサイクル

```text
Idle ──Start──▶ Connecting ──publishが受理──▶ Live ──Stop──▶ Stopping ──▶ Idle
                    │                          │ ▲
                    │                          ▼ │ 再接続できた
                    │                     Reconnecting ──あきらめた──▶ Failed
                    └──失敗（キーなし、断られた等）──▶ Failed
```

- `Start`はキーが登録済みかを確かめ、Pluginを開く。接続とエンコードはPluginのスレッドで始まり、`publish`が受理されると`Live`になる。
- 接続が切れたら`Reconnecting`とし、1、2、4、8、15、30秒…の間隔で（最大30秒）再接続する。10分続けて失敗したら`Failed`とし、利用者に知らせる。再接続のたびにキーフレームから送り直す。
- 配信が失敗しても、録画、アバター、トラッキング、取り込みは止めない（方式設計10.16）。
- 終了時に配信中なら、送り終えて切る。
- 解像度、コーデック、送信先、URL、キーは、配信中は変えられない（方式設計10.15）。設定画面では、配信中は無効にして理由を示す。

## 8. 主な流れ

### 8.1 1つのFrame

1. VideoがStream Render Targetを描く。
2. 配信の出力のFrame Rateで、`StreamFrameReader`が読み出す。配信ではComputeShaderでBT.709 limited rangeのNV12にしてから読み出す（RGBの4分の3の量）。エンコーダーへそのまま渡せ、Media Foundationの色の変換（CPU）も要らない。
3. Pluginのエンコードのスレッドが、NV12をMFT（ハードウェアのH.264またはAV1のエンコーダー）に渡し、出てきた圧縮済みのFrameを受け取る。
4. 同じスレッドで、そのFrameの時刻までの音声を読み出し口から取り出し、AACのMFTでエンコードする（録画と同じく時刻どおりに置く、録画詳細設計8.1）。
5. FLVのタグにして送信の待ち行列に入れる。送信のスレッドがRTMPのChunkにしてTLSの上で送る。

- 録画と配信を同時に行うときは、それぞれが別にエンコードする（NVENCは同時に複数のエンコードを扱える）。エンコード結果の共有は17章。
- 配信用のバスを録画と配信の2か所から読むため、Audioの読み出し口を読み手ごとの位置を持てる形にする（Audio API version 3。`vv_audio_get_stream_tap`に`VV_AUDIO_READER_RECORDING`か`VV_AUDIO_READER_STREAMING`を渡し、それぞれ固定の読み出し口を得る）。

### 8.2 送信の詰まり

- 送信の待ち行列が3秒分を超えたら、映像のFrameを次のキーフレームまで捨てる（音声は捨てない）。回線が遅いとき、遅れがたまり続けないようにするためである。捨てたFrameは数え、「回線が追いつきません」と知らせる。
- 待ち行列があふれ続ける（30秒）ときは、ビットレートを下げるよう案内する。自動でビットレートを変えることは17章。

### 8.3 送信先

| 送信先 | 既定のURL | 映像のコーデック | 推奨の映像ビットレート（1080p30） |
|---|---|---|---|
| YouTube | `rtmps://a.rtmps.youtube.com/live2` | AV1（GPUが対応していれば）、なければH.264 | 6Mbps（範囲3〜10） |
| そのほかのRTMP(S) | 利用者が入力（`rtmp://`か`rtmps://`） | H.264（AV1を受け付けないサービスが多いため） | 6Mbps |

- AV1はEnhanced RTMP（FLVの`av01`）で送る。YouTubeがRTMPSでAV1を受け付けることは、実装の初めに確かめる（試作）。受け付けないと分かればH.264にする。
- キーフレームの間隔は2秒（YouTubeの推奨）。映像は固定のビットレート（CBR）。

## 9. Thread

- メインスレッド：読み出しの依頼と、画素を受け皿へ写すこと（録画と同じ）。
- エンコードのスレッド（ネイティブ）：映像と音声のエンコード、FLVへの多重化。
- 送信のスレッド（ネイティブ）：接続、TLS、RTMP、送信、再接続。エンコードを待たせない。
- どれもメインスレッドを待たせない。

## 10. 設定

`Profiles/Streaming/<id>.json`、録画と共有のStreaming Profile（録画詳細設計10章）に加える：

```json
{
  "schemaVersion": 1,
  "width": 1920, "height": 1080, "frameRate": 30,
  "videoBitrate": 6000000, "audioBitrate": 160000,
  "recordingFolder": null, "videoCodec": null,
  "stream": {
    "destination": "youtube",
    "serverUrl": null,
    "credentialId": "<16進数32桁>",
    "videoCodec": null,
    "videoBitrate": 6000000
  }
}
```

- `serverUrl`がnullのときは、送信先の既定のURLを使う。
- `stream.videoBitrate`は録画の`videoBitrate`と別に持つ（録画は画質、配信は回線で決まるため）。
- 既存のファイルに`stream`がなければ、上の既定とみなす（Schema version 1のまま、手順なし）。

## 11. 保存

- ストリームキーは、Windowsの資格情報マネージャーに、汎用の資格情報として保存する（`CredWrite`、対象名`VirtualVesselStudio/stream/<credentialId>`、そのユーザーだけ、そのPCだけ（`CRED_PERSIST_LOCAL_MACHINE`））。Profileには対象名を作るためのIDだけを書く。IDは32桁の16進数で、Profileは編集されたり他人から届いたりするので、使う前に形を検査し、このアプリの対象名の外を指せないようにする。
- Profileを書き出して共有しても、キーは含まれない。別のPCでは、キーを登録し直す必要がある。
- 比べた方法（利用者の確認、2026-10-30）：設定ファイルにそのまま書くと、共有、Issueへの添付、バックアップから漏れる。DPAPIで暗号化してファイルに書くのと資格情報マネージャーは、どちらもほかのユーザーやほかのPCからは読めないが、資格情報マネージャーはキーがファイルに入らず、利用者がWindowsの画面で確かめて消せる。
- 同じユーザーとして動くプログラム（マルウェア）からは、どの保存方法でも守れない。キーそのものを持たない方法（YouTube APIのOAuth、取り消せるトークン）は、方式設計10.11の後の段とする。

## 12. エラーと回復

| 状況 | 状態と表示 | 利用者への案内 |
|---|---|---|
| キーが登録されていない | 開始しない、`NoStreamKey` | 設定画面の「配信」でストリームキーを登録してください |
| サーバーにつながらない | `Failed`、`ServerUnreachable` | インターネットへの接続とURLを確かめてください |
| 配信を断られた | `Failed`、`Rejected` | ストリームキーが正しいか、YouTube Studioで確かめてください |
| 配信中に切れた | `Reconnecting`、`ConnectionLost` | 自動で再接続しています。録画は続いています |
| エンコーダーが使えない | `Failed`、`EncoderFailed` | もう一度試すか、診断画面のログを確かめてください |
| 音声がない | `Live`のまま、`AudioUnavailable` | 音声なしで配信しています |

- TLSの証明書はWindowsの通常の検証に従い、検証を飛ばす設定は作らない。
- `rtmp://`（暗号化なし）を選ぶと、キーが暗号化されずに送られる。設定画面で警告し、できる送信先では`rtmps://`を勧める。

## 13. ログと診断

- 開始と終了、送信先の種類、サーバーのホスト名、コーデック、ビットレート、エンコーダー（ハードウェアかどうか）、接続の状態の変化、再接続の回数、問題を記録する。毎Frameの記録はしない。
- **キーは、ログ、診断、エラーの文字列、診断情報の書き出しのどこにも出さない**（方式設計10.12）。URLは、パスにキーを含む送信先があるため、ホスト名だけを記録する。キーの状態は「登録済み／未登録」だけを書く。
- 計測（診断画面、方式設計5.15）：`Streaming/Bitrate`、`Streaming/QueueMs`、`Streaming/FramesDropped`、`Streaming/Reconnects`、`Streaming/EncodeTime`、`Streaming/AvOffset`。
- フレームが失われたら、読み戻せなかった数、エンコーダーが追いつかなかった数、接続が追いつかなかった数を分けて、5秒に1回までWarningに記録する。原因ごとに対処が違うためである。配信の終わりにも同じ内訳を記録する。

## 14. Testの方針

| 種類 | 対象 |
|---|---|
| ネイティブ（ctest） | FLVのタグ（AVC、AAC、Enhanced RTMPのAV1）、RTMPのChunkとAMF0、ハンドシェイク、待ち行列と詰まったときのFrameの捨て方、再接続の間隔。キーがログやエラーの文字列に出ないこと |
| ネイティブ（結合） | このPCで、ローカルのRTMPサーバー（ffmpegの受信モード、開発者のPATHにあれば）へ数秒送り、受け取った映像と音声を読み戻す |
| EditMode | `StreamingService`の状態遷移（偽のPlugin）、`StreamDestination`、設定と移行、`ICredentialStore`（偽物と、Windowsの資格情報マネージャーへの書き込みと消去） |
| PlayMode | 配信の操作のUI（開始、終了の確認、状態、再接続中の表示） |
| 手動 | YouTubeの限定公開の配信で、映像、音声、同期、切断と再接続を確かめる |

## 15. 性能

- 録画と同じく、メインスレッドの負荷は1Frame 1ms以下。NV12で読み出すため、読み出しの量は録画の4分の3。
- 録画と配信を同時に行っても、1080p30で取りこぼさないこと（エンコードは2つ、NVENCの使用率は合わせて10%程度の見込み）。
- 送信の待ち行列は最大3秒分（6Mbpsで約2.3MB）。

## 16. Folder / Namespace

```text
native/stream/                          VirtualVessel.Stream.Native（エンコード、FLV、RTMP、TLS、Test）
Streaming/Runtime/                      IStreamingServiceの実装、StreamingService、StreamDestination、StreamPublisher
Streaming/Tests/EditMode/
ProjectData/Runtime/Credentials/        ICredentialStore、WindowsCredentialStore
UI/Runtime/Streaming/IStreamingService.cs
UI/Runtime/Screens/                     StreamControl（ライブ画面）、StreamingCard（設定画面）
Streaming/Plugins/x86_64/               VirtualVessel.Stream.Native.dll（Gitには含めない）
Video/Runtime/Resources/                NV12への変換（StreamNv12.compute）
```

DLLは`tools/native/build-stream.ps1`で作り、Gitに入れない。

## 17. 未解決の課題

| 項目 | 方針 |
|---|---|
| 録画と配信でエンコード結果を共有する | エンコードを2つ動かして負荷が問題になったとき。録画もMFTとNV12に移すと共有できる |
| 回線に合わせてビットレートを自動で下げる | 詰まりが実際に問題になったとき |
| YouTube APIとの連携（配信枠の作成、タイトル、開始と終了） | 方式設計10.11の後の段 |
| 複数の送信先へ同時に送る | 求められたとき |

## 18. UI

### 18.1 ライブ画面

録画の操作の隣に、配信の操作を置く。

| 状態 | 表示 |
|---|---|
| Idle | Button（Primary）「● 配信を開始」。キーが未登録なら押せず、理由を示す |
| Connecting | Buttonを無効にし、「接続しています…」 |
| Live | Status Chip「LIVE 00:12:34」（Liveの形と文字）と、DangerのButton「■ 配信を終了」 |
| Reconnecting | Status Chip「再接続中」（警告の形）と「■ 配信を終了」 |
| Failed | 問題と対処を示すStatus Chipと、もう一度「● 配信を開始」 |

- 配信の開始は、確認のダイアログを出す：「配信を開始しますか？　YouTubeの設定によっては、すぐに公開されます。」に「配信を開始」と「キャンセル」。間違えて始めると公開されてしまうためである（録画の開始は確認しないのと違う）。
- 終了はDangerの操作とし、確認の後でだけ行う：「配信を終了しますか？」に「配信を終了」（Danger）と「キャンセル」。
- 配信中は、実際のビットレートと、回線が追いつかないときの警告を小さく示す。

### 18.2 設定画面

「配信」のCard：

| 行 | 内容 |
|---|---|
| 送信先 | Segmented：YouTube、そのほかのRTMP(S) |
| サーバーURL | YouTubeは既定のURLを表示（変更可）。そのほかは入力。`rtmp://`なら暗号化されない警告 |
| ストリームキー | 「登録済み」か「未登録」。「登録…」でダイアログを開き、伏せ字の入力欄に貼り付ける。「削除」。キーの値は表示しない |
| 形式 | Segmented：AV1、H.264（送信先とGPUが対応しないものは選べない） |
| 映像のビットレート | Slider、2〜12Mbps。送信先の推奨を説明に書く |
| 接続テスト | Button：サーバーへつないでRTMPの`connect`までを確かめる（`publish`はしないため、配信は始まらない） |

- 配信中は、どの行も変えられない（理由を示す）。
