# 16. 今後の拡張方針

## 16.1 基本方針

本システムは、初期バージョンですべての機能を実装することを目的とせず、将来的な機能追加を前提として設計する。

ただし、将来利用する可能性のある機能をすべて初期段階から実装するのではなく、

**将来変更が予想される箇所に適切な境界を設け、必要になった時点で機能を追加できる構造とする**

ことを基本方針とする。

主な拡張対象として以下を想定する。

- 複数人・複数アバター
- 新しいTracking方式
- 複数Tracking Sourceの統合
- 新しい3Dモデル形式
- 新しいCapture Source / Capture Backend
- 拡張ボーン制御方式の追加
- 新しいVoice Conversion方式
- Voice Labの機能拡張
- 新しい配信サービス
- 複数配信先への同時出力
- 新しいVideo / Audio Codec
- 新しい外部入力デバイス
- 新しいStage機能
- 新しいLocale
- UI Theme
- Plugin機構
- 外部連携API

### 採用理由

将来機能を予測して初期段階からすべて実装すると、現在必要のない複雑性が増加する。

一方で、現在の機能だけを前提とした密結合な構造にすると、将来機能追加時に大規模な再設計が必要となる。

そのため、

**機能は必要になってから実装するが、拡張可能な境界はあらかじめ設ける**

という方針を採用する。

---

## 16.2 複数人・複数アバター対応

初期実装では1人の利用を主対象とするが、将来的に複数人を同時に扱える構成への拡張を想定する。

概念的には以下のような構成とする。

```text
Tracking Person A
      ↓
Avatar Runtime A

Tracking Person B
      ↓
Avatar Runtime B
```

将来的には以下を扱える構成を想定する。

- 複数Tracking Person
- 複数Avatar Runtime
- PersonとAvatarの割り当て
- 個別Spawn Point
- 個別Voice設定
- 個別Tracking Profile
- 個別Camera構成

### 採用理由

初期実装から複数人制御を完全実装すると複雑性が高くなる。

一方で、

- Global CurrentAvatar
- Global TrackingFrame

等、システム全体で1人しか存在できない構造を採用すると、後から複数人対応する際の変更範囲が大きくなる。

そのため、初期バージョンでは利用数を1つに制限しても、内部表現は複数Instanceを扱える構造を維持する。

---

## 16.3 新規Tracking Provider対応

初期実装ではMediaPipeを主なTracking方式として利用するが、将来的に異なるTracking Providerを追加可能とする。

対象例：

- mocopi
- その他モーションキャプチャ機器
- 別のCamera Tracking Library
- VR機器
- Network経由Tracking
- Smartphone Sensor

追加Providerは、8章で定義したTracking Provider境界を介して共通Tracking Dataへ変換する。

```text
New Tracking Device
        ↓
New Tracking Provider
        ↓
TrackingFrame
        ↓
Existing Avatar Pipeline
```

### 採用理由

Avatar Moduleを新しいTracking方式ごとに変更するのではなく、Provider追加だけで既存Pipelineを再利用できるようにするためである。

---

## 16.4 複数Tracking Sourceの統合

将来的には、複数のTracking Sourceを組み合わせる構成を可能とする。

例：

```text
Camera
  ↓
Face / Upper Body

mocopi
  ↓
Body

Hand Tracking Device
  ↓
Fingers
```

これらを統合し、1つのTracking Sessionとして扱う方式への拡張を可能とする。

複数Source間では、

- 時刻同期
- 座標系統一
- 信頼度
- 優先順位
- 欠損補完

等を扱う。

### 採用理由

単一Providerが身体、顔、手指のすべてを高精度に取得できるとは限らない。

複数入力を組み合わせることで、それぞれの長所を利用可能とする。

---

## 16.5 新規3Dモデル形式対応

初期対応形式はVRM 1.0以降（VRM 0.xは1.0へ変換して受け付ける）およびFBXとする。

将来的に別の3D Avatar形式を追加する場合は、7章で定義したAvatar Loaderを追加することで対応する。

概念的には以下とする。

