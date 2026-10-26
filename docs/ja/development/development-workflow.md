# Virtual Vessel Studio 開発運用方針

## 1. 目的

本書は、Virtual Vessel Studioを今後どのように開発・管理・検証・リリースしていくかを定義する。

方式設計では「システムをどのような構成にするか」を定義した。本書では、その方式設計を前提として、

- Git / GitHubをどのように使うか
- Claude Codeをどこまで利用するか
- 詳細設計を誰が作るか
- 実装・テスト・レビューをどう進めるか
- CI/CDをどう構成するか
- Benchmark済みコードをどう扱うか
- どの時点で人間が判断するか
- 何をもって開発完了とするか

を定義する。

本プロジェクトでは、Claude Codeを単なるコード生成ツールとしてではなく、**Repositoryを理解して詳細設計・実装・テスト・レビュー・文書更新まで行う開発Agent**として利用する。

一方、方式設計、製品としての方向性、UX、主要Architecture Decision、性能基準の受け入れ等はユーザーが最終判断する。

---

# 2. 開発全体像

開発は、原則として以下のCycleで進める。

```mermaid
flowchart TD

    Task["Issue / 開発Task"]
    Branch["作業Branch作成"]
    Investigate["Claude Codeによる調査"]
    Design["詳細設計作成・更新"]
    Decision{"重要な設計変更が必要?"}
    Human["ユーザー確認"]
    Implement["Claude Codeによる実装"]
    LocalTest["Local Build / Test"]
    Benchmark["Benchmark / Regression Test"]
    Review["Claude Code Self Review"]
    Docs["Documentation更新"]
    PR["Pull Request"]
    CI["GitHub Actions CI"]
    HumanReview["ユーザー Review"]
    Merge["mainへMerge"]
    Nightly["Nightly / Hardware Benchmark"]
    Release["Release"]

    Task --> Branch
    Branch --> Investigate
    Investigate --> Design
    Design --> Decision
    Decision -->|Yes| Human
    Human --> Implement
    Decision -->|No| Implement
    Implement --> LocalTest
    LocalTest --> Benchmark
    Benchmark --> Review
    Review --> Docs
    Docs --> PR
    PR --> CI
    CI --> HumanReview
    HumanReview --> Merge
    Merge --> Nightly
    Merge --> Release
```

すべてのTaskで全工程を同じ重さで実施する必要はない。

小規模なBug Fixでは詳細設計書の新規作成やBenchmarkを省略できる。一方、Realtime処理、Native Plugin、Module境界、Public API、保存形式等へ影響する変更では、調査・設計・性能確認を省略しない。

---

# 3. 役割分担

## 3.1 ユーザー

ユーザーは主に以下を担当する。

- Product Goalの決定
- 方式設計
- 主要Architecture Decision
- UXの最終判断
- 新しい外部依存を採用するかの判断
- Benchmark結果の受け入れ
- 実際の使用感・音質・映像品質等の評価
- Pull Requestの最終確認
- Release判断

ユーザーが全ClassやPrivate Methodまで設計する必要はない。

## 3.2 Claude Code

Claude Codeは主に以下を担当する。

- Repository調査
- 方式設計の確認
- 既存コードの分析
- Benchmark / Prototypeの分析
- 詳細設計の作成・更新
- Class / Interface / Data Model設計
- 実装
- Unit Test
- Integration Test
- Build
- Benchmark実行
- Self Review
- Refactoring
- Logging / Diagnostics実装
- Documentation更新
- UI Design / UI実装
- Pull Request説明文の作成

Claude Codeには実装詳細を広く任せる。

ただし、方式設計で確定しているArchitecture DecisionをClaude Codeが独自判断で変更してはならない。

## 3.3 GitHub / CI

GitHubは以下を担当する。

- Source Codeの正本
- Issue / Task管理
- Branch / Pull Request管理
- Review履歴
- CI
- Build Artifact保存
- Release
- Version管理

CIは「Claude Codeが完了と言ったか」ではなく、Repositoryとして変更を受け入れてよいかを機械的に確認する。

---

# 4. Repositoryの基本構成

概念的に以下の構成とする。

```text
virtual-vessel-studio/
├─ CLAUDE.md
├─ README.md
├─ .editorconfig
├─ .gitignore
├─ .gitattributes
│
├─ .github/
│  ├─ workflows/
│  ├─ ISSUE_TEMPLATE/
│  └─ pull_request_template.md
│
├─ docs/
│  ├─ ja/
│  │  ├─ architecture/
│  │  │  └─ system-design.md
│  │  ├─ detailed-design/
│  │  ├─ development/
│  │  │  └─ development-workflow.md
│  │  ├─ decisions/
│  │  └─ ui/
│  └─ en/
│     └─ (jaと同一の構成)
│
├─ unity/
│  └─ VirtualVesselStudio/
├─ native/
├─ services/
├─ tools/
└─ tests/
   └─ performance/
```

`docs/ja/architecture/system-design.md` / `docs/en/architecture/system-design.md`には、完成した方式設計書を配置する。

本書は、`docs/ja/development/development-workflow.md` / `docs/en/development/development-workflow.md`として配置する。

製品の性能計測（夜間・実機CIで実行するBenchmark等）について、Unity上で実行する性能テスト本体は各Moduleの`Tests/Performance/`へ配置し、`tests/performance/`には実機CI用の実行Script、比較用の基準値、結果の履歴を置く。

過去のBenchmark / Prototype実装はRepository外の参照用Snapshotとして扱い、本Repositoryへコピーしない（12章参照）。

---

# 5. CLAUDE.mdの役割

Repository Rootの`CLAUDE.md`は、Claude Codeに毎回説明し直したくない**Project共通の固定ルール**を記載する。

`CLAUDE.md`と本書は、以下のように役割を分ける。

