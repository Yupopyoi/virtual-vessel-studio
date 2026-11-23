# BGM・SE詳細設計

## 1. 目的

配信で流すBGMと効果音（SE）を、アプリに登録して再生する（方式設計12章）。BGMとSEは、マイクやゲームの音声と同じく、Audio Moduleの音の元としてミキサーに入る（Audio詳細設計17章の段階4）。配信と録画は、混ぜた後の音だけを受け取り、BGMとSEを知らない（方式設計12.8）。

最初の版で扱う形式は、WAVとMP3とする（利用者の決定、2026-11-16）。BGMは1曲を選んで流し、ループするかを選べる。SEは、押すとすぐ鳴り、いくつでも重ねられる。

アプリには、すぐ使えるBGMとSEを同梱する（利用者の依頼、2026-11-17。6.3）。

後で加える前提のもの（利用者の決定、2026-11-16）。最初の版はこれらを作らないが、加えるときに作り直しが要らない形にする（17章）：

- BGMの並べ方：プレイリスト、シャッフル、繰り返し、曲の間のクロスフェード（方式設計12.4）
- 外部の機器やイベントからSEを鳴らす（方式設計12.5）。キーボードのショートカットは作った（2026-11-21。8.3）
- 扱う形式：M4A（AAC）など、Windowsがデコードできるもの
- アプリの中で作ったBGM（方式設計16.32）

曲の切り替えのクロスフェード（8.5）と、話している間だけBGMを下げるダッキング（8.4）は作った（利用者の依頼、2026-11-23）。

## 2. 責務

| 責務 | 担当 |
|---|---|
| BGMとSEのファイルの登録、名前、音量、ループ、削除（CRUD） | Audio（`AudioClipLibrary`） |
| BGMを少しずつデコードして流す、止める、フェード | Audio、ネイティブの`VirtualVessel.Audio.Native`（BGMのデッキ） |
| SEのデコードとメモリーへの保持、重ねての再生 | Audio、ネイティブ（SEのボイス） |
| 何を次に流すかを決める（1曲、後でプレイリスト） | Audio（`IBgmSequence`） |
| BGMとSEの音の元の音量、ミュート、配信とモニターへの送り、レベル | Audio（既存の音の元の仕組み） |
| 登録と設定の画面、ライブ画面の操作 | UI。各操作はCommandを通す |

## 3. Moduleの境界

- BGMとSEは、Audio Moduleの中に置く（方式設計12.15）。新しいModuleは作らない。混ぜ合わせ、バス、リミッター、メーターを共有するためである。
- ほかのModuleに見せるのは、`IAudioClipLibrary`、`IBgmPlayer`、`ISoundEffectPlayer`と、既存の`IAudioService`である。Media FoundationやWASAPIの型は見せない。
- UIは、再生を`IBgmPlayer`と`ISoundEffectPlayer`に依頼する。SEは、設定画面とライブ画面のボタンとショートカットが、同じ`PlaySoundEffectCommand`を通る（方式設計14.28。8.3）。外部の機器を加えるときも、このCommandを入口にする。
- ショートカットのキーを受け取るのはUIである（`VirtualVessel.UI.Shortcuts`。方式設計14.27）。Audioはキーの文字をそのまま保存し、意味を知らない。
- アプリの中で作るBGM（方式設計16.32）は、作った音声をこのライブラリへ登録する。作る側はライブラリの公開APIだけを使う。

## 4. 主なClassとInterface

| Class | 種類 | 役割 |
|---|---|---|
| `IAudioClipLibrary` | Interface | BGMとSEの登録、一覧、名前・音量・ループの変更、削除。結果は`CrudResult`（方式設計6.31） |
| `AudioClipLibrary` | Class | `Audio/BGM/<id>/`と`Audio/SE/<id>/`を読み書きする |
| `AudioClipKind` | Enum | `Bgm`、`SoundEffect` |
| `BuiltInAudioClips` | Class（Service） | 同梱のBGMとSEを、初めての起動時にライブラリへ登録する（6.3） |
| `AudioClipRecord` | Record | 1つの登録：ID、種類、名前（同梱のものは言語ごとの名前も）、`Settings`（`AudioClipSettings`：利用者が変える音量とループ）、`File`（`AudioClipFile`：登録したときから変わらない、ライブラリーの中のファイル、元のファイル名、長さ、登録の日時） |
| `IBgmPlayer` | Interface | BGMの再生、停止、今の状態 |
| `IBgmSequence` | Interface | 次に流す曲を決める。最初の版は`SingleTrack`（1曲、ループするかしないか） |
| `ISoundEffectPlayer` | Interface | SEを鳴らす、全部止める |
| `AudioSourceRole.Music`、`SoundEffect` | Enumの値 | ミキサーの行になる音の元（Audio詳細設計4章で予定していたもの） |
| ネイティブのBGMのデッキ | C++ | デコード用のスレッドがファイルを先読みし、リングへ書く。混ぜ合わせのスレッドがそこから読む |
| ネイティブのSEのボイス | C++ | メモリーに置いたSEを、決まった数まで重ねて鳴らす |
| `BgmMixOptions` | 構造体 | ダッキング（オン／オフ、下げる量、反応する声の大きさ）とクロスフェードの長さ。`IAudioService`が持ち、Audio Profileに保存する（6.2） |
| ネイティブのクロスフェード（`BgmCrossfader`） | C++ | BGMのデッキを2つ持ち、切り替えで交互に使い、2つを足して1つの音の元にする（8.5） |
| ネイティブのダッキング | C++ | ミキサーの中で、マイクのブロックの大きさから、BGMの音の元の音量を下げる（8.4） |

