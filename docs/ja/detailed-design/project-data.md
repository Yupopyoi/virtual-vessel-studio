# Project Data詳細設計

## 1. 目的

各機能の設定を、ProjectとProfileとしてどこに置くかを定める（方式設計6.2〜6.17）。これまでは、各Moduleが設定を、設定のFolder（`Config/`）にUIの設定と並べて1ファイルずつ置いていた。これをData RootのProjectとProfileに移す。のちに利用者が複数のProject（配信ごと等）を持ち、トラッキングや取り込みのProfileを共有できるようにするためである。

最初の版では、Projectは1つで、起動時に開く。Projectの画面はまだない。

## 2. 責務

| 責務 | 担当 |
|---|---|
| 開くProjectを決める、最初のProjectを作る、各設定ファイルの場所を示す | ProjectData（`ProjectOpener`） |
| Projectより前の版の設定ファイルを、最初のProjectへ写す | ProjectData |
| 各Moduleの設定の中身と形式 | 各Module（Tracking、Capture、Avatar、Stage）。変えない |
| 起動時にProjectを開き、各Serviceに設定ファイルを渡す | Application（Composition） |

- ProjectDataは何を使うかを決め、各Moduleが自分の設定を読んで反映する（方式設計6.17）。
- ProjectDataは各Moduleの設定の型を知らない。古いファイル名はCompositionが渡す。

## 3. Moduleの境界

- ProjectDataが参照するのはCoreだけである。ログは書かず、記録すべきことを返し、Compositionが記録する。
- 各Moduleは`Projects`を参照しない。Storeを作る関数が、Folderと、Profileではファイル名を受け取る。

## 4. 主なClassとInterface

| Class | 種類 | 役割 |
|---|---|---|
| `ProjectOpener` | Static Class | Projectを開く・作る、古いファイルを写す、IDを確かめる（7章） |
| `ProjectManifest` | Class | `project.json`（6章） |
| `AppSettings` | Class | `Config/app-settings.json`：使っているProject |
| `ProfileKind` | Enum | `Tracking`、`Capture` |
| `OpenedProject` | Class | 開いたProject：そのFolder、ProfileのFolderとファイル名、記録 |
| `ProjectFolders`、`LegacySettings` | 値、Class | 各Folderの場所。古いファイル名と、それぞれの行き先 |

## 5. 公開API

```csharp
public static class ProjectOpener
{
    static OpenedProject Open(ProjectFolders folders, LegacySettings legacy, ISystemClock clock, Func<string> newId);
    static string NewId();                                        // 小文字の16進数32桁
    static bool IsValidId(string id);                             // 純粋
    static string Choose(string current, IReadOnlyList<string> existing);   // 純粋
    static ProjectManifest Repair(ProjectManifest manifest, string id, Func<string> newId, List<string> notes);
}

public sealed class OpenedProject
{
    ProjectManifest Manifest;
    string ProjectId;
    string Directory;                          // Projects/<ProjectId>
    bool Created;
    IReadOnlyList<string> Notes;
    string ProfileDirectory(ProfileKind kind); // Profiles/<Kind>
    string ProfileFileName(ProfileKind kind);  // <ProfileId>.json
}
```

## 6. Data Modelとファイル

```text
%LOCALAPPDATA%/VirtualVesselStudio/Config/
├─ app-settings.json        { "schemaVersion": 1, "currentProjectId": "<id>" }
└─ ui-settings.json         UIの設定（変えない、方式設計6.8）

DataRoot/
├─ Projects/
│  └─ <ProjectId>/
│     ├─ project.json       Project（下記）
│     ├─ avatar-settings.json   使うアバターと表情の調整（Avatar詳細設計10章）
│     └─ stage-settings.json    配信画面の配置とライト（Stage詳細設計6.3）
└─ Profiles/
   ├─ Tracking/<ProfileId>.json   トラッキングの設定（Tracking詳細設計10章）
   └─ Capture/<ProfileId>.json    取り込みの設定（Capture詳細設計10章）
```

```json
{
  "schemaVersion": 1,
  "projectId": "<16進数32桁>",
  "displayName": "Default",
  "createdAt": "2026-10-27T01:00:00.0000000Z",
  "updatedAt": "2026-10-27T01:00:00.0000000Z",
  "trackingProfileId": "<16進数32桁>",
  "captureProfileId": "<16進数32桁>"
}
```

- **Profile**（方式設計6.3、6.11）：トラッキングと取り込み。本人、カメラ、機器についての設定で、Projectどうしで共有できる。
- **Project自身の設定**（方式設計6.4）：使うアバターと表情の調整、配信画面の配置。Projectのフォルダーに、これまでと同じ形式のファイルとして置く。
- **方式設計6.4との違い：**
  - 使うアバターは、`project.json`の`AvatarId`ではなく、Avatarが持つ`avatar-settings.json`に残す。Avatarはこれまでどおり読み書きする。
  - Voice、Camera、Audio、StreamingのProfileは、それぞれの機能とともに加える。
- どのファイルも`schemaVersion`を持つ。移したファイルの形式は変えていないため、版もそのままとする（方式設計6.14）。
- IDは、アバターと同じく小文字の16進数32桁とする（方式設計6.6）。

## 7. 状態とライフサイクル

