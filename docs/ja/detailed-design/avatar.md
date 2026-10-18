# Avatar 詳細設計

## 1. 目的

本書は、Avatar Moduleの詳細設計を定義する。Avatar Moduleは、3Dモデルを読み込み、Trackingの観測（`TrackingFrame`）をアバターの姿勢と表情へ適用する（方式設計7章）。

本書で作る範囲は以下とする（v0.1.0）。

- VRMの読み込み（UniVRM）と、アバターの抽象（Avatar Runtime、Skeleton、Expression）
- アバターの登録：Data Rootへのコピー、Avatar Profile、サムネイル、モデルのライセンス情報の表示
- 使うアバターの選択と、起動時の読み込み、配信画面への配置
- 姿勢の適用（上半身、手指）と表情の適用（目、口、視線、眉）
- 見失ったときの扱い、鏡写し、観測の間の補間

次は本書の対象外とし、後続の設計で扱う。

- FBXの読み込み（方式設計7.3。Libraryの選択から）
- 拡張ボーンとExtension Motion（猫耳、尻尾等。方式設計7.8）。Expression Signalは出しておく（7.4）
- 全身（`BodyRange.FullBody`）の適用：腰の移動、脚、足の接地
- 複数アバターの同時表示（方式設計7.9。内部は1体専用にしない）
- 配信中のアバターの切り替えの演出（フェード等）
- Stage SceneのAvatar Spawn Point（方式設計11.6）。それまでは既定の位置を使う（6.5）

関連する方式設計：7章 3Dアバター制御方式、8章 トラッキング方式、6.9〜6.11 Data Root、11.6 Avatar Spawn Point、15.3 リアルタイム性

関連する詳細設計：Tracking詳細設計（`tracking.md`）、Video詳細設計（`video.md` 7.2 アバターの層）、Stage詳細設計（`stage.md`）

参照した試作（Repository外）：`E:\VirtualVessel\benchmarks\Voom`。VRM 1.0の読み込み（UniVRM 0.129.1の`Vrm10`）と、顔の動きの表情への変換の調整の知見を参考にする。体の適用（胸の傾き1か所、オイラー角の平均）は引き継がない（Tracking詳細設計1章）。

### 全体像

```mermaid
flowchart LR
    File[".vrm（利用者のファイル）"] --> Import["AvatarImporter<br/>確かめてData Rootへコピー"]
    Import --> Store["DataRoot/Avatars/&lt;AvatarId&gt;/"]
    Store --> Loader["VrmAvatarLoader（UniVRM）"]

    subgraph Avatar["Avatar Module"]
        Loader --> Runtime["AvatarRuntime<br/>Skeleton / Expressions"]
        Runtime --> Ctrl["AvatarController"]
        Ctrl --> Pose["PoseController<br/>（Retargeting）"]
        Ctrl --> Expr["ExpressionController"]
    end

    Tracking["Tracking：ITrackingSource.Current"] --> Ctrl
    Expr --> Signal["Expression Signal<br/>（Extension Motionは後続）"]
    Runtime --> Layer["Video：アバターの層"]
```

---

## 2. 責務

| 責務 | 担当 |
|---|---|
| モデルファイルを確かめ、Data Rootへコピーし、登録する | Avatar（`AvatarImporter`） |
| ファイルを選ぶ画面（OSのダイアログ） | UI（`IFileDialog`、6.7） |
| モデル形式の読み込みと、形式に依存しない表現への変換 | Avatar（形式ごとのLoader） |
| 観測した姿勢をアバターの骨格へ合わせる（体格差、骨の向き） | Avatar（`PoseController`） |
| 顔の動きをアバターの表情へ変換する | Avatar（`ExpressionController`） |
| 鏡写し、観測の間の補間、見失ったときの扱い | Avatar |
| 観測、平滑化、入力の補正 | Tracking |
| アバターを置く位置（Spawn Point） | Stage（v0.1.0は既定の位置、6.5） |
| アバターを描くカメラと層 | Video |
| アバターの選択と設定の画面 | UI（セットアップ画面） |

