# セットアップ画面

## 1. 目的

配信の前に、使う機器や設定を整える画面。使っているソフトウェアのクレジットも、ここで見られる。

映像の取り込み（Capture）のCard、音声のCard（11章）、声の変換のCard（13章、設計中）、トラッキングのCard、アバターのCard、表情の調整のCard（9章）、録画のCard（10章）、アプリの設定のCard（UI基盤詳細設計7.7）、クレジットのCard 2枚（8章）を置く。ほかの設定は、それぞれの機能を作るときに加える。

- 画面の名前（ナビゲーションと見出し）は「設定」（英語 Settings）とする。機器の準備だけでなく、アプリの設定やクレジットも置くためである。
- 画面の識別子（`setup`）、文字列のKey（`nav.setup`、`setup.*`）、文書の中の呼び方（セットアップ画面）は変えない。識別子は`ui-settings.json`の`lastScreen`に保存されているため、変えると前回の画面に戻れなくなる。

## 2. 構成

Cardを主題ごとのページに分け、画面の上のTab（`components.md` 3.8）で切り替える。Cardが増えて主題が見つけにくくなったため、1列に並べるのをやめた（利用者の決定、2026-10-31）。

| ページ | Key | Card |
|---|---|---|
| 映像 | `setup.page.video` | 映像の取り込み（ゲーム画面、サブモニター） |
| 音声 | `setup.page.audio` | 音声（11章。音声を使えないときは「音声は使えません。」の一文）、BGM、効果音（14章）、声の変換（13章） |
| アバター | `setup.page.avatar` | アバター（5章）、トラッキング（4章）、表情の調整（9章。アバターを使えるときだけ）、照明（15章。Stageを使えるときだけ） |
| 配信・録画 | `setup.page.output` | 録画（10章。録画を使えないときは「録画は使えません。」の一文）、配信（12章）
| アプリ | `setup.page.app` | アプリの設定、クレジットとライセンス（8章） |

- 最後に開いたページを`ui-settings.json`の`lastSetupPage`に保存し、次はそのページを開く（UI基盤詳細設計8.1）。知らない値のときは先頭のページを開く。
- 各ページの上に、そのページのCardへのリンク（Page Index、`components.md` 3.29）を置く（利用者の要望、2026-11-22）。
- ページはすべて作っておき、開いているページだけを表示する。ページを切り替えても、Cardの状態は失われない。
- トラッキング用カメラは、取り込みのCardを持たない。トラッキングのCardで選ぶ。
- Cardは基本的に折りたたまない。ページに分けたことで、閉じて画面を短くする必要がなくなった。あまり使わない設定のCard（細かい設定など）は、どのページでも、折りたためるCard（`components.md` 3.10）にして閉じて始めてよい（利用者の決定、2026-11-03）。例外として、BGMと効果音のCard（14章）は、よく使うが一覧が長くなるため、開いて始まる折りたためるCardにする（利用者の決定、2026-11-20）。
- 状態（Status Indicator）は、Cardのタイトル部分の右端に置く。問題の説明は本文の先頭に置く。
- 設定の説明は、行の下に書かず、設定名の後のHelp Hint（`components.md` 3.28）に置く。初回の案内を作らないため、設定の説明はここで行う（決定記録`setup-guidance.md`）。行の下には、状況で出る注意（「rtmp:// は暗号化されません」「このPCのGPUはAV1に対応していない」など）だけを置く。表の列は、見出しの後に置く。文はstring table（`.help`で終わるキー）にあり、日本語は利用者が校正した（2026-11-22）。

## 3. 映像の取り込みのCard

Capture Sourceごとに1枚。`ICaptureDeviceSource`（Capture）を直接表示する。

| 行 | 部品 | 内容 |
|---|---|---|
| 状態 | Status Indicator（`components.md` 3.13） | 状態と、問題があればその説明（6章） |
| 映像 | 画像（256×144、16:9に収める） | 取り込んだ映像を小さく表示し、選んだ機器が合っているか確かめられるようにする。映像がないときは「映像なし」 |
| 取り込む | Toggle | オンで開始、オフで停止。開始していたかは保存し、次の起動で戻す |
| 入力元（ゲーム画面のみ） | Dropdown（キャプチャーボード、このPCのウィンドウ） | ゲーム画面を何から取り込むか（Capture詳細設計8.6）。「このPCのウィンドウ」では、「機器」にこのPCで開いているウィンドウを、プログラムごとに1つ（いちばん大きいもの）並べる。選んでいる機器があれば、その種類を示す。種類を変えると、ほかの種類の機器の選択を外す。 |
| 機器 | Dropdown | 見つかった機器。ゲーム画面は先頭に「カメラを選択」（ウィンドウでは「ゲームのウィンドウを選択」）、サブモニターは先頭に「自動（メイン以外のモニター）」。ウィンドウは「タイトル (プログラム名)」と表示する。見つからないときは「機器が見つかりません」（ウィンドウでは「ウィンドウが見つかりません」） |
| 音声（ゲーム画面のみ） | Dropdown | ゲームの音を取り込む音声の機器（`GameAudioRow`）。音声のCard（11章）の「ゲームの音声」のデバイスと同じ設定で、どちらから変えてもよい。映像の機器を選ぶと、名前が一致する音声の機器を自動で選ぶ。ウィンドウを選ぶと、それまでの音声の機器に関わらず、そのゲームの音だけ（「game.exe の音」、Audio詳細設計8.7）を選び、ウィンドウを選んでいる間は一覧にもそれを出す。モニターへは送らない。ゲームの音はPCから直接聞こえているので、送ると二重に聞こえる。キャプチャーボードに戻すと、モニターへ送る。音声がないときは行を置かない |
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
| 動かす範囲 | Segmented（上半身、全身（準備中）） | 上半身。全身はまだ選べないが、今後対応することが分かるよう、無効の色で表示する（Tracking詳細設計6.5、`components.md` 3.18）。英語：Upper body、Full body (soon) |
| 使う部分 | Toggle「顔」「体」「手」を縦に並べる | 既定はすべてオン。切った部分は推論しないため負荷が下がり、その部分は動かない（Tracking詳細設計6.5）。「手」は両手と指をまとめて切り替える。どの言語でも行のControl（最大260px）に収まるよう、縦に並べる。変えるとすぐに反映し、保存する |
| 体の動き | Segmented（3D、2D） | 既定は3D。2Dでは、体の奥行きを使わず、正面から見た動きだけでアバターを動かす（Tracking詳細設計8.6）。変えるとすぐに反映し、保存する |
| 動きの速さ | Slider（1〜10、1刻み） | 平滑化の段階（Tracking詳細設計8.2）。既定は5（2026-10-24に段階を2つ、さらに4つずらし、遅れ300ms程度までの安定な段階を5とした）。変えるとすぐに反映し、保存する |
| 体の検出 | Segmented（軽量、標準） | Poseのモデル（Tracking詳細設計10章）。既定は標準。トラッキング中に変えると、新しいモデルで開始し直す。保存する。Heavy（「高精度」）は出さない。利用者のカメラでは標準より悪くなった（2026-10-26） |
| カメラ | Dropdown | 見つかった機器。先頭に「カメラを選択」。選ぶまでカメラは開かない |
| 機器の一覧 | Button（Secondary）「探し直す」 | 機器の一覧を取り直す |

### 4.1 トラッキングの状態の表示

