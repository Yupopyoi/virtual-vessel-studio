# External Services詳細設計

## 1. 目的

Unityの外で動く処理の起動、監視、停止を、1か所で管理する（方式設計4章）。各機能が勝手にProcessを起動しないようにする。

最初の利用者はVoice Labである。

この設計に含めないもの：環境の取得と更新（[Voice Lab詳細設計](voice-lab.md)、方式設計4.19）。

## 2. 責務

| 責務 | 担当 |
|---|---|
| 1回で終わるProcessの実行 | ExternalServices（`ExternalProcessRunner`） |
| 常駐するServiceの起動、確認、停止 | ExternalServices（`ExternalServiceManager`） |
| 自分が起動したProcessの把握 | ExternalServices（Process Ownership） |
| 何を起動するかの指定 | 各機能のAdapter |
| 環境の場所の解決 | VoiceLab |

## 3. Moduleの境界

- `VirtualVessel.ExternalServices`はPython、RVC、VoxCPM2を知らない。実行ファイル、引数、環境変数、作業フォルダーだけを扱う。
- 配信の経路は参照しない。Voiceはこのモジュールを参照しない（方式設計4.3）。
- UIへはPID、Port、パスを出さない（方式設計4.27）。Developer Modeの診断だけに出す。

## 4. 主なClassとInterface

| Class | 種類 | 役割 |
|---|---|---|
| `IExternalProcessRunner` | Interface | 1回で終わるProcessを実行する |
| `ExternalProcessRunner` | Class | 実装。Job ObjectでProcessの木を管理する |
| `ExternalProcessRequest` | Class | 実行ファイル、引数、環境、秘密の環境変数の名前、作業フォルダー、ログの場所、Timeout、取り消しの猶予 |
| `ExternalProcessResult` | Class | 終わり方（`Completed`、`Failed`、`Cancelled`）、Exit Code、時間切れか、始まらなかった理由、開始と終了の時刻、標準エラーの最後の行 |
| `ProcessRules` | 静的Class | 副作用のない判定：引数の引用、許可された場所か、終わり方の分類、ログに出す環境 |
| `JobObject` | Class（内部） | Job ObjectのP/Invoke |
| `IExternalServiceManager` | Interface | 常駐Serviceの管理（第2段階） |
| `ServiceDescriptor` | Class | 方式設計4.6の項目 |
| `ServiceIdentityChecker` | Class | Portの相手が本物かを確かめる（第2段階） |

## 5. 公開API

```csharp
public interface IExternalProcessRunner
{
    /// 出力を1行ずつ渡す。取り消すとProcessの木を止める。
    Task<ExternalProcessResult> RunAsync(
        ExternalProcessRequest request,
        Action<string> onOutputLine,
        CancellationToken cancellationToken);
}

public interface IExternalServiceManager
{
    Task<ServiceHandle> AcquireAsync(string serviceId, CancellationToken cancellationToken);
    IReadOnlyObservable<ServiceState> StateOf(string serviceId);
}
```

## 6. Data Model

`ServiceDescriptor`はコードに持つ。Voice Labの構成はアプリの版に固定なので、ファイルにしない。

進捗はProcessの標準出力にJSON Linesで流す。形式はAdapterごとに決め、ExternalServicesは中身を見ない。

```json
{"type":"progress","stage":"training","epoch":10,"total":150}
```

## 7. 状態とライフサイクル

Processは`Running`、`Completed`、`Failed`、`Cancelled`のいずれかである。

常駐Serviceは方式設計4.7の状態を使う。必要なときに起動し、一定時間使わなければ止める（方式設計4.15、4.16）。

## 8. 主な流れ

### 8.1 Processの実行

```text
要求の検証（実行ファイルは許可された場所の中か）
  ↓
Job Objectを作る（アプリが落ちたら子も止まる）
  ↓
起動し、Jobへ入れる
  ↓
標準出力と標準エラーを読み、ログへ書き、1行ずつ渡す
  ↓
終了またはCancellation
  ↓
結果を返す
```