---

## 3. Module境界

- Avatarは、Core、Diagnostics、ProjectData、Trackingの公開Contract（`ITrackingSource`、`TrackingFrame`）、Videoの公開Contract（アバターの層）を参照する。Capture、UIは参照しない。
- UniVRMの型（`UniVRM10.*`、`UniGLTF.*`）は、VRMのLoader（別のAssembly、16章）の中だけで使う。Avatarの他の部分は、`IAvatarRig`、`IAvatarExpressions`（5.2）だけを使う（方式設計7.2）。
- 他のModuleは`IAvatarService`（5.1）だけを使う。TransformやBlendShapeを直接操作しない（方式設計7.2 Avatar Controller）。
- Trackingの`MediaPipe`の型には依存しない。入力は`TrackingFrame`だけである。

---

## 4. 主要ClassとInterface

| 名前 | 種別 | 概要 |
|---|---|---|
| `IAvatarService` | Interface | 登録済みのアバターの一覧、登録、選択、現在のアバターの状態（5.1） |
| `AvatarService` | Class（`IApplicationService`） | 上の実装。読み込み、配置、毎Frameの適用の呼び出し |
| `AvatarEntry` | Class | 登録済みのアバター：Id、表示名、形式、サムネイル、ライセンス情報 |
| `AvatarImporter` | Class | ファイルを確かめ、Data Rootへコピーし、Profileを作る（6.2） |
| `IAvatarLoader` | Interface | 形式ごとの読み込み（方式設計7.3） |
| `VrmAvatarLoader` | Class（internal） | UniVRMによるVRMの読み込み |
| `AvatarRuntime` | Class | 読み込んだ1体：Rig、表情、Profile、状態（方式設計7.2） |
| `IAvatarRig` | Interface | 形式に依存しない骨格。標準の骨（`AvatarBone`）の回転を、基準の姿勢を単位とした空間で設定する（7.1） |
| `IAvatarExpressions` | Interface | 形式に依存しない表情。標準の表情（`AvatarExpression`）の重みと視線を設定する |
| `AvatarBone` | Enum | 標準の人体の骨（Unity Humanoidと同じ並び） |
| `AvatarExpression` | Enum | 標準の表情：`Blink`、`BlinkLeft`、`BlinkRight`、`Aa`、`Ih`、`Ou`、`Ee`、`Oh`、`Happy`、`Angry`、`Sad`、`Relaxed`、`Surprised`、`Neutral` |
| `AvatarController` | Class | 1体のアバターの制御の窓口。姿勢と表情へ振り分ける |
| `PoseRetargeting` | Static Class | 観測から骨の回転を求める（純粋関数、7章） |
| `PoseController` | Class | 補間、見失ったときの扱い、範囲（`BodyRange`）に応じた適用 |
| `ExpressionMapping` | Class | 表情の値（`FaceShape`）から表情への変換の設定（7.4） |
| `ExpressionController` | Class | 表情と視線の適用、Expression Signalの生成 |
| `AvatarProfile` | Class | 保存する利用の設定（6.3） |
| `AvatarSettings` | Class | 使うアバターと鏡写し等（10章） |

---

## 5. Public API

### 5.1 IAvatarService

```csharp
public interface IAvatarService
{
    IReadOnlyObservable<IReadOnlyList<AvatarEntry>> Avatars { get; }   // 登録済みの一覧
    IReadOnlyObservable<string> CurrentAvatarId { get; }                // 使うアバター。なければnull
    IReadOnlyObservable<AvatarState> State { get; }                     // None、Loading、Ready、Error
    IReadOnlyObservable<AvatarProblem> Problem { get; }                 // 利用者へ伝える問題（11章）

    Task<AvatarEntry> ImportAsync(string path, CancellationToken cancellationToken);  // 登録（6.2）
    Task SelectAsync(string avatarId, CancellationToken cancellationToken);           // 読み込んで差し替える
    Task RemoveAsync(string avatarId, CancellationToken cancellationToken);           // 登録を消す（確認はUI）
    void SetMirror(bool mirror);
}
```

