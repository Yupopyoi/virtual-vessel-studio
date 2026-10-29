# Audio詳細設計

## 1. 目的

Audio Moduleは、配信の映像とともに出す音を集め、録画と後の配信のため、また利用者自身のモニターのために混ぜる（方式設計9.2〜9.4、10.8、10.9）。

音の元と処理は、これから増えていく：マイクのリアルタイムのボイスチェンジ（RVC）、BGM、効果音（SE）（利用者の指摘、2026-10-27）。そのため、Moduleは**音の元**と、そのそれぞれが通る**処理の段**と、送り先の**バス**で組み立てる。新しい種類の音は、新しい音の元か処理の段として足し、混ぜ合わせ、バス、エンコーダーは変えない（方式設計9.3）。

最初の版は、音の元を2つとする：マイクとゲームの音声（どちらも音声入力の機器。キャプチャーボードの音声は入力の機器として見える）。それぞれの音量とミュート、配信用のバスを作る（利用者の決定、2026-10-27）。残りは17章の段階で加える。

この設計に含めないもの：仮想マイク（方式設計9.4）。

## 2. 責務

| 責務 | 担当 |
|---|---|
| 音声の機器の一覧、入力の機器からの取り込み、モニターの機器への再生 | Audio、ネイティブの`VirtualVessel.Audio.Native`（WASAPI） |
| 音の元、処理の段、バス、混ぜ合わせ、メーターの値、音の元どうしの時刻合わせ | Audio（ネイティブのエンジン、マネージドの操作） |
| Unityの中で動く処理の段（Inference EngineでのRVC等） | Voice（後で）。マネージドの処理の段の受け渡し口を通す（8.3） |
| 音の元、機器、音量、送りの量の保存 | Audio（ProjectのAudio Profile） |
| 配信用のバスのエンコード | Video（録画詳細設計） |
| AudioのCardとメーター | UI |

## 3. Moduleの境界

- エンジンはネイティブのコードで動く：取り込み、ネイティブの処理の段（音量、ミュート、リミッター、後でEQ）、混ぜ合わせ、バス。マネージドのコードは、音の元を足し引きし、処理の段と送りの量を設定し、レベルを1秒に数回読む。
- Unityの中で動かす必要がある処理の段（Inference EngineでのRVC、方式設計9.5）は、ネイティブへは移さない。マネージドの処理の段の受け渡し口（8.3）を通して、音の元の音声をエンジンから読み、結果を書き戻す。遅れの量を申告し、混ぜ合わせはほかの音の元をそれに合わせる（8.4）。
- ほかのModuleに見えるのは、`IAudioService`、処理の段の受け渡し口、配信用の音声の読み出し口（5.3）だけで、WASAPIの型は見せない。
- Captureのキャプチャーボードの取り込みは映像だけのままとし、その音声の機器はここで「ゲームの音声」の元として選ぶ（方式設計10.21：取り込んだ音声はAudioを通す）。
- Voice（RVC）がAudioに依存し、その逆はない。

## 4. 主なClassとInterface

| Class | 種類 | 役割 |
|---|---|---|
| `IAudioService` | Interface | 音の元、機器、バス、配信用の音声の読み出し口（5章） |
| `IAudioSource` | Interface | 1つの音の元：状態、音量、ミュート、送りの量、レベル、処理の段 |
| `AudioSourceRole` | Enum | 音の元の用途：`Microphone`、`GameAudio`、後で`Music`、`SoundEffect` |
| `AudioSourceInput` | 種類 | 音がどこから来るか：入力の機器、後でクリップの再生（BGMとSEのファイル） |
| `AudioBus` | Enum | `Stream`（録画、配信）、`Monitor`（利用者のヘッドホン） |
| `IManagedAudioStage` | Interface | Unityの中で動く処理の段。遅れの量を申告する（8.3） |
| `AudioService` | Class | Application Service：Profileから音の元を作り、機器を開き、問題を知らせる |
| `AudioSettings` | Class | Audio Profile（10章） |
| `NativeAudioEngine` | Class | `VirtualVessel.Audio.Native`を包む |
| `VirtualVessel.Audio.Native` | ネイティブのDLL | 取り込み、ネイティブの処理の段、混ぜ合わせ、バス、リングバッファー、配信用の音声の読み出し口 |