## 5. 公開API

```csharp
public interface IAudioClipLibrary
{
    CrudResult<IReadOnlyList<AudioClipRecord>> List(AudioClipKind kind);
    CrudResult<AudioClipRecord> Get(string id);
    // 登録できるか、できないならなぜか（形式、読めない、SEが長すぎる）。利用者への表示に使う。
    Task<AudioClipProblem> CheckAsync(AudioClipKind kind, string sourcePath, CancellationToken cancellationToken);
    // ファイルを写し、読めるかと長さを確かめる。重いのでメインスレッドの外で呼ぶ。
    Task<CrudResult<AudioClipRecord>> AddAsync(AudioClipKind kind, string sourcePath, string name, IReadOnlyDictionary<string, string> names, CancellationToken cancellationToken);
    CrudResult<AudioClipRecord> Update(string id, AudioClipChange change); // 名前、音量、ループ、SEのショートカット
    CrudResult Delete(string id);                // 流しているBGMはBusy
    CrudResult Move(string id, int position);    // 種類の一覧の中の位置（0が先頭）へ。後ろを越えたら最後
    IReadOnlyList<string> FileExtensions { get; } // 最初の版は wav、mp3
    event Action<AudioClipKind> Changed;          // 追加、変更、削除の後。プレイヤーが追う
}

public interface IBgmPlayer
{
    IReadOnlyObservable<BgmState> State { get; } // 曲のID、Idle / Starting / Playing / Stopping / Failed、問題
    void Play(IBgmSequence sequence);            // 流しているものとフェードで入れ替わる
    void Stop();                                 // フェードで止まる
    float? Progress { get; }                     // 0から1。流していないときはnull
}

public interface IBgmSequence
{
    // 今の曲が終わったとき（ループしない曲）、または初めに、次に流す曲。nullで終わる。
    BgmCue Next(BgmCue finished);
}

public interface ISoundEffectPlayer
{
    void Play(string id);  // 重ねて鳴らす。読み込みが済んでいなければ鳴らさず、ログに残す
    void StopAll();
    bool IsReady(string id);
}
```

- `CheckAsync`は、`AddAsync`が`Invalid`を返す理由を利用者に伝えるためにある。CRUDの結果（`CrudResult`）は失敗の種類しか持たないためである（方式設計6.31）。
- 流しているBGMは、デッキのスレッドがファイルを開いているので、消せない（`Busy`）。削除のボタンを押せなくし、理由を出す。SEは読み込んだ後はファイルを開いていないので、いつでも消せる。消すと、SEのプレイヤーがエンジンから外す。
- `BgmCue`は、曲のIDと、その曲をループするかを持つ。最初の版の`SingleTrack`は、選んだ曲を1つ返し、その曲のループの設定に従う。プレイリストとシャッフルは、`IBgmSequence`の新しい実装として加える（17章）。プレイヤーとネイティブのデッキは変えない。
- 再生の操作は、配信中に音声の仕組みを作り直さない（方式設計12.11）。音の元とバスは、起動時から持ち続ける。

`IAudioService`（Audio詳細設計5章）に、BGMの混ぜ方を加える（2026-11-23）：