- すべての操作と通知はMain Threadで行う。ファイルのコピーとHashの計算は別のThreadで行う。
- `SelectAsync`は、新しいアバターを読み込み終えてから、古いアバターと入れ替える。読み込みに失敗したら、古いアバターのままにする。

### 5.2 形式に依存しない骨格と表情

```csharp
public interface IAvatarRig
{
    bool Has(AvatarBone bone);
    float BoneLength(AvatarBone bone);                       // 子の骨までの長さ（メートル）
    void SetRotation(AvatarBone bone, Quaternion rotation);  // 基準の姿勢（7.1）からの回転。アバターのRoot空間
    void SetRootPosition(Vector3 position);                  // 全身のとき（後続）
}

public interface IAvatarExpressions
{
    bool Has(AvatarExpression expression);
    void SetWeight(AvatarExpression expression, float weight);   // 0〜1
    void SetLookAt(float yawDegrees, float pitchDegrees);
    IReadOnlyList<string> CustomNames { get; }                   // モデル独自の表情の名前（Perfect Sync等、後続）
}
```

- VRMでは、`IAvatarRig`をUniVRMのControl Rig（基準の姿勢でどの骨の回転も単位になる正規化した骨格）で実装する。モデルごとの骨の軸の違い（ある骨はX軸が前、別の骨はZ軸が前等）を、UniVRMが吸収する。FBXのLoaderを作るときは、同じ正規化をLoaderの中で行う。
- VRMでは、`IAvatarExpressions`をVRM 1.0のExpression（Preset）とLookAtで実装する。

---

## 6. Data Model

### 6.1 アバターの保存（方式設計7.11、6.10）

```text
DataRoot/Avatars/<AvatarId>/
├─ model.vrm               コピーしたモデル（元のファイルは変更しない）
├─ avatar-profile.json     利用の設定（6.3）
└─ thumbnail.png           サムネイル（VRMのメタ情報の画像。なければ読み込み時に撮る）
```

- `AvatarId`は登録時に作るGUID（`N`形式）。名前やファイル名を使わない（重複、使えない文字を避けるため）。
- 一覧は、起動時に`Avatars/`の下を読んで作る。`avatar-profile.json`が読めないものは一覧に「読み込めないアバター」として出し、消すかどうかは利用者が選ぶ（勝手に消さない、CLAUDE.md 25章）。

### 6.2 登録（AvatarImporter）

1. 利用者がファイルを選ぶ（UIの`IFileDialog`）。
2. 確かめる：拡張子`.vrm`、大きさの上限（既定1GB）、ファイルの先頭がglTFのBinary形式（`glTF`の印）であること。
3. 一時Folder（`Avatars/.importing/<AvatarId>/`）へコピーする（別のThread）。SHA-256を計算し、同じHashのアバターが登録済みなら、そのアバターを示して登録しない。
4. 一時Folderのコピーを、実際に読み込んで確かめる（`VrmAvatarLoader`、6.4）。標準の骨がそろっていること、メタ情報を取り出す。
5. `avatar-profile.json`とサムネイルを書き、一時Folderを`Avatars/<AvatarId>/`へ移す。途中で失敗したら一時Folderを消す。
6. 一覧に加える。最初の1体なら、使うアバターにする。

- 元のファイルのPathは、`avatar-profile.json`に参考として残すが、実行時には使わない（方式設計7.11）。

### 6.3 Avatar Profile

```json
{
  "schemaVersion": 1,
  "avatarId": "3f2c...",
  "displayName": "モデルの名前（メタ情報から。利用者が変えられる）",
  "format": "vrm",
  "formatVersion": "1.0",
  "sourceFileName": "original.vrm",
  "sha256": "...",
  "importedAt": "2026-10-20T12:00:00Z",
  "license": { "...": "VRMのメタ情報のライセンスの項目（6.4）" },
  "scale": 1.0,
  "expressionMapping": { "...": "既定から変えた項目だけ（7.4）" },
  "pose": { "armDownAngle": 70 },
  "extensions": {}
}
```

