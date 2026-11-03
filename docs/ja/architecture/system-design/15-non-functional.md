# 15. 非機能方式

## 15.1 基本方針

本システムは、3Dアバター制御、リアルタイムトラッキング、音声変換、映像生成、ライブ配信等を同時に実行するリアルタイムアプリケーションである。

そのため、単に各機能が動作するだけでなく、以下を重要な非機能要件として扱う。

- 性能
- リアルタイム性
- 安定性
- 障害分離
- 拡張性
- 保守性
- セキュリティ
- プライバシー
- データ保全性
- 互換性
- 可観測性
- 利用者環境への導入容易性

各機能は独立して最適化するだけではなく、システム全体のCPU、GPU、Memory、Audio、Storage、Network等の利用状況を考慮する。

### 採用理由

本システムでは、

- Unity Rendering
- Tracking
- RVC
- Video Encoding
- Audio Processing
- Streaming

等の高負荷処理が同時に動作する。

個々の機能だけを基準に性能設計すると、単体では正常でも統合時にFrame Dropや音切れが発生する可能性がある。

そのため、システム全体を一つのリアルタイムPipelineとして扱う。

---

## 15.2 性能方式

性能は、単純な処理速度だけではなく、各Runtime Pipelineが必要な時間内に処理を完了できることを基準とする。

主な監視対象として以下を扱う。

- Rendering Frame Time
- Tracking Processing Time
- Voice Conversion Processing Time
- Capture Processing Time / Capture Frame Rate
- Audio Buffer状態
- Video Encoding Time
- Streaming Queue
- Memory使用量
- GPU使用量
- CPU使用量

Developer Diagnosticsから必要に応じて確認可能とする。

### 採用理由

CPU使用率等の単一指標だけでは、リアルタイム処理の品質を判断できない。

例えばCPU使用率が低くても、一部処理だけが瞬間的に遅延すれば音切れが発生する。

そのため、Pipeline単位の処理時間も測定する。

---

## 15.3 リアルタイム性

以下の処理については特にリアルタイム性を重視する。

- Tracking
- Avatar Pose
- Face Expression
- Voice Conversion
- Audio Mixing
- GameCapture / SubScreenCapture
- Rendering
- Video Encoding

各処理では、平均処理時間だけでなく、処理時間のばらつきも考慮する。

### 採用理由

リアルタイム配信では、平均性能が十分でも、特定Frameだけ極端に遅い状態が繰り返されると品質が低下する。

そのため、

- 平均値
- 最大値
- 遅延分布
- Drop発生

等を確認可能な構成とする。

---

## 15.4 音声リアルタイム性能

音声処理は、映像処理以上に瞬間的な処理停止の影響を受けやすいため、優先的にリアルタイム性を確保する。

RVCを含む音声変換Pipelineでは、End-to-End遅延を主要な性能指標として扱う。

現行設計では、リアルタイム音声変換の処理Deadlineとして**200 ms以内**を一つの基準とする。

ただし、単純に総遅延のみを計測するのではなく、

- Input Buffer
- Feature Extraction
- Pitch Estimation
- RVC Inference
- Post Processing
- Output Buffer

等へ分解して計測可能とする。

### 採用理由

遅延原因を特定可能にすることで、性能低下時にどの処理を改善すべきか判断しやすくする。

また、音声処理が他のUI処理やScene処理によって停止しない構成を維持する。

---

## 15.5 映像リアルタイム性能

GameCapture / SubScreenCapture、Rendering、Video Encoding、Streamingについては、設定されたFrame Rateを継続的に維持することを目標とする。

Frame生成が間に合わない場合には、その状態を検出し、

- Capture Dropped / Missed Frame
- Dropped Frame
- Encode遅延
- Streaming Queue増大

等をDeveloper Diagnosticsへ記録する。

### 採用理由

映像処理の遅延が蓄積すると、配信が実時間から徐々に遅れていく可能性がある。

