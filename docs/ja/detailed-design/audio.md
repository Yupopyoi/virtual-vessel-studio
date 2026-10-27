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
    IReadOnlyObservable<IReadOnlyList<IAudioSource>> Sources { get; }
    IReadOnlyList<AudioDevice> InputDevices { get; }    // IDと名前
    IReadOnlyList<AudioDevice> OutputDevices { get; }   // モニターのバス用（段階2）
    void RefreshDevices();
    IAudioSource AddSource(AudioSourceRole role);       // 後の用途。最初の版は2つを最初から持つ
    void RemoveSource(IAudioSource source);
    AudioStreamTap StreamTap { get; }                   // エンコーダー用（5.3）
    TimeSpan StreamLatency { get; }                     // 音の元の遅れの最大：配信用のバスがどれだけ遅れているか（8.4）
}

public interface IAudioSource
{
    string Id { get; }
    AudioSourceRole Role { get; }
    IReadOnlyObservable<AudioSourceState> State { get; } // Off、Starting、Running、Unavailable、Error
    string DeviceId { get; }
    void SelectDevice(string deviceId);                 // nullで止める
    float GainDb { get; set; }                          // -60〜+12
    bool Muted { get; set; }
    float SendDb(AudioBus bus);                         // それぞれのバスへ送る量。送らないときは-inf
    void SetSend(AudioBus bus, float db);
    float PeakDb { get; }                               // 処理の段と音量の後。メーター用
    void Attach(IManagedAudioStage stage);              // 後で：マイクのRVC
}
```

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

- `AudioService`は、Loggingの後、Videoの録画の前に始まる。Profileの音の元を作り、その機器を開く。機器がなければ`Unavailable`とし、現れたら開き直す（機器の通知）。
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
- `StreamLatency`がその量を示す。Videoは、エンコードの前に配信の映像を同じだけ遅らせ、口の動きと声を合わせる（方式設計10.9）。RVCがなければ約20〜30msで、映像は遅らせない。

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
  ]
}
```

- 音の元は一覧なので、BGM、SE、2本目のマイクも形式を変えずに足せる。知らない項目と用途は残す（CLAUDE.md 25章）。
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
- 計測：音の元ごとの`Audio/<音の元>.Overruns`、`Underruns`、`DriftCorrections`、`StageLate`、`Audio/MixLatency`、`Audio/StreamLatency`。
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
native/audio/                          VirtualVessel.Audio.Native（CMake、Test）
Audio/Runtime/                         IAudioService、IAudioSource、AudioService、NativeAudioEngine、AudioSettings
Audio/Tests/EditMode/
```

新しいModule `VirtualVessel.Audio`（方式設計2.4）。DLLは`tools/native/build-audio.ps1`で作る。

## 17. 段階

1. マイクとゲームの音声、音量とミュート、配信用のバス、レベル、設定（この版）。
2. モニターのバス：ゲームの音声と、後でBGMとSEを、利用者のヘッドホンへ。送りの量は別に持つ（方式設計9.4）。
3. ボイスチェンジを、マイクのマネージドの処理の段として加え（8.3）、配信の映像をそれに合わせて遅らせる（8.4）。
4. BGMとSEのクリップの再生。ファイルはData Rootに置く。

## 18. UI

設定画面の「音声」のCard。音の元ごとに行をまとめる：

| 行 | 内容 |
|---|---|
| 機器 | Dropdown（ゲームの音声は先頭に「なし」） |
| 音量 | Slider、dB |
| ミュート | Toggle |
| レベル | Level Meter（components 3.5） |
| 問題 | 取り込みと同じく、音の元の状態と対処 |

モニターへの送りの量は段階2で加える。ライブ画面では、録画の操作の隣に、音の元ごとの小さなメーターとミュートのButtonを置き、録画中に音が入っていることが分かるようにする。