- `CLAUDE.md`: Claude Codeが常に守る必要があるルール。Git操作の手順や制約、Commit / Push / Pull Requestの扱い等、Claude Codeの行動を直接拘束する内容を含む。
- 本書: 人間向けの背景・理由、役割分担、CI / Release構成、運用の段階的な導入計画等。

Rootの`CLAUDE.md`には主に以下を置く。

- 最重要Architecture Rule
- 参照すべき文書
- Task開始時の標準手順
- Benchmarkの扱い
- Git Workflow（Branch、Commit、Push、禁止操作）
- Test / Reviewの最低条件
- ユーザー確認が必要な変更

ただし、`CLAUDE.md`を巨大な詳細設計書にはしない。

詳細なModule設計は`docs/<lang>/detailed-design/`へ分離する。

これにより、Claude Codeが必ず守るべきルールを確実に読み込ませつつ、Module固有の詳細でContextを必要以上に大きくしない。

---

# 6. Git運用

## 6.1 基本方針

2本の長期Branchを持つ（利用者の決定、2026-10-18。それまでは`main`だけだった）。

| Branch | 役割 |
|---|---|
| `main` | Releaseできる状態。Releaseの版のTag（`vX.Y.Z`）はここに付ける |
| `develop` | 開発中の統合Branch。作業Branchはここから作り、ここへMergeする |

作業ごとに`develop`から短期間のBranchを作成し、Pull Requestを経由して`develop`へMergeする。Releaseするときに、`develop`から`main`へのPull Request（Release PR）をMergeする。

```text
main      ──────────────●(v0.2.0)───────────●(v0.2.1)──
                        ↑ Release PR          ↑     ↘
develop   ──●──●──●─────┴──●──●───────────────┴──────●──
            ↑  ↑  ↑        ↑                    hotfix/… も
feature/… fix/… docs/…    feature/…             developへ戻す
```

- 緊急の修正は`main`から`hotfix/<name>`を作り、`main`へのPull RequestでReleaseしたうえで、`develop`へも戻す（`main`から`develop`へのPull Request）。

## 6.2 Branch命名

| 種類 | Prefix | 例 |
|---|---|---|
| 新機能 | `feature/` | `feature/capture-module` |
| Bug Fix | `fix/` | `fix/audio-underrun` |
| Refactoring | `refactor/` | `refactor/avatar-runtime` |
| 文書 | `docs/` | `docs/update-workflow` |
| CI / Build | `chore/` | `chore/github-actions` |
| Releaseされた版の緊急修正（`main`から作る） | `hotfix/` | `hotfix/capture-crash` |

## 6.3 mainとdevelopの扱い

`main`は原則として以下を満たす状態を維持する。`develop`も同じ状態を目指すが、Release前の確認（21章）は`main`へ入れるときに行う。

- Build可能
- 必須Testが成功
- 重大な既知Regressionがない
- Version管理対象の設定や文書が整合している

`main`と`develop`への直接Pushを通常運用では行わない。

Pull Request経由でMergeする。`main`へのPull Requestは、Release PR（`develop`から）とhotfix（`hotfix/`から）だけとする。

## 6.4 Pull Request

1 Pull Requestは原則として1つの目的に限定する。

Pull Requestには最低限以下を記載する（`CLAUDE.md` 30章と同一）。

```text
Summary
Related issue
Design
Changes
Commits
Tests
Benchmarks
UI verification
Compatibility / Migration
Documentation
Remaining issues
```

関連する方式設計・詳細設計は`Design`へ記載する。

Templateは`.github/pull_request_template.md`とする。

## 6.5 Merge方式

通常のMerge（Merge Commit）を基本とする。

Branch内のCommitは、`CLAUDE.md` 29章に従って検証済みの論理単位として作成する。それらを`main`上にもそのまま残し、機能がどのような手順で構築されたかをCommit履歴から追えるようにする。

Branch内のCommit履歴が分かりにくくなった場合は、Push前に整理を提案する。公開済みの履歴は、ユーザーの明示的な承認なしに書き換えない。

---


# 7. コーディング規約・言語方針

## 7.1 C#コーディング規約

本プロジェクトで新規作成・変更するProject-ownedなC#コードは、`.NET Runtime` Repositoryの以下のCoding Styleを基準とする。

https://github.com/dotnet/runtime/blob/main/docs/coding-guidelines/coding-style.md

この文書をC#コーディング規約の正とする。

主な規則として、以下を含む。

- Allman StyleのBraceを使用する
- インデントは4 Spacesとし、Tabを使用しない
- private / internalのInstance Fieldは`_camelCase`
- private / internalのStatic Fieldは`s_` Prefix
- Thread Static Fieldは`t_` Prefix
- Access Modifierを省略しない
- `using`はFile上部へ配置し、`System.*`を先に配置する
- `Int32`等より`int`、`String`等より`string`のようなC# Keywordを使用する
- 適切な場合は文字列Literalより`nameof(...)`を使用する
- 新規Project-owned Fileでは本規約へ従う

External OSS、Third-party Source等については、上流のStyleを維持する。

本プロジェクトの規約へ合わせるためだけにExternal Source全体をFormattingし直さない。

**副作用のない関数**

関数は、原則として副作用を持たないように書く。つまり、結果が引数だけで決まるようにする。

- 計算、判断、解析、整形は純粋な関数にする。多くの場合は`static`とする。
- 状態の変更と副作用は、薄い外側のMethodに置く。副作用とは、ファイルやNetworkのI/O、Log、Event、時計の読み取り、乱数などである。
- 計算の途中で、変更されうるFieldや共有の作業用Bufferを読まない。必要な値は引数で渡す。
- 結果を返すために引数を書き換えない。新しい値を返す。
- 状態を持つこと自体が役割のClassは、その状態を持ってよい（Serviceの現在の設定、Queue、Cacheなど）。状態は1か所にまとめ、その周りの計算は純粋に保つ。
- 実時間処理（CLAUDE.md 23章）では、計測に基づく性能上の理由がある場合に限り例外を認める。例として、呼び出し側から渡されたBufferの再利用がある。理由はCommentに書く。