そのため、処理遅延を蓄積し続ける構成ではなく、リアルタイム性を維持することを優先する。

---

## 15.6 AudioとVideoの同期

音声と映像は共通の時間基準に基づいて同期する。

Voice Conversion等によってAudio側に遅延が発生する場合には、10章で定義したA/V Synchronization機能によって必要に応じて映像側を遅延させる。

同期状態についてはA/V Offsetを測定可能とする。

### 採用理由

個別にAudioとVideoがリアルタイム処理されていても、それぞれの遅延量が異なると視聴者には口の動きと音声のずれとして認識される。

そのため、個別性能だけでなく最終的な同期品質も非機能要件として扱う。

---

## 15.7 Main Thread負荷抑制

時間のかかる処理をUnity Main Threadへ集中させない。

対象例：

- ファイルI/O
- 外部OSSダウンロード
- Python Service起動
- Model Load
- Dataset処理
- ネットワーク通信
- 長時間解析

可能な処理については非同期処理または適切なWorker処理へ分離する。

### 採用理由

Main Threadが長時間占有されると、

- UI停止
- Rendering停止
- Tracking更新停止

等が発生する。

特に配信Runtimeでは、一つの重い処理がシステム全体の停止につながらないようにする。

---

## 15.8 Resource管理方式

CPU、GPU、Memory等のResource使用量を考慮して各機能を設計する。

特にGPUについては、

- Unity Rendering
- Tracking
- RVC
- Video Encoding

等で同時利用する可能性がある。

各機能が個別にGPU Resourceを大量確保し続けないようにする。

### 採用理由

本システムではGPUを複数用途で共有するため、単一機能の最適化だけではシステム全体の性能を保証できない。

必要に応じてResource使用状況をDeveloper Diagnosticsへ表示可能とする。

---

## 15.9 不要Resourceの解放

以下のような使用していないResourceは、適切なタイミングで解放可能とする。

- 未使用Avatar
- Unload済みStage
- 使用終了したVoice Model
- Diagnostic Render Target
- Capture Texture / Native Capture Resource
- Python Service
- Temporary Audio Buffer
- Preview用Resource

### 採用理由

長時間配信するアプリケーションでは、小さなResource Leakでも数時間後には大きな問題となる可能性がある。

短時間の正常動作だけではなく、長時間運用を前提とする。

---

## 15.10 安定性

一部の機能に異常が発生しても、可能な限りアプリケーション全体を停止させない。

例えば、

- Streaming接続失敗
- Recording失敗
- Voice Conversion失敗
- Tracking Lost
- Python Service停止

等を個別に扱う。

### 採用理由

配信中に単一機能の失敗によってアプリケーション全体が終了すると影響が大きい。

そのため、各Moduleの障害を可能な限りそのModule内に閉じ込める。

---

## 15.11 障害分離方式

主要Module間は、直接内部状態を書き換えるのではなく、明確なInterfaceやデータ境界を介して連携する。

概念的には以下のように扱う。

```text
Tracking Failure
      ↓
Tracking Module Error
      ↓
Avatar Runtimeは継続可能

Streaming Failure
      ↓
Streaming Module Error
      ↓
Avatar / Voice / Recordingは継続可能
```

### 採用理由

Module間が密結合すると、一つのExceptionが複数機能へ連鎖しやすくなる。

責務境界を明確にすることで、障害影響範囲を限定する。

---

## 15.12 Graceful Degradation

一部機能が利用できない場合でも、可能であれば縮退動作を行う。

例：

- RVC使用不能 → Pass ThroughまたはMute
- Hand Tracking不能 → Body Trackingのみ
- Face Tracking不能 → Body Trackingのみ
- YouTube接続失敗 → Local Recording継続
- Capture Device切断 → 対象Capture Sourceのみ停止
- Diagnostic機能失敗 → 通常Runtime継続

縮退方法についてユーザー選択が必要な場合は設定可能とする。

### 採用理由