- 知らない項目は消さずに残す（CLAUDE.md 25章）。`extensions`は、Extension Motion等の後続の設定を入れる場所とする。
- 既定値と同じ項目は書かない。既定を良くしたとき、変えていない利用者にも届くようにするため。

### 6.4 モデルのライセンス（VRMのメタ情報）

VRMには、作者がアバターの使い方の条件を書く（アバターとして使ってよい人、商用利用、暴力・性的表現、改変、再配布等）。

- 登録時と、アバターの詳細で、作者、条件、ライセンスのURLを利用者に表示する。
- 条件を理由に登録や使用を止めることはしない（判断するのは利用者）。ただし、`avatarPermission`が作者のみ等の場合は、登録の前に注意を表示する。
- ライセンスのURLは、利用者が押したときだけ外部のブラウザで開く。

### 6.5 アバターの配置

- アバターは、アプリが持つPersistentな場所（Stage Sceneの外）に置く（方式設計7.10）。
- Stage SceneのSpawn Pointができるまでは、Stageが既定のSpawn Pointを1つ出す（`IAvatarSpawnPoints`、Stage詳細設計に追加する）：原点、カメラの方を向く。Main Cameraの既定の位置は、上半身が配信画面に収まるように合わせる（Video）。
- 描く層はUnityの層`Avatar`とし、VideoのAvatar Camera（Video詳細設計7.2、Main Cameraと同じ位置と画角、Overlay）が描く。平面の映像の前に出る（利用者の決定、2026-10-17）。

### 6.6 鏡写し

- 鏡写し（既定：オン）では、利用者が右手を上げると、アバターは左手を上げる。画面上では同じ側の手が上がり、鏡を見ているように動かせる。
- `TrackingFrame`は利用者自身の左右を持つ（Tracking詳細設計6.1）。鏡写しでは、左右を入れ替え、X軸を反転してから適用する（`PoseRetargeting`の入力の変換、純粋関数）。表情の左右（片目のまばたき等）も入れ替える。

### 6.7 ファイルを選ぶ画面

- UIに`IFileDialog`（`Task<string> OpenAsync(FileDialogOptions options)`）を加える。WindowsではCOMの`IFileOpenDialog`を専用のSTAのThreadで開く。Main Threadで開くと、ダイアログを開いている間、描画と配信の映像が止まるためである。
- 追加の依存（Libraryの導入）はしない。

---

## 7. 姿勢と表情の計算

### 7.1 基準の姿勢と骨の回転

- UniVRMのControl Rigでは、基準の姿勢（T Pose）で、どの骨の回転も単位（回転なし）になる。`PoseRetargeting`は、各骨について「基準の姿勢での骨の向き（例：右の上腕は+X）」から「観測した向き」への回転を求める。
- 骨の向きだけでは、骨を軸にしたねじれが決まらない。ねじれは、もう1つの向きで決める：

| 骨 | 向き | ねじれを決める向き |
|---|---|---|
| 胴（Spine、Chest） | 腰から首へ | 左右の肩を結ぶ線 |
| 首、頭 | 顔の変換行列の回転（Tracking詳細設計6.3）を、首と頭へ配分する（既定：首40%、頭60%） | 同じ |
| 上腕 | 肩から肘へ | 肘の曲がる面（上腕と前腕が作る面の法線） |
| 前腕 | 肘から手首へ | 手のひらの向き（Hand Landmarkerから。なければ上腕から引き継ぐ） |
| 手 | 手首から中指の付け根へ | 手のひらの法線 |
| 指 | 各関節の曲げの角度（7.5） | 手のひら |