```csharp
IReadOnlyObservable<BgmMixOptions> BgmMix { get; } // ダッキングとクロスフェード。Audio Profileに保存する
void SetBgmMix(BgmMixOptions options);             // すぐに効く
IReadOnlyObservable<bool> Ducking { get; }          // 今、声に合わせてBGMを下げているか（0.1秒ごと）

public readonly struct BgmMixOptions
{
    bool Ducking { get; }            // 既定はオフ（利用者の決定、2026-11-23）
    float DuckDepthDb { get; }       // 下げる量。-24〜-3 dB、既定-12
    float DuckThresholdDb { get; }   // 反応する声の大きさ。-60〜-20 dBFS、既定-40
    float CrossfadeSeconds { get; }  // 曲の切り替えの長さ。0〜5秒、既定2。0は今までの切り替え
}
```

- 混ぜ方はミキサーの仕事なので、`IBgmPlayer`ではなく`IAudioService`に置く。`BgmPlayer`は変えない。

## 6. Data Model

### 6.1 登録したファイル

Data Rootの`Audio/`に置く（方式設計6.10、12.6）。

```text
Audio/
├─ BGM/
│  ├─ order.json            利用者が並べた順（下）
│  └─ bgm-<id>/
│     ├─ audio.mp3          元のファイルの写し（拡張子は元のまま）
│     └─ audio-profile.json
└─ SE/
   ├─ order.json
   └─ se-<id>/
      ├─ audio.wav
      └─ audio-profile.json
```

```json
{
  "schemaVersion": 1,
  "name": "ふわふわ午後",
  "gainDb": 0,
  "loop": true,
  "durationSeconds": 31.0,
  "originalName": "fuwafuwa.mp3",
  "addedAtUtc": "2026-11-16T12:00:00Z",
  "shortcut": "Ctrl+Alt+1"
}
```

- 元のファイルは変えずに写す。再生のたびにデコードする。変換した写しを持つと、MP3が約10倍の大きさのWAVになるためである。
- 追加は、隣のフォルダー（`.part`）へ写してから名前を変える。作りかけを一覧に出さないためである（VoiceModelStoreと同じ）。
- `loop`はBGMだけが使う。SEは`false`のままで、使わない（設定のファイルはnullも書くため、省けない）。
- この版が知らない項目は、消さずに書き戻す（CLAUDE.md 25章）。後で加える項目の例：作ったBGMの作り方（方式設計16.32）。
- `shortcut`（SEだけ）：どのアプリを使っていても、そのSEを鳴らすキー（8.3）。`Ctrl`、`Alt`、`Shift`、キーの順に`+`でつなぐ（`"Ctrl+Alt+1"`）。どの言語でも同じ文字である。ないときはnull。40文字まで。音量と同じく素材の性質として、ここに持つ。項目を加えただけなので、schemaVersionは1のままとする。
- `name`は1〜60文字。同じ名前を許す（IDで区別する）。
- `names`（同梱のものだけ）：言語ごとの名前（`{ "ja": "サンプル１（ポップ）", "en": "Sample 1 (Pop)" }`）。利用者が名前を変えるまで持ち、変えたら消す（音量やループを変えても残す）。画面には`AudioClipRecord.NameIn(言語)`の名前を出す：画面の言語の名前、なければ英語、なければ`name`。項目を加えただけなので、schemaVersionは1のままとする（前の版は知らない項目として残す）。
- 一覧（`List`）の順は、利用者が並べた順である（2026-11-20）。`order.json`（`{ "schemaVersion": 1, "ids": ["se-…", …] }`）に、種類ごとにIDを先頭から持つ。そこにないもの（並べ替えの後に追加したもの、並べ替えたことがないもの）は、その後ろに追加した順で並ぶ。消したもののIDは読み飛ばし、次の並べ替えで消える。順を1つのファイルに持つのは、並べ替えのたびに全部の`audio-profile.json`を書き換えないためである。設定画面とライブ画面のどちらも、この順で出す。

### 6.2 Audio Profileの音の元

Audio Profile（Audio詳細設計10章）の`sources`に、`music`と`soundEffect`の音の元を1つずつ加える。機器を持たない。

```json
{ "id": "...", "role": "music", "gainDb": -6, "muted": false, "sends": { "stream": 0, "monitor": 0 } }
```

- 古いファイルにないときは、既定の値で加える：BGMは-6 dB、SEは0 dB。送りは配信とモニターの両方（Audio詳細設計5.2の既定）。
- BGMの混ぜ方（5章の`BgmMixOptions`）は、Audio Profileの`bgm`に持つ（2026-11-23）：`"bgm": { "ducking": false, "duckDepthDb": -12, "duckThresholdDb": -40, "crossfadeSeconds": 2 }`。項目を足しただけなのでschemaVersionは1のまま。ないファイルは既定の値で読み、範囲の外の値は範囲に収める。
- 最後に流していたBGMと、それを流していたかは保存しない。起動していきなりBGMが流れないようにするためである。プレイリストを加えるときに、選んだプレイリストだけを保存する。

