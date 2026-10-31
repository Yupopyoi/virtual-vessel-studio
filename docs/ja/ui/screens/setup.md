# セットアップ画面

## 1. 目的

配信の前に、使う機器や設定を整える画面。使っているソフトウェアのクレジットも、ここで見られる。

映像の取り込み（Capture）のCard、音声のCard（11章）、トラッキングのCard、アバターのCard、表情の調整のCard（9章）、録画のCard（10章）、アプリの設定のCard（UI基盤詳細設計7.7）、クレジットのCard（8章）を置く。ほかの設定は、それぞれの機能を作るときに加える。

- 画面の名前（ナビゲーションと見出し）は「設定」（英語 Settings）とする。機器の準備だけでなく、アプリの設定やクレジットも置くためである。
- 画面の識別子（`setup`）、文字列のKey（`nav.setup`、`setup.*`）、文書の中の呼び方（セットアップ画面）は変えない。識別子は`ui-settings.json`の`lastScreen`に保存されているため、変えると前回の画面に戻れなくなる。

## 2. 構成

上から順に、Cardを縦に並べる。

1. 映像の取り込み（ゲーム画面）
2. 映像の取り込み（サブモニター）
3. 音声（11章。音声を使えないときは「音声は使えません。」の一文）
4. トラッキング（4章）
5. アバター（5章）
6. 表情の調整（9章。アバターを使えるときだけ）
7. 録画（10章。録画を使えないときは「録画は使えません。」の一文）
8. アプリの設定
9. クレジットとライセンス（8章）

- トラッキング用カメラは、取り込みのCardを持たない。トラッキングのCardで選ぶ。
- どのCardも折りたためる（`components.md` 3.10）。項目が増えても画面が長くならないよう、画面を開いた時点ではすべて閉じている。開閉の状態は保存せず、起動するたびに閉じた状態から始める。
- 状態（Status Indicator）は、Cardのタイトル部分の右端に置く。閉じていても状態が分かる。問題の説明は本文の先頭に置く。

## 3. 映像の取り込みのCard

Capture Sourceごとに1枚。`ICaptureDeviceSource`（Capture）を直接表示する。

| 行 | 部品 | 内容 |
|---|---|---|
| 状態 | Status Indicator（`components.md` 3.13） | 状態と、問題があればその説明（6章） |
| 映像 | 画像（256×144、16:9に収める） | 取り込んだ映像を小さく表示し、選んだ機器が合っているか確かめられるようにする。映像がないときは「映像なし」 |
| 取り込む | Toggle | オンで開始、オフで停止。開始していたかは保存し、次の起動で戻す |
| 機器 | Dropdown | 見つかった機器。ゲーム画面は先頭に「カメラを選択」、サブモニターは先頭に「自動（メイン以外のモニター）」 |
| 音声（ゲーム画面のみ） | Dropdown | ゲームの音を取り込む音声の機器（`GameAudioRow`）。音声のCard（11章）の「ゲームの音声」のデバイスと同じ設定で、どちらから変えてもよい。映像の機器を選ぶと、名前が一致する音声の機器を自動で選ぶ。音声がないときは行を置かない |
| 音声（ゲーム画面のみ） | Dropdown | ゲームの音を取り込む音声の機器（`GameAudioRow`）。音声のCard（11章）の「ゲームの音声」のデバイスと同じ設定で、どちらから変えてもよい。映像の機器を選ぶと、名前が一致する音声の機器を自動で選ぶ。音声がないときは行を置かない |
| 機器の一覧 | Button（Secondary）「探し直す」 | 機器の一覧を取り直す |
| 左右反転 | Toggle | 映像の向きを直す |
| 上下反転 | Toggle | 映像の向きを直す |
| マウスカーソルを表示（サブモニターのみ） | Toggle | 既定はオン |
| 実際の映像 | 説明文（等幅、Text 2） | 解像度とフレームレート（例：`1920×1080 · 59.9 fps`）。取り込んでいないときは「—」 |

- 設定の変更はすぐに反映し、保存する（Capture詳細設計10章）。
- 取り込むToggleは状態に合わせる。機器がなくて待っている間（`Unavailable`）もオンのまま。