- 回転だけを適用し、位置は適用しない。そのため、アバターと利用者の体格（腕の長さ等）が違っても、骨の向きは同じになる（方式設計7.6の体格差の吸収）。手を顔に当てる等、位置が合うことが必要な動きは、全身の対応と合わせてIK（関節の位置から回転を解く）を後で検討する（17章）。
- 計算は`PoseRetargeting`（純粋関数）で行う：入力は`TrackingFrame`の体と手、鏡写し、Profileの補正。出力は骨ごとの回転の配列（呼び出し元が渡す、毎Frameの確保を避けるため）。

### 7.2 上半身（BodyRange.UpperBody）

- 腰（Hips）は動かさない。胴は、肩の線の傾きと、首の前後の傾きから、Spine、Chest、Upper Chestへ配分する（既定：30%、35%、35%）。座っていても胴の傾きが出るようにするため。
- 胴の回転には上限を付ける（左右の傾き±30°、前後±25°、ひねり±40°）。推論の誤りで胴が不自然に曲がるのを防ぐ。
- 肩（Shoulder）は、腕を上げたときだけ少し上げる（上腕の上げ角の一部を肩へ配分する）。
- 腕を見失ったときは、腕を下ろした姿勢（`pose.armDownAngle`、既定70°下げる）へ戻す。T Poseへは戻さない。
- 下半身は、腕を下ろした基準の姿勢のまま（座っていても立っていても違和感の少ない、真っすぐ立った姿勢）。

### 7.3 観測の間の補間

- 描画のFrameごとに、直近2回の観測の間を、描画の時刻で補間する（回転は球面補間）。描画の時刻は「今 − 観測1回分の間隔」とし、常に2つの観測の間に入るようにする。遅れは観測1回分（30回/秒で約33ms）増えるが、推論の回数より描画の回数が多いことによる段差が消える。
- 次の観測が遅れたときは、最後の観測で止める（先の予測はしない。外れたときに戻る動きが目立つため）。

### 7.4 表情（ExpressionMapping）

表情の値（`FaceShape`、0〜1）から、アバターの表情（`AvatarExpression`）への変換。値ごとに応答の曲線（下限、上限、曲がり方）を持つ。既定は次のとおりとし、Avatar Profileで変えられる。

| 表情 | 元の値 | 調整（Voomの知見を含む） |
|---|---|---|
| `BlinkLeft` / `BlinkRight` | `EyeBlinkLeft` / `EyeBlinkRight` | 目の閉じやすさ（下限と上限）。左右をそろえる（既定：オン。両目の差が大きいときだけ片目のまばたきとする） |
| `Aa` | `JawOpen` | 口の開き |
| `Ou` | `MouthPucker`、`MouthFunnel` | 口をすぼめる |
| `Oh` | `MouthFunnel`と`JawOpen` | 丸く開く |
| `Ee` | `MouthStretchLeft/Right` | 横に広げる |
| `Ih` | `MouthSmileLeft/Right`と小さい`JawOpen` | 横に引いて少し開く |
| `Happy` | `MouthSmileLeft/Right`、`CheekSquintLeft/Right` | 既定：弱め（0.6倍） |
| `Surprised` | `BrowInnerUp`、`EyeWideLeft/Right` | 既定：弱め |
| `Angry` | `BrowDownLeft/Right` | 既定：弱め |
| `Sad` | `MouthFrownLeft/Right`、`BrowInnerUp` | 既定：弱め |
| 視線 | `EyeLookIn/Out/Up/Down`（左右） | 角度の範囲（既定：左右±12°、上下±10°） |

- 口の5つの形（`Aa`等）は、合計が1を超えないよう比で配分する。感情の表情（`Happy`等）を強く出すと、まばたきや口と重なって崩れるモデルがあるため、感情の表情が出ている間はまばたきと口を弱める（VRM 1.0の`overrideBlink`、`overrideMouth`の設定に従う）。
- 変換の結果から、Expression Signal（表情ごとの0〜1の値）を作る。v0.1.0では使う側（Extension Motion）はまだないが、出す形は決めておく（方式設計7.7）。
- モデルがARKitの52個の表情を独自に持つ場合（Perfect Sync）は、値をそのまま渡す方式を後で足せるよう、`IAvatarExpressions.CustomNames`を用意しておく（17章）。