## 5. 公開API

```csharp
public interface IAudioService
{
    IReadOnlyList<IAudioSource> Sources { get; }                          // マイク、ゲームの音声の順。BGMとSEで増える
    IReadOnlyObservable<IReadOnlyList<AudioDevice>> InputDevices { get; } // ID、名前、Windowsの既定か
    void RefreshDevices();
    AudioStreamTap StreamTap { get; }                   // エンコーダー用（5.3）。Contextと関数のポインター
    TimeSpan StreamLatency { get; }                     // 配信用のバスがどれだけ遅れているか（8.4）
}

public interface IAudioSource
{
    string Id { get; }
    AudioSourceRole Role { get; }
    IReadOnlyObservable<AudioSourceState> State { get; } // Off、Starting、Running、Unavailable、Error
    IReadOnlyObservable<AudioProblem> Problem { get; }   // None、InUse、AccessDenied、Failed
    IReadOnlyObservable<AudioSourceOptions> Options { get; } // 機器、音量（-60〜+12dB）、ミュート、配信用のバスへの送り（-infはなし）
    void SetOptions(AudioSourceOptions options);        // すぐに反映し、Profileに保存する
    float PeakDb { get; }                               // 直近0.1秒の最大。音量の後、ミュートの前。メーター用
}
```

- 選んだ値は、Trackingや録画と同じく`Options`と`SetOptions`でまとめて扱う。設定画面とライブ画面のどちらで変えても、両方に反映される。
- 音の元の足し引き（`AddSource`、`RemoveSource`）、モニターのバスと出力の機器、マネージドの処理の段（`Attach`）は、それぞれの段階（17章）で加える。最初の版は、マイクとゲームの音声の2つを最初から持つ。
- `IAudioEngine`は、`AudioService`とネイティブのエンジン（`NativeAudioEngine`）の間に置く。Testでは偽物に替え、機器とPluginなしで`AudioService`を確かめる。

### 5.2 音の元、処理の段、バス

```text
マイク       ─ 取り込み ─▶ [RVC（マネージド、後で）] ─▶ 音量／ミュート ─┬─▶ 配信用のバス ─▶ リミッター ─▶ 読み出し口 ─▶ エンコーダー
ゲームの音声 ─ 取り込み ───────────────────────────▶ 音量／ミュート ─┼─▶ モニターのバス ─▶ リミッター ─▶ 出力の機器（段階2）
BGM／SE     ─ クリップの再生（後で） ───────────────▶ 音量／ミュート ─┘
```

- 音の元ごとの送りの量で、それぞれのバスに何が届くかを決める。マイクは配信用だけ、BGMは両方、ゲームの音声はモニターにも（段階2）、のようにできる。
- 既定：マイクは配信用だけ。ゲームの音声は配信用（段階2でモニターにも）。BGMとSEは両方。

### 5.3 配信用の音声の読み出し口

```c
typedef struct vv_audio_stream_tap
{
    void* context;
    // 配信用のバスのステレオfloatのFrameを、until_qpcより後にならない範囲で最大framesだけ写し、
    // その数を返す。最初のFrameのQPCをfirst_qpcに書く。
    int32_t (*read)(void* context, float* out, int32_t frames, int64_t until_qpc, int64_t* first_qpc);
} vv_audio_stream_tap;
```

- マネージドのコードは`vv_audio_get_stream_tap`でこれを受け取り、素朴な値としてエンコーダーへ渡す。Videoは音声のDLLにリンクしない。
- 読むのは一度に1つ（録画。配信は、エンコードした同じ音声を共有する）。