`ProjectOpener.Open`は起動時に1回、設定を持つ最初のService（Capture）を作るときに動く。

1. `app-settings.json`を読む。
2. 名前が正しいIDで、`project.json`があるProjectのフォルダーを並べる。
3. Projectを選ぶ（`Choose`、純粋）：`app-settings.json`が示すものがあればそれ、なければ序数順で最初のもの、それもなければなし。
4. なしなら、Projectを作る：ProjectとProfileごとに新しいID、表示名は「Default」。Projectが1つもなかったときは、古い設定ファイルをそこへ写す（8章）。
5. `project.json`を読み、直す（`Repair`、12章）。作ったときと直したときは保存する。
6. 使うProjectが変わったときは`app-settings.json`を保存する。
7. 各設定ファイルの場所を返す。各Serviceは、これまでどおり自分のファイルを読み、保存する（`ConfigFileStore`、`ConfigSaveScheduler`）。

## 8. 古い設定の移行

### 8.1 行き先

| `Config/`の古いファイル | 新しい場所 |
|---|---|
| `tracking-settings.json` | `Profiles/Tracking/<TrackingProfileId>.json` |
| `capture-settings.json` | `Profiles/Capture/<CaptureProfileId>.json` |
| `avatar-settings.json` | `Projects/<ProjectId>/avatar-settings.json` |
| `stage-settings.json` | `Projects/<ProjectId>/stage-settings.json` |
| `ui-settings.json` | そのまま |

### 8.2 方法

- 写すのは最初のProjectだけ、つまりData RootにまだProjectがないときだけである。使っているProjectがなく、別のProjectがあるときに作るProjectには写さない。
- ファイルは移さずに写す。元のファイルはバックアップとして`Config/`に残す（CLAUDE.md 25章）。以後は読まない。
- 新しい場所にファイルがすでにあるときは、上書きしない。
- 中身は知らない項目も含めてそのまま写す。形式の移行は、各Moduleが読むときに行う（例：トラッキングの版1から4）。

## 9. Thread

起動時にMain Threadで行う。ファイルは小さい（数KB）ため、そこで読んでよい。

## 10. 設定

6章のファイルのほかにはない。

## 11. 保存

6章のとおり。ProjectとProfileはData Rootにあるため、利用者がData Rootを移すと一緒に移る（方式設計6.9、6.26）。`app-settings.json`は、どのProjectを開くかを示すため、設定のFolderに置く。

## 12. エラーと回復

- **壊れたファイル**（`app-settings.json`、`project.json`）：`ConfigFileStore`が退避し、既定を使う。壊れていた`project.json`は、下のように直す。
- **直し方**（`Repair`、純粋）：
  - ProjectのIDをフォルダーの名前にする。
  - 正しいIDでないProfileのIDは、新しいProfile（既定から始まる）に替える。
  - 表示名がなければ「Default」にする。
- **IDは信頼しない入力**（CLAUDE.md 26章）：これらのファイルは、利用者として動くどのプログラムでも書ける。そのため、IDは小文字の16進数32桁のときだけ、フォルダーやファイルの名前に使う。書き込まれた「../x」がPathに届くことはない。
- **使っているProjectがない**：あるProjectのうち最初のものを開き、その旨を記録する。新しく作るのは、1つもないときだけである。
- **I/Oのエラー**でProjectを開けないときは、作っていたServiceが失敗する。そのServiceはOptionalなので、アプリはそれなしで起動し、原因はログに残る。

## 13. ログと診断

Compositionは、`OpenedProject.Notes`の1つずつをInformationで記録し、続けてProjectとProfileを記録する：`Project <id> (Default) open: tracking profile <id>, capture profile <id>.` Notesには、写したファイル、退避した壊れたファイル、直したこと、使っているProjectがなかったことが入る。

## 14. Testの方針

`ProjectOpenerTests`（EditMode）：
- 最初の起動でProjectを作り、次の起動でそれを開く。
- 古い設定を最初のProjectへ写して元を残し、その後の変更を上書きしない。
- 使っているProjectがないときは、あるものを開く。`Choose`は序数順で最初のものを選ぶ。
- ファイルから読んだIDは、16進数のときだけフォルダーの名前に使い、ほかの項目は直す。

各Moduleの設定のTestは変えない。テスト用のFolderでStoreを使う。

## 15. 性能

起動時に小さなファイルを数個、1回だけ読む。毎Frameの処理はない。

## 16. Folder / Namespace

```text
ProjectData/Runtime/Projects/
├─ ProjectManifest.cs     ProfileKind、ProjectManifest、AppSettings
└─ ProjectOpener.cs       ProjectFolders、LegacySettings、OpenedProject、ProjectOpener
```

Namespaceは`VirtualVessel.ProjectData.Projects`。

## 17. 未解決の課題

| 項目 | 方針 |
|---|---|
| Projectの画面：作る、名前を変える、切り替える、消す（方式設計6.18、6.22） | 複数のProjectが要る最初の機能とともに |
| ほかのProfile：Voice、Camera、Audio、Streaming | それぞれの機能とともに |
| `project.json`の`AvatarId`（方式設計6.4） | 当面は`avatar-settings.json`に残す（6章） |
| Profileの表示名 | Projectの画面とともに |
| `Config/`の古い設定ファイルを消す | 当面はバックアップとして残す。後の版で消してもよい |