### 7.5 手指

- 各指の関節の曲げを、隣り合う骨の間の角度から求める（Tracking詳細設計8.4）。曲げは0〜90°（親指は別の範囲）、横の開きは±15°に制限する。
- 手を見失ったときは、軽く握った自然な形へゆっくり戻す。
- 手を見失った状態から戻ったときは、急に形が変わらないよう、0.2秒かけて観測へ寄せる。

---

## 8. State / Lifecycle

### 8.1 AvatarService

- OptionalのApplication Serviceとし、Logging、Video、Stage、Trackingの後に起動する。Trackingがなくても、アバターは基準の姿勢で表示する。
- 起動時に一覧を作り、`avatar-settings.json`の使うアバターを読み込む（Main Threadを長く止めない、9章）。
- 終了時に、アバターのGameObject、Texture、Mesh、Materialを破棄する（UniVRMの`RuntimeGltfInstance`の破棄を使う）。

### 8.2 読み込み

1. `Loading`。`VrmAvatarLoader`がファイルを読み、UniVRMで組み立てる。
2. Rigと表情を確かめる。必須の骨（Hips、Spine、Chest、Neck、Head、左右の上腕・前腕・手）がなければ`Error`（`MissingBones`）。
3. 層を`Avatar`にし、Spawn Pointに置く。
4. `Ready`。古いアバターがあれば破棄する。

### 8.3 毎Frameの適用

- Avatar CameraがRenderingする前に、次の順で行う（`LateUpdate`相当、`IFrameLoop`の後段）：
  1. `ITrackingSource.Current`を読む。
  2. `PoseController`：補間、見失った部位の戻し、`PoseRetargeting`、Rigへの設定。
  3. `ExpressionController`：表情と視線。
  4. UniVRMの処理（Control Rigの反映、Expression、LookAt、SpringBone）を明示的に1回呼ぶ。UniVRMの自動の更新は止め、順序をこちらで決める（SpringBoneが姿勢の後に動くようにするため）。
- 毎Frame確保しない。骨の回転の配列、表情の値の配列は使い回す。

### 8.4 見失ったとき

| 状態 | 扱い |
|---|---|
| `Tracked` | 観測を適用する |
| `LowConfidence` | 前の値と観測の間を、信頼度に応じて混ぜる |
| `Lost`（0.5秒以内） | 最後の値を保つ |
| `Lost`（0.5秒を超えた） | 1秒かけて、基準の姿勢（腕は下ろす、手は軽く握る、表情はなし）へ戻す |
| 戻ったとき | 0.2秒かけて観測へ寄せる |
| `Disabled`、`NotSupported` | 基準の姿勢 |

---

## 9. Thread / async

- アバターの組み立て（GameObject、Mesh、Texture）はUnityのAPIのためMain Threadで行う。ファイルの読み込みとglTFの解析は、UniVRMの非同期の読み込み（`Vrm10.LoadBytesAsync`等、`IAwaitCaller`で処理を複数Frameへ分ける）を使い、1Frameを長く止めない。読み込みにかかった時間と、最も長かったFrameを記録する。
- コピー、Hash、サムネイルの書き込みは別のThreadで行う。
- 姿勢と表情の適用はMain Threadで、毎Frame1回行う。

---

## 10. 設定

```csharp
public sealed class AvatarSettings
{
    public string CurrentAvatarId { get; set; }   // なければ、アバターを出さない
    public bool Mirror { get; set; } = true;      // 6.6
}
```

- `avatar-settings.json`に保存する（設定のFolder、`ConfigFileStore`）。Project / Profileができたら移す。
- アバターごとの設定は`avatar-profile.json`（6.3）。

---

## 11. ログ・診断