## 6. Data Model

- 内部の形式：48kHz、32bit float、ステレオ。モノラルの音の元は両方のチャンネルに写す。
- 時刻：ブロックごとに最初のサンプルのQPCを持つ。映像のFrameと同じ時計である（録画詳細設計8.1）。機器ではWASAPIがパケットごとにQPCを渡し、クリップの再生では再生した時刻を付ける。

## 7. 状態とライフサイクル

- `AudioService`は、Loggingの後、Videoの録画の前に始まる。Profileの音の元を作り、その機器を開く。機器がなければ`Unavailable`とし、音の元のスレッドが1秒ごとに開き直す。Windowsの機器の通知は使わない。1秒の遅れで足り、通知のCOMのコールバックとスレッドの扱いがなくて済むためである。
- 機器のない音の元は`Off`とする。バスには、ほかの音の元か、無音が流れる。
- 音の元は動いている間に足し引きでき、混ぜ合わせは次のブロックからそれを使う。
- 終了時に、エンジンのスレッドを止め、機器を手放す。

## 8. 主な流れ

### 8.1 1つのブロック

1. 機器の音の元ごとの取り込みのスレッド（WASAPIの共有モード、イベント駆動、10ms）が、QPCの付いたパケットを受け取り、48kHz floatステレオに変換し（`AUTOCONVERTPCM`）、音の元の入力のリングバッファーに書く。
2. 混ぜ合わせのスレッドが10msごとに、音の元ごとに同じ時刻のブロック（処理の段の後）を取り、音量、ミュート、送りの量をかけて、バスへ足す。
3. それぞれのバスのピークを−1dBFSに抑え、リングバッファーに書く：配信用のバスは読み出し口へ、モニターのバスは出力の機器へ（段階2）。
4. エンコーダーが、読み出し口（5.3）を通して配信用のバスを読む。

### 8.2 時計

機器はそれぞれ別の時計で動く。そのずれ（1秒に数サンプル）は、音の元の入力でときどき1サンプルを捨てるか繰り返して吸収し、どのリングバッファーもあふれたり空になったりしないようにする。

### 8.3 Unityの中で動く処理の段（RVC）

- マネージドの処理の段は、遅れの量とブロックの大きさを申告する。エンジンは、音の元の入力のリングバッファーをそれに渡し、出力を別のリングバッファーから受け取る。どちらも48kHz floatである。
- 処理の段の仕事（RVCではGPUでのInference Engine）は、Unityの中で自分の予定で動く。エンジンはブロックを出し入れするだけなので、遅い処理の段が混ぜ合わせを止めることはない。出力が遅れたときは、その音の元を無音にして数える。
- 処理の段を外すと、音の元はそのまま通しに戻る。機器は開き直さない（方式設計9.3、9.8）。

### 8.4 音の元どうしの時刻合わせ

- 音の元の遅れは、その処理の段の遅れの合計とする（音量とミュートは0）。混ぜ合わせは、ほかの音の元を最も大きい遅れに合わせて遅らせる。変換した声とゲームの音声がずれないようにするためである。
- `StreamLatency`がその量を示す。Videoは、エンコードの前に配信の映像を同じだけ遅らせ、口の動きと声を合わせる（方式設計10.9）。RVCがなければ30ms（音の元が鳴り始める前に溜める20msと、機器の周期約10ms）で、映像は遅らせない。
- 配信用のバスの時刻は、最初のブロックからのサンプル数で決まる。混ぜ合わせのスレッドは、起きた時刻ではなく、QPCの時計で何ブロック目が済んでいるべきかで混ぜるため、起きるのが遅れても時刻はずれない。

### 8.5 アバターに合わせて配信の音を遅らせる