すべての機能が完全正常でなければアプリケーションを利用できない構造を避ける。

---

## 15.13 Recovery方式

復旧可能な障害については、アプリケーション全体再起動を必要としない復旧方法を可能な範囲で用意する。

例：

- Streaming再接続
- Tracking Provider再初期化
- Audio Device再初期化
- Voice Model再読み込み
- Python Service再起動

### 採用理由

長時間の配信中に一時障害が発生した場合でも、可能な限り影響範囲を限定して復旧できるようにする。

---

## 15.14 長時間運用

本システムは数時間以上の連続配信を想定する。

長時間運用時には以下を考慮する。

- Memory Leak
- GPU Memory Leak
- Buffer蓄積
- Log容量増加
- Temporary File増加
- Network再接続
- Device切断・再接続

### 採用理由

短時間のテストでは検出できない問題が、長時間運用によって顕在化する可能性がある。

そのため、System Testでは長時間動作試験も実施する。

---

## 15.15 拡張性

新しい機能追加によって既存Moduleを大幅変更しない構成を基本とする。

対象例：

- 新3Dモデル形式
- 新Tracking Provider
- 新Voice Conversion Engine
- 新Streaming Service
- 新Video Encoder
- 新Locale
- 新Stage形式
- 新外部入力デバイス

### 採用理由

本システムでは将来対応する外部技術が増えることが想定される。

そのため、現在利用する具体技術そのものをシステム中心に置かない。

---

## 15.16 Interface・Adapter方式

外部技術との境界には、必要に応じてInterfaceまたはAdapterを設ける。

例：

```text
IAvatarLoader
ITrackingProvider
IVoiceConverter
IVideoEncoder
IStreamPublisher
```

外部OSSについてもAdapter / Wrapperを基本とする。

### 採用理由

外部技術の変更をシステム全体へ波及させないためである。

---

## 15.17 外部OSS更新耐性

外部OSS本体への独自改変は可能な限り避ける。

RVC等については13章で定義した、

- Version固定
- Adapter
- Source + venvのVersion分離
- 新Versionの並行導入
- Rollback

を利用する。

### 採用理由

外部OSSを大量に改変すると、上流更新への追従が困難になる。

外部コードと自作コードの境界を明確に維持する。

---

## 15.18 保守性

機能ごとに責務を分離し、巨大なManagerやControllerへ処理を集中させない。

Module間の依存方向を明確にする。

可能な限り、

- Core Data
- Interface
- Runtime
- Setup
- Adapter
- UI

等を分離する。

### 採用理由

機能追加や修正時に影響範囲を把握しやすくするためである。

---

## 15.19 コード可読性

コードは、単に動作することだけでなく、後から責務および設計意図を理解できることを重視する。

特に、

- Classの責務
- Public API
- 複雑な制御
- 非自明な処理
- 外部OSSとの境界

については必要なCommentおよびDocumentationを残す。

### 採用理由

OSSとして複数の開発者がコードを読む可能性があり、自動開発エージェントによる継続的な変更も想定しているためである。

---

## 15.20 テスト容易性

各Moduleを可能な範囲で独立してテスト可能な構造とする。

テストを大きく以下に分類する。

```text
tests/
├─ unit/
├─ integration/
├─ system/
└─ performance/
```

特定のComponentやUnity Assemblyへ自然に属するテストは、そのComponent側へ配置してもよい。上記はそれ以外のテストの配置先を示す（3.3参照）。

### Unit Test

個々の処理を検証する。

### Integration Test

複数Module間の連携を検証する。

### System Test

実際のUnity Applicationとして、

- Tracking
- Avatar
- Voice
- Streaming

等を統合して検証する。

### Performance Test

RVC Latency、Capture性能、Frame Time、長時間運用等、リアルタイム処理の性能を計測する。

15.21の性能回帰テストに加え、実機依存の計測は開発運用方針で定義する夜間・実機CI（Self-hosted Runner）で実行する。