純粋な関数は入力と出力だけで確かめられるため、直接Testする。

---

## 7.2 EditorConfig

Repository Rootに`.editorconfig`を配置し、可能な範囲でCoding Styleを機械的に適用する。

概念的には以下の役割を持つ。

```text
.editorconfig
   ↓
IDE / Editor
   ↓
Claude Code
   ↓
CI Style Check
```

Coding Styleを人間やClaude Codeの記憶だけへ依存させず、Toolingでも検出可能にする。

必要に応じて`dotnet format`等によるFormatting / Style CheckをCIへ組み込む。

Unityが利用するC# VersionやAnalyzerとの互換性を確認した上で適用する。

---

## 7.3 Source Code内の言語

Source Code内の開発者向け記述は、原則として**すべて英語**とする。

対象には以下を含む。

- Class / Method / Variable等のIdentifier
- Code Comment
- XML Documentation Comment
- TODO / FIXME
- Test Name
- Test Description
- Assertion Message
- Developer向けException Message
- Developer Log Message
- Diagnostic Message
- Annotationや内部説明用String

例えば以下のような日本語Commentは使用しない。

```csharp
// マイクを初期化する
InitializeMicrophone();
```

以下のように記述する。

```csharp
// Initialize the selected microphone device before starting the audio pipeline.
InitializeMicrophone();
```

Commentは単に処理を英訳するだけではなく、非自明な処理について**なぜその処理が必要なのか**を優先して記述する。

---

## 7.4 User-facing Text

通常利用者へ表示するTextはSource Codeへ直接Hardcodeしない。

Unity Localizationを利用し、少なくとも、

- Japanese
- English

を用意する。

したがって、

```csharp
statusLabel.text = "マイクを開始しています";
```

のような記述を避け、Localization Keyを介して表示する。

User-facing UIの日本語対応と、「Source Code内は英語」という規則は分離して考える。

---

## 7.5 ドキュメントの言語

Project Documentationは**日本語版と英語版の両方**を用意する。

基本Directoryを以下とする。

```text
docs/
├─ ja/
│  ├─ architecture/
│  ├─ detailed-design/
│  ├─ development/
│  ├─ decisions/
│  └─ ui/
│
└─ en/
   ├─ architecture/
   ├─ detailed-design/
   ├─ development/
   ├─ decisions/
   └─ ui/
```

同じDocumentは、`ja`と`en`以下で同じRelative Pathを使用する。

例:

```text
docs/ja/detailed-design/capture.md
docs/en/detailed-design/capture.md
```

両者は翻訳関係にあり、同一の、

- Architecture Decision
- Interface
- Data Model
- Diagram
- File Path
- Version
- Constraint
- Benchmark条件

を表す。

---

## 7.6 Documentation更新Rule

Documentationを変更するTaskでは、Claude Codeは日本語版・英語版を同一Task内で更新する。

基本手順:

```text
設計変更
  ↓
日本語Document更新
  ↓
英語Document更新
  ↓
日英の内容整合確認
  ↓
PR
```

片方の言語だけが古い状態でTaskを完了しない。

CIでは将来的に少なくとも、

- 対応する日英Fileが存在するか
- Heading構造等に重大な差異がないか

を検証する仕組みを追加可能とする。

完全な翻訳一致をCIだけで保証することは困難であるため、Claude CodeのSelf ReviewおよびPull Request Reviewでも整合性を確認する。

---

## 7.7 Git上の開発者向けText

Branch名、Code Identifier、CI Job名、内部Script名等はEnglishを基本とする。

Commit MessageおよびPull Request TitleについてもEnglishを基本とし、OSS Contributorが内容を理解しやすい状態を維持する。

Pull Request本文についてはEnglishを基本とするが、必要に応じて補助的にJapanese説明を併記してもよい。

---


# 8. Unity ProjectのGit管理

Unityでは以下を基本とする。

- Asset Serialization: Force Text
- Version Control Mode: Visible Meta Files
- `.meta` FileをGit管理する
- `Library/`等の生成DirectoryをCommitしない
- 利用者のData RootやVoice DatasetをCommitしない

大容量の開発用Binary Assetについては必要に応じてGit LFSを利用する。

ただし、ユーザーが登録するVRM、RVC学習Dataset、生成Voice Model、Training Artifact、Runtime Log等は通常のRepository管理対象とはしない。

---

# 9. Taskの開始

## 9.1 Issue / Taskを作る

ある程度以上の変更では、まずTaskを明文化する。

例:

```text
Capture Moduleを実装する。

対象:
- GameCapture
- SubScreenCapture
- Capture Source abstraction

非対象:
- Stage UI全体の再設計
- Streaming Encoder変更

完了条件:
- Capture SourceからTextureを取得できる
- Device disconnectを処理できる
- Benchmarkに重大Regressionがない
- Integration Testが成功する
```

## 9.2 Branchを作る

```bash
git switch develop
git pull
git switch -c feature/capture-module
```

Pull Requestの宛先（base）は`develop`とする。

Claude CodeはこのBranch内で作業させる。

---

# 10. Claude Codeによる開発

## 10.1 Session開始

Repository RootからClaude Codeを開始する。

Rootの`CLAUDE.md`はClaude Codeによって自動的に読み込まれる。

大きなTaskではPlan Modeから開始し、コードを変更する前に調査させる。

## 10.2 最初に調査させる

大きなTaskでは、最初から「実装して」と依頼しない。

例:

```text
Capture Moduleの実装を開始します。

まだコードを変更しないでください。

以下を確認してください。

- CLAUDE.md
- docs/ja/architecture/system-design.md
- 関係する既存コード
- GameCaptureのBenchmark / Prototype
- SubScreenCaptureのBenchmark / Prototype
- 関係するTest

その上で、
- 現状
- 再利用可能なコード
- 必要な責務
- 主要Interface案
- Thread / Resource Lifecycle
- 性能上維持すべき点
- Test方針
- 未決事項

を整理してください。

方式設計と矛盾する場合は実装せず指摘してください。
```