```text
IAvatarLoader
├─ VrmAvatarLoader
├─ FbxAvatarLoader
└─ FutureAvatarLoader
```

新しいLoaderは、モデル形式固有情報をAvatar RuntimeおよびAvatar Profileから利用可能な共通形式へ変換する。

### 採用理由

Pose、Expression、Tracking等の既存機能へモデル形式固有処理を追加しないことで、新形式追加時の変更範囲を限定する。

---

## 16.6 拡張ボーン制御方式の追加

初期機能で扱う拡張ボーンについて、将来的により多様な制御入力および動作方式を追加可能とする。

追加可能な入力例：

- Voice音量
- Pitch
- BGM
- SE
- Keyboard Shortcut
- 外部デバイス
- Streaming Event
- Network Event
- 任意Parameter

追加可能な動作方式例：

- Animation
- Physics
- Procedural Motion
- Timeline
- Script制御
- Event連動

### 採用理由

拡張ボーンは、標準Humanoid Boneと比べて用途の自由度が高い。

特定の制御方法だけに固定せず、将来的に新しいInput SignalやMotion方式を追加可能とする。

---

## 16.7 新規Voice Conversion方式対応

初期RuntimeではRVCを利用するが、将来的に別のリアルタイムVoice Conversion Engineを追加可能とする。

9章で定義したVoice Converter境界を利用する。

```text
IVoiceConverter
├─ RvcVoiceConverter
├─ PassThroughVoiceConverter
└─ FutureVoiceConverter
```

新しいVoice Conversion方式を追加しても、

- Audio Input
- Post Processing
- Audio Mixing
- Streaming

等の既存Pipelineを可能な限り再利用する。

### 採用理由

Voice Conversion技術は今後も変化する可能性が高い。

RVC自体をシステムの中心概念とせず、現在利用しているVoice Conversion Engineの一つとして扱う。

---

## 16.8 Voice Labの拡張

Voice Labについても、RVCおよびVoxCPM2専用の固定UIとしない。

将来的に以下を追加可能な構成とする。

- 新しいVoice Generation Engine
- 新しいVoice Clone方式
- 新しいTraining方式
- 新しい音声評価指標
- Dataset自動評価
- Model比較
- 自動Parameter Search
- Hyperparameter Search
- 学習条件推薦
- 過去Training Runとの比較

基本Workflowである、

```text
Generate
   ↓
Clone / Select
   ↓
Dataset
   ↓
Train
   ↓
Evaluate
   ↓
Register
```

を維持しつつ、各Stepの内部実装を拡張可能とする。

### 採用理由

音声生成・学習技術が変わっても、Voice Model作成という利用者の目的そのものは大きく変わらないためである。

---

## 16.9 新規配信サービス対応

初期対象はYouTube Liveとするが、Streaming Publisherを抽象化し、将来的に他の配信先を追加可能とする。

概念的には以下とする。

```text
IStreamPublisher
├─ YouTubePublisher
├─ FuturePublisherA
└─ FuturePublisherB
```

Streaming Pipelineの、

- Rendering
- Encoding
- A/V Synchronization
- Recording

は可能な限り配信サービスから独立させる。

### 採用理由

配信サービス追加のためにVideo Pipeline全体を作り直すことを避ける。

---

## 16.10 複数配信先への同時出力

将来的に、1つの配信映像を複数のPublisherへ送信する構成を検討可能とする。

概念的には以下とする。

```text
Encoded Stream
      ↓
Stream Distribution
   ├─ Publisher A
   └─ Publisher B
```

必要に応じて、

- 共通Encode
- 配信先別Encode
- 配信先別Bitrate
- 配信先別解像度

等を選択できる構成を検討する。

### 採用理由

配信サービスごとに要求仕様が異なる可能性があるため、単純な複製だけでなく配信先ごとの調整余地を残す。

---

## 16.11 Codec・Encoder拡張

映像・音声Encoderについて、将来的に新しいCodecやHardware Backendを追加可能とする。

対象例：

- H.264
- H.265
- AV1
- その他将来Codec