### 6.3 同梱のBGMとSE

アプリには、すぐ使えるBGMとSEを同梱する。プロジェクトが作った音源で、ライセンスはCC0 1.0である（利用者の決定、2026-11-20）。配信や動画（収益化を含む）で、許可もクレジットもなく使えるようにするためである。アプリ本体と、音を作るスクリプト（`tools/audio/`）はApache-2.0のままとする。CC0であることは、フォルダーの`LICENSE.txt`（日本語と英語の説明と、CC0の全文）に書き、SBOMの部品「Virtual Vessel Studio built-in BGM and sound effects」として、設定画面のクレジット（設定画面8章）に出す。

```text
StreamingAssets/Audio/BuiltIn/
├─ built-in-audio.json      一覧：ID、種類、ファイル、日本語と英語の名前
├─ LICENSE.txt              CC0 1.0
├─ bgm/<name>.wav
└─ se/<name>.wav
```

```json
{ "schemaVersion": 1, "clips": [ { "id": "bgm-cute-pop", "kind": "bgm", "file": "bgm/cute-pop.wav", "names": { "ja": "サンプル１（ポップ）", "en": "Sample 1 (Pop)" } } ] }
```

- 起動の後に、`BuiltInAudioClips`が、まだ登録していないものを`AddAsync`で登録する。ほかの登録と同じく、Data Rootへ写す。利用者は名前、音量、ループを変えられ、消せる。起動を待たせないよう、登録はバックグラウンドで行う。
- 登録したものは、Data Rootの`Audio/built-in-audio.json`（`{ "schemaVersion": 1, "added": ["bgm-cute-pop", ...] }`）に覚える。1つ登録するたびに書く。利用者が消したものは、もう登録しない。後の版で増えたものだけを登録する。登録済みのものは、後の版で音や名前を変えても置き換えず、なくしても消さない。利用者が名前や音量を変えたかもしれない、利用者の写しだからである。効果音は、最初の公開の前に利用者と見直した（2026-11-20）：ピンポーンを高く、ブブーを明るく長く低く、ドラムロールの最後のシンバルを派手に、拍手を大勢に。「正解」を「キラキラ２」として「キラキラ１」の隣へ、「ポン」を「ポコッ」へ改名し、ファンファーレとツッコミをなくした。BGMの名前は「サンプル１（ポップ）」のように番号と種類にし、41平均律のローファイ（「サンプル６（ローファイ）」）を加えた。調律は利用者に見せない：名前とファイル名に「41」などを入れない（利用者の指示、2026-11-20）。サンプル６は、配信の後ろで濁りが気にならないよう、92BPMの明るい長調（C–G–Am–F／C–G–F–G7）にした。和音はセブンスでなく9度を足し、メロディーは短い音で動く。中立三度、テープの揺れ、ノイズは入れない。41平均律らしさは、G7の自然七度（メロディーは短く通るだけ）と、フレーズの頭のわずかなずり上げだけにする（利用者の確認、2026-11-20）。いろいろな楽器がにぎやかに鳴る「サンプル７（マーチ）」を加えた（おもちゃ箱のような曲として作ったが、名前はほかと同じく種類にする）（12平均律、132BPM）：シロフォン、チューバとピチカートの行進、グロッケン、ウッドブロック、金管の合奏、クラリネット、スライドホイッスル。トランペット、ホルン、シロフォン、ウッドブロックの音は`tools/audio/synth.py`に加えた。トランペットとホルン（チューバも同じ作り）は、人が吹く音に近づける（利用者の要望）：少し低く入って唇で音程へ上げる、タンギングと息の音、強いほど明るい音、長い音のふくらみ、遅れてかかり揺らぐビブラート、1音ごとのわずかなずれ。2本目のトランペットはハ長調の音だけで3度下を吹き、ホルンが和音を支える。
- 登録できなかったもの（Media Foundationがないなど）は覚えず、次の起動でもう一度試す。ほかのものの登録は続ける。
- 登録には、`name`（Windowsの表示言語で選んだ名前、`Localizer.ChooseInitialLocale`）と、全部の言語の名前（`names`）を渡す。利用者が名前を変えるまでは、画面の言語に合わせて表示する（設定画面とライブ画面。言語を切り替えると出し直す）。英語の画面で曲名だけ日本語になるのを避けるためである（利用者の要望、2026-11-20）。利用者が名前を変えたら、それは利用者のデータであり、どの言語でもその名前を出す。名前の変更で、表示している名前をそのまま保存したときは、変えたことにしない。
- ファイルはMP3ではなくWAV（モノラル、48 kHz、16 bit）とする。MP3は、エンコーダーの足す無音が頭と終わりにあり、Media Foundationはそれを除かないため、ループの継ぎ目が途切れる。モノラルにしたのは、元の曲の左右の違いが数ミリ秒の遅れだけで、大きさを半分にできるためである。合わせて約20 MB。
- 音源は`tools/audio/build_builtin_audio.py`が作る（numpyとscipy。`tools/audio/requirements.txt`）。乱数の種が決まっているので、何度作っても同じファイルになる。BGMは音の大きさ（RMS -19 dBFS）、SEはピーク（-3 dBFS）でそろえる。