Claude Codeは、実装に入る前に、読んだ方式設計の節と、そこから出る制約を報告する。Moduleの詳細設計だけで済ませず、方式設計そのものを読む。読んだかどうかを利用者から見えるようにするため（CLAUDE.md 13章 Step 2）。

---

# 11. 詳細設計

## 11.1 詳細設計はClaude Codeが作成する

方式設計でModule境界や基本方式はすでに決定している。

したがって、人間が全Class設計を先に作成するのではなく、Claude Codeに既存RepositoryとBenchmarkを調査させ、その結果から詳細設計を作成させる。

保存先:

```text
docs/ja/detailed-design/<module>.md
docs/en/detailed-design/<module>.md
```

## 11.2 詳細設計に含める内容

原則として以下を含める。

1. 目的・責務
2. Module境界
3. 主要Class
4. Interface / Public API
5. Data Model / DTO / Enum
6. State / Lifecycle
7. 主要Sequence
8. Thread / async / Updateタイミング
9. Resource Lifecycle
10. 設定・保存形式
11. Error / Recovery
12. Logging / Diagnostics
13. Test方針
14. Benchmark / Performance
15. Directory / Namespace
16. 未決事項

Private Methodまで事前設計する必要はない。

## 11.3 ユーザー確認が必要な場合

Claude Codeが以下の変更を必要と判断した場合は、実装前にユーザーへ確認する。

- 方式設計の変更
- Module境界変更
- 主要Public Interfaceの責務変更
- 新しい主要External Dependency追加
- 外部OSS本体へのPatch
- 配信RuntimeへのExternal Service依存追加
- Benchmark済み方式の置き換え
- 保存形式の互換性破壊
- Security / Privacy上の新規判断
- UXの大幅な変更

Class名や内部DTO等の実装詳細についてはClaude Codeへ任せる。

---

# 12. Benchmark / Prototypeの扱い

Benchmark済みコードは単なる旧コードとして扱わない。

**Reference Implementation / Performance Baseline**として扱う。

特に以下を対象とする。

- RVC Realtime Runtime
- Audio I/O
- RVC Index
- GameCapture
- SubScreenCapture
- その他性能検証済みコード

Claude Codeは関連機能を変更する前にBenchmarkコードを分析する。

分析対象例:

- Threading
- Buffering
- Memory Allocation
- Memory Copy
- Native API
- GPU / CPU境界
- Audio Thread
- Render Thread
- Blocking処理

Architectureを綺麗にすることだけを理由として、成立済みの高速化を削除しない。

---

# 13. 実装

詳細設計が方式設計と整合していれば、Claude Codeへ実装を任せる。

依頼例:

```text
docs/ja/detailed-design/capture.md に従って実装してください。

- CLAUDE.mdを厳守してください。
- Benchmarkで成立している性能特性を維持してください。
- 無関係な変更を含めないでください。
- 必要なTestを追加してください。
- Logging / Diagnosticsも実装してください。
- BuildとTestを実行してください。
- Performanceに関係する変更ではBenchmarkも実行してください。
- 最後にSelf Reviewしてください。
- 詳細設計と実装が変わった場合は文書も更新してください。
```

---

# 14. Local Test

Pull Requestを作る前に、可能な範囲でLocal Testを実行する。

変更内容に応じて以下を行う。

## Unit Test

- Mapping
- Validation
- State Transition
- Serialization
- Data Conversion

## Integration Test

例:

- Tracking → Avatar
- Capture → Stage
- Voice → Audio
- Project → Runtime
- Voice Lab → External Service

## Build Test

- Unity Compile
- Windows Build
- Native Plugin Build
- Python Test

## 実行方法

| 対象 | コマンド |
|---|---|
| UnityのEditMode / PlayMode Test | `pwsh tools/unity/run-tests.ps1 -Platform EditMode`（または`PlayMode`）。`-Filter`で絞る。`Assets/VirtualVessel`のTest Assemblyだけが動く（PackageのTestは動かさない）。`[Explicit]`のTestは`Explicit`のCategoryを付け、名前を指定したときだけ動く |
| MediaPipeUnityPluginとモデル | `pwsh tools/deps/fetch-mediapipe.ps1`。初めてProjectを開く前と、版を変えたときに実行する（Tracking詳細設計16.2） |
| Native Plugin | `pwsh tools/native/build-capture.ps1`（BuildとTest） |
| Repository checks | `python tools/ci/repo_checks.py` |
| UIの画像 | `pwsh tools/ui/capture-ui.ps1` |

- UnityのTestとBuildは、そのProjectをEditorで開いている間は実行できない（Unityが同じProjectを2つ同時に開けないため）。Editorを開いたまま動かすときは`-Copy`を付ける：作業中の内容（Commitしていない変更を含む）を隣のFolder（`VirtualVesselStudio-test`）へ写し、そこで動かす。写した側は自分のLibraryを持つため、最初の1回はProject全体の読み込みで時間がかかり、2回目からは変わったFileだけを写す。ほかに、Editorを閉じるか、`git worktree`で作った別の作業Copyを`-Project`で指定してもよい。
- 結果とLogは`build/`（Gitの管理外）に出る。

## 14.1 取得して使う依存の更新

GitHubのRelease等から取得して使う依存（現在はMediaPipeUnityPlugin）は、`tools/deps/<name>.json`に版、ダウンロード先、SHA-256を書き、Scriptで取得する。更新は次の手順で、1つの`chore:`のBranchとする。

