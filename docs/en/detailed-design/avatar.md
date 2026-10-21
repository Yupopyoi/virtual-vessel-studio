# Avatar Detailed Design

## 1. Purpose

This document defines the detailed design of the Avatar module. The Avatar module loads 3D models and applies Tracking observations (`TrackingFrame`) to the avatar's pose and expressions (system design chapter 7).

The scope built here (v0.1.0):

- Loading VRM (UniVRM; VRM 0.x is converted to 1.0 at load, 6.8) and the avatar abstraction (Avatar Runtime, skeleton, expressions)
- Registering avatars: copying into the Data Root, Avatar Profile, thumbnail, showing the model's license information
- Choosing the avatar to use, loading it at startup, and placing it on the stream
- Applying the pose (upper body, fingers) and expressions (eyes, mouth, gaze, brows)
- Handling lost tracking, mirroring, and interpolating between observations

Out of scope, handled by later designs:

- Loading FBX (system design 7.3; starting with the choice of library)
- Extension bones and Extension Motion (cat ears, tails, and so on; system design 7.8). The Expression Signal is output already (7.4)
- Applying full body (`BodyRange.FullBody`): hip movement, legs, foot contact
- Showing several avatars at once (system design 7.9; internally not limited to one)
- Transitions when switching avatars during a stream (fades and so on)
- Avatar Spawn Points in Stage Scenes (system design 11.6). Until then a default position is used (6.5)

Related system design sections: chapter 7 3D avatar control, chapter 8 tracking, 6.9–6.11 Data Root, 11.6 Avatar Spawn Point, 15.3 real-time behavior

Related detailed designs: Tracking (`tracking.md`), Video (`video.md` 7.2 the avatar layer), Stage (`stage.md`)

Prototype referenced (outside the repository): `E:\VirtualVessel\benchmarks\Voom`. Its VRM 1.0 loading (`Vrm10` of UniVRM 0.129.1) and its tuning knowledge for converting face movement to expressions are used as reference. Its body application (only the chest tilt, averaging Euler angles) is not kept (Tracking detailed design chapter 1).

### Overview

```mermaid
flowchart LR
    File[".vrm (the user's file)"] --> Import["AvatarImporter<br/>check and copy into the Data Root"]
    Import --> Store["DataRoot/Avatars/&lt;AvatarId&gt;/"]
    Store --> Loader["VrmAvatarLoader (UniVRM)"]

    subgraph Avatar["Avatar Module"]
        Loader --> Runtime["IAvatarModel<br/>Rig / Expressions"]
        Runtime --> Ctrl["AvatarController"]
        Ctrl --> Pose["PoseController<br/>(retargeting)"]
        Ctrl --> Expr["ExpressionController"]
    end

    Tracking["Tracking: ITrackingSource.Current"] --> Ctrl
    Expr --> Signal["Expression Signal<br/>(Extension Motion comes later)"]
    Runtime --> Layer["Video: avatar layer"]
```

---

## 2. Responsibilities

| Responsibility | Owner |
|---|---|
| Checking a model file, copying it into the Data Root, and registering it | Avatar (`AvatarImporter`) |
| The file-picking screen (the OS dialog) | UI (`IFileDialog`, 6.7) |
| Loading a model format and converting it to a format-independent representation | Avatar (per-format loaders) |
| Fitting the observed pose to the avatar skeleton (body size, bone orientation) | Avatar (`PoseController`) |
| Converting face movement to the avatar's expressions | Avatar (`ExpressionController`) |
| Mirroring, interpolating between observations, handling lost tracking | Avatar |
| Observation, smoothing, input correction | Tracking |
| Where the avatar is placed (spawn point) | Stage (a default position in v0.1.0, 6.5) |
| The camera and layer that draw the avatar | Video |
| Screens for choosing and setting up avatars | UI (setup screen) |

---

## 3. Module Boundaries

- Avatar references Core, Diagnostics, ProjectData, and the public contracts of Tracking (`ITrackingSource`, `TrackingFrame`). It does not reference Capture, UI, Video, or Stage (the Tracking reference was added in step 4).
- Where the avatar goes (Video's avatar layer and Stage's spawn point) is passed by the composition root as `AvatarPlacement` (the layer number and a position and rotation). UI references Avatar, and Video and Stage reference UI, so Avatar referencing Video or Stage would make the assembly references circular ("UI → Avatar → Video → UI").
- UniVRM types (`UniVRM10.*`, `UniGLTF.*`) are used only inside the VRM loader (a separate assembly, chapter 16). The rest of Avatar uses only `IAvatarRig` and `IAvatarExpressions` (5.2) (system design 7.2).
- Other modules use only `IAvatarService` (5.1). They do not operate transforms or blendshapes directly (system design 7.2 Avatar Controller).
- It does not depend on Tracking's MediaPipe types. Its only input is `TrackingFrame`.

---

## 4. Main Classes and Interfaces