- アバターの動きは声より遅れる。カメラ、推定、揺れを抑える平滑化に時間がかかるためである（Tracking詳細設計8章）。利用者が手を叩いた録画では、アバターの手は音より少なくとも0.2秒遅れて現れた（2026-10-29）。
- そこで、録画と配信は、利用者が決めた遅延（0〜1000ms、既定は利用者の決定で300ms）だけ遅れて配信用のバスを受け取る。読み出し口は、各Frameをその分遅れて聞こえたものとして渡す（`vv_audio_set_stream_delay`）。利用者自身が聞く音（モニターのバス、段階2）は遅らせない。
- この遅延は`StreamLatency`に含めない。`StreamLatency`は処理の遅れで、Videoはその分映像を保つ（8.4）。こちらは逆向きである。
- 録画中に変えると、バスの時刻が跳ぶ。エンコーダーは、隙間を無音で埋めるか、重なりを書かない（録画詳細設計8.1）。
- アバターの遅れを測って自動で設定することは、未解決の課題とする（#100）。

## 9. Thread

- 機器の音の元ごとに1つの取り込みのスレッドと、1つの混ぜ合わせのスレッド。ネイティブで、標準より高い優先度（MMCSSの「Audio」）。Allocation、待つ間のLock、ログの記録はせず、数を数えるだけとする。
- マネージドの処理の段はUnityの中で動き、これらのスレッドでは動かさない（8.3）。
- マネージドは、メインスレッドから100msごとにレベルと状態を読む。

## 10. 設定

`Profiles/Audio/<id>.json`。ProjectのAudio Profile（Project Data詳細設計6章、方式設計6.3）：

```json
{
  "schemaVersion": 1,
  "sources": [
    { "id": "...", "role": "microphone", "deviceId": "...", "gainDb": 0, "muted": false, "sends": { "stream": 0 } },
    { "id": "...", "role": "gameAudio", "deviceId": null, "gainDb": -6, "muted": false, "sends": { "stream": 0 } }
  ],
  "streamDelayMs": 300
}
```

- 音の元は一覧なので、BGM、SE、2本目のマイクも形式を変えずに足せる。知らない用途の音の元は残し、鳴らさない。ファイルの最上位の知らない項目は残す（CLAUDE.md 25章）。音の元の中の知らない項目は、今は残らない。
- JSONは-infを書けないため、送りの-120dB以下を「なし」とする。
- `streamDelayMs`（8.5）は、Schema version 1のまま手順なしで加えた。ない古いファイルは300として読む。
- マイクは、初めて使うときにWindowsの既定の入力の機器とする。ゲームの音声には既定がない（キャプチャーボードを選ぶ必要がある）。
- `ProfileKind`に`Audio`を加える。

## 11. 保存

設定だけ（10章）。このModuleは音声を保存しない。録画はVideoが書く。BGMとSEのクリップのファイル（後で）は、Data Rootの`Audio/BGM`と`Audio/SE`に取り込む（方式設計6.10）。

## 12. エラーと回復

- 機器を抜いた：`Unavailable`。その部分は無音になり、挿し直すと開き直す。
- 機器をほかのアプリが占有している：`Error`と「ほかのアプリがこの機器を使っています」。
- Windowsのプライバシーの設定でマイクが許可されていない：`Error`と「Windowsの設定 > プライバシーでマイクへのアクセスを許可してください」、そのページを開くButton。
- マネージドの処理の段が失敗した、遅れた：その音の元は無音になり（数える）、通しに戻せる（方式設計9.13）。
- Audioの失敗は、映像も録画も止めない。録画は`AudioUnavailable`を示す。

## 13. ログと診断

- 開いた機器（名前、形式）、なくなった機器、また見つかった機器、足した・外した音の元、処理の段の遅れ、問題を記録する。ブロックごとの記録はしない。
- 計測：音の元ごとの`Audio/<用途>.Overruns`、`Underruns`、`DriftCorrections`（用途は`Microphone`、`GameAudio`）。`StageLate`は処理の段とともに加える。`StreamLatency`は起動時にログに書く。
- 機器は名前でログに書き、IDは書かない。
- 音声そのものは、ログにも診断にも書かない（CLAUDE.md 20章）。