1. `tools/deps/<name>.json`の版、URL、SHA-256（モデル等の中身のHashも）を新しい版に変える。
2. 取得のScriptを`-Force`で実行し、UnityでCompileとTestを行う。
3. SBOMの版、ダウンロード先、Hashを変える（Repository checksが食い違いを見つける）。
4. 更新の前後で、Test、性能の計測（Tracking詳細設計15章の`AllTasks_Timing`）、実機での確認を比べ、結果をPull Requestに書く。

## Manual / System Test

必要に応じて実際のDeviceを利用する。

---

# 15. Claude CodeによるSelf Review

実装完了後、Claude Codeに変更内容を別視点でReviewさせる。

確認対象:

- 方式設計違反
- Module境界違反
- Thread Safety
- Resource Leak
- Native Handle
- Dispose
- Cancellation
- Error Recovery
- Realtime PathのBlocking
- Allocation
- Logging
- Security
- Privacy
- Test不足
- Documentationとの不整合
- 複雑さ：大きくなったファイル、引数の多いConstructorやMethod、2か所以上にある同じ計算、値を渡すだけの変更

問題が見つかった場合は、Task範囲内で修正してからPull Requestへ進む。

複雑さは、機能を足すたびに少しずつ増える。構造を変えるより、引数を1つ足すことや、似た計算をもう1つ書くことの方が手軽なためである。Claude Codeは段階ごとに上の点を確かめて報告に書き、次の変更が楽になる整理があれば、次の段階の前に提案する（CLAUDE.md 13章 Step 9、37章）。

リファクタリングでは、動作が変わらないことを示す。できる場合は、古い実装と新しい実装に同じ入力を与えて結果を比べる（古い実装はTestの中に一時的に置き、Commitの前に消してよい）。何をどう比べたかを、Commit MessageとPull Requestに書く（CLAUDE.md 34章）。

---

# 16. Documentation更新

Documentationは日本語版と英語版の両方を維持する。

`docs/ja/`以下を変更した場合は対応する`docs/en/`を、`docs/en/`以下を変更した場合は対応する`docs/ja/`を同一Task内で更新する。


Codeと設計文書を意図的に乖離させない。

以下が変わった場合は文書も更新する。

- Public Interface
- Data Model
- Module構成
- Directory構造
- JSON Schema
- Database Schema
- External Component
- UI
- Setup手順
- Benchmark条件

詳細設計は「実装前に一度書いて終わり」ではなく、実装と同期させる。

---

# 17. Pull RequestとCI

Localで確認後、BranchをPushしPull Requestを作成する。

```text
Local development
      ↓
Push
      ↓
Pull Request
      ↓
GitHub Actions
      ↓
Review
      ↓
Merge
```

Claude Codeの「テスト成功」とGitHub Actionsの「CI成功」は別の確認として扱う。

---

# 18. CI構成

CIは負荷と目的に応じて分ける。

## 18.1 PR CI

Pull Requestごとに比較的軽量な確認を行う。

対象例:

- Compile
- C# Unit Test
- Unity EditMode Test
- Native Plugin Build
- Python Test
- Static Check
- Secret Scan
- Dependency Check
- Documentation Check

PR CIが失敗している変更は原則Mergeしない。

### 18.1.1 現在のPR CI

現在は、Unityを使わない軽い確認だけを行う（Phase 2の一部）。Pull Requestごとと`main`へのPushごとに、GitHub Actionsの`Repository checks`（`.github/workflows/repository-checks.yml`）が実行する。

| 確認 | 内容 |
|---|---|
| `.meta`ファイル | `Assets/`以下のすべてのファイルとフォルダに`.meta`があること、対応するファイルのない`.meta`がないこと |
| GUIDの重複 | 2つの`.meta`が同じGUIDを持たないこと（Assetのコピーやマージで起きやすい） |
| 文字列表 | 各言語のJSONが読めること、すべての言語で同じKeyがそろっていること |
| 文書の日英対応 | `docs/ja`と`docs/en`に同じ文書がそろっていること |
| SBOM | `sbom/virtual-vessel-studio.cdx.json`に、すべてのUnity Package（ロックされた版で）、`Assets/ThirdParty`の各フォルダー、Workflowが使うGitHub Actionが載っていること。各項目に版、ライセンス、範囲（`required`／`excluded`）があること。取得して使う依存（`tools/deps/*.json`、Embedded Package）は、そこに固定した版で載り、SBOMのダウンロード先とSHA-256が一致すること |
| 取得して使う依存 | `tools/deps/*.json`に、Package名、版、`https`のダウンロード先、SHA-256（モデル等の中身のHashも）があること |
| ライセンスのファイル | `tools/release/license-files.json`が、配布物に入れるライセンスのファイルと、その元のファイルを対応づけていること。SBOMの項目が挙げるファイル（18.1.2）がすべてそこにあること。`Assets/ThirdParty`のライセンスのファイルがすべて配布されること。Gitで管理する元のファイルが存在すること |

- 確認はgitで追跡しているファイルに対して行う。手元にだけあるファイルは対象にしないため、コミットし忘れや`.meta`の抜けを見つけられる。
- 確認処理は`tools/ci/repo_checks.py`にあり、確認処理自体の単体テスト（`tools/ci/test_repo_checks.py`）もCIで実行する。
- 標準ライブラリだけを使うため、venvや`pip`は不要である。手元ではPython 3.10以上で、リポジトリの直下から`python tools/ci/repo_checks.py`を実行する。CIはPython 3.12を使う。
- Unityのテスト（EditMode、PlayMode）は、実行環境とUnityのライセンスの扱いを決めてから追加する。

Native Pluginは、C++のソースをGitで管理し、ビルドしたDLLはGitに入れない。GitHub Actionsの`Native plugins`（`.github/workflows/native-plugins.yml`）が、`native/`、`tools/native/`、またはこのWorkflowが変わったPull Requestと、`main`へのPushで、Windows上でビルドとテストを行う。