| Name | Kind | Summary |
|---|---|---|
| `IAvatarService` | Interface | The list of registered avatars, registering, choosing, the state of the current avatar (5.1) |
| `AvatarService` | Class (`IApplicationService`) | Implements the above. Loads, places, and calls the per-frame application |
| `AvatarPlacement` | Struct | Where the avatar goes: Video's avatar layer and Stage's spawn point (chapter 3) |
| `AvatarEntry` | Class | A registered avatar: id, profile (null when unreadable), thumbnail path |
| `AvatarStore` | Class | The avatar folders in the Data Root: listing, the temporary folder, moving in, deleting (6.1) |
| `AvatarFileCheck` | Static class | Checks before parsing: extension, size, glTF magic (pure functions, 6.2) |
| `AvatarImporter` | Class | Checks the file, copies it into the Data Root, and creates the profile (6.2). Returns an `AvatarImportResult` |
| `IAvatarLoader` | Interface | Per-format loading (system design 7.3) |
| `VrmAvatarLoader` | Class | Loads VRM with UniVRM. The only UniVRM-side class, created by the composition root |
| `IAvatarModel` | Interface | One loaded avatar: root, rig, expressions, model information (`AvatarModelInfo`), visibility, per-frame application, disposal (system design 7.2) |
| `AvatarLicense` | Class | The terms of use the author set, independent of the format (6.4) |
| `AvatarRestPose` | Static class | The pose when nothing moves the avatar: the reference pose with the arms lowered (7.2) |
| `IAvatarRig` | Interface | A format-independent skeleton. Sets standard bone (`AvatarBone`) rotations in a space where the reference pose is identity (7.1) |
| `IAvatarExpressions` | Interface | Format-independent expressions. Sets the weights of standard expressions (`AvatarExpression`) and the gaze |
| `AvatarBone` | Enum | Standard human bones (same order as Unity Humanoid) |
| `AvatarExpression` | Enum | Standard expressions: `Blink`, `BlinkLeft`, `BlinkRight`, `Aa`, `Ih`, `Ou`, `Ee`, `Oh`, `Happy`, `Angry`, `Sad`, `Relaxed`, `Surprised`, `Neutral` |
| `AvatarController` | Class | The control entry point for one avatar; dispatches to pose and expressions |
| `PoseRetargeting` | Static class | Computes bone rotations from observations (pure functions, chapter 7) |
| `PoseController` | Class | Interpolation, handling lost tracking, application according to the range (`BodyRange`) |
| `ExpressionMapping` | Class | Settings for converting expression values (`FaceShape`) to expressions (7.4) |
| `ExpressionController` | Class | Applies expressions and gaze; produces the Expression Signal |
| `FaceTarget` | Class | What the avatar's face shows: a weight per expression, the gaze, and the head's turn. Reused every frame |
| `FaceMotion` | Class | Interpolation between face observations and handling a lost face (7.3, 8.4) |
| `HeadPose` | Static class | The head's turn: mirroring, the limit, and the neck's share (pure functions, 7.1) |
| `ExpressionCurve` | Struct | The response curve from a value to an expression weight (7.4) |
| `FaceTuning` | Class | The user's face tuning: a sensitivity per part (7.4) |
| `AvatarProfile` | Class | Saved usage settings (6.3) |
| `AvatarSettings` | Class | The avatar in use and the face tuning (chapter 10) |

---

## 5. Public API

### 5.1 IAvatarService

```csharp
public interface IAvatarService
{
    IReadOnlyObservable<IReadOnlyList<AvatarEntry>> Avatars { get; }   // Registered avatars
    IReadOnlyObservable<string> CurrentAvatarId { get; }                // The avatar in use, or null
    IReadOnlyObservable<AvatarState> State { get; }                     // None, Loading, Ready, Error
    IReadOnlyObservable<AvatarProblem> Problem { get; }                 // Problem shown to the user (chapter 11)

    IReadOnlyList<string> Extensions { get; }                           // Extensions that can be registered (for the file-picking screen)

    // Registers (6.2). confirm shows the model's information and terms before registering; false declines
    Task<AvatarImportResult> ImportAsync(string path, Func<AvatarModelInfo, Task<bool>> confirm, CancellationToken cancellationToken);
    Task SelectAsync(string avatarId, CancellationToken cancellationToken);   // Loads and swaps; null takes the avatar away
    Task RemoveAsync(string avatarId, CancellationToken cancellationToken);   // Unregisters (UI confirms)

    IReadOnlyObservable<FaceTuning> FaceTuning { get; }                        // Face tuning (7.4)
    void SetFaceTuning(FaceTuning tuning);                                     // Applies at once and saves
    FaceTarget Face { get; }                                                   // The face this frame; null without an avatar
}
```

- `AvatarImportResult` is one of: registered (`Imported`), declined by the user (`Declined`), or a problem (`Problem`; for `AlreadyImported`, `Entry` is the avatar already registered).

- All operations and notifications happen on the main thread. Copying files and computing hashes happen on another thread.
- `SelectAsync` swaps out the old avatar only after the new one has loaded. If loading fails, the old avatar stays.

### 5.2 Format-independent Skeleton and Expressions

```csharp
public interface IAvatarRig
{
    bool Has(AvatarBone bone);
    float BoneLength(AvatarBone bone);                       // Length to the child bone (meters)
    void SetRotation(AvatarBone bone, Quaternion rotation);  // Rotation from the reference pose (7.1), in the avatar root space
    void SetRootPosition(Vector3 position);                  // For full body (later)
    void ResetPose();                                        // Back to the reference pose
}

public interface IAvatarExpressions
{
    bool Has(AvatarExpression expression);
    void SetWeight(AvatarExpression expression, float weight);   // 0–1
    void SetLook(float yaw, float pitch);                         // A share of the model's eye range (-1 to 1)
    IReadOnlyList<string> CustomNames { get; }                   // The model's own expression names (Perfect Sync and so on, later)
}
```

