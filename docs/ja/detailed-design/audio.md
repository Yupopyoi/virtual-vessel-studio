# Audio詳細設計

## 1. 目的

Audio Moduleは、配信の映像とともに出す音を集める：マイクと、キャプチャーボードからのゲームの音声を混ぜ、録画と、後には配信のための配信用の音声にする（方式設計9.2〜9.4、10.8）。最初の版は、録画に要る最小の流れとする。リアルタイムのボイスチェンジ（RVC）は、後でマイクの流れの1段として加え、ほかは変えない（方式設計9.3）。

- 音の元：マイクとゲームの音声。どちらもWindowsの音声入力の機器である（キャプチャーボードの音声は入力の機器として見える）（利用者の決定、2026-10-27）。
- 出力：配信用の音声。エンコーダーが読む（録画詳細設計）。ゲームの音声を利用者のヘッドホンでモニターすることは、この設計の段階2とする（17章）。

この設計に含めないもの：ボイスチェンジ、エフェクト、仮想マイク（方式設計9.4）。

## 2. 責務

| 責務 | 担当 |
|---|---|
| 音声入力の機器の一覧、開く、取り込む | Audio、ネイティブの`VirtualVessel.Audio.Native`（WASAPI） |
| 音量、ミュート、配信用の音声への混ぜ合わせ、メーターの値 | Audio（混ぜるのはネイティブ、操作はマネージド） |
| 選んだ機器と音量の保存 | Audio（ProjectのAudio Profile） |
| 混ぜた音声のエンコード | Video（録画詳細設計） |
| AudioのCardとメーター | UI |

## 3. Moduleの境界

- リアルタイムの流れ（取り込み → 混ぜる → エンコーダー）は、ネイティブのコードの中にとどめる。マネージドのコードは、機器、音量、ミュートを設定し、レベルを1秒に数回読む。
- ほかのModuleに見えるのは`IAudioService`と配信用の音声の読み出し口（5.2）だけで、WASAPIの型は見せない。
- Captureのキャプチャーボードの取り込みは映像だけのままとし、その音声の機器はここで「ゲームの音声」の元として選ぶ（方式設計10.21：取り込んだ音声はAudioを通す）。

## 4. 主なClassとInterface

| Class | 種類 | 役割 |
|---|---|---|
| `IAudioService` | Interface | 音の元、その状態、機器、音量とミュート、レベル、配信用の音声の読み出し口（5章） |
| `AudioService` | Class | Application Service：選んだ機器を開き、設定を戻し、問題を知らせる |
| `AudioSourceKind` | Enum | `Microphone`、`GameAudio` |
| `AudioSettings` | Class | Audio Profile（10章） |
| `NativeAudioEngine` | Class | `VirtualVessel.Audio.Native`を包む |
| `VirtualVessel.Audio.Native` | ネイティブのDLL | 音の元ごとのWASAPIの取り込み、48kHz floatステレオへの変換、混ぜ合わせ、読み出し口のためのリングバッファー |

## 5. 公開API

```csharp
public interface IAudioService
{
    IReadOnlyList<IAudioSource> Sources { get; }        // Microphone、GameAudio
    IReadOnlyList<AudioDevice> Devices { get; }         // 入力の機器：IDと名前
    void RefreshDevices();
    AudioStreamTap StreamTap { get; }                   // エンコーダー用（5.2）
}

public interface IAudioSource
{
    AudioSourceKind Kind { get; }
    IReadOnlyObservable<AudioSourceState> State { get; } // Off、Starting、Running、Unavailable（機器がない）、Error
    string DeviceId { get; }
    void SelectDevice(string deviceId);                 // nullで止める
    float GainDb { get; set; }                          // -60〜+12
    bool Muted { get; set; }
    float PeakDb { get; }                               // 前に読んでからの最大。メーター用
}
```

### 5.2 配信用の音声の読み出し口

```c
typedef struct vv_audio_stream_tap
{
    void* context;
    // 混ぜた音声のステレオfloatのFrameを、until_qpcより後にならない範囲で最大framesだけ写し、
    // その数を返す。最初のFrameのQPCをfirst_qpcに書く。
    int32_t (*read)(void* context, float* out, int32_t frames, int64_t until_qpc, int64_t* first_qpc);
} vv_audio_stream_tap;
```

- マネージドのコードは`vv_audio_get_stream_tap`でこれを受け取り、素朴な値としてエンコーダーへ渡す。Videoは音声のDLLにリンクしない。
- 読むのは一度に1つ（録画。配信は、エンコードした同じ音声を共有する）。

## 6. Data Model

- 内部の形式：48kHz、32bit float、ステレオ。モノラルのマイクは両方のチャンネルに写す。
- 時刻：ブロックごとに最初のサンプルのQPCを持つ（WASAPIがパケットごとに渡す）。映像のFrameと同じ時計である（録画詳細設計8.1）。

## 7. 状態とライフサイクル

- `AudioService`は、Loggingの後、Videoの録画の前に始まる。音の元はそれぞれ、保存した機器を開く。機器がなければ`Unavailable`とし、現れたら開き直す（機器の通知）。
- 機器のない音の元は`Off`とする。混ぜた音声は、もう一方の元だけか、無音になる。
- 終了時に、取り込みのスレッドを止め、機器を手放す。