- 登録、読み込み、差し替え、失敗を、AvatarId、形式、版、読み込み時間とともにInformationで記録する。足りない骨や表情はWarningで記録する（方式設計7.13）。ファイルのフルパスはLogに書かない（利用者名を含むため）。ファイル名だけを書く。
- 性能計測：

| 計測 | 種類 | 内容 |
|---|---|---|
| `Avatar/Load` | Timer | 読み込みの時間 |
| `Avatar/LoadLongestFrame` | Timer | 読み込み中に最も長かったFrame |
| `Avatar/Apply` | Timer | 毎Frameの姿勢と表情の適用の時間 |

- 利用者へ伝える問題（`AvatarProblem`）：`NotVrm`（VRMのファイルではありません）、`TooLarge`、`MissingBones`（このモデルは人の形の骨がそろっていないため使えません）、`LoadFailed`（アバターを読み込めませんでした）、`AlreadyImported`、`StorageFailed`（保存できませんでした。保存先の空きを確認）。

---

## 12. エラー処理・復旧

| 事象 | 扱い |
|---|---|
| 登録の途中の失敗 | 一時Folderを消し、一覧は変えない |
| 使うアバターが読み込めない（起動時） | `Error`。アバターなしで続ける。他のアバターを選べる |
| 差し替えの読み込みの失敗 | 古いアバターのまま |
| 骨の欠け | 必須の骨（8.2）がなければ使えない。指等の任意の骨は、その骨の適用だけを止める |
| 表情の欠け | その表情の適用だけを止める |
| Trackingがない、止まっている | 基準の姿勢で表示する |
| UniVRMの例外 | Loaderの中で受け止め、`LoadFailed`にする。アプリは止めない |

---

## 13. セキュリティとプライバシー

- モデルのファイルは、外から持ち込まれた信頼できない入力である。拡張子、大きさの上限、形式の印を確かめてから解析する。解析はUniVRMで行い、モデルに含まれるScript等を実行する仕組みはない。
- 解析の例外はLoaderで受け止め、途中で作ったObjectを破棄する。
- 登録したモデルは、利用者のData Rootの中だけに置き、外へ送らない。
- モデルのライセンス（6.4）を利用者に見せる。条件の判断は利用者が行う。
- ライセンスのURLは、`https`のURLだけを、利用者の操作で開く。

---

## 14. テスト方針

| 種類 | 対象 |
|---|---|
| EditMode | `PoseRetargeting`：既知の姿勢（腕を横へ90°、前へ90°、肘を曲げる、手のひらを返す、胴を傾ける）の合成の観測から、期待する骨の回転になること。鏡写し。可動域の上限 |
| EditMode | 補間（7.3）、見失ったときの戻し（8.4）、`LowConfidence`の混ぜ方 |
| EditMode | `ExpressionMapping`：応答の曲線、左右をそろえる、口の形の配分、感情の表情との重なり |
| EditMode | `AvatarImporter`：拡張子・大きさ・形式の印の確認、一時Folderの後始末、同じHashの検出、Profileの読み書き（知らない項目を残す） |
| EditMode | 偽の`IAvatarRig`、`IAvatarExpressions`で`AvatarController`を動かす |
| PlayMode | 実際のVRMを読み込み、骨と表情がそろうこと、破棄で漏れがないこと。Repositoryに入れられる小さなTest用のモデルがあれば使い、なければ利用者から渡されたサンプル（Repository外）で`[Explicit]` |
| 実機 | カメラ、MediaPipe、アバターをつないで目視で確かめる（デモブランチ） |
| 性能 | 読み込みの時間と最も長いFrame、毎Frameの適用の時間 |

---

## 15. 性能

- 毎Frameの適用（`Avatar/Apply`）は0.3ms以下を目標とする（開発用PCで計測し、本書に記録する）。
- 読み込み中の1Frameは50ms以下を目標とする。配信中にアバターを切り替えても、映像が目立って止まらないようにするため。
- SpringBoneの負荷はモデルに依存する。計測して記録する。