| Plugin | 内容 |
|---|---|
| Capture（`native/capture/`） | 開発者と同じ`tools/native/build-capture.ps1`で、CMakeの構成、Releaseのビルド、Nativeのテスト（`vv_capture_tests`）を行う。できたDLLをArtifactとして14日間保存する |

- 手元では、同じスクリプトでビルドし、DLLをUnityのProjectへ写す（Visual StudioとCMakeが必要）。
- 配布物へのDLLの組み込みは、main Build（18.2）のNative Plugin Packagingで扱う。

### 18.1.2 SBOM

言語の標準ライブラリとOS以外に使うもの（Unity Package、フォントやモデル等の同梱Asset、Pluginに組み込むNativeのライブラリやランタイム、Python Package、外部OSS、GitHub Action、ビルドツール）は、すべて`sbom/virtual-vessel-studio.cdx.json`（CycloneDX 1.6）に記録する。

- 追加、更新、削除と同じ変更でSBOMを直す。
- 各項目に版、ライセンス、範囲を書く。配布物に含まれるものは`required`、開発やCIだけで使うものは`excluded`とする。
- Repository checks（18.1.1）が、Unity Package、`Assets/ThirdParty`、GitHub Actionの抜けや古い項目を検出する。それ以外（Nativeのランタイム、ツール等）は、追加した人が書く。
- 配布物に含まれる項目には、その項目に当てはまるライセンスの全文のファイルを、Property `virtual-vessel-studio:license-file`で挙げる（値は配布物の中のパス。例：`licenses/UniGLTF-LICENSE.md`）。1つの項目に複数のファイルを挙げてよく、複数の項目が同じファイルを挙げてもよい。アプリ自身（`metadata.component`）は`LICENSE`と`NOTICE`を挙げる。
- 挙げたファイルは`tools/release/license-files.json`に、配布物の中のパスと、リポジトリの中の元のファイルの組で書く。Gitに入らない元のファイル（Unityが展開するPackage、取得して使うPackage）は、Packageのハッシュの部分を`*`で書ける。リリースの手順（21章）は、この表のとおりにファイルを配布物へ写す。設定画面のクレジット（`docs/ja/ui/screens/setup.md` 8章）は、SBOMとこれらのファイルを読んで表示する。
- Unity Package（Unity Companion License等）、Unity本体、Microsoft C/C++ Runtimeのように、配布に全文の同梱を求めないものは、ファイルを挙げずにライセンスの名前だけを載せる。

## 18.2 Release Build

Releaseのときに、`main`の版のTagの状態でBuildする（21章）。

- Native Plugin：`develop`と`main`へのPushごとに、GitHub Actionsの`Native plugins`がBuildとTestを行い、DLLをArtifactとして14日間保存する（18.1.1）。
- Windows Application：UnityはGitHub-hosted Runnerでまだ動かせない（Unityのライセンスの扱いを決めていない）。そのため、開発者のPCで`tools/release/build-release.ps1`がBuildする。同じScriptは、将来のSelf-hosted Runnerでもそのまま動かせる。

## 18.3 Nightly / Hardware CI

RVCやCapture等の実機依存処理は通常のCloud Runnerだけでは十分検証できない。

そのため、将来的に専用Windows PCをSelf-hosted Runnerとして利用する。

対象例:

- RVC Latency Benchmark
- GameCapture Benchmark
- SubScreenCapture Benchmark
- GPU利用
- Capture Board実機
- Multi Monitor
- Audio Device
- Long Run Test

Nightly CIでは、正しさだけでなく**性能Regression**を監視する。

## 18.4 Self-hosted Runnerの安全性

公開OSS Repositoryの場合、外部から送られた未信頼PRを開発PCや実機Benchmark PC上で直接実行しない。

```text
外部PR
  ↓
GitHub-hosted CI

main / trusted code
  ↓
Self-hosted Hardware CI
```

Self-hosted Runnerは信頼済みCodeを対象とする。

---

# 19. main / develop Branchの保護

GitHub側で`main`と`develop`を同じ設定で保護する。Pull Requestの既定の宛先（Default Branch）は`develop`とする。

最低限、以下を設定する方針とする。

- Pull Requestを必須とする
- Required Status Checksを必須とする
- Force Pushを通常禁止する
- Branch削除を禁止する
- 必要に応じてConversation Resolutionを必須とする

一人開発の初期段階では「他者から1 Approval必須」とすると自分でMergeできなくなる場合があるため、Review Approvalの必須化はContributorが増えてから導入してもよい。

現在の設定は以下とする。

- Pull Request必須（必要Approval数は0）
- 管理者にも保護を適用する（管理者も`main`へ直接Pushしない）
- Force Push禁止
- Branch削除禁止
- Conversation Resolution必須
- Required Status Checksに`Repository checks`を設定する（18.1.1）
- `main`と`develop`の両方に同じ設定を適用する

## 19.1 Merge権限

Pull RequestをMergeできるのは、Repositoryへの書き込み権限（Write以上）を持つ者に限られる。

現在の書き込み権限保持者はRepository Ownerのみとし、Owner以外がPull RequestをMergeできない状態を維持する。

Collaboratorを追加する場合は、原則としてTriage以下の権限を付与する。

Write以上の権限を付与する必要が生じた場合は、CODEOWNERSおよびReview必須化を併せて検討する。

---

# 20. CIと性能試験の違い

## CI

確認すること:

**正しくBuild・動作するか**

例:

- Compileできる
- Testが通る
- Serializationできる
- Module連携が壊れていない

## Performance / Hardware CI

確認すること:

**前より遅くなっていないか、実機環境で成立するか**

例:

- RVC Latency
- Frame Drop
- Capture FPS
- Audio Underrun
- GPU Memory
- Long Run Stability

両方をPassして初めてRealtime機能の品質を確認できる。

---

# 21. Release

ReleaseはGit Tagを基準とする。Tagは`main`のCommitにだけ付ける。

例:

```text
v0.1.0
v0.2.0
v1.0.0
```