| 状態 | Chip | 文字 |
|---|---|---|
| 停止中 | Idle | 停止中 |
| 準備中 | Idle | 準備中 |
| トラッキング中 | Success | トラッキング中 |
| 人が映っていない | Warning | 人が映っていません。説明：「カメラの前に座り、顔と上半身がカメラに映るようにしてください。」 |
| カメラを選ぶ必要がある | Warning | カメラを待っています。説明：「「カメラ」でトラッキングに使うWebカメラを選択してください。」 |
| カメラが見つからない | Warning | カメラを待っています。説明：「カメラが見つかりません。カメラをつなぐと自動で探し直します。」 |
| ゲーム画面と同じカメラ | Error | トラッキングできません。説明：「このカメラはゲーム画面の取り込みで使っています。別のカメラを選択してください。」 |
| 失敗 | Error | トラッキングできません。説明：「カメラを開けないか、トラッキングを始められませんでした。ほかのアプリがカメラを使っていないか確認してください。」 |

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
| 登録 | Button（Primary）「アバターを登録…」、Button（Secondary）「サンプルのアバターを使う」 | 「アバターを登録…」は、ファイルを選ぶ画面（VRMファイルだけ）を開き、確認のダイアログ（5.2）の後に登録する。「サンプルのアバターを使う」は、アプリに付属のサンプル（`Samples\otoha-chan.vrm`。Editorでは`tests/samples/`）を、ファイルを選ばずに同じ確認の後で登録する。サンプルがないときは出さない（利用者の決定、2026-11-22。決定記録`setup-guidance.md`）。2つのボタンは縦に並べる。横に並べると操作部品の260pxからはみ出したため（利用者の指摘、2026-11-22） |
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
| ウィンドウを選ぶ必要がある | Warning | 停止中。説明：「「機器」でゲームのウィンドウを選択してください。」 |
| 開始中 | Idle | 開始中 |
| 映像を待っている | Idle | 映像を待っています |
| 信号がない | Warning | 映像を待っています。説明：「映像が届いていません。ゲーム機の電源とケーブル接続を確認してください。」 |
| 取り込み中 | Success | 取り込み中 |
| 機器が見つからない | Warning | 機器が見つかりません（探し続けています）。説明：「機器をつなぐと自動で探し直します。」 |
| ウィンドウが見つからない | Warning | 機器が見つかりません（探し続けています）。説明：「ゲームを起動すると自動で探し直します。最小化しているウィンドウは見つかりません。」ゲームを閉じたときもこれになり、ゲームを起動し直すと自動で取り込みを始める |
| トラッキングと同じカメラ | Error | 取り込めませんでした。説明：「同じカメラをトラッキングで使っています。別の機器を選んでください。」 |
| 失敗 | Error | 取り込めませんでした。説明：「ほかのアプリが機器を使っている可能性があります。」 |

- Pluginがない（`PluginMissing`）は、利用者には「失敗」として表示する（Capture詳細設計6.2）。

## 7. 文字列

Keyは、取り込みのCardが`setup.capture.*`、トラッキングのCardが`setup.tracking.*`、アバターのCardが`setup.avatar.*`、表情の調整のCardが`setup.face.*`、クレジットのCardが`setup.credits.*`とする。文言は3〜6章、8章、9章の表のとおり（英語は同じ意味で書く）。

## 8. クレジットとライセンスのCard

アプリに同梱したSBOM（`sbom/virtual-vessel-studio.cdx.json`）から、使っているソフトウェアと素材を並べる。ライセンスの全文は、アプリに同梱したファイルから読んで表示する（開発ワークフロー18.1.2）。

Cardは2枚に分け、アプリの設定のページの最後に、次の順で置く。Voice Labの学習の環境はPythonのPackageだけで数十あり、1枚にするとアプリ本体の部品が埋もれるためである。

どちらも折りたためるCard（`components.md` 3.10）にして、閉じて始める。読む人は少なく、開いたままだと長い一覧がページの大半を占めるためである（利用者の決定、2026-11-13）。SBOMを読めないときは、問題が見えるよう、折りたたまないCardにする。

| Card | タイトル | 並べる部品 |
|---|---|---|
| アプリ本体 | 「クレジットとライセンス：アプリ本体」 | 先頭にアプリ自身。続けて、配布物に含まれる部品（`required`）をSBOMの順に並べる |
| Voice LabのPython環境 | 「クレジットとライセンス：Voice Lab の Python 環境」 | 配布物には含めず、Voice Labの学習の環境を用意するときに利用者のPCへ入れる部品（`optional`）を、SBOMの順に並べる。SBOMでは、Python、RVC、その事前学習モデル、VoxCPM、そのモデル、PyTorch、uvのように主なものを先に置き、残りのPackageを名前の順に続ける（開発ワークフロー18.1.2）。SBOMに`optional`の部品がないときは、このCardを出さない |

| 行 | 部品 | 内容 |
|---|---|---|
| 説明 | 説明文（Text 2） | アプリ本体：「Virtual Vessel Studio は、次のソフトウェアと素材を使っています。ライセンスの全文があるものは「全文を見る」から確認可能です。」 Voice LabのPython環境：「Voice Lab で声を作るための学習の環境を用意するとき、次のソフトウェアとモデルをこのPCに入れます。アプリには含まれていないため、ライセンスの全文はそれぞれの提供元が公開しています。」 |
| 部品ごとの行 | Setting Rowと同じ見た目 | 左に名前、その下に「版 · 提供元 · ライセンス」（SBOMにあるものだけ、Caption、Text 3）。組み込まれた部品は親の下に余白L（24px）だけ字下げする。開発にだけ使う部品（`excluded`）は、どちらのCardにも出さない |
| 全文 | Button（Secondary）「全文を見る」／「全文を閉じる」 | SBOMがライセンスのファイルを挙げている部品だけに置く。押すと行の下に全文を開く。ファイルが複数あるときは、ファイル名（等幅）を見出しにして順に並べる |
| 全文の欄 | Scrollする欄（高さ320pxまで、背景Surface 3、Radius S） | 全文（Caption、Text 2）。長い通知文でも画面が伸びないよう、欄の中だけをScrollする |