## 14. Testの方針

| 種類 | 対象 |
|---|---|
| ネイティブ（ctest） | 合成の信号での混ぜ合わせ：音量、ミュート、送りの量、ピークの抑制、ずれの補正、リングバッファー、読み出し口の時刻、遅れのある処理の段とほかの音の元の時刻合わせ |
| EditMode | 偽のエンジンでの`AudioService`：Profileからの音の元、足し引き、機器の消失と復帰、設定と移行 |
| 実機（手動） | 実際のマイクとキャプチャーボード：レベル、抜き差し、録画の同期（録画詳細設計14章） |

## 15. 性能

- 機器から配信用のバスまでの遅れ：約20〜30ms（10msの周期2つと混ぜ合わせ）に、処理の段それぞれの遅れが加わる。
- CPU：機器の音の元2つで、1コアの1%を十分に下回る。

## 16. Folder / Namespace

```text
native/audio/include/vv_audio.h        公開のC API（version 2：配信の遅延）
native/audio/src/                      mixer（混ぜ合わせ、バス、読み出し口。スレッドも機器も持たない）、frame_ring、dsp（音量、リミッター）、
                                       device_source（WASAPIでの取り込み）、exports（エンジンと混ぜ合わせのスレッド）
native/audio/tests/                    合成の信号での混ぜ合わせ、ずれ、時刻。このPCの機器（CIでは飛ばす）
Audio/Runtime/                         IAudioService、AudioService、AudioSettings、NativeAudioEngine（IAudioEngine）
Audio/Tests/EditMode/
UI/Runtime/Screens/                    AudioCard（設定画面）、AudioStrip（ライブ画面）
```

新しいModule `VirtualVessel.Audio`（方式設計2.4）。UIがAudioを参照する（CaptureやTrackingと同じ向き）。DLLは`tools/native/build-audio.ps1`で`Audio/Plugins/x86_64`に作り、Gitには入れない。リリースのビルドとCIでも作る。

## 17. 段階

1. マイクとゲームの音声、音量とミュート、配信用のバス、レベル、設定（この版）。
2. モニターのバス：ゲームの音声と、後でBGMとSEを、利用者のヘッドホンへ。送りの量は別に持つ（方式設計9.4）。
3. ボイスチェンジを、マイクのマネージドの処理の段として加え（8.3）、配信の映像をそれに合わせて遅らせる（8.4）。
4. BGMとSEのクリップの再生。ファイルはData Rootに置く。

## 18. UI

設定画面の「音声」のCard。音の元ごとに行をまとめる：

| 行 | 内容 |
|---|---|
| 見出し | 音の元の名前と、その状態のStatus Chip（形と文字。ミュート中は警告の形の「ミュート中」） |
| 問題 | 取り込みと同じく、何が起きたかと対処。Windowsがマイクへのアクセスを許可していないときは、「マイクのプライバシー設定を開く」Button（`ms-settings:privacy-microphone`） |
| デバイス | Select（先頭に「なし」、どちらの音の元も）。選んだ機器が抜かれているときは「接続されていないデバイス」として残す |
| 音量 | Slider、-60〜+12dB、1dB刻み |
| ミュート | Toggle |
| 入力レベル | Level Meter（components 3.5）。-60dB（空）〜0dB（満） |
| 配信・録画の音声の遅延（音の元の後に1つ） | Slider、0〜1000ms、10ms刻み（8.5） |

- Cardを表示している間、機器を5秒ごとに数え直し、つないだ機器が選べるようになる。
- モニターへの送りの量は段階2で加える。
- ライブ画面では、録画の操作の下に、音の元ごとのミュートのIconButton（ミュート中は斜線のマイク）、名前、小さなメーターを置き、録画中に音が入っていることが分かるようにする（`live.md`）。