```mermaid
flowchart LR
    Bump["developで版を上げる<br/>（Pull Request）"]
    PR["Release PR<br/>develop → main"]
    Tag["mainにTag vX.Y.Z"]
    WF["Release Workflow<br/>Tagと版の確認、下書きのRelease"]
    Local["Self-hosted Runner（管理者のPC）<br/>build-release.ps1 -Upload<br/>Test、Build、Package、Checksum"]
    Publish["内容を確かめて手で公開"]

    Bump --> PR --> Tag --> WF --> Local --> Publish
```

手順：

1. `develop`で、Projectの版（`PlayerSettings.bundleVersion`、22章）を上げるPull Requestを作り、Mergeする。
2. `develop`から`main`へのRelease PRを作り、Mergeする。
3. `main`のMerge Commitに`vX.Y.Z`のTagを付け、Pushする（Push、Tagは利用者が行うか、Claude Codeが承認を得て行う）。
4. GitHub Actionsの`Release`（`.github/workflows/release.yml`）が、Tagの形、Projectの版との一致、Tagが`main`にあること、Repository checksを確かめ、下書き（Draft）のGitHub Releaseを作る（変更点はPull Requestから自動で書く）。
5. 同じWorkflowの`build` Jobが、管理者のPC（Self-hosted Runner、21.1）で動く。Tagの状態を取り出し、`pwsh tools/release/build-release.ps1 -Version X.Y.Z -Upload`を実行する。ScriptはNative PluginのBuildとTest、UnityのEditMode / PlayMode Test、Windows Build（`ReleaseBuild.BuildWindows`）、License類とSBOMの同梱を行う。その後、ZIPとChecksumを作り、下書きのReleaseへ載せる。Runnerを用意していないとき等は、Tagの状態を取り出してScriptを手で実行してもよい。
6. 下書きの内容を確かめ、GitHub上で手で公開する。

- ReleaseのBuildは、Build識別子`vX.Y.Z+<commit>`を`StreamingAssets/build-id.txt`に書く。Applicationは起動時にそれを読み、Logと診断に出す（方式設計5.29）。開発中のBuildやEditorでは`development`となる。
- Packageには、`TRADEMARKS.md`と、`tools/release/license-files.json`に挙げたファイル（`LICENSE`、`NOTICE`、`licenses/`以下の各License、SBOM）を入れる（18.1.2）。

生成物例:

```text
VirtualVesselStudio-v0.1.0-win-x64.zip
SHA256SUMS.txt
```

## 21.1 ReleaseのRunner（Self-hosted）

UnityはまだGitHubのRunnerで動かせない（ライセンスのため）。そのため、ReleaseのBuildは、管理者のWindows PCをGitHub ActionsのSelf-hosted Runnerとして動かす（利用者の決定、2026-10-25）。

準備（1回だけ）：

1. GitHubで、RepositoryのSettings → Actions → Runners → New self-hosted runner（Windows、x64）を開き、その手順でRunnerを取得して設定する。追加のLabelに`vvs-release`を付ける。
2. Runnerは、Unity Hubを使うのと同じWindowsのユーザーで動かす。Unityのライセンスと、入れたEditorが見つかるようにするためである。手で起動するなら`run.cmd`を実行する。そのユーザーで動くServiceとして入れてもよい。
3. PCに、`build-release.ps1`が使うものを入れる：`ProjectVersion.txt`の版のUnity Editor、Visual StudioのC++ Build Tools（Native Plugin用）、PowerShell 7、GitとGit LFS、GitHub CLI。
4. Settings → Actions → Generalで、「Fork pull request workflows from outside collaborators」を「Require approval for all outside collaborators」にする。

安全（方式設計6.13、29.10章）：

- Repositoryは公開している。ForkからのPull Requestは、そのPull RequestのWorkflowを動かすため、`vvs-release`のLabelを指定して、PCでCodeを動かせてしまう。4の設定で、そうした実行は管理者の承認を待つ。Workflowに触れるForkの実行は承認しない。
- ReleaseのWorkflowは、このRepository（`github.repository`）へのTagのPushのときだけ、`draft` JobがTagが`main`にあることを確かめた後でBuildを始める。TagをPushできるのは管理者だけである。
- Jobが受け取るのは、Workflowの`GITHUB_TOKEN`（Releaseのための、contentsへの書き込み）だけである。ほかの秘密情報は置かない。
- Release以外のときはRunnerを止めておく（`run.cmd`を閉じる）のが、最も安全である。その場合、BuildはRunnerを起動したときに始まる（Jobが待ち時間の上限の24時間を過ぎていなければ）。

---

# 22. Versioning

Semantic Versioningを基本とする。

開発中:

```text
0.1.0
0.2.0
0.3.0
```

安定版:

```text
1.0.0
```

目安:

- PATCH: Bug Fix
- MINOR: 後方互換な機能追加
- MAJOR: 大きな非互換変更

Project Schema等はApplication Versionと完全に同一である必要はなく、独自の`schemaVersion`を持たせる。

Applicationの版の正は、Unityの`PlayerSettings.bundleVersion`（`ProjectSettings/ProjectSettings.asset`）とする。Release WorkflowとRelease Scriptは、Tagとこの値が一致しないと止まる。

---

# 23. Claude CodeのSession運用

Claude CodeとのConversationそのものをProjectの唯一の記憶場所にはしない。

原則:

**Chatに覚えさせるのではなく、Repositoryに覚えさせる。**

重要な情報は以下へ残す。

- `CLAUDE.md`
- 方式設計
- 詳細設計
- ADR / Decision
- Issue
- Pull Request
- Test
- Benchmark Result

Taskが完了したら、新しいModuleでは新しいSessionを開始してよい。

---

# 24. CLAUDE.mdに書くもの / 書かないもの

## 書く