## 4. トラッキングのCard

`ITrackingSource`（Tracking）とトラッキング用カメラ（Captureの`camera`）を直接表示する（Tracking詳細設計5.4、11章）。

| 行 | 部品 | 内容 |
|---|---|---|
| 状態 | Status Indicator | 状態と、問題があればその説明（4.1） |
| 映像 | 画像（256×144、16:9に収める）と、その上の点と骨 | カメラの映像に、取れている体の点と骨、手の点と指を重ねる。点は`Primary`、骨は`Accent Line`、それぞれ`On Accent`の縁取りを付け、明るい映像の上でも暗い映像の上でも見えるようにする。約33msごとに描き直す。配信には出さない |
| 検出 | 説明文（Text 2） | 部位ごとに印と言葉：「顔 ✓　体 ✓　左手 –　右手 ✓」。✓は取れている、–は見失った、×は無効。色だけで伝えない。止まっているときは表示しない |
| 負荷 | 説明文（等幅、Text 2） | 1秒ごと：「アプリ 60 fps · トラッキング 30 回/秒 · CPU 12% · GPU 8%」。CPUはこのアプリが全コアのうち使った割合、GPUは1Frameのうちこのアプリの描画にGPUを使った時間の割合（UnityのFrameTimingManager。分からない環境では「—」）。システム全体のGPU使用率はUnityから読めないため出さない |
| トラッキングする | Toggle | オンで開始（カメラも開く）、オフで停止。開始していたかは保存し、次の起動で戻す |
| 鏡写しにする | Toggle | 既定はオン。カメラの映像と点を左右反転して、鏡のように見せる。アバターも同じ設定に従う（Tracking詳細設計10章） |
| 動かす範囲 | Segmented（上半身、全身（準備中）） | 上半身。全身はまだ選べないが、今後対応することが分かるよう、無効の色で表示する（Tracking詳細設計6.5、`components.md` 3.18）。説明：「座って配信するときの範囲です。全身はこれから対応します。」英語：Upper body、Full body (soon) |
| 使う部分 | Toggle「顔」「体」「手」を縦に並べる | 既定はすべてオン。切った部分は推論しないため負荷が下がり、その部分は動かない（Tracking詳細設計6.5）。「手」は両手と指をまとめて切り替える。説明：「使わない部分を切ると、負荷が下がります。切った部分は動きません。」どの言語でも行のControl（最大260px）に収まるよう、縦に並べる。変えるとすぐに反映し、保存する |
| 体の動き | Segmented（3D、2D） | 既定は3D。2Dでは、体の奥行きを使わず、正面から見た動きだけでアバターを動かす（Tracking詳細設計8.6）。説明：「2Dでは、カメラへ近づく・離れる動きを使わず、正面から見た動きだけでアバターを動かします。腕の向きが安定します。」変えるとすぐに反映し、保存する |
| 動きの速さ | Slider（1〜10、1刻み） | 平滑化の段階（Tracking詳細設計8.2）。説明：「大きくするほど動きが速く、小さくするほど揺れが少なくなります。」既定は5（2026-10-24に段階を2つ、さらに4つずらし、遅れ300ms程度までの安定な段階を5とした）。変えるとすぐに反映し、保存する |
| 体の検出 | Segmented（軽量、標準） | Poseのモデル（Tracking詳細設計10章）。既定は標準。説明：「軽量は負荷が下がりますが、体のとらえ方が粗くなります。変えると、数秒かけて準備し直します。」トラッキング中に変えると、新しいモデルで開始し直す。保存する。Heavy（「高精度」）は出さない。利用者のカメラでは標準より悪くなった（2026-10-26） |
| カメラ | Dropdown | 見つかった機器。先頭に「カメラを選択」。選ぶまでカメラは開かない |
| 音声（ゲーム画面のみ） | Dropdown | ゲームの音を取り込む音声の機器（`GameAudioRow`）。音声のCard（11章）の「ゲームの音声」のデバイスと同じ設定で、どちらから変えてもよい。映像の機器を選ぶと、名前が一致する音声の機器を自動で選ぶ。音声がないときは行を置かない |
| 機器の一覧 | Button（Secondary）「探し直す」 | 機器の一覧を取り直す |