- 名前、版、ライセンス名、全文は、ファイルに書かれたとおりに文字として表示する（Rich Textとして解釈しない）。言語で変えない。
- Markdownのファイル（`.md`）は、記号をそのまま出さず、見出し、段落、項目、表の行に分けて表示する。リンクは文字とアドレスにする（利用者の確認、2026-11-15。`MarkdownText`）。それ以外のファイルは、行のとおりに表示する。
- SBOMは画面を作るときに読む。全文は、初めて開いたときに読む。
- SBOMを読めないときは、アプリ本体のCardだけを置き、行の代わりに「クレジットを読み込めませんでした。アプリの再起動を試してください。」を出す。ファイルがないときは、その欄に「ライセンスの全文が見つかりません。」を出す。開発者向けには、ファイルのパスと理由をLogに書く。
- ファイルを読む場所：ビルドしたアプリでは実行ファイルのフォルダー（`licenses/`等）。Editorでは、`tools/release/license-files.json`がそれぞれのファイルの元として挙げる、リポジトリの中のファイル。
- SBOMに書かれたパスは、そのフォルダーの中だけを読む（`..`、絶対パス、`\`を含むものは読まない）。SBOMはアプリと一緒に配布するファイルで、信頼の境界は越えないが、書き換えられても画面がほかのファイルを読まないようにする。

## 9. 表情と指の調整のCard

利用者の顔の動きが、アバターの表情へどれだけ強く出るかを、アバターを見ながら部位ごとに合わせる（Avatar詳細設計7.4）。調整は利用者のもので、アバターを替えても変わらない。

| 行 | 部品 | 内容 |
|---|---|---|
| 説明 | 説明文（Text 2） | 「トラッキングを動かし、アバターの動きを見ながら合わせます。値を大きくするほど、小さな動きで表情が出るようになります。各スライダーの下は、いまのアバターの動きです。」 |
| 映像 | 画像（384×216、16:9） | 配信の映像の中央を拡大して表示する。表情を見やすくするため。映像がないときは「映像なし」 |
| 映像の高さ | Slider（0〜100%、5%刻み）、映像の下 | アバターの枠（Stage詳細設計7.2）のどの高さを見せるか（0%が枠の下端、100%が上端）。アバターがいなければ映像全体のどの高さか。既定は85%（カメラの既定の位置で、平均的な身長のアバターの顔が入る所）。顔の位置はアバターの身長によるため、利用者が合わせる。保存はしない |
| 拡大 | Slider（100〜400%、10%刻み）、映像の高さの下 | どれだけ拡大するか。100%でアバターの枠の高さ全体（アバターがいなければ配信の映像全体）、既定は220%。左右は枠の中央を見せる。アバターを小さく置いても、端に置いても、枠を基準にするので顔を見失わない（利用者の指摘、2026-11-19）。見せる範囲は配信の映像と同じ形で、映像の外は見せない。顔の大きさはアバターによるため、利用者が合わせる（利用者の要望、2026-10-21）。保存はしない |
| ウインク | Toggle | 片目だけを閉じられるか。既定はオフ。オフの間、「ウインクのしやすさ」は無効にする |
| 部位ごと | Slider（50〜300%、10%刻み）と、その下の帯 | 目の閉じやすさ、ウインクのしやすさ、眉の動き（驚き・怒り・悲しみ）、口の動き、視線の動き、笑顔の強さ。100%が既定。帯は、その部位のいまのアバターの動き（0〜100%）で、約50msごとに描き直す |
| 指の曲がり、親指の曲がり | Slider（50〜200%、10%刻み） | 指の曲がる強さ（Avatar詳細設計7.5）。100%が既定。2つのSliderに同じHelp Hintを付ける。手は「映像の高さ」と「拡大」で見る |
| すべて既定に戻す | Button（Secondary）「既定に戻す」 | すべての部位と指を100%へ戻し、ウインクをオフにする |

- 映像を左、スライダーを右に並べ、どのスライダーを動かしている間もアバターが見えるようにする。窓が狭いときは、スライダーを映像の下へ回す。
- 帯は進捗のBar（`components.md` 3.15）を使い、見出しは付けない（説明文で意味を伝える）。
- 変えるとすぐに配信中のアバターへ反映し、保存する（`avatar-settings.json`）。
- アバターを使えないとき（Avatarがない）は、このCardを置かない。アバターを選んでいないときは、帯は動かない。
- 切り抜きは、映像全体を枠の中で拡大して置き、位置をずらし、枠の外を隠して行う。UnityのImageの表示範囲（UV）を変える方法では、変えても描き直されなかったため（2026-10-21）。

## 10. 録画のCard

`IRecordingService`（Video）を直接表示する（`RecordingCard`）。詳細は録画詳細設計18.2。

| 行 | 内容 |
|---|---|
| 保存先 | 右に「変更…」（Windowsのフォルダーを選ぶ画面）と「開く」（エクスプローラー）。フォルダーのパスは説明の下に置き、そこで折り返す（`components.md` 3.9） |
| 画質 | Segmented：標準、高画質。ビットレート（6 Mbps、12 Mbps）はHelp Hintに書く |
| 形式 | Segmented：AV1、H.264。AV1がおすすめであることと、H.264を選ぶときはHelp Hintに書く。GPUにAV1のエンコーダーがないときは、AV1を選べず、行の下の説明がその理由を示す（録画詳細設計18.2） |
| 空き容量 | 保存先のドライブの空き。Cardを表示している間、10秒ごとに読み直す |
| 映像 | 大きさ、Frame Rate、容器（1920×1080・30 fps・MP4）。当面は変えられない |

- 変更は次の録画から使う。録画中の録画は、始めたときの設定のまま続く。
- 録画の開始と停止はライブ画面で行う（`live.md`）。

| Key | 日本語 | English |
|---|---|---|
| `setup.recording.title` | 録画 | Recording |
| `setup.recording.unavailable` | 録画は使えません。 | Recording is not available. |
| `setup.recording.folder` | 保存先 | Folder |
| `setup.recording.folder.help` | 録画したファイルを保存するフォルダーです。変更は次の録画から反映されます。 | Where recordings are saved. A change applies from the next recording. |
| `setup.recording.folder.pick` | 変更… | Change… |
| `setup.recording.folder.pick-title` | 録画の保存先を選んでください | Choose where to save recordings |
| `setup.recording.folder.open` | 開く | Open |
| `setup.recording.quality` | 画質 | Quality |
| `setup.recording.quality.help` | 標準は6 Mbps、高画質は12 Mbpsです。高画質ではきれいに映りますが、ファイルの大きさが約2倍になります。1時間の録画で、標準は約2.7 GB、高画質は約5.4 GBです。 | Standard is 6 Mbps and High 12 Mbps. High looks better, and its files are about twice as large. An hour of recording is about 2.7 GB in Standard and about 5.4 GB in High. |
| `setup.recording.quality.standard` | 標準 | Standard |
| `setup.recording.quality.high` | 高画質 | High |
| `setup.recording.codec` | 形式 | Format |
| `setup.recording.codec.help` | AV1を推奨します。同じ画質で、ファイルが小さくなります。古い編集ソフトを使用するなどで録画ファイルを開けない場合は、H.264を選択してください。 | AV1 is recommended: smaller files at the same quality. If the files cannot be opened, for example in an older editor, choose H.264. |
| `setup.recording.codec.no-av1` | このPCのGPUはAV1に対応していないため、H.264で録画します。 | This PC's GPU does not support AV1, so recordings use H.264. |
| `setup.recording.codec.av1` | AV1 | AV1 |
| `setup.recording.codec.h264` | H.264 | H.264 |
| `setup.recording.free` | 空き容量 | Free space |
| `setup.recording.free.help` | 保存先のドライブの空き容量です。10秒ごとに取得します。 | The free space on the folder's drive, read every 10 seconds. |
| `setup.recording.free.value` | {size} GB | {size} GB |
| `setup.recording.free.unknown` | わかりません | Unknown |
| `setup.recording.format` | 映像 | Video |
| `setup.recording.format.help` | 録画する映像の解像度とフレームレートです。 | The resolution and frame rate of the recorded video. |
| `setup.recording.format.value` | {width}×{height}・{rate} fps・MP4 | {width}×{height}, {rate} fps, MP4 |

## 11. 音声のCard

`IAudioService`（Audio）をミキサーの形で表示する（`AudioCard`、`components.md` 3.25）。音の元（マイク、ゲームの音声、BGM、効果音）とモニターを1行ずつ、同じ列に並べる。BGMと効果音は機器を持たず、デバイスの列は空ける（14章）。音の元が増えても、1行ずつ増えるだけで済む。詳細はAudio詳細設計18章。

| 行 | 内容 |
|---|---|
| 音の元 | 名前と、状態のStatus Chip（オフ、開始中、動作中、ミュート中、未発見、使えません。形と文字で示す） |
| デバイス | Select。先頭に「なし」。抜かれている機器は「接続されていないデバイス」として残す。このPCのゲームの音は「game.exe の音」と表示する。ゲームの音声は、映像の取り込みのCardで選ぶ（3章）ので、ここでは無効にして機器の名前だけを見せる。モニターは出力先を選ぶ |
| 音量 | Slider、-60〜+12dB |
| 聞く | Toggle。モニターの行にはない |
| ノイズ除去 | Toggle。マイクの行だけ。既定はオン。声の変換を使わないときも効く（利用者の決定、2026-11-11。Audio詳細設計8.6）。ProjectのAudio Profileに保存する |
| ミュート | Toggle |
| レベル | Level Meter、-60〜0dB |

行の下に、問題があるときだけ、何が起きたかと対処を出す。マイクへのアクセスが許可されていないときは「マイクのプライバシー設定を開く」を置く。各列の説明は、列の見出しの後のHelp Hint（components.md 3.28）に置き、表の下に文を並べない（利用者の要望、2026-11-11）。モニターの行は、出力先の説明を行の名前の後のHelp Hintに置く。ゲームの音声の機器を「映像」のページで選ぶことは、無効にした機器の選択のツールチップで伝える。表の下に、配信・録画の音声の遅延（Slider、0〜1000ms、既定300ms。Audio詳細設計8.5）を置く。

- Cardの幅が870pxより狭いときは列の見出しを隠し、行を折り返して、操作部品ごとに名前を添える。
- Cardを表示している間、機器を5秒ごとに数え直す。
- マイクは、初めて使うときにWindowsの既定の入力の機器になる。ゲームの音声は「なし」から始まる（キャプチャーボードの音声を選ぶ。映像の機器を選ぶと自動で選ばれる。3章）。

| Key | 日本語 | English |
|---|---|---|
| `setup.audio.title` | 音声 | Audio |
| `setup.audio.unavailable` | 音声は使えません。 | Audio is not available. |
| `setup.audio.microphone` | マイク | Microphone |
| `setup.audio.game` | ゲームの音声 | Game audio |
| `setup.audio.music` | BGM | BGM |
| `setup.audio.sound-effect` | 効果音 | Sound effects |
| `setup.audio.source` | 音の元 | Source |
| `setup.audio.device` | デバイス | Device |
| `setup.audio.device.help` | 音声入力に使う機器です。なお、ゲームの音声の機器は「映像」ページで選択してください。 | The device the sound comes in from. The game audio's device is chosen on the Video page. |
| `setup.capture.audio` | 音声 | Audio |
| `setup.capture.audio.help` | ゲームの音を取り込む機器です。映像の機器を選択すると、同じ機器の音声が自動で選択されます。「音声」ページの「ゲームの音声」と同じ設定です。 | The device the game's sound comes from. Choosing a video device chooses its audio too. It is the same setting as Game audio on the Audio page. |
| `setup.audio.device.none` | なし | None |
| `setup.audio.device.missing` | 接続されていないデバイス | A device not connected now |
| `setup.audio.device.application` | {name} の音 | Sound of {name} |
| `setup.audio.gain` | 音量 | Volume |
| `setup.audio.gain.help` | 配信と録画に入れる音量です。0 dBが元の大きさです。 | The volume in the stream and recordings. 0 dB is the original level. |
| `setup.audio.mute` | ミュート | Mute |
| `setup.audio.mute.help` | オンの間、その音を配信と録画に入れないようにします。 | While on, the sound is kept out of the stream and recordings. |
| `setup.audio.level` | レベル | Level |
| `setup.audio.level.help` | 現在の音の大きさです。メーターが右端に届く場合は、音が割れる可能性があるため、音量を下げてください。 | How loud the sound is now. If the meter reaches the right end, the sound may distort, so lower the volume. |
| `setup.audio.state.off` | オフ | Off |
| `setup.audio.state.starting` | 開始中 | Starting |
| `setup.audio.state.running` | 動作中 | Running |
| `setup.audio.state.not-found` | 未発見 | Not found |
| `setup.audio.state.failed` | 使えません | Unavailable |
| `setup.audio.state.muted` | ミュート中 | Muted |
| `setup.audio.problem.not-found` | デバイスが接続されていません。接続すると自動で使い始めます。 | The device is not connected. It is used again as soon as it is connected. |
| `setup.audio.problem.in-use` | ほかのアプリがこのデバイスを専有しています。そのアプリを閉じるか、そのアプリの設定で専有を解除してください。 | Another application is using this device exclusively. Close it, or turn off exclusive use in its settings. |
| `setup.audio.problem.access-denied` | Windowsの設定で、マイクへのアクセスが許可されていません。「マイクのプライバシー設定を開く」から、デスクトップアプリのアクセスを許可してください。 | Windows does not allow access to the microphone. Open the microphone privacy settings and allow desktop apps to access it. |
| `setup.audio.problem.failed` | デバイスを開けませんでした。もう一度選び直してください。続く場合は診断画面のログを確認してください。 | The device could not be opened. Choose it again; if it keeps happening, check the log on the Diagnostics screen. |
| `setup.audio.open-privacy` | マイクのプライバシー設定を開く | Open microphone privacy settings |
| `setup.audio.monitored` | 聞く | Hear |
| `setup.audio.monitored.help` | 「モニター」の出力先から聞くことができます。配信と録画には影響しません。スピーカーではマイクに音が入るため、ヘッドホンの使用を推奨します。 | Lets you hear the source on the Monitor output. It does not change the stream or recordings. Headphones are recommended: speakers leak into the microphone. |
| `setup.audio.denoise` | ノイズ除去 | Noise removal |
| `setup.audio.denoise.help` | 話していない間の、キーボードやマウスなどの音を除去します。声の変換を使わない場合にも有効です。 | Removes keyboard, mouse, and other sounds while you are not speaking. It works without voice conversion too. |
| `setup.audio.game-device` | ゲームの音声の機器は、「映像」のページで選びます。 | The game audio's device is chosen on the Video page. |
| `setup.audio.monitor` | モニター | Monitor |
| `setup.audio.monitor.device` | 出力先 | Output |
| `setup.audio.monitor.help` | 自分で聞く音を出す機器です。ヘッドホンを選択することを推奨します。配信と録画には影響しません。 | The device that plays what you hear yourself. Headphones are recommended. It does not change the stream or recordings. |
| `setup.audio.delay` | 配信・録画の音声の遅延 | Stream and recording audio delay |
| `setup.audio.delay.help` | アバターの動きは、カメラの映像から読み取る分だけ、声より遅れます。配信と録画の音声をこの時間だけ遅らせて、動きに合わせます。自分に聞こえる音は遅れません。 | The avatar moves later than your voice, by the time it takes to read the motion from the camera. Streaming and recording get the sound this much later to match the motion. What you hear is not delayed. |

## 12. 配信のCard

`IStreamingService`（Streaming）を直接表示する（`StreamingCard`）。行と動きは配信詳細設計18.2。ストリームキーの値は表示せず、登録するダイアログは伏せ字で入力する。配信中はどの行も変更できず、その理由を示す。

| Key | 日本語 | English |
|---|---|---|
| `setup.streaming.title` | 配信 | Streaming |
| `setup.streaming.unavailable` | 配信は使えません。 | Streaming is not available. |
| `setup.streaming.locked` | 配信中は変更できません。 | These cannot change while streaming. |
| `setup.streaming.destination` | 配信先 | Destination |
| `setup.streaming.destination.help` | YouTubeか、ほかのRTMP(S)のサーバーを選択します。 | Choose YouTube or another RTMP(S) server. |
| `setup.streaming.destination.youtube` | YouTube | YouTube |
| `setup.streaming.destination.custom` | 他のサーバー | Other server |
| `setup.streaming.url` | サーバーのURL | Server URL |
| `setup.streaming.url.help` | YouTubeは既定のままで使用できます。ほかのサーバーでは、rtmp:// か rtmps:// で始まるURLを入力してください。 | YouTube works with the default. For another server, enter a URL that starts with rtmp:// or rtmps://. |
| `setup.streaming.url.placeholder` | rtmps://example.com/live | rtmps://example.com/live |
| `setup.streaming.url.invalid` | rtmp:// または rtmps:// で始まるURLを入力してください。 | Enter a URL that starts with rtmp:// or rtmps://. |
| `setup.streaming.url.unencrypted` | rtmp:// は暗号化されません。ストリームキーも暗号化されずに送られます。rtmps:// を使用することを推奨します。 | rtmp:// is not encrypted, and neither is the stream key sent over it. rtmps:// is recommended. |
| `setup.streaming.key` | ストリームキー | Stream key |
| `setup.streaming.key.help` | YouTube Studioの「ライブ配信」に表示されるキーです。キーを知っている人は、このチャンネルで配信できるため、ほかの人に見せないでください。アプリはWindowsの資格情報マネージャーに保存し、登録したあとは表示しません。 | The key shown under Go live in YouTube Studio. Anyone who knows it can stream on your channel, so do not show it to others. The app saves it in Windows Credential Manager and does not show it after it is registered. |
| `setup.streaming.key.registered` | 登録済み | Registered |
| `setup.streaming.key.not-registered` | 未登録 | Not registered |
| `setup.streaming.key.register` | 登録… | Register… |
| `setup.streaming.key.remove` | 削除 | Remove |
| `setup.streaming.key.dialog.title` | ストリームキーの登録 | Register the stream key |
| `setup.streaming.key.dialog.body` | YouTube Studioのストリームキーを入力してください。 | Enter the stream key from YouTube Studio. |
| `setup.streaming.key.dialog.field` | ストリームキー | Stream key |
| `setup.streaming.key.dialog.confirm` | 登録 | Register |
| `setup.streaming.key.failed` | 登録できませんでした。もう一度試してください。 | It could not be registered. Please try again. |
| `setup.streaming.key.remove.title` | ストリームキーを削除しますか？ | Remove the stream key? |
| `setup.streaming.key.remove.body` | 配信するには、もう一度登録が必要です。 | You will need to register it again to stream. |
| `setup.streaming.key.remove.confirm` | 削除 | Remove |
| `setup.streaming.codec` | 形式 | Format |
| `setup.streaming.codec.help` | AV1を推奨します。同じビットレートできれいに映ります。配信が正常に行えない場合は、H.264を選択してください。 | AV1 is recommended: it looks better at the same bitrate. If the stream does not work properly, choose H.264. |
| `setup.streaming.codec.no-av1` | このPCのGPU、または配信先がAV1に対応していないため、H.264で配信を行います。 | This PC's GPU or the destination does not support AV1, so the stream uses H.264. |
| `setup.streaming.codec.av1` | AV1 | AV1 |
| `setup.streaming.codec.h264` | H.264 | H.264 |
| `setup.streaming.bitrate` | 映像のビットレート | Video bitrate |
| `setup.streaming.bitrate.help` | 1080p 30 fpsの場合は、6 Mbpsを推奨します。回線の上りの速度より低くしてください。高くするほどきれいに映りますが、回線に余裕がないと配信が途切れます。 | 6 Mbps is recommended for 1080p at 30 fps. Keep it below your connection's upload speed. Higher looks better, but without room on the connection the stream breaks up. |
| `setup.streaming.test` | 接続テスト | Connection test |
| `setup.streaming.test.help` | サーバーに接続できるかを確認します。配信は開始しません。 | Checks that the server can be reached. The stream does not start. |
| `setup.streaming.test.run` | テスト | Test |
| `setup.streaming.test.ok` | 接続できました。 | Connected. |

## 13. 声の変換のCard

`IVoiceConversion`（Voice）を表示する（`VoiceCard`）。音声のページで、音声のCardの下に置く。マイクに掛ける処理なので、マイクと同じページにする。画面案：https://claude.ai/artifact/4XQ54BFpSmHzcqtrfq6G4R（利用者の確認、2026-11-10）

普段は簡単な設定だけで使えるようにする（利用者の要望、2026-11-03）。窓を1秒に固定して細かい設定はノイズ除去だけになり、そのノイズ除去も音声のCardへ移した（11章）ので、Cardは1つにする（13.2）。

### 13.1 声の変換のCard

タイトルの右端に状態のStatus Chip（動作中 ✓、準備中 ○、オフ ○、使用不可 ×、停止 ×）。

| 行 | 部品 | 内容 |
|---|---|---|
| 声を変換する | Toggle | オフでは元の声のまま配信と録画に入る。オンにしてから声の準備ができるまでと、変換できないときは、マイクの音を入れない（Voice詳細設計12章）。その間もオンと表示し、声やGPUがなくても押せる。オフにすれば元の声に戻せるようにするためである。前回の状態を保存し、起動時に戻す。声を変えて配信する人には、起動時にオフで地声が乗るほうが重い失敗であるため（利用者の決定、2026-11-10） |
| 声 | Select | Voice Labで作った、使える声。試作の声は開発者用なので出さない（診断画面にだけ出す）。選ぶとVoice Profileに保存する（Voice詳細設計6.2） |
| 高さを自動で合わせる | Toggle、「測り直す」 | 名前の下に「自分の声 200 Hz → この声 285 Hz（+6）」。測る前は「測定前」 |
| 高さの調整 | Slider | -24〜+24半音。自動がオンなら自動の量に足す。Help Hintの文は自動のオンとオフで変わる。名前の下に「変換後の高さ 317 Hz（合計 +8）」（利用者の確認、2026-11-10） |
| 声の仕上げ | Slider 4つ、「仕上げを元に戻す」 | 選んだ声の厚み、こもり、明るさ、空気感（-6〜+6 dB）。動かすとその声に保存し、配信にすぐかかる。声がないときは無効（Voice詳細設計8.6。利用者の決定、2026-11-15） |

「声を変換する」のHelp Hintで、声の変換にGPUを使うことも伝える（利用者の要望、2026-11-11。画面の文字を増やさない）。

- 使えるGPUがないとき：Cardの先頭に「このPCでは声の変換を使用できません」とその理由を出し、すべての行を無効にする（13.3）。
- 声がないとき：Cardの先頭に「声がありません」と「Voice Labを開く」を出し、ほかの行を無効にする。
- 止まったとき、または声を読み込めないとき：Cardの先頭に、止まったことと、配信と録画にマイクの音を入れていないこと、やり直すにはオフにしてからオンにすること、オフのままなら元の声が入ることを出す（2026-11-15）。「声がありません」より先に出す。配信に入るものを伝えるためである。`onFailure`が`PassThrough`のときは、元の声が流れていることを出す。
- 「自分に聞こえる声」（13.4）、「変換できないとき」、「利用上の注意を見る」は、後の段階で加える。音声の処理の段の変更、変換できないときの扱い、規約（Voice Lab詳細設計8.2）がそれぞれ要るためである。

### 13.2 固定した値

窓（HuBERTと生成器が一度に見る長さ）は1秒に固定し、選べるようにしない（利用者の決定、2026-11-03）。窓の前に利用者の話し声を置いてからは、2.5秒と5.4秒を聞いても違いが分からず、1秒はGPUも一番軽い（Voice詳細設計17章）。何を優先するかで選ぶ簡単な設定（GPUを軽く、なめらかさ）も、選ぶ理由のある組み合わせがないので置かない。

つなぎ目の重ねは50 msだけとし、選べるようにしない（利用者の決定、2026-11-03）。選べるようにすると、声ごとに生成器とひな形が2組要る。

### 13.3 GPUが足りないPC

GPUが足りないPCでは、この機能を使うべきでない（利用者の指摘、2026-11-03）。ただ、どのGPUから使えるかは分からないので、使えるGPUがないときだけオンにできなくする（利用者の決定、2026-11-03）。GPUで計算できないとき（Compute Shaderが使えない、ソフトウェアで描画している）を「ない」とみなす。

「遅れの目安」の行は置かない。窓と重ねを固定したので、遅れは常に約0.38秒で、選ぶ助けにならない（利用者の判断、2026-11-03）。

### 13.4 自分に聞こえる声

変換した声は約0.38秒遅れて聞こえるので、歌うときはリズムを取りにくい（Voice詳細設計17章）。「元の声」では、モニターにはマイクの音をそのまま送り、配信と録画には変換した声を送る。Audioの処理の段の変更が要るので、別の段階で作る（Audio詳細設計8.3）。

### 13.5 文字列

| Key | 日本語 | English |
|---|---|---|
| `setup.voice.title` | 声の変換 | Voice conversion |
| `setup.voice.state.running` | 動作中 | Running |
| `setup.voice.state.loading` | 準備中 | Loading |
| `setup.voice.state.off` | オフ | Off |
| `setup.voice.state.unavailable` | 使用不可 | Unavailable |
| `setup.voice.state.stopped` | 停止 | Stopped |
| `setup.voice.no-voice` | 声がありません | No voice |
| `setup.voice.no-voice.body` | Voice Labで声を作成すると、変換先の声を選択できるようになります。 | Make a voice in Voice Lab to choose the voice to convert to. |
| `setup.voice.open-lab` | Voice Labを開く | Open Voice Lab |
| `setup.voice.no-gpu` | このPCでは声の変換を使用できません | Voice conversion cannot be used on this PC |
| `setup.voice.no-gpu.body` | 声の変換にはGPUが必要です。使用できるGPUが見つかりませんでした。 | It needs a GPU, and no usable GPU was found. |
| `setup.voice.failed` | 声の変換が停止しました | Voice conversion stopped |
| `setup.voice.failed.body` | 元の声が流れないよう、配信と録画にマイクの音を入れていません。やり直すには、「声を変換する」をオフにしてからオンにしてください。オフのままにすると、元の声が入ります。解決しない場合は、診断画面のログを確認してください。 | Your microphone is kept out of the stream and recordings, so your own voice does not go out. To try again, turn Convert my voice off and on. Left off, your own voice goes out. If it keeps happening, check the log on the Diagnostics screen. |
| `setup.voice.failed.body-pass-through` | 配信と録画には元の声が流れています。もう一度オンにしてください。解決しない場合は、診断画面のログを確認してください。 | The stream and recordings get your own voice. Turn it on again; if it keeps happening, check the log on the Diagnostics screen. |
| `setup.voice.enabled` | 声を変換する | Convert my voice |
| `setup.voice.enabled.help` | オフの場合、元の声が配信と録画に入ります。オンの場合、声の準備を進めている間と、変換に失敗したときは、マイクの音を入れません。声の変換にはGPUを使用します。 | Off, your own voice goes to the stream and recordings. On, the microphone is kept out while the voice gets ready and whenever conversion fails. Voice conversion uses the GPU. |
| `setup.voice.voice` | 声 | Voice |
| `setup.voice.voice.help` | Voice Labで作成した声から選択します。 | Choose from the voices made in Voice Lab. |
| `setup.voice.auto-pitch` | 高さを自動で合わせる | Match the pitch automatically |
| `setup.voice.auto-pitch.help` | 自分の声の高さを測定し、選択した声の高さに合わせます。「測り直す」で測定をやり直せます。 | Measures your voice and moves it to the chosen voice's pitch. Measure again measures it once more. |
| `setup.voice.pitch-match` | 自分の声 {user} → この声 {voice}（{shift}） | Your voice {user} → this voice {voice} ({shift}) |
| `setup.voice.unmeasured` | 測定前 | not measured |
| `setup.voice.remeasure` | 測り直す | Measure again |
| `setup.voice.adjust` | 高さの調整 | Pitch adjustment |
| `setup.voice.adjust.help-auto` | 半音単位で調整します。自動で合わせた高さに加算されます。 | In semitones, added to the automatic match. |
| `setup.voice.adjust.help-manual` | 半音単位で調整します。男声を女声にする場合は、+12前後が目安です。 | In semitones. From a man's voice to a woman's, about +12. |
| `setup.voice.final` | 変換後の高さ {hz}（合計 {total}） | Converted pitch {hz} (total {total}) |

## 14. BGMと効果音のCard

「音声」のページの、音声のCard（11章）の下に、BGMのCardと効果音のCardを置く（BGM・SE詳細設計。2026-11-17）。登録したファイルを1行ずつ、表（`components.md` 3.24）の形で並べる。どちらも折りたためるCard（`components.md` 3.10）で、開いて始まる。一覧が長くなるため、使わないほうを閉じられるようにする（画面全体の決まりの例外。利用者の決定、2026-11-20）。開閉は覚えない。

| 列 | 内容 |
|---|---|
| 並べ替え | 行の先頭の「≡」の印（`text-3`、ボタンではなくLayer Listのつまみと同じ印）。つかんで上下にドラッグすると、その行を動かす。ドラッグ中は行を薄くし、落ちる位置にAccent Lineの線を出す（Layer List、`components.md` 3.20と同じ）。印はフォーカスを取らない。キーボードでは、行のボタンにフォーカスがあるとき`Ctrl`+上下キーで1つずつ動き、フォーカスは動かした行の同じボタンに残る（押せない状態なら名前の変更ボタン）（名前の入力中は動かさない）。並べた順はライブ画面のBGMの選択と効果音のボタンにも使う |
| 再生 | Icon Button。BGMは押すと配信と録画で再生し、再生中は停止の形（■）に変わる。効果音は押すたびに頭から鳴らし、重ねて鳴る。読み込みが済むまでは無効で、ツールチップで読み込み中と伝える |
| 名前 | 同梱のものは、名前を変えるまで画面の言語の名前を出す（`bgm-se.md` 6.3）。残りの幅を使い、収まらなければ「…」で省略し、ツールチップで全体を出す。BGMは再生中に「再生中」、再生できないときに「再生できません」のStatus Chipを名前の後に出す（形と文字で示す） |
| 長さ | 分:秒、等幅、右寄せ。ファイルが長さを持たないときは空ける |
| ループ | BGMだけ。Toggle。既定はオン。変更は次の再生から。説明は見出しの後のHelp Hint |
| 音量 | Slider、-60〜+12 dB。そのファイルの音量で、ミキサーのBGMと効果音の行の音量とは別。再生中のBGMには、その場で反映する |
| ショートカット | 効果音だけ。押すと「キーを押してください」になり、Accent Lineの枠で待つ。`Ctrl`、`Alt`、`Shift`のどれかと一緒に押したキーを割り当てる（どのアプリを使っていても鳴る。`bgm-se.md` 8.3）。単独のキーでは割り当てず、ツールチップで修飾キーが要ると伝える。`Esc`で取り消し、`Delete`か`Backspace`で外す。待っている間は、ほかのキーを受け取らない（Tabで離れず、`Esc`で抜ける）。キーは「Ctrl+Alt+1」のように等幅で出し、ないときは「なし」。ほかの効果音と同じキーは割り当てず、Cardの下に理由を出す。ほかのアプリがそのキーを持っているときは、名前の後に「キーをほかのアプリが使用中」のStatus Chip（警告の形と文字）を出す。説明は見出しの後のHelp Hint |
| 操作 | 「名前を変更」（名前のあった場所で入力欄に替わり、「保存」になる。1〜60文字）、「削除」（確認のダイアログの後。元のファイルは残る）。再生中のBGMの「削除」は無効にし、ツールチップで理由を出す |

- Cardの下に「ファイルを追加…」を置く。ファイルの選択の画面ではWAVとMP3を選べる。選んだファイルを確かめ、登録できないときはCardの下にその理由を出す（形式が違う、読み込めない、効果音が30秒を超える、保存できない）。名前はファイルの名前で登録し、後から変更できる。
- 効果音のCardのタイトルの右端に「効果音を停止」を置き、鳴っている効果音をすべて止める。
- 登録がないときは、何を追加すればよいかの一文を出す。
- ミキサー（11章）には、BGMと効果音の行が加わる。機器を持たないので、デバイスの列は空ける。ノイズ除去の列も空ける。「聞く」は既定でオン。
- ライブ画面の音声の表示（`live.md`）にも、BGMと効果音が加わる。BGMは音符、効果音はスピーカーの形のアイコンで、ミュート中は斜線が入る。

| Key | 日本語 | English |
|---|---|---|
| `setup.bgm.title` | BGM | BGM |
| `setup.bgm.add.help` | WAVまたはMP3のファイルを追加します。アプリはファイルの複製をローカルに保存します。 | Adds a WAV or MP3 file. The app keeps a copy of the file on this PC. |
| `setup.bgm.gain.help` | このBGMの音量です。再生している間も変更できます。 | This BGM's volume. It can be changed while it plays. |
| `setup.se.title` | 効果音 | Sound effects |
| `setup.se.add.help` | 30秒までのWAVまたはMP3のファイルを追加します。アプリはファイルの複製をローカルに保存します。 | Adds a WAV or MP3 file of up to 30 seconds. The app keeps a copy of the file on this PC. |
| `setup.se.gain.help` | この効果音の音量です。鳴らしている間も変更できます。 | This sound effect's volume. It can be changed while it plays. |
| `setup.clips.name` | 名前 | Name |
| `setup.clips.length` | 長さ | Length |
| `setup.clips.loop` | ループ | Loop |
| `setup.clips.loop.help` | 曲の終わりで先頭に戻り、繰り返し再生します。変更は次の再生から反映されます。 | Plays again from the start at the end. A change applies from the next play. |
| `setup.clips.gain` | 音量 | Volume |
| `setup.clips.add` | ファイルを追加… | Add a file… |
| `setup.clips.rename` | 名前を変更 | Rename |
| `setup.clips.save` | 保存 | Save |
| `setup.clips.delete` | 削除 | Delete |
| `setup.clips.move` | ドラッグで並べ替えます。行のボタンを選択したうえで、Ctrl+↑↓を押下しても移動可能です。 | Drag to reorder, or select one of the row's buttons and press Ctrl+Up or Ctrl+Down. |
| `setup.bgm.move` | ドラッグで並べ替えます。行のボタンを選択したうえで、Ctrl+↑↓を押下しても移動可能です。BGMの並び順は、ライブ画面の一覧にも反映されます。 | Drag to reorder, or select one of the row's buttons and press Ctrl+Up or Ctrl+Down. The Live screen's list follows the same order. |
| `setup.clips.name.required` | 名前を入力してください | Enter a name |
| `setup.clips.filter` | 音声ファイル | Audio files |
| `setup.bgm.play` | このBGMを配信と録画で再生します。 | Plays this BGM on the stream and in recordings. |
| `setup.bgm.stop` | このBGMを停止します。 | Stops this BGM. |
| `setup.bgm.playing` | 再生中 | Playing |
| `setup.bgm.failed` | 再生できません | Cannot play |
| `setup.bgm.empty` | BGMはまだ登録されていません。WAVかMP3のファイルを追加してください。 | No BGM yet. Add a WAV or MP3 file. |
| `setup.bgm.delete.title` | BGMの削除 | Delete the BGM |
| `setup.bgm.delete.body` | このBGMを削除します。追加した元のファイルは削除されません。 | This BGM is deleted. The file it was added from is not. |
| `setup.se.play` | この効果音を配信と録画で再生します。 | Plays this sound effect on the stream and in recordings. |
| `setup.se.stop-all` | 効果音を停止 | Stop sound effects |
| `setup.se.empty` | 効果音はまだ登録されていません。30秒までのWAVかMP3のファイルを追加してください。 | No sound effects yet. Add a WAV or MP3 file of up to 30 seconds. |
| `setup.se.loading` | 読み込み中です。 | Loading. |
| `setup.se.shortcut` | ショートカット | Shortcut |
| `setup.se.shortcut.help` | クリックしてから、割り当てるキーを押してください。<br>Ctrl、Alt、Shiftのいずれかとの組み合わせのみ登録可能です。<br>Deleteキーでショートカットキーを解除できます。<br>ほかのアプリを使用している間も鳴らせます。 | Click this, then press the keys to assign.<br>Only combinations with Ctrl, Alt, or Shift can be registered.<br>Press Delete to remove the shortcut keys.<br>It plays while you use other apps too. |
| `setup.se.shortcut.none` | なし | None |
| `setup.se.shortcut.press` | キーを押してください | Press keys |
| `setup.se.shortcut.need-modifier` | Ctrl、Alt、Shiftのどれかと一緒に押してください。 | Press it with Ctrl, Alt, or Shift. |
| `setup.se.shortcut.taken` | このキーはほかの効果音で使っています。別のキーを選んでください。 | Another sound effect uses these keys. Choose other keys. |
| `setup.se.shortcut.held-elsewhere` | キーをほかのアプリが使用中 | Keys used by another app |
| `setup.se.delete.title` | 効果音の削除 | Delete the sound effect |
| `setup.se.delete.body` | この効果音を削除します。追加した元のファイルは削除されません。 | This sound effect is deleted. The file it was added from is not. |
| `setup.clips.delete-in-use` | 再生中は削除できません。停止してから削除してください。 | It cannot be deleted while it plays. Stop it first. |
| `setup.clips.problem.unsupported` | WAVかMP3のファイルを選択してください。 | Choose a WAV or MP3 file. |
| `setup.clips.problem.unreadable` | 音声ファイルを読み込めませんでした。ファイルが壊れていないか確認してください。 | The audio file could not be read. Check that it is not damaged. |
| `setup.clips.problem.too-long` | 効果音は30秒までです。短いファイルを選択してください。 | Sound effects can be up to 30 seconds. Choose a shorter file. |
| `setup.clips.problem.storage` | ファイルを保存できませんでした。保存先の空き容量を確認してください。 | The file could not be saved. Check the free space where it is kept. |

## 15. 照明のCard

舞台のライトを組み立てる（Stage詳細設計7.3）。強さだけを変えられた仮のSlider（表情の調整のCardにあった。2026-10-21）を置き換える（利用者の要望、2026-11-22）。アバターのページの、表情の調整の下に置く。どちらも配信の顔を見て決めるため。Stageがないときは置かない。

| 行 | 部品 | 内容 |
|---|---|---|
| 映像 | 「配置」と「配信の映像」のTabと画像（448×252、16:9）、左 | 「配置」はステージビュー（15.2）。「配信の映像」は配信の映像全体で、映像がないときは「映像なし」。ライトを動かす間も顔が見えるように |
| タイトル部分の右 | Button（Secondary）「ライトを追加」「このライトを削除」 | 追加したライトは、右からのスポット（左右45°、上下15°、距離2m）で、それを選んだ状態にする。4つのときは追加を、1つのときは削除を無効にし、ツールチップで理由を示す |
| ライト | Segmented（1〜4） | 下の行で変えるライト。まだないライトは無効 |
| 種類 | Segmented（全体、スポット） | 平行光か、顔へ向けたスポット |
| 左右の向き | Slider（-180〜180°、5°刻み） | 0°が正面（カメラの側）、プラスが画面の右 |
| 上下の向き | Slider（-30〜90°、5°刻み） | 0°が顔の高さ、90°が真上 |
| 距離 | Slider（0.5〜5m、0.1m刻み） | スポットだけ。全体のときは無効にし、ツールチップで理由を示す |
| 光の広がり | Slider（10〜120°、5°刻み） | スポットだけ。全体のときは無効 |
| 明るさ | Slider（10〜200%、5%刻み） | 既定は70% |
| 色 | 色の見本とDropdown（白、ピンク、オレンジ、黄色、緑、水色、青、紫、カスタム）。「カスタム」にすると、それまでの色から始める | 色は名前でも伝える（色だけに頼らない） |
| 色温度 | Slider（2700〜9000K、100K刻み） | 白だけ。ほかの色のときは無効 |
| カラーコード | 文字の入力（`#RRGGBB`） | 色の「カスタム」だけ。Enterか、入力を離れたときに反映する。読めないときは入力の下に理由を出す。ほかの色のときは無効で、その色のコードを表示する |
| 色の指定 | Dropdown（RGB、HSV） | 下の3つのSliderで、色をRGBとHSVのどちらで決めるか（利用者の要望、2026-11-22）。保存しない。色の「カスタム」だけ |
| R、G、B／H、S、V | Slider。RGBは各0〜255、HSVはHが0〜360°、SとVが0〜100% | 色の「カスタム」だけ。ほかの色のときは無効。3つに同じHelp Hintを付ける |
| 環境光 | Slider（0〜200%、5%刻み） | すべてのライトに共通 |
| 照明を既定に戻す | Button（Secondary）「既定に戻す」 | ライト1つの初めの状態と、環境光100%に戻す |

