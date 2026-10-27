# Project Data Detailed Design

## 1. Purpose

Where the settings of each function are kept, as projects and profiles (system design 6.2–6.17). Before this, each module kept its settings in one file in the settings folder (`Config/`), next to the UI settings. They now belong to a project and its profiles in the Data Root, so that later a user can keep several projects (for example, one per stream) that share a tracking or capture profile.

This first version has one project, opened at startup. There is no screen for projects yet.

## 2. Responsibilities

| Responsibility | Owner |
|---|---|
| Deciding which project to open, making the first one, and where its settings files are | ProjectData (`ProjectOpener`) |
| Copying the settings files of the version before projects into the first project | ProjectData |
| The contents and format of each module's settings | Each module (Tracking, Capture, Avatar, Stage), unchanged |
| Opening the project at startup and giving each service its settings file | Application (the composition) |

- ProjectData resolves what to use; each module reads and applies its own settings (system design 6.17).
- ProjectData does not know the modules' settings types. The composition tells it the old file names.

## 3. Module Boundaries

- ProjectData references only Core. It writes no log; it returns notes, which the composition logs.
- Modules do not reference `Projects`. Their store factories take a folder, and for profiles a file name.

## 4. Main Classes and Interfaces

| Class | Kind | Role |
|---|---|---|
| `ProjectOpener` | Static class | Opens or makes the project, copies the old files, checks IDs (chapter 7) |
| `ProjectManifest` | Class | `project.json` (chapter 6) |
| `AppSettings` | Class | `Config/app-settings.json`: the project in use |
| `ProfileKind` | Enum | `Tracking`, `Capture` |
| `OpenedProject` | Class | The opened project: its folder, its profiles' folders and file names, and notes |
| `ProjectFolders`, `LegacySettings` | Value, class | Where the folders are; the old file names and where each goes |

## 5. Public API

```csharp
public static class ProjectOpener
{
    static OpenedProject Open(ProjectFolders folders, LegacySettings legacy, ISystemClock clock, Func<string> newId);
    static string NewId();                                        // 32 lowercase hex digits
    static bool IsValidId(string id);                             // Pure
    static string Choose(string current, IReadOnlyList<string> existing);   // Pure
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

## 6. Data Models and Files

```text
%LOCALAPPDATA%/VirtualVesselStudio/Config/
├─ app-settings.json        { "schemaVersion": 1, "currentProjectId": "<id>" }
└─ ui-settings.json         UI settings (unchanged, system design 6.8)

DataRoot/
├─ Projects/
│  └─ <ProjectId>/
│     ├─ project.json       the project (below)
│     ├─ avatar-settings.json   the avatar in use and the face tuning (Avatar detailed design 10)
│     └─ stage-settings.json    the stream screen's layout and the light (Stage detailed design 6.3)
└─ Profiles/
   ├─ Tracking/<ProfileId>.json   the tracking settings (Tracking detailed design 10)
   └─ Capture/<ProfileId>.json    the capture settings (Capture detailed design 10)