### 4.1 トラッキングの状態の表示

| 状態 | Chip | 文字 |
|---|---|---|
| 停止中 | Idle | 停止中 |
| 準備中 | Idle | 準備中 |
| トラッキング中 | Success | トラッキング中 |
| 人が映っていない | Warning | 人が映っていません。説明：「カメラの前に座り、顔と上半身が映るようにしてください。」 |
| カメラを選ぶ必要がある | Warning | カメラを待っています。説明：「「カメラ」でトラッキングに使うWebカメラを選んでください。」 |
| カメラが見つからない | Warning | カメラを待っています。説明：「カメラが見つかりません。つなぐと自動で見つけ直します。」 |
| ゲーム画面と同じカメラ | Error | トラッキングできません。説明：「このカメラはゲーム画面の取り込みで使っています。別のカメラを選んでください。」 |
| 失敗 | Error | トラッキングできません。説明：「カメラを開けないか、トラッキングを始められませんでした。ほかのアプリがカメラを使っていないか確かめてください。」 |

- MediaPipeのPluginがない等の開発者向けの問題は、利用者には「失敗」として表示する。
- ToggleはカメラやMediaPipeを待っている間もオンのまま。

## 5. アバターのCard

`IAvatarService`（Avatar）を直接表示する（Avatar詳細設計5.1、6.2、6.4）。

| 行 | 部品 | 内容 |
|---|---|---|
| 状態 | Status Indicator | 状態と、問題があればその説明（5.1） |
| 画像 | 画像（96×96、正方形に収める） | 使うアバターのモデルに入っている画像。なければ「画像なし」 |
| 使うアバター | Dropdown | 先頭に「なし」、続けて登録したアバターの名前。読み込めないアバターは「読み込めないアバター」。選ぶとすぐ読み込んで差し替える |
| 形式 | 説明文（Text 2） | 「VRM 1.0」。VRM 0.xは「VRM 0.x（読み込むときに1.0へ変換）」。なければ「—」 |
| 登録 | Button（Primary）「アバターを登録…」 | ファイルを選ぶ画面（VRMファイルだけ）を開き、確認のダイアログ（5.2）の後に登録する |
| 登録の解除 | Button（Danger）「登録を解除」 | 使っているアバターの登録を解除する。Dangerの確認ダイアログ（「このアプリにコピーしたモデルを削除します。元のファイルは残ります。」）を経てから行う |
| 利用条件（モデルの作者による） | 名前と値の行の並び（値はText 2） | モデル名、作者、アバターとして使える人、商用利用、過度な暴力表現、過度な性的表現、政治・宗教での利用、反社会的・憎悪表現、クレジット表記、再配布、改変、（VRM 0.xのみ）ライセンス名、ライセンスのURL、その他の条件のURL。ファイルに書かれていない条件は「記載なし」。`https`のURLだけ横に「開く」ボタンを置き、押したときだけブラウザで開く |

- 操作の間（ファイルを選ぶ、登録、解除、読み込み）はボタンを無効にし、1つずつ行う。
- 登録の失敗（VRMではない、大きすぎる、骨が足りない、読み込めない、登録済み、保存できない）は、失敗の通知（`components.md`のNotice）で5.1の説明を出す。
- 作者の書いた文は、そのまま文字として表示する（Rich Textとして解釈しない）。

### 5.1 アバターの状態の表示

| 状態 | Chip | 文字 |
|---|---|---|
| 未選択 | Idle | 未選択 |
| 読み込み中 | Idle | 読み込み中 |
| 表示中 | Success | 表示中。差し替えに失敗したときは、その説明を添える |
| 読み込めない | Error | 読み込めません。説明は問題による：「人型のボーンが足りないため、このモデルは使えません。」「アバターを読み込めませんでした。ファイルが壊れていないか確かめてください。」「保存できませんでした。保存先の空き容量を確かめてください。」 |

登録のときの問題の説明：