UnityのTestはUnity Project内にのみ配置できるため、Unity上で実行する性能テスト本体は各Moduleの`Tests/Performance/`へ配置する。`tests/performance/`にはHardware CI用の実行Script、比較用の基準値、結果の履歴を置く。過去のBenchmark / Prototype実装は参照用としてRepository外に置く。

### 採用理由

すべてをUnityアプリケーション全体でしか検証できない構造では、障害原因の切り分けが困難になる。

また、性能計測を正しさのテストと分離することで、通常のPR CIと実機を必要とする性能計測を別々に実行できる。

---

## 15.21 性能回帰テスト

リアルタイム性が重要な処理について、機能が正しく動作するだけでなく性能劣化も検出可能とする。

対象例：

- Voice Conversion Latency
- Tracking Processing Time
- Render Frame Time
- Video Encoding Time

### 採用理由

リファクタリングや機能追加によって、結果は同じでも処理時間だけ悪化する可能性がある。

リアルタイムアプリケーションでは性能低下もRegressionとして扱う。

---

## 15.22 セキュリティ基本方針

外部入力および秘密情報を信頼しないことを基本とする。

対象例：

- Project Import
- Avatar File
- Stage
- Audio File
- External Component Download
- Streaming Credential
- Python Process
- Local API

入力値およびファイルについて必要なValidationを行う。

### 採用理由

OSSとして配布されるアプリケーションであり、利用者が外部から取得したファイルを読み込む可能性がある。

不正または破損した入力によってシステム全体が不安定になることを防ぐ。

---

## 15.23 秘密情報保護

以下の情報は秘密情報として扱う。

- Stream Key
- OAuth Token
- API Key
- その他Credential

秘密情報は、

- 通常JSON
- Log
- Diagnostic Export
- Screenshot向け表示
- Git Repository

へ出力しない。

保存には6章で定義したOS Credential Store等を利用する。

### 採用理由

OSS開発では、ログや設定ファイルがIssue等へ添付される可能性がある。

秘密情報を通常データと分離することで、漏洩リスクを低減する。

---

## 15.24 外部ダウンロードの安全性

RVC等の外部コンポーネントを自動取得する場合、取得元およびVersionをManifestで管理する。

必要に応じてHash等による検証を行う。

### 採用理由

自動セットアップでは、利用者が実際に何を取得しているか意識しない。

そのため、アプリケーション側が意図したComponentを取得していることを確認可能にする。

---

## 15.25 Local Serviceの公開範囲

Python Local Service等は、原則として外部ネットワークからアクセス可能な状態にしない。

必要な場合を除き、Localhostでのみ待ち受ける構成とする。

### 採用理由

学習・解析用サービスをLANやInternetへ公開する必要は通常ない。

不要なAttack Surfaceを増やさない。

---

## 15.26 プライバシー基本方針

本システムは、

- Camera映像
- GameCapture / SubScreenCaptureで取得する画面映像
- Microphone音声
- Voice Dataset
- Voice Model
- Tracking Data

等、利用者にとって重要なデータを扱う。

これらについて、意図しない外部送信を行わないことを基本とする。

### 採用理由

特に実写Camera映像や音声Datasetは、外部へ送信されることによる影響が大きい。

ローカル処理可能な機能についてはローカル処理を基本とする。

---

## 15.27 Diagnostic Cameraのプライバシー

Diagnostic CameraやCamera Input映像は、10章および14章で定義した通りStreaming Pipelineから構造的に分離する。

### 採用理由

実写Camera映像やTracking Debug表示が、操作ミスによってYouTubeへ送信されることを防止する。

単なる設定ではなくPipeline構造によって分離する。

---

## 15.28 診断情報のプライバシー

Developer Diagnosticsおよび診断情報Exportでは、以下をそのまま出力しない。

- Stream Key
- OAuth Token
- API Key
- Password
- 不要なCamera画像
- 不要なMicrophone音声