- 映像を左、設定を右に並べ、設定は高さ480pxまでとしてその中でScrollする。どのライトを動かしている間もアバターが見えるようにするため（利用者の指摘、2026-11-22。設定を映像の下に並べると、映像が画面の外へ出た）。窓が狭いときは、設定を映像の下へ回す。
- Sliderは、行（Setting Row）にせず、表情の調整のCardと同じく名前を上に置き、名前の後にHelp Hintを付ける。行にすると1つが高く、一覧が長くなりすぎたため。
- 変えるとすぐに配信へ反映し、`stage-settings.json`へ保存する（Stage詳細設計6.3）。
- 無効の行も隠さずに見せる。スポットや白にすると何が増えるかが分かるように。
- 日本語の文は仮で、利用者が校正する（`ui-guidelines.md`）。

### 15.1 文字列

| Key | 日本語 | English |
|---|---|---|
| `setup.lighting.title` | 照明 | Lighting |
| `setup.lighting.light` | ライト | Light |
| `setup.lighting.light.help` | 下の設定を変えるライトを選択します。ライトは4つまで追加できます。 | Choose the light the settings below change. Up to four lights can be added. |
| `setup.lighting.light.1` | 1 | 1 |
| `setup.lighting.light.2` | 2 | 2 |
| `setup.lighting.light.3` | 3 | 3 |
| `setup.lighting.light.4` | 4 | 4 |
| `setup.lighting.add` | ライトを追加 | Add a light |
| `setup.lighting.add.full` | ライトは4つまでです。 | There can be up to four lights. |
| `setup.lighting.remove` | このライトを削除 | Remove this light |
| `setup.lighting.remove.last` | ライトは1つ以上必要です。 | At least one light is needed. |
| `setup.lighting.kind` | 種類 | Kind |
| `setup.lighting.kind.help` | 「全体」は、ステージ全体を同じ向きから照らします。「スポット」は、アバターの顔に向けた光で、当たる範囲を絞れます。 | Whole stage lights everything from one direction. Spot is aimed at the avatar's face, and its cone can be narrowed. |
| `setup.lighting.kind.directional` | 全体 | Whole stage |
| `setup.lighting.kind.spot` | スポット | Spot |
| `setup.lighting.yaw` | 左右の向き | Left and right |
| `setup.lighting.yaw.help` | アバターから見て、光が来る方向です。0°が正面（カメラの側）で、プラスにすると画面の右から照らします。 | Where the light comes from around the avatar. 0° is the front (the camera's side); a plus value lights from the screen's right. |
| `setup.lighting.pitch` | 上下の向き | Up and down |
| `setup.lighting.pitch.help` | 光が来る高さです。0°が顔と同じ高さで、90°が真上です。マイナスにすると下から照らします。 | How high the light comes from. 0° is the face's height and 90° straight above; a minus value lights from below. |
| `setup.lighting.distance` | 距離 | Distance |
| `setup.lighting.distance.help` | スポットライトから顔までの距離です。遠くするほど、光の当たる範囲が広がります。明るさは変わりません。 | How far the spot is from the face. Farther lights a wider area; the brightness stays the same. |
| `setup.lighting.spot-only` | スポットライトで使います。 | Used by a spot. |
| `setup.lighting.spot-angle` | 光の広がり | Spread |
| `setup.lighting.spot-angle.help` | スポットライトの光が広がる角度です。小さくするほど、狭い範囲だけを照らします。 | The angle of the spot's cone. Smaller lights a narrower area. |
| `setup.lighting.intensity` | 明るさ | Brightness |
| `setup.lighting.intensity.help` | このライトの明るさです。既定は70%です。顔が白くなりすぎる場合は下げてください。 | This light's brightness, 70% by default. Lower it if the face looks washed out. |
| `setup.lighting.color` | 色 | Color |
| `setup.lighting.color.help` | 光の色です。「白」では、下の色温度で暖かい色から冷たい色まで変えられます。 | The light's color. With White, the color temperature below makes it warmer or cooler. |
| `setup.lighting.color.white` | 白 | White |
| `setup.lighting.color.pink` | ピンク | Pink |
| `setup.lighting.color.orange` | オレンジ | Orange |
| `setup.lighting.color.yellow` | 黄色 | Yellow |
| `setup.lighting.color.green` | 緑 | Green |
| `setup.lighting.color.sky` | 水色 | Sky blue |
| `setup.lighting.color.blue` | 青 | Blue |
| `setup.lighting.color.purple` | 紫 | Purple |
| `setup.lighting.color.custom` | カスタム | Custom |
| `setup.lighting.temperature` | 色温度 | Color temperature |
| `setup.lighting.temperature.help` | 白い光の色合いです。小さくするほど暖かい色（電球の色）に、大きくするほど冷たい色になります。6500 Kが昼の光です。 | The shade of the white light. Lower is warmer, like a light bulb; higher is cooler. 6500 K is daylight. |
| `setup.lighting.white-only` | 色が「白」のときに使います。 | Used when the color is White. |
| `setup.lighting.code` | カラーコード | Color code |
| `setup.lighting.code.help` | 光の色を「#FF88CC」のような形で入力します。 | Enter the light's color like "#FF88CC". |
| `setup.lighting.code.invalid` | 「#」と6桁の英数字（0〜9、A〜F）で入力してください。 | Enter # and six characters from 0–9 and A–F. |
| `setup.lighting.channels` | 色の指定 | Color by |
| `setup.lighting.channels.help` | RGBとHSVのどちらで指定するかを選択します。 | Choose whether to set the color as RGB or HSV. |
| `setup.lighting.channels.rgb` | RGB | RGB |
| `setup.lighting.channels.hsv` | HSV | HSV |
| `setup.lighting.hsv.h` | H | H |
| `setup.lighting.hsv.s` | S | S |
| `setup.lighting.hsv.v` | V | V |
| `setup.lighting.hsv.help` | 光の色を、色相（H、0〜360°）、彩度（S、0〜100%）、明度（V、0〜100%）で指定します。 | Sets the light's color by hue (H, 0–360°), saturation (S, 0–100%), and value (V, 0–100%). |
| `setup.lighting.rgb.help` | 光の色を、赤（R）、緑（G）、青（B）の強さ（0〜255）で指定します。 | Sets the light's color by how much red (R), green (G), and blue (B) it has, from 0 to 255. |
| `setup.lighting.rgb.r` | R | R |
| `setup.lighting.rgb.g` | G | G |
| `setup.lighting.rgb.b` | B | B |
| `setup.lighting.custom-only` | 色が「カスタム」のときに使います。 | Used when the color is Custom. |
| `setup.lighting.ambient` | 環境光 | Ambient light |
| `setup.lighting.ambient.help` | ライトが当たらない影の側の明るさです。下げると影が濃くなり、スポットライトが目立ちます。すべてのライトに共通です。 | How bright the shade is where no light reaches. Lower makes the shadows deeper and a spot stand out. It is shared by all lights. |
| `setup.lighting.reset` | 照明を既定に戻す | Reset the lighting |
| `setup.lighting.reset.help` | ライトを1つにし、向き、明るさ、色と環境光を初めの状態に戻します。 | Goes back to one light, with its direction, brightness, and color, and the ambient light, as they started. |
| `setup.lighting.reset.button` | 既定に戻す | Reset |
| `setup.lighting.tab.placement` | 配置 | Placement |
| `setup.lighting.tab.stream` | 配信の映像 | Stream |
| `setup.lighting.view.help` | 番号の印をドラッグすると、ライトが顔を向いたまま動きます。何もない所をドラッグすると視点が回り、ホイール操作で寄ったり引いたりできます。Shiftを押しながらドラッグすると、見る中心が動きます。ダブルクリックで視点が戻ります。 | Drag a numbered marker to move its light; it keeps facing the face. Drag elsewhere to turn the view, and use the wheel to zoom. Drag with Shift to move the center of the view. Double-click to reset the view. |
| `setup.lighting.invert-x` | 左右を反転 | Reverse left and right |
| `setup.lighting.invert-y` | 上下を反転 | Reverse up and down |
| `setup.lighting.invert.help` | 何もない所をドラッグしたときに、視点が回る向きを反転します。 | Reverses which way the view turns when you drag on empty space. |

### 15.2 配置の表示（ステージビュー）

映像の場所を、「配置」と「配信の映像」のTab（`components.md` 3.8）で切り替える。既定は「配置」。「配置」は、ライトの位置と光の向きを3Dで見て、ドラッグで動かす表示（Stage詳細設計7.4、方式設計11.15）。「配信の映像」は今までのプレビュー。利用者の要望（2026-11-22）：数値だけでは、ライトがどこにあり、光がどちらへ向かうか分からなかった。

| 要素 | 内容 |
|---|---|
| 表示 | ステージビューの画像（16:9、幅448px）。舞台、背景の板、アバター、案内の線（光の向き、スポットの円錐）、配信のカメラの位置 |
| 印 | ライトの位置に、番号（1〜4）を書いた丸。地は`surface`、文字は`text`、枠は光の色の太い線。どの色のライトでも番号が読めるようにするため（濃い色で読めなかった、2026-11-23）。選んでいるライトは大きく、枠が太く、地は`surface-3`。カメラの後ろ等で見えないときは出さない |
| Tabの横のHelp Hint | 操作の説明（下の表を短く） |
| 表示の下 | Toggle「左右を反転」「上下を反転」とHelp Hint。何もない所をドラッグしたときに視点が回る向きを反転する。人によって自然に感じる向きが違うため選べる（利用者の要望、2026-11-22）。`ui-settings.json`に保存する（UI基盤詳細設計8.1）。「配置」のときだけ出す |

| 操作 | 結果 |
|---|---|
| 印をクリック | そのライトを選ぶ（「ライト」のSegmentedも変わる） |
| 印をドラッグ | ライトが顔を向いたまま球の上を動く。「左右の向き」「上下の向き」のSliderも動く |
| 何もない所をドラッグ | 視点を回す。左右は舞台がポインターについて回る（右へドラッグすると右へ回る。UnityのScene Viewと同じ）。上下は視点がポインターについて動く（下へドラッグすると視点が上がる。利用者の決定、2026-11-22）。どちらも反転できる |
| 中ボタンでドラッグ、またはShiftを押してドラッグ | 中心を動かす |
| ホイール | 寄る・引く |
| 何もない所をダブルクリック | 視点を既定に戻す |
| 表示にFocusして矢印キー | 選んでいるライトを5°ずつ動かす（左右、上下）。Shiftを押すと視点を回す |
| F | 選んでいるライトを中心にする |

- 「配置」を開いている間だけ、ステージビューを開く（`IStageView.Open`）。Tabを替えたとき、Cardが見えなくなったとき、別のページへ移ったとき、Setup画面を離れたときは閉じる。
- ドラッグ中も、変えるたびに配信へ反映する。保存は、最後の変更から0.5秒後（Stage詳細設計6.3）。
- Sliderは残す。細かい値と、キーボードだけの操作のため。
- ステージビューを使えないとき（描画先を作れない等）は、「配置」のTabを無効にし、ツールチップで理由を示して、「配信の映像」を出す。
- 文字は仮で、利用者が校正する。