- 起動は`System.Diagnostics.Process`で行い、起動の直後にJobへ入れる。UnityのMonoからもP/Invokeで使えることを確かめた（2026-11-04、14章のテスト）。
- Jobは実行ごとに作り、最後のハンドルを閉じたら中のProcessをすべて止める設定（`JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE`）にする。アプリが落ちても、OSがハンドルを閉じて子が止まる。
- 起動からJobへ入れるまでの間に作られた子はJobに入らない。Pythonは読み込みに時間がかかり、すぐには子を作らないので、実際には起きない。起動を一時停止の状態で行う方法（`CREATE_SUSPENDED`）は、必要になるまで使わない。
- 本体が終わったあと、Jobに残った子を止めてから、出力の読み取りの終わりを待つ。残った子が出力のパイプを握ったままだと、読み取りが終わらないためである。残った子も自分が起動したProcessの木の一部である。
- 引数は、Windowsの規則（`CommandLineToArgvW`、Cのランタイム、Python）で元どおりに読めるように引用する。
- 始まらなかったとき（許可された場所の外、実行ファイルやフォルダーが無い、起動の失敗）も、例外ではなく`Failed`の結果を返す。理由は`Failure`に入れる。

### 8.2 取り消し

まず標準入力を閉じて、要求の猶予（`CancelGrace`、既定5秒）だけ終了を待つ。終わらなければJobごと止める。止めるのは自分が起動したProcessだけである（方式設計4.13）。止めたときのExit Codeは`null`とする。時間切れも同じ手順で止め、`Failed`かつ`TimedOut`とする。

### 8.3 常駐Service（第2段階）

方式設計4.8のとおり。起動時に使い捨てのトークンを渡し、Health Checkでトークンと版を確かめる。Portが開いているだけでは信用しない（CLAUDE.md 26章）。

## 9. Threadと非同期

Processの出力は専用のThreadで読み、`onOutputLine`は同じThreadから呼ぶ。UIへ渡すのは呼び出し側の責任である。メインスレッドでは待たない。

## 10. 設定

利用者の設定はない。Timeoutは要求ごとに指定する。

## 11. 保存

ログは要求で指定された場所へ書く。自身は状態を保存しない。

Job Objectを使うので、アプリの異常終了のあとに残ったProcessを探す処理は持たない（方式設計4.33）。

## 12. エラーと回復

| 起きたこと | 結果 |
|---|---|
| 実行ファイルが無い | `Failed`（開始前） |
| 許可された場所の外 | 拒否する |
| Timeout | Jobを止めて`Failed` |
| Exit Codeが0以外 | `Failed`。自動で再実行はしない |

意味のあるメッセージにするのはAdapterの責任である。

## 13. ログと診断

- 開始、終了、Exit Code、所要時間を記録する。
- Processの出力は、本体のログではなく、要求で指定されたファイルへ書く。
- 環境変数にトークンなどの秘密を含むときは、ログに出さない（CLAUDE.md 20章）。
- 診断画面に、実行中のProcessと最後の結果を表示する。

## 14. Testの方針

| 種類 | 内容 |
|---|---|
| EditMode | 要求の検証、引数の引用、許可された場所、終わり方の分類、秘密を隠すこと（`ProcessRulesTests`） |
| 結合（EditMode） | Windowsの`cmd.exe`とPowerShellで、出力の読み取り、Exit Code、環境変数と秘密、取り消しで子Processごと止まること、時間切れ、残った子が実行を終わらせないこと（`ExternalProcessRunnerTests`） |
| 手動 | アプリを強制終了して、子Processが残らないこと |

## 15. 性能

配信中は動かない。Process管理の負荷はほとんどない。

## 16. Folder / Namespace

```text
Assets/VirtualVessel/ExternalServices/Runtime/
├─ Process/    IExternalProcessRunner、ExternalProcessRunner、ExternalProcessRequest、ExternalProcessResult、ProcessRules、JobObject
├─ Services/   IExternalServiceManager、ServiceDescriptor
└─ Identity/   ServiceIdentityChecker
```

## 17. 段階と未解決

段階：

1. 1回で終わるProcess（アクティベーションとRVCの学習に必要）
2. 常駐Service（VoxCPM2に必要）

未解決：

- VoxCPM2を常駐Serviceにするか、1回のProcessにするか。モデルの読み込みに時間がかかるので、候補を続けて作るときは常駐が向く。方式設計13.18は常駐である。
- アプリを強制終了したときに子Processが残らないことは、手動ではまだ確かめていない（Jobの設定と、Unityの中での取り消しのテストまで）。