- For VRM, `IAvatarRig` is implemented with UniVRM's control rig (a normalized skeleton where every bone's rotation is identity in the reference pose). UniVRM absorbs per-model differences in bone axes (one bone's X axis points forward, another's Z axis, and so on). An FBX loader will do the same normalization inside the loader.
- Values given to `SetRotation` are applied together by `IAvatarModel.Apply`, parents first, so the order of calls does not matter.
- For VRM, `IAvatarExpressions` is implemented with VRM 1.0 expressions (presets) and LookAt.
- The gaze is given as a share of the model's eye range, not as an angle, because each model maps look angles onto its eyes over a different range (7.4). For VRM, the share is multiplied by the model's LookAt input range (sideways, up, and down) to make an angle.

```csharp
public interface IAvatarModel : IDisposable
{
    Transform Root { get; }            // Faces +Z in the reference pose
    IAvatarRig Rig { get; }
    IAvatarExpressions Expressions { get; }
    AvatarModelInfo Info { get; }      // Name, format, original version, whether converted, terms, thumbnail
    void SetVisible(bool visible);
    void Apply();                      // Applies the values set this frame and moves secondary motion (SpringBone)
}
```

---

## 6. Data Model

### 6.1 Storing Avatars (System Design 7.11, 6.10)

```text
DataRoot/Avatars/<AvatarId>/
├─ model.vrm               The copied model (the original file is not changed)
├─ avatar-profile.json     Usage settings (6.3)
└─ thumbnail.png / .jpg    Thumbnail (the image in the VRM meta, saved in the format stored in the file)
```

- `AvatarId` is a GUID (`N` format) created at registration. Names and file names are not used (to avoid duplicates and invalid characters).
- The list is built at startup by reading under `Avatars/`. An entry whose `avatar-profile.json` cannot be read (or whose model file is missing) is listed as "an avatar that cannot be loaded", and the user decides whether to remove it (never removed silently, CLAUDE.md chapter 25).
- Only folders named like an AvatarId are handled and deleted; other folders the user put there are left alone. The profile's `format` and `thumbnail` accept only lower-case letters and digits or the fixed name, so editing a profile cannot point outside the folder.
- `Avatars/.importing/` is deleted at startup; it holds what an interrupted registration left behind.

### 6.2 Registering (AvatarImporter)

1. The user picks a file (UI `IFileDialog`).
2. Check: the `.vrm` extension, a size limit (default 1 GB), and that the file starts as glTF binary (the `glTF` magic).
3. Copy into a temporary folder (`Avatars/.importing/<AvatarId>/`) on another thread. Compute SHA-256; if an avatar with the same hash is registered, point to it and do not register.
4. Check the copy in the temporary folder by actually loading it (`VrmAvatarLoader`, 6.4): the required bones (8.2) are present; extract the meta information. Then destroy it.
5. Show the model's name and terms of use (6.4) in a confirmation dialog. If the user declines, nothing is registered.
6. Write `avatar-profile.json` and the thumbnail, then move the temporary folder to `Avatars/<AvatarId>/`. If anything fails on the way, or it is canceled, delete the temporary folder.
7. Add it to the list. If no avatar is in use yet, make it the one in use.

- The original file path is kept in `avatar-profile.json` for reference but is not used at run time (system design 7.11).

### 6.3 Avatar Profile

```json
{
  "schemaVersion": 1,
  "avatarId": "3f2c...",
  "displayName": "The model's name (from the meta; the user can change it)",
  "format": "vrm",
  "formatVersion": "1.0",
  "converted": false,
  "sourceFileName": "original.vrm",
  "sha256": "...",
  "importedAt": "2026-10-20T12:00:00Z",
  "license": { "...": "The license fields of the VRM meta (6.4)" },
  "scale": 1.0,
  "pose": { "armDownAngle": 70 },
  "extensions": {}
}
```

- Unknown fields are kept (CLAUDE.md chapter 25). `extensions` is where later settings such as Extension Motion go.
- Fields equal to the defaults are not written, so improved defaults reach users who did not change them.
- Face tuning belongs to the user, not to an avatar, and is kept in `avatar-settings.json` (7.4, chapter 10; user's decision, 2026-10-20).

### 6.4 Model License (VRM Meta)

In VRM, the author writes the conditions for using the avatar (who may use it as an avatar, commercial use, violent or sexual expression, modification, redistribution, and so on).

- The author, conditions, and license URL are shown in the registration confirmation dialog and on the setup screen's avatar Card (`docs/en/ui/screens/setup.md` chapter 5).
- Registration or use is not blocked because of the conditions (the user judges). However, when who may use the model as an avatar is not "everyone" (the author only, people the author allows, or not stated), the confirmation dialog shows a notice.
- Conditions the file does not state are shown as "not stated". Leaving them out could be read as allowed.
- Only `https` license URLs are opened, in the external browser, and only when the user presses them. Other kinds (`file:` and so on) are shown as text only.
- `license` in `avatar-profile.json` holds the format-independent `AvatarLicense`.

### 6.5 Placing the Avatar

- The avatar is placed in the persistent area owned by the application (outside Stage Scenes) (system design 7.10).
- Until Stage Scene spawn points exist, Stage provides one default spawn point (`IAvatarSpawnPoints`, Stage detailed design 7.1): at the origin, facing the camera (-Z). The default Main Camera position is set so the upper body fits the stream (Video detailed design 7.5).
- Placing puts every part on the `Avatar` layer, moves it to the spawn point, and lowers the arms (`AvatarRestPose`, 70° by default) before showing it. Loading happens hidden, so it never shows for a moment at the origin or on the wrong layer.
- It is drawn on the Unity layer `Avatar`, by Video's Avatar Camera (Video detailed design 7.2: same position and field of view as the Main Camera, overlay). It appears in front of the flat videos (user's decision, 2026-10-17).