| 問題 | 説明 |
|---|---|
| VRMではない | VRMファイルではありません。拡張子が .vrm のファイルを選んでください。 |
| 大きすぎる | ファイルが大きすぎます。1 GBまでのファイルを選んでください。 |
| 登録済み | このモデルはすでに登録されています。 |

### 5.2 登録の確認のダイアログ

- タイトル「このアバターを登録しますか？」、実行ボタン「登録する」（Primary）。
- 本文は「作者が決めた利用条件を確かめてから登録してください。」。アバターとして使える人が「誰でも」でないときは「このモデルは、誰でもアバターとして使ってよい設定ではありません。使ってよいか、利用条件を確かめてください。」とする。
- 本文の下に、5章の利用条件の行を表示する（見るだけ。「開く」ボタンは置かず、Tabはボタンの間だけを移る）。長いときは、その部分だけをScrollする（高さ280pxまで）。

## 6. 取り込みの状態の表示

形・色・文字を必ず組み合わせる（`design-system.md` 3.4）。

| 状態 | Chip | 文字 |
|---|---|---|
| 停止中 | Idle | 停止中 |
| 機器を選ぶ必要がある | Warning | 停止中。説明：「機器でキャプチャーボードを選んでください。Webカメラで顔が映らないよう、カメラは自動では開きません。」 |
| 開始中 | Idle | 開始中 |
| 映像を待っている | Idle | 映像を待っています |
| 信号がない | Warning | 映像を待っています。説明：「映像が届いていません。ゲーム機の電源とケーブルを確かめてください。」 |
| 取り込み中 | Success | 取り込み中 |
| 機器が見つからない | Warning | 機器が見つかりません（探し続けています）。説明：「つなぐと自動で見つけ直します。」 |
| トラッキングと同じカメラ | Error | 取り込めませんでした。説明：「同じカメラをトラッキングで使っています。別の機器を選んでください。」 |
| 失敗 | Error | 取り込めませんでした。説明：「ほかのアプリが機器を使っている可能性があります。」 |

- Pluginがない（`PluginMissing`）は、利用者には「失敗」として表示する（Capture詳細設計6.2）。

## 7. 文字列

Keyは、取り込みのCardが`setup.capture.*`、トラッキングのCardが`setup.tracking.*`、アバターのCardが`setup.avatar.*`、表情の調整のCardが`setup.face.*`、クレジットのCardが`setup.credits.*`とする。文言は3〜6章、8章、9章の表のとおり（英語は同じ意味で書く）。

## 8. クレジットとライセンスのCard

アプリに同梱したSBOM（`sbom/virtual-vessel-studio.cdx.json`）から、使っているソフトウェアと素材を並べる。ライセンスの全文は、アプリに同梱したファイルから読んで表示する（開発ワークフロー18.1.2）。

| 行 | 部品 | 内容 |
|---|---|---|
| 説明 | 説明文（Text 2） | 「Virtual Vessel Studio は、次のソフトウェアと素材を使っています。ライセンスの全文があるものは「全文を見る」で読めます。」 |
| 部品ごとの行 | Setting Rowと同じ見た目 | 左に名前、その下に「版 · 提供元 · ライセンス」（SBOMにあるものだけ、Caption、Text 3）。先頭はアプリ自身。続けて、配布物に含まれる部品をSBOMの順に並べ、組み込まれた部品は親の下に余白L（24px）だけ字下げする。開発にだけ使う部品（`excluded`）は出さない |
| 全文 | Button（Secondary）「全文を見る」／「全文を閉じる」 | SBOMがライセンスのファイルを挙げている部品だけに置く。押すと行の下に全文を開く。ファイルが複数あるときは、ファイル名（等幅）を見出しにして順に並べる |
| 全文の欄 | Scrollする欄（高さ320pxまで、背景Surface 3、Radius S） | 全文（Caption、Text 2）。長い通知文でも画面が伸びないよう、欄の中だけをScrollする |