## 7. 状態とライフサイクル

- `AudioService`は、起動時にBGMとSEの音の元をミキサーに作る。ネイティブのデッキとSEのボイスは、エンジンと一緒に作り、終了時に一緒に止める。
- SEは、起動後にメインスレッドの外で、登録したものを全部デコードしてネイティブへ渡す。追加したときは、そのSEだけを渡す。削除したときは外す。
- BGMの状態：`Idle` →（Play）`Starting`（先読み）→ `Playing` →（Stop、または終わってSequenceが何も返さない）`Stopping`（フェード）→ `Idle`。読めないときは`Failed`。
- BGMのプレイヤーは、曲がある間だけ毎フレームの処理を持ち、0.1秒ごとにデッキの状態を読む。曲がなければフレームの負荷はない。
- 流しているBGMやSEを削除するときは、先にそれを止める。

## 8. 主な流れ

### 8.1 BGMを流す

1. UIが`IBgmPlayer.Play`に、選んだ曲の`SingleTrack`を渡す。
2. `BgmPlayer`が`Next`で最初の曲を受け取り、ネイティブのデッキへファイルのパス、ループ、曲の音量を渡す。
3. デッキのデコード用のスレッドが、Media Foundationでファイルを開き、48 kHzのfloatのステレオにして、約1.5秒先までリング（約2.7秒）へ書く。足りなくなる前に書き足す。チャンネルは、モノラルを両側へ、3つ以上は前の左右を使う。レートは、プラグインが4点のHermite補間で変える（2026-11-16）。Media Foundationには形式だけを頼み、レートとチャンネルはファイルのままで受け取る。変換の状態を曲の間で持ち続けるので、少しずつ読んでも、ループで頭へ戻っても継ぎ目が出ない。44.1 kHzの1 kHzの音で、誤差は0.0004だった。
4. 混ぜ合わせのスレッドは、10 msごとにリングから1ブロックを読み、BGMの音の元として、音量、ミュート、送りをかけてバスへ足す。ファイルは読まない。
5. ループする曲は、デコード用のスレッドが終わりで頭へ戻り、続けて書く。継ぎ目に無音は入らない。
6. ループしない曲が終わると、デッキが終わりを知らせる。`BgmPlayer`は`Next`を呼び、次の曲があればそれを流し、なければ`Idle`になる。

- 停止では、流しているものを約0.3秒で下げる。何も流していないときに始める曲は、頭から約0.05秒で上げる。プツッと鳴らないためである。曲を流している間の切り替えは、クロスフェード（8.5）で入れ替わる。長さが0秒のときは、約0.3秒で下げてから新しい曲を約0.05秒で上げる。
- リングが空になった（デコードが遅れた）ときは、その分を無音にして数える（`Audio/Music.Underruns`）。

### 8.2 SEを鳴らす

1. `PlaySoundEffectCommand`が`ISoundEffectPlayer.Play(id)`を呼ぶ。
2. ネイティブのSEのボイスの空きに、そのSEを頭から入れる。次の10 msのブロックから鳴る。
3. 空きがなければ、一番古く鳴り始めたものを短く下げて入れ替える。
4. ボイスの数は16とする。ボイスごとに位置と曲の音量だけを持ち、サンプルはSEごとに1つを共有する。

- SEはファイルの音量（`gainDb`）を、ボイスに入れるときにかける。SEの音の元全体の音量とミュートは、ミキサーでかける。

### 8.3 ショートカットでSEを鳴らす

配信中は、ゲームなどほかのアプリを前に出していることが多い。そのため、SEのショートカットは、Virtual Vessel Studioが前にないときも効く（利用者の決定、2026-11-21）。WindowsのRegisterHotKeyを使う。