---

## 16. Directory / Namespace と依存

```text
Assets/VirtualVessel/Avatar/
├─ Runtime/                                   VirtualVessel.Avatar
│  ├─ IAvatarService.cs     IAvatarService, AvatarEntry, AvatarState, AvatarProblem
│  ├─ AvatarService.cs      AvatarService, AvatarSettings
│  ├─ Core/                 AvatarRuntime, IAvatarRig, IAvatarExpressions, AvatarBone, AvatarExpression, AvatarProfile
│  ├─ Loading/              IAvatarLoader, AvatarImporter, AvatarStore
│  ├─ Pose/                 PoseRetargeting, PoseController, PoseInterpolation
│  ├─ Expression/           ExpressionMapping, ExpressionController, ExpressionSignal
│  └─ Runtime/              AvatarController
├─ Vrm/                                       VirtualVessel.Avatar.Vrm（UniVRMを参照する唯一のAssembly）
│  ├─ VrmAvatarLoader.cs
│  ├─ VrmAvatarRig.cs       Control RigによるIAvatarRig
│  └─ VrmAvatarExpressions.cs
└─ Tests/
   ├─ EditMode/
   └─ PlayMode/
```

- 参照：Core、Diagnostics、ProjectData、Tracking、Video。VRMのAssemblyは、加えてUniVRM（`com.vrmc.vrm`、`com.vrmc.gltf`）を参照する。
- UniVRMは、UPMのGitのURLで、版のTagを固定して導入する（現在v0.131.3。`path=/Packages/VRM10`等、v0.131からの配置）。`packages-lock.json`がCommitを固定する。Unity 6.6で動くことを導入の段階で確かめる。ライセンスはMIT。同じ変更でSBOMへ記録する。
- VRMのShader（MToon、URP用）が配布物のBuildに含まれることを確かめる（実行時に読み込むモデルのMaterialが使うため、Buildから外れると表示されない）。

---

## 17. 未決事項

| 項目 | 対応予定 |
|---|---|
| VRM 0.xのモデルを受け付けるか | **利用者の確認待ち**。方式設計7.3はVRM 1.0以降を対象とする。UniVRMはVRM 0.xを読み込み時に1.0へ変換できる。0.xのモデルは今も多いため、変換して受け付ける案を提案する（受け付ける場合は方式設計7.3を更新する） |
| Test用のVRMモデル | 利用者からサンプルを受け取る（Repository外に置く）。Repositoryに入れられるライセンスの小さなモデルがあれば、自動のTestに使う |
| 手を顔に当てる等の位置合わせ（IK） | 全身の対応と合わせて検討する |
| Perfect Sync（ARKitの52個の表情を持つモデル） | v0.1.0の後 |
| FBX | Libraryの選択から。v0.1.0の後 |
| Spawn Point、カメラの位置の調整の画面 | Stage Scene、Camera Pointの設計時 |
| 設定をProject / Profileへ移す | Project / Profileの設計時 |

---

## 18. 実装の順序

TrackingとAvatarを合わせて、次の順で進める。各段階を1つのBranchとし、それぞれにTestと、試すためのデモブランチを付ける（CLAUDE.md 29.2）。段階7の後にv0.1.0をリリースする。

1. Trackingの土台（Tracking詳細設計18章の1）
2. MediaPipeとカメラ（Tracking詳細設計18章の2）
3. アバターの読み込み：UniVRMの導入（SBOM）、登録、Data Root、`IFileDialog`、Videoのアバターの層とAvatar Camera、既定のSpawn Point、セットアップ画面のアバターのCard
4. 顔：表情、視線、頭の向き
5. 上半身：胴、肩、腕。奥行きの推定の比較（Tracking詳細設計8.4）
6. 手指
7. トラッキングの設定画面（範囲の選択と「全身：近日対応」、平滑化の段階、鏡写し、基準の姿勢）、診断の仕上げ