### 6.6 Mirroring

- With mirroring (default: on), when the user raises the right hand, the avatar raises its left hand. On screen the hand on the same side goes up, so the user can move as if looking in a mirror.
- The setting is `Mirror` in `ITrackingSource.Options`: one setting with the left-right flip of the camera image on the setup screen, saved by Tracking in `tracking-settings.json` (Tracking detailed design chapter 10).
- `TrackingFrame` holds the user's own left and right (Tracking detailed design 6.1). Mirroring swaps left and right and flips the X axis before application (input conversion of `PoseRetargeting`, a pure function). Left and right of expressions (a one-eye blink and so on) are swapped as well.

### 6.7 The File-picking Screen

- UI gets `IFileDialog` (`Task<string> OpenAsync(FileDialogOptions options)`; null when canceled). On Windows, `GetOpenFileNameW` (comdlg32) is opened on a dedicated STA thread (`WindowsFileDialog`). Opening it on the main thread would stop rendering and the stream image while the dialog is open.
- `GetOpenFileNameW` is used instead of the COM `IFileOpenDialog`: it shows the same Explorer-style screen and needs no COM interop, which Mono and IL2CPP support unevenly.
- The application window owns the dialog, so it stays in front and the window takes no input until it closes. Only one can be open at a time.
- No additional dependency (library) is added.

### 6.8 VRM 0.x