必要なPath等についても、将来的に個人情報を含み得る場合はMaskingを検討する。

### 採用理由

OSSのIssueへ診断情報を添付する際、利用者が内容を詳細確認せず公開する可能性があるためである。

---

## 15.29 データ保全性

Project、Profile、Voice Lab DB等の重要データを更新する場合、処理途中でアプリケーションが終了しても可能な限り破損しにくい方式を採用する。

設定ファイル更新では必要に応じて、

```text
Temporary File
      ↓
Validation
      ↓
Atomic Replace
```

等を利用する。

### 採用理由

設定ファイル書き込み途中でアプリケーションが終了すると、元データまで失われる可能性がある。

---

## 15.30 Backup・Migration安全性

MigrationやData Root移動等、大量データを変更する処理では、既存データを即座に破棄しない。

変換・コピー・Validation完了後に新データへ切り替える。

### 採用理由

Migration失敗によって、正常に利用できていた既存データまで失うことを防止する。

---

## 15.31 互換性

アプリケーション更新後も、可能な範囲で旧VersionのProject、Profile等を読み込める構成とする。

6章で定義したSchema VersionおよびMigrationを利用する。

### 採用理由

アプリ更新のたびに既存Projectを作り直す必要がある構成は利用者負担が大きい。

---

## 15.32 外部環境互換性

以下の環境差異を考慮する。

- GPU
- GPU Driver
- Display Resolution
- Audio Device
- Camera Device
- Capture Board
- Display / Monitor構成
- Desktop Duplication対応可否
- CUDA環境
- Python環境
- 外部OSS Version

特定環境にしか存在しない機能を利用する場合は、起動時またはSetup時にCapabilityを確認する。

### 採用理由

開発環境で動作した構成が、すべての利用者環境で利用可能とは限らない。

機能使用時に初めて失敗するのではなく、可能な範囲で事前検出する。

---

## 15.33 Capability Detection方式

HardwareやRuntime機能について、利用可能性を検出可能とする。

対象例：

- GPU利用可否
- Hardware Video Encoder
- Camera
- Microphone
- Capture Board
- SubScreenCapture / Desktop Duplication
- Tracking Provider
- Python Service
- External Component

UIでは、利用できない機能を単に失敗させるのではなく、

- 利用不可
- 未セットアップ
- 非対応

等の状態として表示する。

### 採用理由

環境依存機能を明示的に管理することで、利用者が問題原因を理解しやすくする。

---

## 15.34 可観測性

システム内部の重要状態を、Developer Diagnosticsから観測可能とする。

対象例：

- Frame Time
- Tracking FPS
- Tracking Confidence
- Voice Latency
- Audio Buffer
- Encoder Time
- Dropped Frames
- A/V Offset
- Memory
- GPU
- External Service State

### 採用理由

リアルタイムシステムの問題は、単純なExceptionとして現れない場合が多い。

内部状態を観測可能にすることで、性能問題や一時的な異常を分析しやすくする。

---

## 15.35 Logging方式

Logは少なくとも以下のLevelを持つ。

```text
Trace
Debug
Information
Warning
Error
Critical
```

通常Runtimeでは必要以上に大量のDebug Logを出力しない。

Developer Mode等で詳細度を変更可能とする。

### 採用理由

常時大量のLogを出力すると、Storage使用量およびI/O負荷が増える。

一方で障害解析には詳細Logが必要となるため、用途に応じて切り替える。

---

## 15.36 Log Rotation

Log Fileが無制限に増加しないようにする。

以下のような方式を利用可能とする。

- File SizeによるRotation
- 日付によるRotation
- 保持世代数
- 古いLogの自動削除

### 採用理由

長時間利用や長期間利用によってLogだけでStorageを圧迫することを防止する。

---

## 15.37 導入容易性

一般利用者へ、

- Git操作
- PythonのInstall / Version選択
- venv作成・Activation
- pip install
- RVC内部設定
- Port設定