1. `SoundEffectShortcuts`（アプリの起動から終了まで生きるサービス）が、ライブラリのSEの一覧から、キーとSEの対応を作る。2つのSEに同じキーがあれば、一覧で先のものを使う。ライブラリが変わると作り直す。
2. `WindowsGlobalShortcuts`が、専用のスレッドでキーを登録し、そのスレッドのメッセージループでWM_HOTKEYを待つ。ホットキーは登録したスレッドに届くため、専用のスレッドにする。Unityのフレームに頼らないので、ゲームの裏でフレームが遅くなっても遅れない。
3. キーが押されると、そのスレッドで`PlaySoundEffectCommand`を実行する。`SoundEffectPlayer.Play`はどのスレッドからでも呼べる（中でLockを取る）ので、メインスレッドへ渡さない。
4. キーには`Ctrl`、`Alt`、`Shift`のどれかが要る。ゲームが単独のキーで使うキーを奪わないためである。押し続けても繰り返さない（`MOD_NOREPEAT`）。使えるキーは、英字、数字、テンキーの数字、F1〜F12である。記号のキーはキーボードの配列で位置が変わるため除く。
5. ほかのアプリが同じキーを持っているとWindowsが断る。断られたキーは`Refused`に入り、設定画面の行に出す（`setup.md` 14章）。キーの組み合わせを変えるたびに、断られたキーをもう一度試す。
6. 設定画面でキーを割り当てている間は、すべてのキーの登録を外す（`Paused`）。押したキーが効果音を鳴らさず、画面に届くようにするためである。

- RegisterHotKeyは、登録したキーが押されたことだけを知らせる。ほかのキーの入力は読まない。キーの入力を記録もしない。
- 終了時は、スレッドがキーの登録をすべて外してから終わる。外さないと、ほかのアプリがそのキーを使えないままになる。

### 8.4 声に合わせてBGMを下げる（ダッキング）

話している間だけBGMを下げ、話し終わると戻す（利用者の依頼、2026-11-23）。既定はオフで、設定画面のBGMのCardで、オン／オフ、下げる量、反応する声の大きさを変える。ライブ画面にもオン／オフを置く（`setup.md` 14章、`live.md`）。

1. 混ぜ合わせのスレッドが、マイクの音の元のブロックの大きさ（ピーク）を、音量とミュートをかけた後で測る。声の変換の段（Audio詳細設計8.3）の後なので、変換した声で測る。ミュートしているときは声がないものとして下げない。
2. 次のブロックの初めに、その大きさが「反応する声の大きさ」以上ならBGMを下げる。下回ってから約0.4秒は下げたままにし（言葉の間で上下しないため）、その後に戻す。下げるのは約30 ms、戻すのは約0.5秒で、なめらかに動かす（`Duck`、dsp.hの純粋な関数）。
3. 下げる分は、BGMの音の元の音量に掛ける。配信とモニターの両方に効き、レベルメーターにも下げた大きさが出る。SEとゲームの音声は下げない。

- 音の元は決まった順に混ぜるので、マイクの大きさは1つ前のブロック（10 ms前）のものを使う。聞いてわかる遅れではない。
- ネイティブのAPI：`vv_audio_set_ducking(engine, key_source, ducked_source, enabled, depth_db, threshold_db)`、今の下げ幅を読む`vv_audio_get_ducking(engine, &gain_db)`（API version 13）。設定はatomicで渡し、混ぜ合わせのスレッドはLockを取らない。
- `IAudioService.Ducking`は、下げ幅が1 dBより大きい間trueになる。設定画面で「下げています」を出し、感度を合わせる目安にする。

### 8.5 曲の切り替えのクロスフェード

曲を流している間に別の曲を流すと、前の曲を下げながら、新しい曲を頭から上げる（利用者の依頼、2026-11-23）。長さは0〜5秒で、既定は2秒。効くのは切り替えだけで、停止は今までどおり約0.3秒で下げる（利用者の決定、2026-11-23）。

1. ネイティブの`BgmCrossfader`が、BGMのデッキ（8.1）を2つ持つ。混ぜ合わせのスレッドは、2つのブロックを足して、1つのBGMの音の元として読む。
2. 曲を流している間の`Play`は、今のデッキに長さの分で下げることを頼み、もう一方のデッキで新しい曲を同じ長さで上げる。下げ終えたデッキは先読みを捨てて空く。
3. 何も流していないとき、長さが0のとき、ループしない曲が終わって次の曲へ移るときは、今までどおり1つのデッキで流す。
4. 下げている途中のデッキでもう一度切り替えたとき（長さより短い間に2回切り替えたとき）は、そのデッキの下げを約0.3秒に縮めてから次の曲を始める。新しい曲を長く待たせないためである。

