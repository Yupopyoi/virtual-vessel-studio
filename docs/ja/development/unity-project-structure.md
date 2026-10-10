# Unity Project構成規約

## 1. 目的

本書は、`unity/VirtualVesselStudio/`配下のUnity Projectにおける、

- Directory構成
- Assembly Definition
- Namespace
- Module間参照
- テスト配置

の規約を定義する。

方式設計2.4で定義したModule分割を、Unity Project上で機械的に守れる構造とすることを目的とする。

すべてのModuleは本規約に従って追加する。

---

## 2. Directory構成

Project-ownedなコードおよびAssetは`Assets/VirtualVessel/`配下へ配置する。

```text
Assets/
├─ VirtualVessel/
│  ├─ Core/
│  ├─ Application/
│  ├─ ProjectData/
│  ├─ Diagnostics/
│  ├─ UI/
│  ├─ Avatar/
│  ├─ Tracking/
│  ├─ Voice/
│  ├─ Audio/
│  ├─ Capture/
│  ├─ Stage/
│  ├─ Video/
│  ├─ Streaming/
│  ├─ VoiceLab/
│  └─ ExternalServices/
│
├─ ThirdParty/
│  └─ <Name>/
│
├─ Scenes/
│  └─ Persistent.unity
│
└─ Settings/
```

Moduleは実装が必要になった時点で追加する。空のModule Directoryを先に作らない。

外部AssetをUnity Package Manager以外の方法で取り込む必要がある場合は、`Assets/ThirdParty/<Name>/`へ改変せずに配置する。

### 採用理由

外部AssetやUnity Packageが`Assets/`直下へDirectoryを作成する場合がある。

Project-ownedなコードを`Assets/VirtualVessel/`へまとめることで、自作コードと外部コードを明確に区別する。

---

## 3. Module内部構成

各Moduleは以下の構成を基本とする。

```text
Assets/VirtualVessel/<Module>/
├─ Runtime/
│  ├─ VirtualVessel.<Module>.asmdef
│  └─ ...
│
├─ Editor/
│  ├─ VirtualVessel.<Module>.Editor.asmdef
│  └─ ...
│
└─ Tests/
   ├─ EditMode/
   │  ├─ VirtualVessel.<Module>.Tests.EditMode.asmdef
   │  └─ ...
   │
   └─ PlayMode/
      ├─ VirtualVessel.<Module>.Tests.PlayMode.asmdef
      └─ ...
```

`Editor/`および`Tests/`は必要な場合のみ作成する。

---

## 4. Assembly Definition

### 4.1 命名

| 種類 | Assembly名 |
|---|---|
| Runtime | `VirtualVessel.<Module>` |
| Editor | `VirtualVessel.<Module>.Editor` |
| EditMode Test | `VirtualVessel.<Module>.Tests.EditMode` |
| PlayMode Test | `VirtualVessel.<Module>.Tests.PlayMode` |

### 4.2 参照方向

Assembly間の参照は以下の方向のみ許可する。

```text
VirtualVessel.Application
        ↓
Feature Modules（Avatar, Tracking, Voice, ...）
        ↓
VirtualVessel.Diagnostics
        ↓
VirtualVessel.Core
```

- `VirtualVessel.Application`はComposition Rootとして各Moduleを参照してよい。
- Feature ModuleはApplicationを参照しない。
- Feature Module同士の参照は、方式設計で定義されたデータの流れ（例：Avatar → Tracking）に限定し、相手Moduleの公開Contractのみを利用する。
- 循環参照は禁止する。Assembly Definitionにより機械的に検出される。
- `VirtualVessel.Core`は他のProject Assemblyを参照しない。

### 4.3 Core Assemblyの範囲

`VirtualVessel.Core`には、特定Moduleに属さない基本要素のみを配置する。

対象例：

- 時刻（Clock）
- Application Service Lifecycle Contract
- Main Thread Dispatch Contract
- 共通のResult / Error表現

特定Moduleの機能やData Contractを「共有したいから」という理由でCoreへ移動しない。

Coreが肥大化した場合は、Global Managerと同様の問題とみなして見直す。

`VirtualVessel.Core`は可能な限り`noEngineReferences`とし、UnityEngineへ依存しない。

### 4.4 公開範囲

- Module外へ公開するInterface、Data Contract、Command、EventのみをPublicとする。
- 実装ClassはInternalとする。
- テストからInternalへアクセスする場合は`InternalsVisibleTo`を使用する。

### 採用理由

Assembly DefinitionとC#のアクセス修飾子によって、方式設計2.4の「他Moduleの内部Classへ自由に依存しない」を機械的に保証する。

---

## 5. Namespace

Namespaceは`VirtualVessel.<Module>`を基本とし、Assembly名と一致させる。

Module内の下位分類は`VirtualVessel.<Module>.<Area>`とする。

例：

```text
VirtualVessel.Application
VirtualVessel.ProjectData
VirtualVessel.Diagnostics.Logging
```

Namespace名は方式設計2.4のModule名と対応させる。`Project / Data`は`ProjectData`とする。

---

## 6. テスト配置

- 特定Moduleに属するUnity TestはそのModuleの`Tests/`へ配置する。
- 特定Moduleに属さないテストは、Repository直下の`tests/`へ配置する（方式設計3.3、15.20）。
- テストはUnity Test Framework（NUnit）を使用する。

---

## 7. Static状態

- Static Fieldによる状態保持は原則として避ける。
- 必要な場合は、Unity EditorのEnter Play Mode設定でDomain Reloadを無効化した場合でも正しく初期化されるよう、`[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]`等で明示的にResetする。

### 採用理由

Domain Reloadを無効化すると、Static状態が前回のPlay Sessionから残り、再現困難な不具合の原因となる。

---

## 8. C#言語機能

- Unity 6.6が対応するC#およびAPI Compatibility Level（.NET Standard 2.1）の範囲で記述する。
- C#コーディング規約は`CLAUDE.md`および`.editorconfig`に従う。