```

```json
{
  "schemaVersion": 1,
  "projectId": "<32 hex digits>",
  "displayName": "Default",
  "createdAt": "2026-10-27T01:00:00.0000000Z",
  "updatedAt": "2026-10-27T01:00:00.0000000Z",
  "trackingProfileId": "<32 hex digits>",
  "captureProfileId": "<32 hex digits>",
  "streamingProfileId": "<32 hex digits>"
}
```

- **Profiles** (system design 6.3, 6.11): tracking, capture, and streaming. They are about the person, the camera, the devices, and the output, and can be shared by projects. The streaming profile holds the encoding values and the recording folder (recording detailed design 10).
- **The project's own settings** (system design 6.4): the avatar in use with the face tuning, and the stream screen's layout. They are files in the project's folder, in the formats they had before.
- **Differences from system design 6.4:**
  - The avatar in use stays in `avatar-settings.json`, which Avatar owns, instead of an `AvatarId` in `project.json`. Avatar keeps reading and writing it as before.
  - Voice, Camera, and Audio profiles are added with their functions.
- Every file keeps `schemaVersion`, and the formats of the moved files did not change, so their versions stay as they were (system design 6.14).
- IDs are 32 lowercase hexadecimal digits, as for avatars (system design 6.6).

## 7. State and Lifecycle

`ProjectOpener.Open` runs once at startup, when the first service that keeps settings (Capture) is created.

1. Read `app-settings.json`.
2. List the project folders whose name is a valid ID and that have a `project.json`.
3. Choose the project (`Choose`, pure): the one `app-settings.json` names when it exists, else the first in ordinal order, else none.
4. With none, make a project: new IDs for it and for each profile, display name "Default". When there was no project at all, copy the old settings files into it (chapter 8).
5. Load `project.json` and repair it (`Repair`, chapter 12). Save it when it was made or repaired.
6. Save `app-settings.json` when the project in use changed.
7. Return where each settings file is. Each service then reads and saves its own file as before (`ConfigFileStore`, `ConfigSaveScheduler`).

## 8. Moving Over the Old Settings

### 8.1 What Goes Where

| Old file in `Config/` | New place |
|---|---|
| `tracking-settings.json` | `Profiles/Tracking/<TrackingProfileId>.json` |
| `capture-settings.json` | `Profiles/Capture/<CaptureProfileId>.json` |
| `avatar-settings.json` | `Projects/<ProjectId>/avatar-settings.json` |
| `stage-settings.json` | `Projects/<ProjectId>/stage-settings.json` |
| `ui-settings.json` | Stays |

### 8.2 How

- Only the first project takes them: when the Data Root has no project yet. A project made later, when the one in use is missing but another exists, does not.
- The files are copied, not moved. The originals stay in `Config/` as a backup (CLAUDE.md 25). They are no longer read.
- A file already at the new place is never overwritten.
- The contents are copied unchanged, unknown fields included. Each module migrates its own format when it reads it (for example, tracking version 1 to 4).

## 9. Threading

Main thread, at startup. The files are small (a few KB), so reading them there is fine.

## 10. Configuration

None beyond the files of chapter 6.

## 11. Storage

As in chapter 6. Projects and profiles live in the Data Root, so they follow it when the user moves it (system design 6.9, 6.26). `app-settings.json` is in the settings folder, as it says which project to open.

## 12. Error Handling and Recovery

- **A broken file** (`app-settings.json`, `project.json`): `ConfigFileStore` moves it aside and the defaults are used. A broken `project.json` is then repaired as below.
- **Repair** (`Repair`, pure):
  - the project ID is set to the folder's name;
  - a profile ID that is not a valid ID, or missing as in a project made before that profile existed, is replaced by a new profile (which starts at the defaults);
  - a missing display name becomes "Default".
- **IDs are untrusted input** (CLAUDE.md 26): any program running as the user can write these files, so an ID names a folder or a file only when it is 32 lowercase hexadecimal digits. A written "../x" never reaches a path.
- **The project in use is missing:** the first existing project is opened, and this is noted. A new one is made only when there is none.
- **An I/O error** while opening the project fails the service being created. That service is Optional, so the app starts without it, and the log has the cause.

## 13. Logging and Diagnostics

The composition logs each note of `OpenedProject.Notes` at Information, then the project and its profiles: `Project <id> (Default) open: tracking profile <id>, capture profile <id>, streaming profile <id>.` Notes include files copied, broken files moved aside, repairs, and a missing project in use.

## 14. Testing Strategy

`ProjectOpenerTests` (EditMode):
- the first start makes a project and the next start opens it;
- the old settings are copied into the first project and kept, and a later change is not overwritten;
- a missing project in use opens an existing one, and `Choose` picks the first in ordinal order;
- IDs from files name folders only when they are hexadecimal, and other fields are repaired.

The modules' own settings tests are unchanged; they use their stores with a test folder.

## 15. Performance

A few small files at startup, once. Nothing per frame.

## 16. Directory / Namespace

```text
ProjectData/Runtime/Projects/
├─ ProjectManifest.cs     ProfileKind, ProjectManifest, AppSettings
└─ ProjectOpener.cs       ProjectFolders, LegacySettings, OpenedProject, ProjectOpener
```

Namespace `VirtualVessel.ProjectData.Projects`.

## 17. Open Issues

| Item | Plan |
|---|---|
| A screen for projects: making, renaming, switching, deleting (system design 6.18, 6.22) | With the first feature that needs more than one project |
| More profiles: Voice, Camera, Audio | With their functions |
| `AvatarId` in `project.json` (system design 6.4) | Stays in `avatar-settings.json` for now (chapter 6) |
| Profile display names | With the projects screen |
| Removing the old settings files in `Config/` | Kept as a backup for now; a later version may remove them |