Hardware Encoderについても、特定GPUベンダーへPipeline全体を依存させず、Encoder実装内部へ閉じ込める。

### 採用理由

CodecおよびHardware Encoderは今後変化する可能性がある。

そのため、Streaming Pipelineの他部分を変更せずEncoderだけを追加・交換できる構成とする。

---

## 16.12 Stage機能の拡張

Stageについて、将来的に以下の機能追加を想定する。

- ユーザー作成Stage
- Stage Package
- 外部配布Stage
- Stage Animation
- Interactive Object
- Stage固有Effect
- Weather
- Time of Day
- Dynamic Lighting
- Event Trigger

ただし、StageからApplication内部を自由に操作できる構造にはしない。

Stageからシステムへ要求する操作については、公開されたInterface、EventまたはCommandを介する。

### 採用理由

外部作成Stageを扱う場合、Stage Scriptとシステム本体の境界が重要となる。

Stage追加によってTrackingやVoice等の内部状態が直接変更されない構造を維持する。

---

## 16.13 外部入力デバイス対応

将来的にKeyboard以外の操作デバイスを利用可能とする。

対象例：

- Stream Deck系デバイス
- MIDI Controller
- Game Controller
- Smartphone
- Network Controller
- その他Shortcut Device

14章で定義したCommand境界を利用する。

```text
UI
Keyboard
External Device
Network
      ↓
Command
      ↓
Runtime
```

### 採用理由

入力方法ごとにRuntime処理を再実装することを避ける。

---

## 16.14 UI Locale拡張

初期UI言語は、

- 日本語
- English

とする。

将来的な言語追加については、14章で定義した文字列表をLocaleごとに追加して対応する。

新規言語追加時にRuntime処理や画面ロジックを変更しない構成とする。

必要に応じてLocaleごとに、

- Font Asset
- Theme Style Sheet
- Text Direction
- Layout

等を変更可能とする。

### 採用理由

多言語機能を日本語・英語専用実装とせず、Localization Resource追加によって対応言語を増やせるようにする。

---

## 16.15 UI Theme拡張

UI ToolkitのTheme Style SheetおよびDesign Systemを利用し、将来的にUI Themeを追加可能とする。

対象例：

- Dark Theme
- Light Theme
- High Contrast Theme
- Custom Theme

初期実装で多数のThemeを実装することは必須としない。

### 採用理由

UI Componentが共通Design Systemを利用していれば、各画面を個別変更せずにVisual Designを変更できるためである。

---

## 16.16 Plugin機構

将来的に、本システムの一部機能をPluginとして追加可能な構成を検討する。

Plugin化候補として以下を想定する。

- Tracking Provider
- Voice Converter
- Stream Publisher
- External Device
- Audio Processor
- Stage機能
- Developer Tool

概念的には以下とする。

```text
Application Core
       ↓
Plugin API
   ├─ Tracking Plugin
   ├─ Voice Plugin
   ├─ Streaming Plugin
   └─ Device Plugin
```

初期実装では完全なPlugin SDKを必須としない。

まずは内部Module間の境界を安定させ、必要になった段階で外部Plugin APIとして公開する。

### 採用理由

初期段階から汎用Plugin Systemを構築すると設計・互換性管理コストが大きい。

内部拡張点を先に安定させ、その後必要な範囲だけを外部公開する方が安全である。

---

## 16.17 PluginのVersion互換性

将来的にPlugin機構を導入する場合は、Plugin API Versionを明示する。

概念例：

```text
Plugin
├─ PluginId
├─ PluginVersion
├─ RequiredApiVersion
└─ Capabilities
```

互換性のないPluginをそのまま読み込まない構成とする。

### 採用理由

本体更新によってPlugin APIが変更された場合、旧PluginがApplication内部で予期しない動作をすることを防ぐ。

---

## 16.18 外部連携API

将来的に、外部アプリケーションから本システムを操作できるAPIの提供を検討する。

対象例：

- Avatar切り替え
- Stage切り替え
- Camera切り替え
- SE再生
- BGM操作
- Expression操作
- Runtime状態取得