- 名前、版、ライセンス名、全文は、ファイルに書かれたとおりに文字として表示する（Rich Textとして解釈しない）。言語で変えない。
- SBOMは画面を作るときに読む。全文は、初めて開いたときに読む。
- SBOMを読めないときは、行の代わりに「クレジットを読み込めませんでした。アプリを入れ直すと直る場合があります。」を出す。ファイルがないときは、その欄に「ライセンスの全文が見つかりません。」を出す。開発者向けには、ファイルのパスと理由をLogに書く。
- ファイルを読む場所：ビルドしたアプリでは実行ファイルのフォルダー（`licenses/`等）。Editorでは、`tools/release/license-files.json`がそれぞれのファイルの元として挙げる、リポジトリの中のファイル。
- SBOMに書かれたパスは、そのフォルダーの中だけを読む（`..`、絶対パス、`\`を含むものは読まない）。SBOMはアプリと一緒に配布するファイルで、信頼の境界は越えないが、書き換えられても画面がほかのファイルを読まないようにする。

## 9. 表情の調整のCard

利用者の顔の動きが、アバターの表情へどれだけ強く出るかを、アバターを見ながら部位ごとに合わせる（Avatar詳細設計7.4）。調整は利用者のもので、アバターを替えても変わらない。

| 行 | 部品 | 内容 |
|---|---|---|
| 説明 | 説明文（Text 2） | 「トラッキングを動かし、アバターを見ながら合わせます。大きくするほど、小さな動きで表情が出ます。各スライダーの下は、いまのアバターの動きです。」 |
| 映像 | 画像（384×216、16:9） | 配信の映像の中央を拡大して表示する。表情を見やすくするため。映像がないときは「映像なし」 |
| 映像の高さ | Slider（0〜100%、5%刻み）、映像の下 | 映像のどの高さを見せるか（0%が下端、100%が上端）。既定は85%（カメラの既定の位置で、平均的な身長のアバターの顔が入る所）。顔の位置はアバターの身長によるため、利用者が合わせる。保存はしない |
| 拡大 | Slider（100〜400%、10%刻み）、映像の高さの下 | 映像をどれだけ拡大するか。100%で配信の映像全体、既定は220%。拡大の中心は映像の左右の中央で、映像の外は見せない。顔の大きさはアバターによるため、利用者が合わせる（利用者の要望、2026-10-21）。保存はしない |
| 照明の強さ | Slider（10〜150%、5%刻み）、拡大の下 | 舞台のライトの強さ（Stage詳細設計7.1）。100%が既定。説明（Caption）：「配信の照明も変わり、保存されます。」顔が白く飛ぶときに弱める（利用者の要望、2026-10-21）。顔を見ながら変えるため、映像の横に置く（利用者の決定、2026-10-21）。Stageがないときは置かない |
| ウインク | Toggle | 片目だけを閉じられるか。説明：「オンにすると、片目だけを閉じられます。顔を向けたときなどに片目が誤って閉じる場合はオフにします。」既定はオフ。オフの間、「ウインクのしやすさ」は無効にする |
| 部位ごと | Slider（50〜300%、10%刻み）と、その下の帯 | 目の閉じやすさ、ウインクのしやすさ、眉の動き（驚き・怒り・悲しみ）、口の動き、視線の動き、笑顔の強さ。100%が既定。帯は、その部位のいまのアバターの動き（0〜100%）で、約50msごとに描き直す |
| すべて既定に戻す | Button（Secondary）「既定に戻す」 | すべての部位を100%へ戻し、ウインクをオフにする |

- 映像を左、スライダーを右に並べ、どのスライダーを動かしている間もアバターが見えるようにする。窓が狭いときは、スライダーを映像の下へ回す。
- 帯は進捗のBar（`components.md` 3.15）を使い、見出しは付けない（説明文で意味を伝える）。
- 変えるとすぐに配信中のアバターへ反映し、保存する（`avatar-settings.json`）。
- アバターを使えないとき（Avatarがない）は、このCardを置かない。アバターを選んでいないときは、帯は動かない。
- 照明の強さは、すぐに配信へ反映し、`stage-settings.json`へ保存する（Stage詳細設計6.3）。「既定に戻す」は表情の調整だけを戻し、照明の強さは戻さない。
- 切り抜きは、映像全体を枠の中で拡大して置き、位置をずらし、枠の外を隠して行う。UnityのImageの表示範囲（UV）を変える方法では、変えても描き直されなかったため（2026-10-21）。

## 10. 録画のCard

`IRecordingService`（Video）を直接表示する（`RecordingCard`）。詳細は録画詳細設計18.2。

| 行 | 内容 |
|---|---|
| 保存先 | 右に「変更…」（Windowsのフォルダーを選ぶ画面）と「開く」（エクスプローラー）。フォルダーのパスは説明の下に置き、そこで折り返す（`components.md` 3.9） |
| 画質 | Segmented：標準、高画質。ビットレート（6 Mbps、12 Mbps）は説明に書く |
| 形式 | Segmented：AV1、H.264。AV1がおすすめであることと、H.264を選ぶときは説明に書く。GPUにAV1のエンコーダーがないときは、AV1を選べず、説明がその理由を示す（録画詳細設計18.2） |
| 空き容量 | 保存先のドライブの空き。Cardを表示している間、10秒ごとに読み直す |
| 映像 | 大きさ、Frame Rate、容器（1920×1080・30 fps・MP4）。当面は変えられない |

- 変更は次の録画から使う。録画中の録画は、始めたときの設定のまま続く。
- 録画の開始と停止はライブ画面で行う（`live.md`）。

| Key | 日本語 | English |
|---|---|---|
| `setup.recording.title` | 録画 | Recording |
| `setup.recording.unavailable` | 録画は使えません。 | Recording is not available. |
| `setup.recording.folder` | 保存先 | Folder |
| `setup.recording.folder.description` | 録画したファイルを保存するフォルダーです。次の録画から使われます。 | Where recordings are saved. Used from the next recording. |
| `setup.recording.folder.pick` | 変更… | Change… |
| `setup.recording.folder.pick-title` | 録画の保存先を選んでください | Choose where to save recordings |
| `setup.recording.folder.open` | 開く | Open |
| `setup.recording.quality` | 画質 | Quality |
| `setup.recording.quality.description` | 標準は6 Mbps、高画質は12 Mbpsです。高画質はきれいですが、ファイルが約2倍の大きさになります。 | Standard is 6 Mbps and High 12 Mbps. High looks better, and its files are about twice as large. |
| `setup.recording.quality.standard` | 標準 | Standard |
| `setup.recording.quality.high` | 高画質 | High |
| `setup.recording.codec` | 形式 | Format |
| `setup.recording.codec.description` | AV1がおすすめです。同じ画質でファイルが小さくなります。古い編集ソフトやスマートフォンで開けないときは、H.264を選んでください。 | AV1 is recommended: smaller files at the same quality. Choose H.264 if an older editor or phone cannot open the files. |
| `setup.recording.codec.no-av1` | このPCのGPUはAV1に対応していないため、H.264で録画します。 | This PC's GPU does not support AV1, so recordings use H.264. |
| `setup.recording.codec.av1` | AV1 | AV1 |
| `setup.recording.codec.h264` | H.264 | H.264 |
| `setup.recording.free` | 空き容量 | Free space |
| `setup.recording.free.value` | {size} GB | {size} GB |
| `setup.recording.free.unknown` | わかりません | Unknown |
| `setup.recording.format` | 映像 | Video |
| `setup.recording.format.value` | {width}×{height}・{rate} fps・MP4 | {width}×{height}, {rate} fps, MP4 |

## 11. 音声のCard

`IAudioService`（Audio）を直接表示する（`AudioCard`）。音の元（マイク、ゲームの音声）ごとに行をまとめる。詳細はAudio詳細設計18章。

| 行 | 内容 |
|---|---|
| 見出し | 音の元の名前と、状態のStatus Chip（オフ、開始中、動作中、ミュート中、見つかりません、使えません。形と文字で示す） |
| 問題 | 何が起きたかと対処。マイクへのアクセスが許可されていないときは「マイクのプライバシー設定を開く」 |
| デバイス | Select。先頭に「なし」。抜かれている機器は「接続されていないデバイス」として残す |
| 音量 | Slider、-60〜+12dB |
| ミュート | Toggle |
| 入力レベル | Level Meter、-60〜0dB |
| 配信・録画の音声の遅延 | Slider、0〜1000ms、既定300ms。音の元の後に1つ（Audio詳細設計8.5） |

- Cardを表示している間、機器を5秒ごとに数え直す。
- マイクは、初めて使うときにWindowsの既定の入力の機器になる。ゲームの音声は「なし」から始まる（キャプチャーボードの音声を選ぶ。映像の機器を選ぶと自動で選ばれる。3章）。

| Key | 日本語 | English |
|---|---|---|
| `setup.audio.title` | 音声 | Audio |
| `setup.audio.unavailable` | 音声は使えません。 | Audio is not available. |
| `setup.audio.microphone` | マイク | Microphone |
| `setup.audio.game` | ゲームの音声 | Game audio |
| `setup.audio.device` | デバイス | Device |
| `setup.audio.device.microphone` | 話す声を拾うマイクです。 | The microphone that picks up your voice. |
| `setup.capture.audio` | 音声 | Audio |
| `setup.capture.audio.description` | ゲームの音を取り込む機器です。映像の機器を選ぶと、同じ機器の音声を自動で選びます。 | The device the game's sound comes from. Choosing a video device chooses its audio too. |
| `setup.audio.device.game` | キャプチャーボードの音声などを選びます。 | Choose the capture board's audio, for example. |
| `setup.audio.device.none` | なし | None |
| `setup.audio.device.missing` | 接続されていないデバイス | A device not connected now |
| `setup.audio.gain` | 音量 | Volume |
| `setup.audio.mute` | ミュート | Mute |
| `setup.audio.level` | 入力レベル | Input level |
| `setup.audio.state.off` | オフ | Off |
| `setup.audio.state.starting` | 開始中 | Starting |
| `setup.audio.state.running` | 動作中 | Running |
| `setup.audio.state.not-found` | 見つかりません | Not found |
| `setup.audio.state.failed` | 使えません | Unavailable |
| `setup.audio.state.muted` | ミュート中 | Muted |
| `setup.audio.problem.not-found` | デバイスが接続されていません。接続すると自動で使い始めます。 | The device is not connected. It is used again as soon as it is connected. |
| `setup.audio.problem.in-use` | ほかのアプリがこのデバイスを専有しています。そのアプリを閉じるか、そのアプリの設定で専有をやめてください。 | Another application is using this device exclusively. Close it, or turn off exclusive use in its settings. |
| `setup.audio.problem.access-denied` | Windowsの設定で、マイクへのアクセスが許可されていません。「マイクのプライバシー設定を開く」から、デスクトップアプリのアクセスを許可してください。 | Windows does not allow access to the microphone. Open the microphone privacy settings and allow desktop apps to access it. |
| `setup.audio.problem.failed` | デバイスを開けませんでした。もう一度選び直してください。続く場合は診断画面のログを確認してください。 | The device could not be opened. Choose it again; if it keeps happening, check the log on the Diagnostics screen. |
| `setup.audio.open-privacy` | マイクのプライバシー設定を開く | Open microphone privacy settings |
| `setup.audio.monitored` | 自分で聞く | Hear it myself |
| `setup.audio.monitored.description` | オンにすると、下の「モニター」から聞こえます。配信と録画には関係しません。 | Plays on the monitor below. This does not change the stream or recordings. |
| `setup.audio.monitor` | モニター | Monitor |
| `setup.audio.monitor.device` | 出力先 | Output |
| `setup.audio.monitor.device.description` | 自分が聞く音を出す機器です。ヘッドホンをおすすめします。スピーカーだと、マイクに音が入ることがあります。 | The device you listen on. Headphones are recommended: speakers can leak into the microphone. |
| `setup.audio.monitor.gain` | 音量 | Volume |
| `setup.audio.monitor.mute` | ミュート | Mute |
| `setup.audio.delay` | 配信・録画の音声の遅延 | Stream and recording audio delay |
| `setup.audio.delay.description` | アバターの動きは、カメラから読み取るぶん声より遅れます。配信と録画の音声をこの分だけ遅らせて合わせます。自分に聞こえる音は遅れません。 | The avatar moves later than your voice, since its motion is read from the camera. Streaming and recording get the sound this much later to match. What you hear is not delayed. |