等を通常利用手順として要求しない。

必要なPython Componentは、アクティベーションのときにApplication側のSetup機能から取得・検証・構築する（13.4）。

### 採用理由

システム内部では複数技術を利用していても、一般利用者にその複雑性を露出させないことを基本とする。

---

## 15.38 Runtime依存最小化

配信Runtimeは可能な限り少ない外部依存で動作可能とする。

特にVoice Lab用Python環境が利用できなくても、既に登録済みの、

- Avatar
- Stage
- Voice Model
- Project

を利用した通常配信が可能な構成を維持する。

### 採用理由

Setup環境や学習環境の問題によって通常配信までできなくなることを防止する。

---

## 15.39 Claude Codeによる品質維持

Claude Codeを用いた開発では、機能追加時に既存の非機能要件を確認する。

特に以下をReview対象とする。

- Module境界
- Main Thread Block
- Resource Leak
- Error Handling
- Security
- Secret出力
- Localization
- Performance
- Test
- Documentation

必要な規則は`CLAUDE.md`および各設計ドキュメントへ記載する。

### 採用理由

自動化された開発でも、機能要件だけを満たして非機能要件が劣化することを防ぐ必要がある。

---

## 15.40 ユーザーによる品質確認

自動テストやClaude CodeによるReviewだけで、最終的な品質を判断しない。

実際の利用環境において、ユーザー自身も、

- Voice Quality
- Tracking Quality
- UI
- Latency
- Streaming
- Stability

等を確認する。

特に体感品質が重要な機能については、人間による評価を最終判断へ含める。

### 採用理由

音声品質、Avatarの自然さ、UI操作感等は数値指標だけでは完全に評価できないためである。

---

## 15.41 非機能テスト

Release前には必要に応じて以下を検証する。

- 長時間動作
- Memory使用量
- GPU Memory使用量
- Voice Latency
- Frame Rate
- A/V Sync
- GameCapture / SubScreenCaptureの連続取得
- Capture Device / Monitor切断時の動作
- Streaming再接続
- Device切断・再接続
- Project Migration
- Data Root移動
- External OSS Setup
- 日本語 / English UI
- Secret情報非出力

### 採用理由

単純な正常系の機能テストだけでは、実際の長時間配信や環境差による問題を検出できない。

---

## 15.42 非機能方式の基本原則

本システムでは、非機能要件について以下を基本原則とする。

1. 各機能単体ではなく、システム全体のリアルタイム性能を考慮する。
2. Audio、Tracking、Rendering等の時間制約がある処理をMain Thread上の重い処理から保護する。
3. Module単位で障害を分離する。
4. 一部機能障害時も可能な限り縮退動作する。
5. 長時間配信を前提としてResource Leakを防止する。
6. 外部技術はInterface / Adapterを介して利用する。
7. 外部OSS本体への変更を最小限にする。
8. Performance Regressionも不具合として扱う。
9. Projectや設定データの破損を防止する。
10. 秘密情報を通常設定・ログ・診断情報へ出力しない。
11. Camera、Microphone、Dataset等のプライバシーを保護する。
12. Local Serviceを不要に外部Networkへ公開しない。
13. 旧ProjectやProfileとの互換性をMigrationによって維持する。
14. Hardware / Device差異はCapability Detectionによって扱う。
15. Developer Diagnosticsによって内部状態を十分に観測可能とする。
16. Logを無制限に蓄積しない。
17. 一般利用者へGitやPython環境構築等の内部複雑性を要求しない。
18. 学習環境が利用できなくても通常配信Runtimeは利用可能とする。
19. Claude Codeによる実装でも非機能要件をReview対象とする。
20. 体感品質についてはユーザー自身による確認も行う。
21. Capture処理の負荷・Frame Drop・Device切断を非機能要件として扱う。
22. Capture Backend固有の制約をCapability DetectionおよびAdapter内部へ閉じ込める。