## 8. 主な流れ

1. 音の元ごとの取り込みのスレッド（WASAPIの共有モード、イベント駆動、10ms）が、QPCの付いたパケットを受け取る。
2. 48kHz floatステレオに変換し（WASAPI自身の変換、`AUTOCONVERTPCM`）、音量かミュートをかけ、自分のリングバッファーに書く。
3. 混ぜ合わせのスレッドが、時刻に沿って音の元を配信用のリングバッファーへ足し合わせ、ピークを−1dBFSに抑え、ピークのレベルを更新する。
4. エンコーダーが、読み出し口（5.2）を通して配信用のリングバッファーを読む。

- マイクとキャプチャーボードは別々の時計で動く。そのずれ（1秒に数サンプル）は、混ぜるときにときどき1サンプルを捨てるか繰り返して吸収し、どちらのリングバッファーもあふれないようにする。

## 9. Thread

- 音の元ごとに1つの取り込みのスレッドと、1つの混ぜ合わせのスレッド。ネイティブで、標準より高い優先度（MMCSSの「Audio」）。Allocation、待つ間のLock、ログの記録はせず、数を数えるだけとする。
- マネージドは、メインスレッドから100msごとにレベルと状態を読む。

## 10. 設定

`Profiles/Audio/<id>.json`。ProjectのAudio Profile（Project Data詳細設計6章、方式設計6.3）：

```json
{
  "schemaVersion": 1,
  "microphone": { "deviceId": "...", "gainDb": 0, "muted": false },
  "gameAudio": { "deviceId": null, "gainDb": -6, "muted": false }
}
```

- マイクは、初めて使うときにWindowsの既定の入力の機器とする。ゲームの音声には既定がない（キャプチャーボードを選ぶ必要がある）。
- `ProfileKind`に`Audio`を加える。

## 11. 保存

設定だけ（10章）。このModuleは音声を保存しない。録画はVideoが書く。

## 12. エラーと回復

- 機器を抜いた：`Unavailable`。その部分は無音になり、挿し直すと開き直す。
- 機器をほかのアプリが占有している：`Error`と説明（「ほかのアプリがこの機器を使っています」）。
- Windowsのプライバシーの設定でマイクが許可されていない：`Error`と「Windowsの設定 > プライバシーでマイクへのアクセスを許可してください」、そのページを開くButton。
- Audioの失敗は、映像も録画も止めない。録画は`AudioUnavailable`を示す。

## 13. ログと診断

- 開いた機器（名前、形式）、なくなった機器、また見つかった機器、問題を記録する。ブロックごとの記録はしない。
- 計測：音の元ごとの`Audio/Overruns`と`Audio/Underruns`、`Audio/DriftCorrections`、`Audio/MixLatency`。
- 音声そのものは、ログにも診断にも書かない（CLAUDE.md 20章）。

## 14. Testの方針

| 種類 | 対象 |
|---|---|
| ネイティブ（ctest） | 混ぜ合わせ：音量、ミュート、ピークの抑制、ずれの補正、リングバッファー、読み出し口の時刻。合成の信号で |
| EditMode | 偽のエンジンでの`AudioService`：状態、機器の消失と復帰、設定と移行 |
| 実機（手動） | 実際のマイクとキャプチャーボード：レベル、抜き差し、録画の同期（録画詳細設計14章） |

## 15. 性能

- マイクから混ぜ合わせまでの遅れ：約20〜30ms（10msの周期2つと混ぜ合わせ）。RVCは後で自分の遅れを加え、そのとき映像を遅らせて合わせる（方式設計10.9）。
- CPU：音の元2つで、1コアの1%を十分に下回る。

## 16. Folder / Namespace

```text
native/audio/                          VirtualVessel.Audio.Native（CMake、Test）
Audio/Runtime/                         IAudioService、AudioService、NativeAudioEngine、AudioSettings
Audio/Tests/EditMode/
```

新しいModule `VirtualVessel.Audio`（方式設計2.4）。DLLは`tools/native/build-audio.ps1`で作る。

## 17. 未解決の課題と段階

1. マイクとゲームの音声を配信用の音声へ混ぜる、レベル、設定（この設計）。
2. ゲームの音声を出力の機器（利用者のヘッドホン）でモニターする。音量は別に持つ（方式設計9.4）。
3. ボイスチェンジをマイクの流れの1段として加え、映像をそれに合わせて遅らせる。

## 18. UI

設定画面の「音声」のCard：

| 行 | 内容 |
|---|---|
| マイク | 機器（Dropdown）、音量（Slider、dB）、ミュート（Toggle）、レベルメーター（Level Meter、components 3.5） |
| ゲームの音声 | 機器（Dropdown、先頭は「なし」）、音量、ミュート、レベルメーター |
| 問題 | 取り込みと同じく、音の元の状態と対処 |

ライブ画面では、録画の操作の隣に、音の元ごとの小さなメーターとミュートのButtonを置き、録画中に音が入っていることが分かるようにする。