- VRM 0.x models are accepted too (user's decision, 2026-10-18; system design 7.3). UniVRM's loading converts VRM 0.x to VRM 1.0 (enabling the 0.x conversion of `Vrm10`).
- The original file (`model.vrm`) is stored as it is, and the conversion runs at every load. The converted result is not saved as a file, so improvements to UniVRM's conversion take effect without re-importing.
- The profile's `formatVersion` is the original version (`0.x`), and `converted` shows that it was converted.
- The license (6.4) is shown from the 0.x meta before conversion, mapping the 0.x fields to the 1.0 fields (`VrmLicense`). UniVRM's conversion replaces the license URL with the VRM 1.0 license, so its result is not used. Conditions 0.x has no field for (political or religious use, redistribution, modification, and so on) are "not stated"; the 0.x license name (such as `CC_BY`) and the other-permission URL are shown with their original values. Commercial use "allowed" maps to "personal profit allowed", as UniVRM's conversion does.
- Bone reference orientations and expression names differ between 0.x and 1.0, but after conversion the model is handled as 1.0. Remaining differences (such as the gaze range) are checked by tests.

---

## 7. Computing Pose and Expressions

### 7.1 Reference Pose and Bone Rotations

- In UniVRM's control rig, every bone's rotation is identity (no rotation) in the reference pose (T pose). For each bone, `PoseRetargeting` computes the rotation from "the bone direction in the reference pose (for example +X for the right upper arm)" to "the observed direction".
- A direction alone does not decide the twist around the bone. The twist is decided by a second direction:

| Bone | Direction | Direction that decides the twist |
|---|---|---|
| Torso (Spine, Chest) | Hips to neck | The line between the shoulders |
| Neck, head | The rotation of the facial transformation matrix (Tracking detailed design 6.3), split between neck and head (default: neck 40%, head 60%). Turns beyond 75° are stopped | Same |
| Upper arm | Shoulder to elbow | The plane the elbow bends in (normal of the plane of upper arm and forearm) |
| Forearm | Elbow to wrist | Palm orientation (from the Hand Landmarker; otherwise carried over from the upper arm) |
| Hand | Wrist to the base of the middle finger | The palm normal |
| Fingers | The flexion angle of each joint (7.5) | The palm |

- Neck and head (step 4, `HeadPose`): person space and the avatar's root space have the same axes (+X right, +Y up, +Z front), so the rotation is used as it is. Mirrored, it is reflected through the plane X = 0 (turning sideways and tilting go the other way; nodding stays the same). In root space the head gets the whole turn and the neck 40% of it, so the head turns the remaining 60% relative to the neck. Turns beyond 75° are stopped, because the face is then mostly out of the camera's view and the values are unreliable.
- Only rotations are applied, not positions. So even if the avatar's and the user's bodies differ (arm length and so on), the bone directions are the same (absorbing body-size differences, system design 7.6). Movements that need matching positions, such as touching the face with a hand, are considered later with IK (solving rotations from joint positions) together with full body (chapter 17).
- The computation is done by `PoseRetargeting` (pure functions): the input is the body and hands of `TrackingFrame`, mirroring, and profile corrections; the output is an array of rotations per bone (passed in by the caller, to avoid per-frame allocations).

### 7.2 Upper Body (BodyRange.UpperBody)

- The hips do not move. The torso is distributed over Spine, Chest, and Upper Chest from the tilt of the shoulder line and the forward/back tilt of the neck (default: 30%, 35%, 35%), so the torso moves even when seated.
- Torso rotation has limits (side tilt ±30°, forward/back ±25°, twist ±40°) so inference errors do not bend the torso unnaturally.
- Shoulders rise slightly only when the arms are raised (part of the upper arm's raise angle goes to the shoulder).
- When an arm is lost, it returns to the arms-down pose (`pose.armDownAngle`, lowered 70° by default), never to the T pose.
- The lower body stays in the arms-down reference pose (a straight standing pose that looks natural whether seated or standing).

### 7.3 Interpolating Between Observations

- For each rendered frame, the shown values move toward the newest observation over one observation interval (rotations with spherical interpolation). Each move starts from what was shown when the observation arrived, so a frame always lies between the previous display and the newest observation and never jumps. This adds one observation interval of lag (about 33 ms at 30 per second) but removes the steps caused by rendering more often than inference.
- The move takes the time between the two observations, kept within 1/120 s to 0.2 s (so a late observation does not drag the face along slowly).
- When the next observation is late, it stops at the newest observation (no prediction ahead, because the correction when a prediction is wrong is noticeable).
- Step 4 uses this for the face (expressions, gaze, head turn; `FaceMotion`). The body and hands use the same idea in steps 5 and 6.

### 7.4 Expressions (ExpressionMapping)

Conversion from expression values (`FaceShape`, 0–1) to the avatar's expressions (`AvatarExpression`) and gaze (`ExpressionMapping`). Each value has a response curve (`ExpressionCurve`: 0 at or below the lower end, the most at or above the upper end, rising smoothly between). MediaPipe's values rarely reach 1, so the upper ends are below 1.

| Expression | Source values | How (including Voom's knowledge) |
|---|---|---|
| `Blink` (both), `BlinkLeft`, `BlinkRight` | `EyeBlinkLeft/Right` | Both eyes together. One eye (a wink) only when the eyes differ by 0.3 or more after the curve, because closing one eye squints the other a little, so a wink never reads as one eye alone |
| `Aa` | `JawOpen` | Mouth opening; the `MouthFunnel` share goes to `Oh` |
| `Oh` | `JawOpen` and `MouthFunnel` | Rounded and open |
| `Ou` | `MouthPucker`, `MouthFunnel` | Pursed lips, as far as the mouth is open |
| `Ee` | `MouthStretchLeft/Right` | Stretched sideways, as far as the mouth is open |
| `Ih` | `MouthSmileLeft/Right` | Pulled sideways and slightly open (half the smile), as far as the mouth is open |
| `Happy` | `MouthSmileLeft/Right`, `CheekSquintLeft/Right` | The larger; at most 0.8 |
| `Surprised` | `BrowInnerUp`, `EyeWideLeft/Right` | Raised brows alone give half, wide eyes the rest; at most 0.8 |
| `Angry` | `BrowDownLeft/Right` | At most 0.8 |
| `Sad` | `BrowInnerUp` and `MouthFrownLeft/Right` | When both show; at most 0.8 |
| Gaze | `EyeLookIn/Out/Up/Down` (mean of both eyes) | A share of the model's eye range (-1 to 1), reaching the end at a value of 0.35. Up and down are weakened as far as the lids are closed (a closing lid reads as looking down; Voom also stopped the up-and-down movement by default) |

- Whether the mouth is open (`JawOpen` from 0.04 to 0.12) gates `Ou`, `Ee`, and `Ih`: a closed mouth shows none of them. Lips at rest give `MouthPucker` and `MouthSmile` values of about 0.1–0.3, which showed a small open "u" on a closed mouth (2026-10-21).
- The five mouth shapes are distributed by ratio so their sum does not exceed 1.
- VRM has no brow expressions, so the brows show through the feelings (surprise, anger, sadness).
- How feelings overlap blinks, the mouth, and the gaze (VRM 1.0 `overrideBlink`, `overrideMouth`, `overrideLookAt`) follows the model's settings and is applied by UniVRM.
- The gaze is given as a share, not an angle (5.2). VRM divides a look angle by its range (90° on the sample models) to turn the eyes, so the angles first given (±12°) moved the eyes only about an eighth of their range (2026-10-20).
- The defaults come from camera checks. The first values (2026-10-20) moved the eyes, brows, and mouth too little and were raised; then the values the user tuned (eyes 70%, brows 120%, mouth 70%, others 100%) became the new defaults (100%) (2026-10-21).
- The weights actually set (after substituting blinks) are the Expression Signal (a 0–1 value per expression; `ExpressionController.Signal`). v0.1.0 has no consumer (Extension Motion) yet (system design 7.7).
- For a model with only a blink of both eyes (`Blink`), the per-eye values go to it; for one with only per-eye blinks, the both-eye value goes to each eye. The substitution uses the smaller value so one closed eye does not close both.

#### Face Tuning (FaceTuning)

How much a face moves differs from person to person, so the user can change the sensitivity of each part (user's request, 2026-10-20).

| Part | What changes |
|---|---|
| Eye closing | The blink curve |
| Winking | The difference between the eyes that counts as a wink |
| Brows | The curves of the brows, wide eyes, and the frown (surprise, anger, sadness) |
| Mouth | The curves of the mouth opening and shapes |
| Gaze | The value at which the eyes reach the end of their range |
| Smile | The smile curve |

- Each value is a sensitivity (0.5 to 3, shown as 50 to 300%) that divides both ends of the curves: 2 gives the same expression for half the movement. A value outside the range (such as a hand-edited file) is read as the nearest end.
- The tuning belongs to the user and stays when the avatar changes (user's decision, 2026-10-20). It is saved in `avatar-settings.json` (chapter 10).
- A change applies to the avatar in use at once (from the next observation). The tuning screen is UI `setup.md` chapter 9.
- `IAvatarExpressions.CustomNames` exists so a method passing values straight through can be added later for models that have their own 52 ARKit expressions (Perfect Sync) (chapter 17).

### 7.5 Fingers

- The flexion of each finger joint comes from the angle between neighboring bones (Tracking detailed design 8.4). Flexion is limited to 0–90° (the thumb has its own range) and spreading to ±15°.
- When a hand is lost, it slowly returns to a lightly closed natural shape.
- When a lost hand returns, it moves toward the observation over 0.2 s so the shape does not change suddenly.

---

## 8. State / Lifecycle

### 8.1 AvatarService

- An optional application service, started after Logging, Video, and Stage (not created without Video and Stage). Without Tracking, the avatar is shown in the reference pose.
- At startup it builds the list and starts loading the avatar in use from `avatar-settings.json`. Startup goes on without waiting for the load (not blocking the main thread for long, chapter 9). If the avatar in use is no longer registered, no avatar is in use.
- At shutdown it destroys the avatar's GameObjects, textures, meshes, and materials (using the disposal of UniVRM's `RuntimeGltfInstance`).

### 8.2 Loading

1. `Loading`. `VrmAvatarLoader` reads the file and builds it with UniVRM.
2. Check the rig and expressions. If a required bone (Hips, Spine, Head, left and right upper arms, forearms, hands) is missing, `Error` (`MissingBones`). These are the upper-body bones VRM 1.0 requires. Chest, Upper Chest, and Neck are optional in VRM 1.0, so when one is missing its rotation goes to the nearest bone.
3. Set the layer to `Avatar` and place it at the spawn point (6.5).
4. `Ready`. Destroy the old avatar, if any.

- If another avatar is chosen while one is loading, the earlier load is canceled. When a swap fails, the old avatar stays `Ready` and `Problem` says what failed.

### 8.3 Per-frame Application

- Before the Avatar Camera renders, in this order (equivalent of `LateUpdate`, after `IFrameLoop`):
  1. Read `ITrackingSource.Current`. Tracking registers with the per-frame work (`IFrameLoop`) before Avatar, so it reads the observation updated in the same frame.
  2. `AvatarController`: the face (`FaceMotion`: interpolation between observations, handling a lost face, `ExpressionMapping`) and the neck and head turn. The body and arms (`PoseController`: interpolation, returning lost parts, `PoseRetargeting`) are added in step 5.
  3. `ExpressionController`: expressions and gaze.
  4. Call UniVRM's processing (applying the control rig, expressions, LookAt, SpringBone) explicitly once. UniVRM's automatic update is turned off and the order is decided here (so SpringBone runs after the pose).
- Nothing is allocated per frame. The bone rotation and expression value arrays are reused.

### 8.4 When Tracking Is Lost

| Status | Handling |
|---|---|
| `Tracked` | Apply the observation |
| `LowConfidence` | Blend between the previous value and the observation by confidence |
| `Lost` (within 0.5 s) | Keep the last value |
| `Lost` (over 0.5 s) | Return over 1 s to the reference pose (arms down, hands lightly closed, no expression) |
| On return | Move toward the observation over 0.2 s |
| `Disabled`, `NotSupported` | Reference pose |

- For the face it was built as follows (step 4, `FaceMotion`): the time since the face was lost counts from the last usable observation. A presence (0 to 1) moves to 1 over 0.2 s while the face is found, and to 0 over 1 s once it has been lost for 0.5 s; the face is blended with a neutral face (no expression, eyes ahead, head straight) by it. For `LowConfidence`, a new observation is moved toward from the previous one only as far as its confidence. Without tracking, or while it is stopped, the face is neutral.

---

## 9. Threading / async

- Building the avatar (GameObjects, meshes, textures) uses Unity APIs and happens on the main thread. Reading the file and parsing glTF use UniVRM's asynchronous loading (`Vrm10.LoadBytesAsync` and so on, splitting the work over several frames with `IAwaitCaller`), so no single frame is blocked for long. The loading time and the longest frame are recorded.
- Copying, hashing, and writing the thumbnail happen on another thread.
- Pose and expression application happens on the main thread once per frame.

---

## 10. Settings

```csharp
public sealed class AvatarSettings
{
    public string CurrentAvatarId { get; set; }   // No avatar is shown when null
    public FaceTuning Face { get; set; }          // Face tuning (7.4); the user's
}
```

- Saved to `avatar-settings.json` (settings folder, `ConfigFileStore`). Moves to Project / Profile once those exist.
- Per-avatar settings are in `avatar-profile.json` (6.3).
- An older file without `Face` reads as the default tuning (a field was only added, so the version stays).
- Mirroring is a Tracking setting (6.6).

---

## 11. Logging and Diagnostics

- Registration, loading, swapping, and failures are logged at Information with the avatar id, format, version, and loading time. Missing bones and expressions are logged at Warning (system design 7.13). Full file paths are not logged (they contain the user name); only file names are.
- Performance metrics:

| Metric | Kind | Content |
|---|---|---|
| `Avatar/Load` | Timer | Loading time |
| `Avatar/LoadLongestFrame` | Timer | The longest frame while loading |
| `Avatar/Apply` | Timer | Per-frame pose and expression application time |

- Problems shown to the user (`AvatarProblem`): `NotVrm` (this is not a VRM file), `TooLarge`, `MissingBones` (this model cannot be used because its humanoid bones are incomplete), `LoadFailed` (the avatar could not be loaded), `AlreadyImported`, `StorageFailed` (could not save; check free space at the save location).

---

## 12. Error Handling and Recovery

| Event | Handling |
|---|---|
| Registration fails midway | Delete the temporary folder; the list does not change |
| The avatar in use cannot be loaded (at startup) | `Error`. Continue without an avatar; another avatar can be chosen |
| Loading for a swap fails | Keep the old avatar |
| Missing bones | Unusable if a required bone (8.2) is missing. For optional bones such as fingers, only that bone's application stops |
| Missing expressions | Only that expression's application stops |
| No Tracking, or it is stopped | Show the reference pose |
| UniVRM exceptions | Caught in the loader and turned into `LoadFailed`. The application keeps running |

---

## 13. Security and Privacy

- Model files are untrusted input brought in from outside. The extension, size limit, and format magic are checked before parsing. Parsing is done by UniVRM; there is no mechanism to run scripts contained in a model.
- Parsing exceptions are caught in the loader, and objects created midway are destroyed.
- Registered models stay only in the user's Data Root and are not sent anywhere.
- The model license (6.4) is shown to the user. The user judges the conditions.
- Only `https` license URLs are opened, and only by the user's action.

---

## 14. Testing Strategy

| Kind | Target |
|---|---|
| EditMode | `PoseRetargeting`: from synthetic observations of known poses (arm 90° to the side, 90° forward, bent elbow, turned palm, tilted torso), the bone rotations are as expected. Mirroring. Joint limits |
| EditMode | Interpolation (7.3), returning when lost (8.4), blending for `LowConfidence` |
| EditMode | `ExpressionMapping`: response curves, keeping eyes together, distributing mouth shapes, overlap with emotion expressions |
| EditMode | `AvatarImporter`: checking extension, size, and magic; cleaning up the temporary folder; detecting the same hash; reading and writing the profile (keeping unknown fields) |
| EditMode | `AvatarController` driven by fake `IAvatarRig` and `IAvatarExpressions` |
| PlayMode | Loading a real VRM: bones and expressions are present, and destroying leaves nothing behind. Uses a small test model that can be committed, if one exists; otherwise the sample from the user (outside the repository) with `[Explicit]` |
| Hardware | Camera, MediaPipe, and avatar connected and checked visually (demo branch) |
| Performance | Loading time and the longest frame; per-frame application time |

Tests made in step 4: `AvatarFaceTests` (EditMode: response curves, both eyes and winks, the mouth's shares and a closed mouth, feelings, gaze, tuning, mirroring and limiting the head, interpolation and a lost face, substituting blinks, `AvatarController`), `AvatarServiceTests` (the head turn from tracking, the mirroring option, applying and saving tuning), `VrmLookTests` (from a share of the eye range to angles), `VrmLoaderTests.Face_TurnsTheHead_MovesTheFace_AndAppliesWithinItsBudget` (head, expressions, gaze, and application time on real models). UI: `AvatarCardPanelTests` (the face tuning Card) and `LiveSetupPanelTests` (movement speed).

Tests made in step 3: `AvatarCoreTests`, `AvatarImportTests`, `AvatarServiceTests` (EditMode, fake loader), `VrmLoaderTests` (PlayMode, `[Explicit]`; samples are read from the folder in the `VV_SAMPLES_DIR` environment variable, else `samples` next to the repository). The setup screen's Card: `AvatarCardPanelTests` (UI PlayMode).

---

## 15. Performance

- Per-frame application (`Avatar/Apply`) targets 0.3 ms or less (measured on the development PC and recorded here).
- A single frame while loading targets 50 ms or less, so switching avatars during a stream does not visibly freeze the image.
- SpringBone load depends on the model. It is measured and recorded.
- Measured (2026-10-20, development PC, `VrmLoaderTests`): the two VRM 1.0 samples (14 MB, 17 MB) loaded in 0.33–0.37 s, and the VRM 0.x sample (15 MB) in 0.58 s including the conversion to 1.0. The longest frame while loading was 46–47 ms for each. Per-frame application (`Avatar/Apply`) was measured in step 4.
- Measured (2026-10-20, development PC, `VrmLoaderTests`): per-frame application, the face computation and UniVRM's processing (SpringBone included) together, took a median of 0.031–0.033 ms, a 95th percentile of 0.035–0.038 ms, and at most 0.044–0.146 ms on the three samples, within the 0.3 ms target.

---

## 16. Directory / Namespace and Dependencies

```text
Assets/VirtualVessel/Avatar/
├─ Runtime/                                   VirtualVessel.Avatar
│  ├─ IAvatarService.cs     IAvatarService, AvatarState, AvatarPlacement
│  ├─ AvatarService.cs      AvatarService
│  ├─ Core/                 IAvatarModel, IAvatarRig, IAvatarExpressions, AvatarBone, AvatarExpression,
│  │                        AvatarProfile, AvatarSettings, AvatarEntry, AvatarLicense, AvatarModelInfo
│  ├─ Loading/              IAvatarLoader, AvatarProblem, AvatarFileCheck, AvatarImporter, AvatarStore
│  ├─ Pose/                 AvatarRestPose, HeadPose (PoseRetargeting, PoseController in step 5)
│  ├─ Expression/           ExpressionCurve, ExpressionMapping, ExpressionController (Expression Signal), FaceTarget, FaceTuning
│  └─ Runtime/              AvatarController, FaceMotion
├─ Vrm/                                       VirtualVessel.Avatar.Vrm (the only assembly that references UniVRM)
│  ├─ VrmAvatarLoader.cs
│  ├─ VrmAvatarModel.cs     IAvatarModel; turns UniVRM's own update off and runs it in Apply
│  ├─ VrmAvatarRig.cs       IAvatarRig on the control rig
│  ├─ VrmAvatarExpressions.cs
│  └─ VrmLicense.cs         VRM meta to AvatarLicense (pure functions)
└─ Tests/
   ├─ EditMode/
   └─ PlayMode/
```

- References: Core, Diagnostics, ProjectData, Tracking (from step 4). The VRM assembly references Avatar and UniVRM (`UniGLTF`, `UniGLTF.Utils`, `UniHumanoid`, `VRM10`, `VrmLib`). The composition root (Application) creates `VrmAvatarLoader`.
- UniVRM was added by UPM Git URL with a pinned version tag (v0.131.4: `https://github.com/vrm-c/UniVRM.git?path=/Packages/UniGLTF#v0.131.4` and `...?path=/Packages/VRM10#v0.131.4`). `packages-lock.json` pins the commit (`efd20f6`). It was confirmed to run on Unity 6.6. The license is MIT. The SBOM records `com.vrmc.gltf`, `com.vrmc.vrm`, and the `com.unity.mathematics` they depend on. The repository checks read a Git package's version from its tag and fail for one without a tag.
- UniVRM finds the shaders of run-time loaded models' materials by name, so MToon 1.0 (URP), UniUnlit, and URP Lit are in the Graphics Settings' always-included shaders. The release package's licenses include UniVRM's `LICENSE.txt` (`tools/release/licenses`) and UniGLTF's `LICENSE.md`.

---

## 17. Open Issues

| Item | Plan |
|---|---|
| Whether to accept VRM 0.x models | Decided: accepted by converting to 1.0 at load (6.8, system design 7.3; user's decision, 2026-10-18) |
| A VRM model for tests | Checked with the user's samples (outside the repository, in `E:\VirtualVessel\samples\`; two VRM 1.0 models and one 0.x model). After conversion, the 0.x model passed the tests of bones, root-space rotation, expressions, and disposal. If a small model with a license that allows committing exists, use it for automated tests |
| Models without a thumbnail | "No image" is shown for now. Capturing one at load is considered if wanted |
| How models of different heights are framed | The default camera position is fixed (Video detailed design 7.5). If the upper body of a much taller or shorter model does not fit, fitting to the head height is considered with the Camera Point design |
| Position matching such as touching the face (IK) | Considered together with full body |
| Perfect Sync (models with the 52 ARKit expressions) | After v0.1.0 |
| Puffing the cheeks | MediaPipe's `CheekPuff` hardly responds, and VRM has no standard expression for it. The three samples (VRoid) have neither such an expression nor a blend shape for it. Considered together with Perfect Sync (user's request, 2026-10-21) |
| The cropped video of the face tuning Card | A fixed part (where the camera's default position puts the face and chest). If the face of an avatar of a very different height falls outside, choosing the part from the head's position is considered with Camera Points |
| Expression defaults | Decided: the values the user tuned on a camera became the defaults (7.4, 2026-10-21). Someone whose face moves differently changes them on the tuning screen |
| FBX | Starting with the choice of library. After v0.1.0 |
| Screens for adjusting the spawn point and camera position | When designing Stage Scenes and Camera Points |
| Moving settings to Project / Profile | When designing Project / Profile |

---

## 18. Implementation Order

Tracking and Avatar proceed together in this order. Each step is one branch with its tests and a demo branch to try it (CLAUDE.md 29.2). v0.1.0 is released after step 7.

1. Tracking foundation (Tracking detailed design chapter 18, item 1)
2. MediaPipe and the camera (Tracking detailed design chapter 18, item 2)
3. Loading avatars: adding UniVRM (SBOM), registration, Data Root, `IFileDialog`, Video's avatar layer and Avatar Camera, the default spawn point, the avatar card on the setup screen
4. Face: expressions, gaze, head orientation, including the face tuning screen and ten levels of movement speed (Tracking detailed design 8.2)
5. Upper body: torso, shoulders, arms. Comparing depth estimation (Tracking detailed design 8.4)
6. Fingers
7. Tracking settings screen (range choice with "full body: coming soon", smoothing levels, mirroring, reference pose), finishing diagnostics