- 2つのデッキは、再生の番号（generation）を1つの数え方で分け合う。状態は最後に頼んだデッキのものを返し、足りなかった数は2つを足す。`BgmPlayer`は変えない。
- 音量は直線で動かす。音の大きさをそろえた同梱のBGM（6.3）では、重なる間に大きく膨らまない。
- ネイティブのAPI：`vv_audio_bgm_set_crossfade(engine, milliseconds)`（API version 13）。次の切り替えから効く。
- 2つ目のデッキのぶん、リング（約1.6 MB）とデコード用のスレッドが1つ増える。使っていない間、スレッドは待つだけである。

## 9. Threadと非同期

- 混ぜ合わせのスレッド（既存）：デッキのリングとSEのボイスを読むだけ。ダッキング（8.4）の計算もここで行う。Allocation、待つ間のLock、ファイルの読み込みはしない（Audio詳細設計9章）。
- デッキのデコード用のスレッド（新規、ネイティブ、通常の優先度）：Media Foundationでデコードし、リングへ書く。リングは書き手1つ、読み手1つで、Lockを取らない。
- SEのデコード：マネージドで、メインスレッドの外で行い、できたサンプルをエンジンのLockの中でネイティブへ渡す（入れ替えだけ。プレビューと同じ。Audio詳細設計5.5）。
- 登録（ファイルの写しと確認）：マネージドで、メインスレッドの外で行う。
- ショートカットのスレッド（新規、マネージド）：キーの登録と、押されたときのSEの再生（8.3）。メインスレッドを待たない。

## 10. 設定

- 6.2の音の元の音量、ミュート、送り。
- BGMの混ぜ方（ダッキング、クロスフェード）。Audio Profileの`bgm`（6.2）。
- 曲ごと、SEごとの音量とループは、登録のファイル（6.1）に持つ。ProjectのProfileではない。素材の性質であり、Projectが変わっても同じだからである。

## 11. 保存

6.1と6.2のとおり。BGMとSEのファイルは利用者のAssetとして扱う（CLAUDE.md 25章）。

## 12. エラーと回復

| 起きたこと | 動作 | 利用者への表示 |
|---|---|---|
| 登録するファイルを読めない、形式が違う | 登録しない（`Invalid`） | 音声ファイルを読み込めません |
| SEが30秒より長い | 登録しない（`Invalid`） | 効果音は30秒までです |
| 再生するファイルがない、壊れている | `Failed`。ほかの音は止めない | このBGMを再生できません |
| デコードが遅れた | 足りない分を無音にして数える | 表示しない。診断に数える |
| Media Foundationがない（Nエディション） | BGMとSEを使えない | 音声ファイルを読み込めません。Media Feature Packの案内 |
| ショートカットのキーをほかのアプリが持っている | そのキーでは鳴らない。ボタンでは鳴る | その行に「キーをほかのアプリが使用中」（警告の形と文字） |
| ほかのSEが同じキーを持っている | 割り当てない | このキーはほかの効果音で使っています |

- BGMとSEの失敗は、マイク、配信、録画を止めない（方式設計12.11）。

## 13. ログと診断

- 登録、削除、再生の開始と終わり、失敗を記録する。曲はIDで書き、ファイルのパスと名前は書かない（利用者のファイルの名前は個人の情報になりうる）。
- 失敗では、形式、レート、チャンネル数、HRESULTを記録する（方式設計12.14）。
- ショートカットは、ほかのアプリに断られたキーを、変わったときに記録する。押されたことは記録しない。
- BGMの混ぜ方を変えたときに、その値を記録する。下げ始めと戻りは、回数が多いので記録しない。
- 計測：`Audio/Music.Underruns`、`Audio/SoundEffect.VoicesStolen`。
- 音声そのものは記録しない。

## 14. Testの方針