- 最重要Architecture Rule
- 必ず読むDocument
- Benchmarkの扱い
- Task開始手順
- Git Workflow（Branch、Commit、Push、禁止操作）
- Test / Review原則
- User confirmationが必要な条件
- Coding Style、Documentation等、Claude Codeが常に守るべき規約

## 書かない

- 全Moduleの詳細Class構成
- 長大な方式設計本文
- 個別Task専用の指示
- 一時的なBugの内容
- Benchmark結果の全履歴
- 人間向けの背景説明、CI / Release構成の詳細

詳細情報は適切な文書へ分離する。

---

# 25. 小規模Bug Fixの簡略フロー

```text
Task
 ↓
Branch
 ↓
Claude調査
 ↓
Fix
 ↓
Test
 ↓
Self Review
 ↓
PR
 ↓
CI
 ↓
Merge
```

例えば、Null Check、表示Text修正、単純な条件式、Log Message等では新しい詳細設計書を作る必要はない。

---

# 26. 大規模機能の標準フロー

```text
Task / Issue
   ↓
Feature Branch
   ↓
ClaudeによるRepository調査
   ↓
Benchmark / Prototype解析
   ↓
詳細設計
   ↓
必要ならユーザー設計確認
   ↓
実装
   ↓
Unit / Integration Test
   ↓
Benchmark
   ↓
Claude Self Review
   ↓
Documentation更新
   ↓
Pull Request
   ↓
PR CI
   ↓
ユーザーReview
   ↓
Merge
   ↓
Nightly Hardware Benchmark
```

---

# 27. 最初の開発フェーズ

最初から全CI/CDや全自動化を完成させる必要はない。

## Phase 1: Repository基盤

- GitHub Repository
- `.gitignore`
- `.gitattributes`
- `CLAUDE.md`
- 方式設計配置
- 本開発運用文書配置
- Branch / PRルール

## Phase 2: 最低限のCI

- Unity Compile / Test
- C# Test
- Native Plugin Build
- Python Test
- PR Required Check

## Phase 3: 1 Moduleを完成まで回す（完了：Capture、2026-10）

例えばCapture Moduleで、

```text
調査
→ 詳細設計
→ 実装
→ Test
→ Benchmark
→ PR
→ CI
→ Merge
```

を一度通す。

この結果から`CLAUDE.md`やCIを修正する。

## Phase 4: Hardware Benchmark CI

- Windows Self-hosted Runner
- RVC Benchmark
- Capture Benchmark
- Long Run Test

## Phase 5: Release Automation（Tagの確認と下書きはCI、Build、Package、Uploadは管理者のPC（Self-hosted Runner）、公開は手で行う）

- Tag
- Build
- Package
- GitHub Release

Phase 3の振り返り（2026-10-18）：

- UnityのTest実行、Native Build、UIの画像は`tools/`のScriptにまとめた（14章）。
- UnityのTestとBuildをCIで動かすのは、Unityのライセンスの扱いを決めるまで保留する（Phase 2の残り）。
- 開発が進んだため、`develop`を設けた（6.1）。

---

# 28. 開発完了の定義

Taskはコードを書いた時点では完了ではない。

対象Taskに必要な範囲で以下が完了していることを確認する。

- 実装
- Build
- Test
- Benchmark
- Self Review
- Documentation
- Pull Request
- CI

完了報告では以下を明示する。

```text
変更内容:
設計判断:
変更File:
Test:
Benchmark:
Documentation:
残課題:
```

---

# 29. 開発における基本原則

1. 方式設計は人間が管理する。
2. 実装詳細はClaude Codeへ積極的に任せる。
3. 大きな変更ではClaude Code自身に詳細設計を作らせる。
4. Benchmark済みコードはPerformance Baselineとして扱う。
5. 1 Taskごとに短命Branchを作る。
6. `main`へ直接Pushしない。
7. Pull RequestとCIをMerge Gateとする。
8. Claudeの自己申告だけで変更を受け入れない。
9. CIとHardware Benchmarkを分離する。
10. 外部未信頼CodeをSelf-hosted Runnerで実行しない。
11. DocumentationをCodeと同期させる。
12. 開発上の知識をChatではなくRepositoryへ残す。
13. 小さいTaskは軽量に、大きいTaskは設計・Benchmarkまで実施する。
14. 最初から過剰な自動化を作らず、開発Cycleを回しながら強化する。
15. 最終的なProduct / Architecture判断はユーザーが行う。

---

# 付録A. 推奨Pull Request Template

実際のTemplateは`.github/pull_request_template.md`とし、`CLAUDE.md` 30章の項目に合わせる。

```markdown
## Summary

## Related issue

## Design

## Changes

## Commits

## Tests

## Benchmarks

## UI verification

## Compatibility / Migration

## Documentation

## Remaining issues
```

---

# 付録B. Claude Codeへの標準依頼

## 大規模Task開始

```text
このTaskについて、まず実装せず調査してください。

1. CLAUDE.md
2. 関連する方式設計
3. 関連する詳細設計
4. 既存実装
5. Benchmark / Prototype
6. Test

を確認してください。

その上で現状、変更方針、再利用箇所、主要な設計、Test方針、
Performance上の注意、未決事項を整理してください。

方式設計の変更が必要な場合は、実装せず先に提示してください。
```

## 実装開始

```text
調査結果と詳細設計に従って実装してください。

- CLAUDE.mdを厳守
- 無関係な変更を含めない
- 必要なTestを追加
- Logging / Diagnosticsも実装
- Build / Testを実行
- 必要ならBenchmarkを実行
- Self Reviewを実施
- Documentationを同期

完了時に、変更内容、Test、Benchmark、Documentation、残課題を報告してください。
```

## Review

```text
現在の変更を別の実装者の視点でレビューしてください。

特に、
Architecture、
Module境界、
Thread Safety、
Resource Lifecycle、
Realtime Performance、
Security / Privacy、
Logging、
Test、
Documentation
を確認してください。

問題があればTask範囲内で修正し、再度Testしてください。
```