外部APIからの操作も、14章で定義したCommand等の既存Runtime操作経路を利用する。

```text
External Application
        ↓
External API
        ↓
Command
        ↓
Runtime
```

### 採用理由

外部API専用の操作処理を作らず、既存のRuntime操作経路を再利用することで挙動を統一する。

---

## 16.19 外部APIの安全性

外部連携APIを提供する場合、無条件に外部Networkへ公開しない。

初期状態ではLocalhostのみとし、外部Networkからのアクセスを許可する場合には明示的な設定および必要な認証方式を用いる。

### 採用理由

便利な外部操作APIは同時にAttack Surfaceにもなるためである。

15章のSecurity方針に従って公開範囲を制御する。

---

## 16.20 Automation拡張

将来的に複数の操作を組み合わせたAutomationを実行可能な構成を検討する。

例：

```text
Scene Change Command
   ├─ Stage切り替え
   ├─ Camera切り替え
   ├─ BGM切り替え
   └─ Effect再生
```

また、

- 配信開始時
- 配信終了時
- Stage変更時
- 特定Shortcut
- External Event

等をTriggerとして利用可能とする。

### 採用理由

個々の機能がCommandとして統一されていれば、複数Commandを組み合わせた演出や自動化へ発展させやすい。

---

## 16.21 Project機能の拡張

6章で定義したProjectについて、将来的に以下を追加可能とする。

- Project Template
- Project複製
- Project共有
- Project Package
- Read-only Project
- Project差分
- Profile共有

### 採用理由

設定やAssetが増加すると、新しいProjectを一から構築する負担が大きくなる。

既存Projectを再利用できる仕組みを追加可能とする。

---

## 16.22 Asset配布・共有

将来的に以下のAssetを他ユーザーと共有可能な構成を検討する。

- Stage
- Profile
- UI Theme
- Extension Motion設定
- Audio設定
- その他Preset

共有データには、

- Version
- Author
- Compatibility
- Dependency

等のMetadataを持たせる。

秘密情報や個人データは共有Packageへ含めない。

### 採用理由

OSSとして利用者や開発者が設定やAssetを共有できるようになることで、システムの拡張性を高められる。

---

## 16.23 Extension Motion共有

将来的に、拡張ボーンに対するMotion設定をPresetとして保存・共有可能とする。

例えば、

- 耳の動き
- 尻尾の動き
- 翼の動き
- アホ毛
- 装飾

等について、モデルごとに調整されたMotion Profileを保存できる構成を想定する。

### 採用理由

拡張ボーンの動きは利用者ごとの好みやモデル構造によって異なる。

動作設定をモデルデータそのものから分離することで、調整・再利用・共有を容易にする。

---

## 16.24 Developer Tool拡張

Developer / Diagnosticsについて、将来的に以下を追加可能とする。

- Performance Profiler
- Tracking Visualizer
- Audio Analyzer
- Stream Statistics
- Runtime Object Inspector
- Extension Motion Debugger
- Plugin Inspector

### 採用理由

機能増加に伴い、内部状態の確認手段も拡張する必要がある。

Developer Toolも通常Runtimeから分離された拡張可能な機能として扱う。

---

## 16.25 AIを利用したSetup支援

将来的に、設定作業の一部を自動化または補助する機能を検討可能とする。

対象例：

- Avatar Bone Mapping候補
- Extension Bone分類
- Tracking調整値推薦
- Voice Model比較支援
- Training Parameter推薦
- UI設定支援

ただし、自動結果を無条件に適用せず、利用者が結果を確認・修正可能とする。

### 採用理由

設定項目が増えるとSetup作業の負担も増加する。

自動化によって負担を減らしつつ、最終的な判断を利用者が行える構成を維持する。

---

## 16.26 Backward Compatibility

将来機能追加時も、可能な範囲で既存の、

- Project
- Profile
- Avatar
- Voice Model
- Stage
- Training History

を継続利用可能とする。

6章で定義したSchema VersionおよびMigration方式を使用する。

### 採用理由

機能追加によって既存ユーザーデータが利用不能になることを避ける。

---

## 16.27 Deprecation方式