| 種類 | 対象 |
|---|---|
| ネイティブ（ctest） | 書き出したWAVとMP3でのデッキ：ループの継ぎ目に無音がないこと、終わりの知らせ、フェード、リングが空のときの無音。SEのボイス：重ね、空きのないときの入れ替え。ステレオのまま混ぜること。ダッキング：`Duck`の動き（下げる、間を保つ、戻る、オフ）、ミキサーで声のある間だけBGMが下がり、SEは下がらないこと、ミュートしたマイクでは下がらないこと。クロスフェード：2つの曲が重なり、前の曲が消え、新しい曲が上がること、0秒では今までどおりのこと、続けて2回切り替えたこと |
| EditMode | `BuiltInAudioClips`：初めての起動での登録と名前の言語、2回目に足さないこと、消したものを戻さないこと、新しいものだけを足すこと、失敗したものを次に残すこと、同梱の一覧の正しさ。本物のプラグインで、同梱のファイルが全部登録できること。`AudioClipLibrary`：追加、一覧、変更、削除、並べ替え（新しいものが後ろに付くこと、消したものが隙間を残さないこと）、同梱のものの言語ごとの名前（名前を変えるまで残ること）、作りかけのフォルダー、知らない項目を残すこと、長すぎるSE。`SingleTrack`、偽のエンジンでの`BgmPlayer`の状態。`BgmMixOptions`の範囲と保存、古いファイルの既定値、エンジンへの反映、`Ducking`の追従。SEのショートカットの保存と削除。`KeyChord`の読み書き、キーの対応（同じキーは先のもの）、押されたSEの再生と一覧への追従、本物のWindowsで、ほかが持つキーを断られ、放されたら取れること |
| PlayMode | 設定画面とライブ画面の操作（画面仕様とともに）。ショートカットの割り当て、取り消し、削除、同じキーの拒否、ほかのアプリが持つキーの表示、ライブ画面のツールチップ |
| 実機（手動） | 配信と録画にBGMとSEが入り、モニターの送りが効くこと |

## 15. 性能

- 混ぜ合わせのスレッドの仕事は、ブロックごとに、リングからの写しと、鳴っているSEのボイスの足し合わせだけ。
- デコード：MP3は1コアの1%を十分に下回る見込み。デッキのリングは約2秒（約0.8 MB）。
- SEのメモリー：30秒のステレオで約11.5 MB。登録の数が多い場合の上限は、実装時に測って決める（17章）。

## 16. Folder / Namespace

```text
native/audio/src/        bgm_deck（デコード用のスレッドとリング）、bgm_crossfade（2つのデッキ）、mixer（ダッキング）、sound_effects（ボイス）、audio_file（ステレオのデコードを加える）
native/audio/include/    vv_audio.h（API version 13）
Audio/Runtime/Clips/     IAudioClipLibrary、AudioClipLibrary、AudioClipRecord、BuiltInAudioClips
StreamingAssets/Audio/BuiltIn/  同梱のBGMとSE（6.3）
tools/audio/             同梱の音源を作るスクリプト
Audio/Runtime/Playback/  IBgmPlayer、BgmPlayer、IBgmSequence、SingleTrack、ISoundEffectPlayer
UI/Runtime/Shortcuts/    KeyChord、IGlobalShortcuts、WindowsGlobalShortcuts、SoundEffectShortcuts、ShortcutButton
UI/Runtime/Commands/     PlaySoundEffectCommand
UI/Runtime/Screens/      BGMとSEのCard（設定画面）、ライブ画面の操作
```

## 17. 段階と未解決

段階：

1. ネイティブ：ステレオのデコード、BGMのデッキ（ループ、終わりの知らせ、フェード）、SEのボイス。完了（2026-11-17）。
2. マネージド：音の元、ライブラリ（CRUD）、`BgmPlayer`、`SingleTrack`、`SoundEffectPlayer`。完了。BGMは、UIから各プレイヤーのメソッドを直接呼ぶ。
3. 設定画面：ミキサーの行、BGMとSEのCard（`setup.md` 14章）。完了。
4. ライブ画面：BGMの選択、再生、停止、音量。SEのボタン（`live.md`）。完了。
5. 同梱のBGMとSE（6.3）。完了（2026-11-17）。
6. SEのショートカット（8.3）。完了（2026-11-21）。
7. ダッキング（8.4）と曲の切り替えのクロスフェード（8.5）。ライブ画面で再生中に曲を切り替えられるようにする。

本物のプラグインでの結合テスト（`AudioPlayersIntegrationTests`）が、WAVの登録、BGMの最後までの再生、再生中の削除の拒否、SEの読み込みと再生を確かめる。

後で加えるもの（作り直さずに加える場所）：

- プレイリスト、シャッフル、繰り返し：`IBgmSequence`の実装と、プレイリストの保存（`Audio/Playlists/`）。
- プレイリストの曲の間のクロスフェード、曲間なし：2つのデッキ（8.5）で、今の曲が終わる前に次の曲を始める。
- M4Aなど：`FileExtensions`を広げる。デコードはMedia Foundationなので、コードの変更は確認だけで済む見込み。
- アプリで作ったBGM（方式設計16.32）：作った音声を`AddAsync`で登録する。

未解決：

- SEの数とメモリーの上限。