将来的に古い機能やInterfaceを廃止する必要が生じた場合、即座に削除せず、一定期間Deprecatedとして扱うことを基本とする。

必要に応じて、

- Deprecated警告
- Migration手段
- 代替機能
- 削除予定Version

を明示する。

### 採用理由

OSSとして外部Pluginやユーザーデータが存在する可能性があり、突然の削除は影響が大きいためである。

---

## 16.28 拡張機能とCoreの分離

将来機能を追加する場合でも、Application Coreへ機能を集中させない。

概念的には以下を維持する。

```text
Application Core
      ↓
Stable Interfaces
      ↓
Feature Modules
      ↓
Adapters / Plugins / External Services
```

### 採用理由

機能追加のたびにCoreへ依存関係を増やすと、最終的にすべてのModuleが相互依存する構造になる。

Coreは可能な限り安定した共通機能へ限定する。

---

## 16.29 将来機能の採用判断

将来機能について、単に実装可能であることだけを理由に追加しない。

追加時には少なくとも以下を確認する。

- 利用価値
- 実装・保守コスト
- Runtime負荷
- UI複雑性
- Security
- Privacy
- 外部依存
- 既存設計への影響

### 採用理由

機能数が増えること自体を目的とせず、統合環境としての使いやすさと保守性を維持するためである。

---


## 16.30 Capture Source・Capture Backend拡張

初期実装では、Capture Boardを利用するGameCaptureおよびDXGI Desktop Duplicationを利用するSubScreenCaptureを提供する。

将来的には、同じCapture Source境界を利用して以下のような映像入力方式を追加可能とする。

- Window Capture
- 別方式のDesktop Capture
- Spout Input
- Network Video Input
- その他Video Device / Capture Backend

新しいCapture方式を追加する場合も、上位のStageやVideo PipelineへBackend固有APIを露出させない。

```text
New Capture Backend
        ↓
Capture Adapter
        ↓
IVideoCaptureSource / IAudioCaptureSource
        ↓
Existing Stage / Audio / Video Pipeline
```

### 採用理由

Capture技術はOS、GPU API、Device等によって実装方式が異なる。

Capture Source境界を維持することで、入力技術を追加・変更しても既存のStage表示、Audio Mixing、Streaming Pipelineを再利用できるようにする。

---

## 16.31 今後の拡張における設計原則

今後の機能追加では、以下を基本原則とする。

1. 将来機能を初期段階から過剰実装しない。
2. ただし、将来変更が想定される箇所には明確な拡張境界を設ける。
3. 初期実装が1人利用でも、内部構造を1人専用に固定しない。
4. Tracking方式はProviderとして追加する。
5. 複数Tracking Sourceを将来的に統合可能とする。
6. 3Dモデル形式はLoaderとして追加する。
7. 拡張ボーン制御は新しいInput SignalやMotion方式を追加可能とする。
8. Voice Conversion Engineは交換可能な構造を維持する。
9. Voice Labは特定のAIモデルだけに固定しない。
10. Streaming ServiceはPublisherとして追加する。
11. Encoder / Codecを交換可能とする。
12. StageからApplication内部へ直接依存させない。
13. 新しい操作入力はCommand境界を利用する。
14. 新しいLocaleはLocalization Resourceとして追加する。
15. UI ThemeはDesign Systemを介して追加する。
16. Plugin機構は内部拡張点が安定してから段階的に公開する。
17. 外部APIは既存Command経路を再利用する。
18. 外部APIを不用意にNetworkへ公開しない。
19. ProjectおよびAssetの再利用・共有を可能にする方向で拡張する。
20. 既存データとのBackward Compatibilityを可能な範囲で維持する。
21. 旧機能廃止時にはMigrationおよびDeprecation期間を設ける。
22. Coreを巨大化させず、追加機能は独立Moduleとして実装する。
23. 新機能追加時には性能・Security・Privacy・UI複雑性も評価する。
24. 最終的な機能採用は実際の利用価値を基準として判断する。
25. 新しい映像入力方式はCapture Source / Adapterとして追加する。
