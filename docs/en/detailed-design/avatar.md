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
    Ctrl --> Signal["Expression Signal<br/>(model-independent; Extension Motion comes later)"]
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
| `IAvatarFaceShapes` | Interface | The model's own face shapes (blend shapes) by name, and their weights (7.4) |
| `FacePart` | Enum | The parts moved one by one: eyes (closing, wide, angry), brows, and mouth (a, i, u, e, o, closed smile, open smile, frown) |
| `FacePartProfile` | Class | Which of a model's shape names is which part (VRoid and so on); decides whether a model fits (pure functions) |
| `AvatarBone` | Enum | Standard human bones (same order as Unity Humanoid) |
| `AvatarExpression` | Enum | Standard expressions: `Blink`, `BlinkLeft`, `BlinkRight`, `Aa`, `Ih`, `Ou`, `Ee`, `Oh`, `Happy`, `Angry`, `Sad`, `Relaxed`, `Surprised`, `Neutral` |
| `AvatarController` | Class | The control entry point for one avatar; dispatches to pose and expressions |
| `PoseRetargeting` | Static class | Computes the upper body's bone rotations from observations (pure functions, 7.2) |
| `BodyMotion` | Class | Interpolation between upper body observations, and handling a lost torso or arm, each on its own (7.2, 7.3, 8.4). First designed as `PoseController`; named after the face's `FaceMotion` |
| `BodyTarget` | Class | What the avatar's upper body shows: each bone's rotation, and which parts the observation saw. Reused every frame |
| `MotionTiming` | Static class | The timing the face, the body, and the hands share: moving toward observations, and how much is shown when lost (pure functions, 7.3, 8.4) |
| `ObservationClock` | Class | One part's observations over time: when the newest was taken and arrived, and how far along the move toward it is (7.3). The face, the body, and each hand hold one. `FrameClock` is the seconds since the last frame alone, for the steps after them |
| `ExpressionMapping` | Class | Settings for converting expression values (`FaceShape`) to expressions (7.4) |
| `FaceFeatures` | Struct (internal) | The face values put through the curves once. The whole-face expressions, the part values, and the Expression Signal are all made from it (7.4) |
| `ExpressionController` | Class | Applies expressions and gaze: part by part or as whole-face expressions, substituting blinks the model lacks |
| `FaceTarget` | Class | What the avatar's face shows: a weight per expression, a weight per part, the Expression Signal, the gaze, and the head's turn. Reused every frame |
| `ExpressionSignal` | Enum | The values of the Expression Signal: smile, anger, sadness, surprise, each eye's opening, and the mouth's opening (7.4, system design 7.7) |
| `FaceMotion` | Class | Interpolation between face observations and handling a lost face (7.3, 8.4; timing in `ObservationClock`) |
| `HeadPose` | Static class | The head's turn: mirroring, the limit, and the neck's share (pure functions, 7.1) |
| `AvatarBodyShape` | Class | The model's body in the reference pose: bone positions, the head's ellipsoid, the torso's parts, the arms' thickness. Measured at load (7.6) |
| `ArmClearance` | Static class | Keeps the arms out of the head and torso by turning them in depth (pure functions, 7.6) |
| `HandRetargeting` | Static class | The hand's turn and the forearm's twist from the palm (pure functions, 7.7) |
| `HandMotion` | Class | Interpolating between palm and finger observations, and handling a lost hand (7.5, 7.7) |
| `FingerPose` | Class | One hand's finger angles: the bend at each joint and the spread. Reused every frame (7.5) |
| `FingerRetargeting` | Static class | Finger angles from the tracked hand, and the finger bones' rotations from them (pure functions, 7.5). `FingerRest` holds the model's hand in the reference pose |
| `SteadyMotion` | Class | The upper body's last step: how fast the arm bones may turn (7.8) |
| `ClearanceMotion` | Class | Keeping the arms out over time: remembers each limb's side and eases the added turn in and out (7.6) |
| `PlacedPart` | Struct | The head (an ellipsoid) or a part of the torso (an elliptic cylinder) this frame, and whether an arm's capsule reaches into it (pure functions, 7.6) |
| `ArmSide` | Struct | Each of the avatar's arms: its bones (`UpperBodyBone`, `AvatarBone`) and the tracked joints of the same name. Keeps choosing bones by side in one place |
| `ExpressionCurve` | Struct | The response curve from a value to an expression weight (7.4) |
| `FaceTuning` | Class | The user's face tuning: a sensitivity per part (7.4) |
| `AvatarProfile` | Class | Saved usage settings (6.3) |
| `AvatarSettings` | Class | The avatar in use and the face tuning (chapter 10) |

### Class Diagram (the face and the upper body, as of step 5)

The classes from a tracking observation to the avatar's upper body (torso, shoulders, arms), head, expressions, and gaze. Hands and fingers come in step 6.

- There are two kinds of abstraction, shown by color and by a stereotype. **Interfaces** (`<<interface>>`, a thick solid purple border) hide the concrete implementations at module boundaries. **Data contracts** (`<<contract>>`, a dashed green border) carry values in a form independent of formats and libraries. Classes without a stereotype are concrete processing on top of the abstractions.
- Every dependency points from the concrete to the abstract. Avatar's computation knows neither MediaPipe's types nor UniVRM's, and UniVRM's types are used only inside Avatar.Vrm (chapter 3).
- Relation notes: `reads` is the read every frame, and `upper body, neck, head` sets the upper body's, neck's, and head's rotations. The per-frame order is in 8.3.

```mermaid
classDiagram
    direction LR

    namespace Tracking {
        class ITrackingSource {
            <<interface>>
            +TrackingFrame Current
            +IReadOnlyObservable~TrackingOptions~ Options
        }
        class TrackingFrame {
            <<contract>>
            +double Time
            +FacePose Face
            +BodyPose Body
        }
        class BodyPose {
            <<contract>>
            +Vector3[20] Positions
            +float[20] JointConfidence
            +TrackingStatus Status
        }
        class FacePose {
            <<contract>>
            +float[52] Shapes
            +Quaternion HeadRotation
            +TrackingStatus Status
            +double Timestamp
        }
    }

    namespace Avatar {
        class IAvatarService {
            <<interface>>
            +IReadOnlyObservable~FaceTuning~ FaceTuning
            +FaceTarget Face
            +SetFaceTuning(FaceTuning tuning)
        }
        class AvatarService {
            -Tick()
            +Normalized(FaceTuning)$ FaceTuning
        }
        class AvatarController {
            +ExpressionMapping Mapping
            +Update(TrackingFrame frame, bool mirror, double now)
        }
        class FaceMotion {
            +FaceTarget Current
            +float Presence
            +Update(FacePose face, bool mirror, double now)
        }
        class MotionTiming {
            +Progress(now, arrivedAt, interval)$ float
            +NextPresence(presence, usable, lost, elapsed)$ float
            +Interval(fresh, observedAt, previous)$ double
        }
        class ObservationClock {
            +double ObservedAt
            +Tick(now) float
            +IsNew(timestamp) bool
            +Arrive(fresh, timestamp, now)
            +Progress(now) float
        }
        class BodyMotion {
            +BodyTarget Current
            +float TorsoPresence
            +Update(BodyPose body, bool mirror, double now)
        }
        class PoseRetargeting {
            +Retarget(BodyPose body, bool mirror, bool hasUpperChest, float armDown, BodyTarget into)$
            +Arm(left, shoulder, elbow, wrist, torso)$
            +WithoutWrist(left, upper, previousUpper, previousLower)$
        }
        class BodyTarget {
            <<contract>>
            +Quaternion[] Rotations
            +bool TorsoSeen, LeftArmSeen, RightArmSeen
        }
        class ExpressionMapping {
            +Evaluate(FacePose face, bool mirror, FaceTarget into)
            +For(FaceTuning tuning)$ ExpressionMapping
            ~Read(FacePose face, bool mirror) FaceFeatures
            ~WriteExpressions(FaceFeatures f, float ihFromSmile, FaceTarget into)$
            ~WriteParts(FaceFeatures f, float eyeAngryFromBrows, FaceTarget into)$
            ~WriteSignals(FaceFeatures f, FaceTarget into)$
        }
        class ExpressionCurve {
            +Evaluate(float value) float
            +Sensitive(float sensitivity) ExpressionCurve
        }
        class FaceTuning {
            +float Eyes, Wink, Brows, Mouth, Gaze, Smile
            +bool Winks
        }
        class FaceTarget {
            <<contract>>
            +float[] Weights
            +float[] Parts
            +float LookYaw
            +float LookPitch
            +Quaternion Head
            +float[] Signals
        }
        class HeadPose {
            +FromTracking(Quaternion tracked, bool mirror)$ Quaternion
            +Neck(Quaternion head)$ Quaternion
        }
        class ExpressionController {
            +FacePartProfile Profile
            +Apply(FaceTarget face)
        }
        class FacePartProfile {
            <<contract>>
            +VRoid$ FacePartProfile
            +Resolve(IReadOnlyList~string~ names) int[]
        }
        class IAvatarModel {
            <<interface>>
            +IAvatarRig Rig
            +IAvatarExpressions Expressions
            +IAvatarFaceShapes FaceShapes
            +AvatarBodyShape BodyShape
            +Apply()
        }
        class IAvatarRig {
            <<interface>>
            +SetRotation(AvatarBone bone, Quaternion rotation)
        }
        class IAvatarExpressions {
            <<interface>>
            +SetWeight(AvatarExpression expression, float weight)
            +SetLook(float yaw, float pitch)
        }
        class IAvatarFaceShapes {
            <<interface>>
            +IReadOnlyList~string~ Names
            +SetWeight(int index, float weight)
        }
    }

    namespace AvatarVrm {
        class VrmAvatarModel
        class VrmAvatarRig
        class VrmAvatarExpressions
        class VrmAvatarFaceShapes
    }

    namespace UI {
        class FaceTuningCard
    }

    classDef interfaceLayer fill:#7c6cf033,stroke:#7c6cf0,stroke-width:3px
    classDef contractLayer fill:#2aa58f26,stroke:#2aa58f,stroke-width:2px,stroke-dasharray:6 4
    cssClass "ITrackingSource,IAvatarService,IAvatarModel,IAvatarRig,IAvatarExpressions,IAvatarFaceShapes" interfaceLayer
    cssClass "TrackingFrame,FacePose,BodyPose,FaceTarget,BodyTarget,FacePartProfile" contractLayer

    TrackingFrame *-- FacePose
    ITrackingSource --> TrackingFrame
    AvatarService ..|> IAvatarService
    AvatarService --> ITrackingSource : reads
    AvatarService --> IAvatarModel : Apply()
    AvatarService *-- AvatarController
    AvatarService ..> ExpressionMapping : For(tuning)
    AvatarController *-- FaceMotion
    AvatarController *-- BodyMotion
    TrackingFrame *-- BodyPose
    BodyMotion --> PoseRetargeting : Retarget
    BodyMotion *-- BodyTarget
    BodyMotion *-- ObservationClock
    FaceMotion *-- ObservationClock
    ObservationClock ..> MotionTiming
    AvatarController *-- ExpressionController
    AvatarController --> IAvatarRig : upper body, neck, head
    AvatarController ..> HeadPose
    FaceMotion --> ExpressionMapping : Evaluate
    FaceMotion *-- FaceTarget
    FaceMotion ..> HeadPose
    ExpressionMapping *-- ExpressionCurve
    ExpressionMapping ..> FaceTuning
    ExpressionController --> IAvatarExpressions
    ExpressionController --> IAvatarFaceShapes
    ExpressionController ..> FacePartProfile
    IAvatarModel *-- IAvatarRig
    IAvatarModel *-- IAvatarExpressions
    IAvatarModel *-- IAvatarFaceShapes
    VrmAvatarModel ..|> IAvatarModel
    VrmAvatarRig ..|> IAvatarRig
    VrmAvatarExpressions ..|> IAvatarExpressions
    VrmAvatarFaceShapes ..|> IAvatarFaceShapes
    FaceTuningCard --> IAvatarService
```

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
    BodyTarget Body { get; }                                                   // The upper body this frame, for the skeleton comparison on the Diagnostics screen; null without an avatar
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

public interface IAvatarFaceShapes
{
    IReadOnlyList<string> Names { get; }                         // The model's face shape (blend shape) names
    void SetWeight(int index, float weight);                     // 0–1; Apply puts it on after the expressions
}
```

- For VRM, `IAvatarRig` is implemented with UniVRM's control rig (a normalized skeleton where every bone's rotation is identity in the reference pose). UniVRM absorbs per-model differences in bone axes (one bone's X axis points forward, another's Z axis, and so on). An FBX loader will do the same normalization inside the loader.
- Values given to `SetRotation` are applied together by `IAvatarModel.Apply`, parents first, so the order of calls does not matter.
- For VRM, `IAvatarExpressions` is implemented with VRM 1.0 expressions (presets) and LookAt.
- `IAvatarModel.FaceShapes` gives the model's face shapes (null when it has none). For VRM, the blend shapes of every SkinnedMeshRenderer are gathered by name and written after UniVRM's processing, so the part values win even where UniVRM's expressions move the same shapes.
- The gaze is given as a share of the model's eye range, not as an angle, because each model maps look angles onto its eyes over a different range (7.4). For VRM, the share is multiplied by the model's LookAt input range (sideways, up, and down) to make an angle.

```csharp
public interface IAvatarModel : IDisposable
{
    Transform Root { get; }            // Faces +Z in the reference pose
    IAvatarRig Rig { get; }
    IAvatarExpressions Expressions { get; }
    AvatarModelInfo Info { get; }      // Name, format, original version, whether converted, terms, thumbnail
    AvatarBodyShape BodyShape { get; } // The body measured at load (7.6); null when it could not be measured
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
| Forearm | Elbow to wrist | Half of the palm's orientation (from the Hand Landmarker) (7.7); carried over from the upper arm when the hand is not seen |
| Hand | Wrist to the base of the middle finger | The palm normal |
| Fingers | The flexion angle of each joint (7.5) | The palm |

- Neck and head (step 4, `HeadPose`): person space and the avatar's root space have the same axes (+X right, +Y up, +Z front), so the rotation is used as it is. Mirrored, it is reflected through the plane X = 0 (turning sideways and tilting go the other way; nodding stays the same). In root space the head gets the whole turn and the neck 40% of it, so the head turns the remaining 60% relative to the neck. Turns beyond 75° are stopped, because the face is then mostly out of the camera's view and the values are unreliable.
- Only rotations are applied, not positions. So even if the avatar's and the user's bodies differ (arm length and so on), the bone directions are the same (absorbing body-size differences, system design 7.6). Movements that need matching positions, such as touching the face with a hand, are considered later with IK (solving rotations from joint positions) together with full body (chapter 17).
- The computation is done by `PoseRetargeting` (pure functions): the input is the body and hands of `TrackingFrame`, mirroring, and profile corrections; the output is an array of rotations per bone (passed in by the caller, to avoid per-frame allocations).

### 7.2 Upper Body (BodyRange.UpperBody)

Built in step 5 (`PoseRetargeting`, `BodyMotion`). Every bone's rotation from the reference pose is worked out in the avatar root's space and set bone by bone (`IAvatarRig.SetRotation`). Only directions are used, never positions, so a person and an avatar of different builds point their bones alike (system design 7.6). The Voom prototype only tilted the chest by the shoulder line, so there is no method to carry over (chapter 1).

- The hips do not move. The torso's rotation takes its right from the line between the shoulders and its up from the line from the hips to the neck (the shoulders' midpoint), and is shared over Spine, Chest, and Upper Chest (30% at Spine, 65% up to Chest, all of it at Upper Chest: 30%, 35%, 35%), so the torso moves even when seated. On a model without an Upper Chest, Chest takes all of it.
- Torso rotation has limits (side tilt ±30°, forward/back ±25°, twist ±40°) so inference errors do not bend the torso unnaturally.
- The neck turns 40% of the way from the top of the torso (Upper Chest) to the head (7.1), so it stays joined to the head when the torso tilts.
- The upper arm points at the elbow. Its twist (the turn about the bone) is set by the plane the elbow bends in, the plane of the upper arm and the forearm. A nearly straight arm's bend plane is unknown, so the twist of the shortest swing from the torso is used, blended smoothly in between: from the torso below a sine of 0.25 between upper arm and forearm, from the bend plane at 0.50 and above. The bounds were 0.10–0.35 at first and were raised because arms brought near straight by the correction for arms pointing at the camera (tracking detailed design 8.4) jumped in twist (2026-10-22). The forearm points at the wrist and keeps the upper arm's twist; the palm's twist comes in step 6.
- A shoulder rises only for an arm raised above level, by 25% of how far it is raised (at most 20°).
- A joint not seen is not used (`BodyPose.IsSeen`, the same as Tracking's corrections: visibility at least 0.5 and inside the camera image, tracking detailed design 8.4). An arm or hand out of the camera is therefore lost like a hidden one: it holds, then goes back to the rest (8.4), rather than following MediaPipe's guess. Both shoulders seen move the torso; a shoulder and an elbow move the upper arm. The wrist only points the forearm.
- While the wrist is hidden (behind the head, for example), the upper arm points at the elbow and the forearm keeps its last seen bend (its rotation relative to the upper arm). The upper arm also keeps its previous twist and only swings to the new direction (`PoseRetargeting.WithoutWrist`): the bend plane is unknown, so a twist worked out afresh would jump. In the user's recording (2026-10-22, hands behind the head), the shoulders and elbows were seen 100% of the time but the wrists only 28%, and a test that needed the wrist dropped the arms 70% of the time.
- While the wrist is out of the camera image, with the elbow in it (hands lowered below the camera, for example), the forearm keeps its bend for 0.5 s, then goes back over 1 s to straight along the upper arm, the rest, and comes back over 0.2 s when the wrist is in the image again (`BodyMotion.LeftForearmPresence`, `RightForearmPresence`, 8.4; the user's decision, 2026-10-24). Kept, the bend from before stayed while the person's arm hung down: in the user's screen recording the avatar's elbow was bent 108° against the person's 40°. On "1024不自然解消用" (Otoha, 3D) the clearance also turned the arms on 9% of the frames instead of 28%, as bent forearms held across the body no longer touched it.
- When an arm is lost (its shoulder or elbow unseen), it returns to hanging from the torso (lowered 70° by default), never to the T pose. Setting the angle through the profile's `pose.armDownAngle` is not built yet (chapter 17).
- In a mirror, the person's sides are swapped and reflected through X = 0 before anything is worked out (`PoseRetargeting.Joint`): the person's right hand moves the avatar's left one. Visibility is read on the person's side.
- The lower body stays in the arms-down reference pose (a straight standing pose that looks natural whether seated or standing).
- On the user's three recordings (arms forward, hands clasped in front of the chest, hands behind the head; about 10 seconds each), no rotation was not a number and the arms moved 100% of the time. Before smoothing, the upper arm jumped more than 30° in one observation 1, 1, and 3 times in about 300 (`AvatarRecordingTests`, Explicit). Bone lengths varied by ±5–10%, the baseline for comparing depth estimation (tracking detailed design 8.4).

### 7.3 Interpolating Between Observations

- For each rendered frame, the shown values move toward the newest observation over one observation interval (rotations with spherical interpolation). Each move starts from what was shown when the observation arrived, so a frame always lies between the previous display and the newest observation and never jumps. This adds one observation interval of lag (about 33 ms at 30 per second) but removes the steps caused by rendering more often than inference.
- The move takes the time between the two observations, kept within 1/120 s to 0.2 s (so a late observation does not drag the face along slowly).
- When the next observation is late, it stops at the newest observation (no prediction ahead, because the correction when a prediction is wrong is noticeable).
- Used for the face (`FaceMotion`, step 4), the upper body (`BodyMotion`, step 5), and the palms (`HandMotion`, step 6). The timing is shared in `MotionTiming` (2026-10-22), and each part's bookkeeping of its observations in `ObservationClock` (2026-10-24). The clearance (7.6) and the arms' turning limit (7.8), which follow every frame rather than observations, use `FrameClock` for the time since the last frame.

### 7.4 Expressions (ExpressionMapping)

Conversion from expression values (`FaceShape`, 0–1) to the avatar's expressions (`AvatarExpression`) and gaze (`ExpressionMapping`). Each value has a response curve (`ExpressionCurve`: 0 at or below the lower end, the most at or above the upper end, rising smoothly between). MediaPipe's values rarely reach 1, so the upper ends are below 1.

- Each time, the face values are put through the curves once into `FaceFeatures` (the avatar's own sides). The whole-face expressions (`WriteExpressions`), the part values (`WriteParts`), and the Expression Signal (`WriteSignals`) are written from it. The three writers are pure functions that know no curves. Before, the whole-face expressions and the part values were computed separately, each putting the same brow and mouth values through the curves, so a tuning fix had to go into two places (2026-10-21).

| Expression | Source values | How (including Voom's knowledge) |
|---|---|---|
| `Blink` (both), `BlinkLeft`, `BlinkRight` | `EyeBlinkLeft/Right` | Both eyes together. Only when winks are allowed (face tuning, off by default), one eye (a wink) when the eyes differ by 0.3 or more after the curve, because closing one eye squints the other a little, so a wink never reads as one eye alone |
| `Aa` | `JawOpen` | Mouth opening; the `MouthFunnel` share goes to `Oh` |
| `Oh` | `JawOpen` and `MouthFunnel` | Rounded and open |
| `Ou` | `MouthPucker`, `MouthFunnel` | Pursed lips, as far as the mouth is open |
| `Ee` | `MouthStretchLeft/Right` | Stretched sideways, as far as the mouth is open |
| `Ih` | `MouthSmileLeft/Right` | Pulled sideways and slightly open (half the smile), as far as the mouth is open |
| `Happy` | `MouthSmileLeft/Right` less `MouthStretchLeft/Right`, `CheekSquintLeft/Right` | The larger; at most 0.8. Only the corners raised beyond the lips pulled sideways count as a smile |
| `Surprised` | `BrowInnerUp`, `EyeWideLeft/Right` | Raised brows alone give half, wide eyes the rest; at most 0.8 |
| `Angry` | `BrowDownLeft/Right` | At most 0.8 |
| `Sad` | `BrowInnerUp` and `MouthFrownLeft/Right` | When both show; at most 0.8 |
| Gaze | `EyeLookIn/Out/Up/Down` (mean of both eyes) | A share of the model's eye range (-1 to 1), reaching the end at a value of 0.5. Up and down are weakened as far as the lids are closed (a closing lid reads as looking down; Voom also stopped the up-and-down movement by default) |

- Whether the mouth is open (`JawOpen` from 0.04 to 0.12) gates `Ou`, `Ee`, and `Ih`: a closed mouth shows none of them. Lips at rest give `MouthPucker` and `MouthSmile` values of about 0.1–0.3, which showed a small open "u" on a closed mouth (2026-10-21).
- Saying "i" pulls the lips sideways and raises the corners a little, which showed a smile, and on models that close the eyes in their smile (VRoid and others) the eyes closed too (2026-10-21). So the smile is the corner value less the sideways pull. Raised cheeks (`CheekSquint`) still make a smile on their own.
- The five mouth shapes are distributed by ratio so their sum does not exceed 1.
- VRM has no brow expressions, so the brows show through the feelings (surprise, anger, sadness).
- How feelings overlap blinks, the mouth, and the gaze (VRM 1.0 `overrideBlink`, `overrideMouth`, `overrideLookAt`) follows the model's settings and is applied by UniVRM.
- The gaze is given as a share, not an angle (5.2). VRM divides a look angle by its range (90° on the sample models) to turn the eyes, so the angles first given (±12°) moved the eyes only about an eighth of their range (2026-10-20).
- The defaults come from camera checks. The first values (2026-10-20) moved the eyes, brows, and mouth too little and were raised; then the values the user tuned became the new defaults (100%) (2026-10-21: eyes 70%, brows 120%, mouth 70%, then eyes 110% and gaze 70% more, then eyes and brows 120% more, and last eyes 120% more).
- The Expression Signal (`FaceTarget.Signals`, 0 to 1 per `ExpressionSignal`) is what other control (Extension Motion for cat ears, tails, and so on) reacts to. It depends neither on the model's format nor on whether the face is moved part by part or as a whole (system design 7.7, 7.8). Its values are the feelings' weights (smile, anger, sadness, surprise), each eye's opening (1 when open; the avatar's own sides), and the mouth's opening. `ExpressionMapping` computes them before anything is fitted to the model (before substituting blinks, and before the whole-face expressions are set to 0 for a face moved part by part). They are interpolated like the other values and go back to neutral (eyes open, everything else 0) when the face is lost. v0.1.0 has no consumer (Extension Motion) yet.
  - In step 4, the weights set on the model (`ExpressionController.Signal`) were the Expression Signal. They changed with the model (substituted blinks, and all 0 for a face moved part by part) and held no continuous values for the eyes and mouth, so they were changed to follow system design 7.7 (2026-10-21).
- For a model with only a blink of both eyes (`Blink`), the per-eye values go to it; for one with only per-eye blinks, the both-eye value goes to each eye. The substitution uses the smaller value so one closed eye does not close both.

#### Moving the Face Part by Part (FacePart)

VRM's standard expressions are whole-face expressions, and on some models the smile (`happy`) closes the eyes and opens the mouth (VRoid's `Fcl_ALL_Joy`). So raising the corners with the mouth closed opened the mouth (2026-10-21). On a model with a shape per part, the eyes, brows, and mouth are moved separately (user's decision, 2026-10-21; Voom also moved VRoid's part shapes). The source values are MediaPipe's face values, as for the standard expressions.

- The face is moved part by part only when the model's shape names fit a known convention (`FacePartProfile`; for now VRoid's `Fcl_EYE_*`, `Fcl_BRW_*`, `Fcl_MTH_*`). A model without both eyes' closing and the five vowel shapes keeps the standard expressions.
- While moving part by part, the standard expressions stay at 0, so they do not add up into a broken face. The gaze moves by LookAt either way.
- Whether a model fits is logged when it loads.
- Whether a model fits is decided from its shape names each time it loads. System design 7.12 does expression recognition and mapping in Setup and saves them to the Avatar Profile. For now the user chooses nothing here, matching the names is cheap, and the model's file does not change after registration (system design 7.11), so the result is the same every time. It is saved to the Avatar Profile when a Setup screen lets the user choose the expression mapping (chapter 17).

| Part | VRoid shape | How |
|---|---|---|
| Eye closing (left, right) | `Fcl_EYE_Close_L/R` | As the standard blink (both eyes together, winks). A smile does not close them |
| Wide eyes | `Fcl_EYE_Spread` | `EyeWide` |
| Angry eyes | `Fcl_EYE_Angry` | Half the lowered brows |
| Raised brows | `Fcl_BRW_Surprised` | The larger of `BrowInnerUp` and `BrowOuterUp`, less the sad brows |
| Lowered brows | `Fcl_BRW_Angry` | `BrowDown` |
| Sad brows | `Fcl_BRW_Sorrow` | Inner brows raised with the corners down (the smaller of `BrowInnerUp` and `MouthFrown`) |
| a, o, u | `Fcl_MTH_A/O/U` | As the standard expressions; "a" gives way to the open smile as far as the corners rise |
| i, e | `Fcl_MTH_I/E` | Lips pulled sideways (`MouthStretch`): a little apart is "i", more open is "e" |
| Closed smile | `Fcl_MTH_Fun` | Raised corners × how closed the mouth is: a smile with the mouth closed |
| Open smile | `Fcl_MTH_Joy` | Raised corners × the opening |
| Frown | `Fcl_MTH_Sorrow` | `MouthFrown` |

- The open mouth shapes (a, i, u, e, o, open smile) are distributed by ratio so their sum does not exceed 1.
- The smiling eyes (`Fcl_EYE_Joy`) are not used: eyes closing into "^^" along with a smile were what looked unnatural (user's decision, 2026-10-21). The eyes move only with their own lids. A model without part shapes cannot separate the eyes from the mouth, so it uses the standard `Happy` expression.
- The six tuning parts (below) work on the part values alike. The bars of the tuning screen come from the part values.
- Models with the 52 ARKit shapes (Perfect Sync) can be added later with one more convention in the same mechanism (chapter 17).

#### Face Tuning (FaceTuning)

How much a face moves differs from person to person, so the user can change the sensitivity of each part (user's request, 2026-10-20).

| Part | What changes |
|---|---|
| Eye closing | The blink curve |
| Winks | Whether one eye may close alone (on or off, off by default) |
| Winking | The difference between the eyes that counts as a wink (only with winks on) |
| Brows | The curves of the brows, wide eyes, and the frown (surprise, anger, sadness) |
| Mouth | The curves of the mouth opening and shapes |
| Gaze | The value at which the eyes reach the end of their range |
| Smile | The smile curve |

- Winks are easily misread, for example when one eye narrows as the head turns, so the user chooses (user's decision, 2026-10-21). They are off by default, and both eyes always close together.
- Each value is a sensitivity (0.5 to 3, shown as 50 to 300%) that divides both ends of the curves: 2 gives the same expression for half the movement. A value outside the range (such as a hand-edited file) is read as the nearest end.
- The tuning belongs to the user and stays when the avatar changes (user's decision, 2026-10-20). It is saved in `avatar-settings.json` (chapter 10). The expression mapping and tracking corrections in system design 7.2's Avatar Profile are specific to a model. How much a person's face moves depends on the person, not the model, so it is not kept in the Avatar Profile.
- A change applies to the avatar in use at once (from the next observation). The tuning screen is UI `setup.md` chapter 9.
- `IAvatarExpressions.CustomNames` exists so a method passing values straight through can be added later for models that have their own 52 ARKit expressions (Perfect Sync) (chapter 17).

### 7.5 Fingers

- The flexion of each finger joint comes from the angle between neighboring bones (Tracking detailed design 8.4). Flexion is limited to 0–90° (the thumb has its own range) and spreading to ±15°.
- When a hand is lost, it slowly returns to a lightly closed natural shape.
- When a lost hand returns, it moves toward the observation over 0.2 s so the shape does not change suddenly.
- Built in step 6, part 2 (2026-10-24):
  - `FingerRetargeting.TryMeasure` (pure) reads the angles from the tracked hand's 21 points in the hand's own frame: the back of the hand as the palm's rotation takes it, and across toward the thumb from the little finger's base to the index finger's. They are angles only, so neither the hand's size nor the mirror changes them. A mirror only chooses which person's hand moves which avatar hand (7.7).
  - Four fingers: the bend at the base (0–90°, toward the palm from the metacarpal) and at the middle joint (0–100°), and the spread toward the thumb at the base (±15°). The spread fades as the finger bends, because a finger pointing into the palm shows no side to spread to.
  - The tracked hand reads its bends small. MediaPipe flattens depth, so fingers curled toward the camera lose most of their bend. On the user's recordings, the most curled hands read 60–84° at the base and 54–78° at the middle joint (medians per finger), where a fist bends about 90° and 100°. The most open hands still read 15–36° and 8–36°. The user saw the fingers bend too little (screen recording, 2026-10-24). So the bend shown is the bend read past 10° (`OpenBend`), scaled by 1.5 at the base (`BaseGain`) and 1.8 at the middle joint (`MiddleGain`).
  - The end joint bends two thirds as far as the middle one (`EndShare`, at most 80°), because it bends with it. The end joint read on its own was noise: an open index finger read a median of 42° there.
  - The thumb: the base (CMC) folds across the palm by as much as its metacarpal is closer to the index finger's than in an open hand (30°), up to 25°. On the user's recordings the angle was 11–52° (5–95%). Its middle (0–60°) and end (0–80°) joints bend by their angles past 10°, scaled by 1.5 (`ThumbGain`).
  - `FingerRest` holds each finger bone's direction, the back of the hand, and the direction toward the thumb, from the bone positions measured at load (`AvatarBodyShape`, 7.6). Where the model lacks a bone, it uses a typical hand: palm down, fingers out to the side, the thumb forward. `FingerRetargeting.Rotations` (pure) turns each joint about the model's own axes, into the palm for the four fingers and across and into it for the thumb, and carries the parent's turn on the hand's final rotation (after 7.6 and 7.8).
  - `HandMotion` moves the fingers with the palm on the same observation clock (7.3): it holds them while the hand is lost, then goes over 1 s to the loosely closed hand (`FingerPose.Relaxed`: bends of 15°, 20°, and 10°; the thumb 5°, 10°, and 10°). Without hand tracking the fingers stay loosely closed instead of the reference pose's straight fingers. A shown angle changes at most 600° a second (10° a frame at 60), a fist closing in about 0.15 s. Without this, a finger angle jumped up to 48° in a frame on the user's recording "1024不自然解消用". With it, no angle changed more than 10° in a frame, on any recording.
  - Checked on the three sample models (`VrmLoaderTests.Fingers_CloseTheModelsOwnHand`): in a fist, the model's own right index finger points into its palm (0° off), and loosely closed it is 75° off.

### 7.6 Keeping the Arms Out of the Head and Torso (ArmClearance)

Only directions are carried over (7.1), so on an avatar built differently from the user, the arms sink into the head or torso. Most anime-style models have heads big for their arms, so an arm raised beside or over the head enters it (the user's camera check, 2026-10-24). Arms folded in front of the chest enter the chest (the user's request, same day). Only when an arm would enter the head or torso is it turned, by the least angle that keeps it out (`ArmClearance`, pure functions). This is one way of absorbing body differences with model-specific information (system design 7.6).

- The body's shape (`AvatarBodyShape`): measured once right after loading, while the model is in the reference pose (8.2). It holds the upper-body bones' positions, the head's ellipsoid, the torso's parts, the thickness of the upper arm, forearm, and hand, and the hand's length.
  - Each mesh point is grouped by following the bone that moves it most up to the first humanoid bone. Sleeves go with the arms and hair and ears with the head, and points of the neck or shoulders never go with the chest. The eyes and jaw go with the head, the fingers with the hand.
  - The head is an ellipsoid around the head's points. Hair hanging below the neck is left out.
  - The torso is an upright elliptic cylinder around the points of each of the Spine, Chest, and Upper Chest bones, turning with its bone. A bone with fewer than 20 points is not measured.
  - For the head's and torso's sizes, the outermost 5% of points at each end of each axis are left out. On one sample, the top 5% of the head's points reached about 20 cm above the crown (ears and hair sticking up).
  - An arm's thickness is the median distance from the bone of the points along its middle (leaving out 20% at each end). An arm whose mesh is not found takes a share of the upper arm's length.
  - When it cannot be measured (no head mesh, for example), it is `null` and the arms stay as tracked.
- Contact: the arm is three capsules (lines with thickness around the bones) for the upper arm, forearm, and hand. Each is checked against the head or a part of the torso grown by that capsule's thickness. The hand's capsule lies along the hand. The upper arm is not checked against the torso: it starts at the torso's edge, where the two always meet.
- A part of the torso is grown by half a limb's thickness (`TorsoContactShare`), and counts as entered only across its front (+Z) or near its middle (within half its size); touching its sides or back does not count. A sleeve against the side of the body is cloth on cloth and looks right, and turning in depth could not take an arm out sideways anyway. In the user's screen recording (2026-10-24), an arm hanging at the side met the sides of a loose cardigan (measured as the torso) and the whole arm was swung forward 30–60°.
- Turning: only the depth (front and back) changes. The camera sees across and up well and depth poorly: tracking places an arm in front of the body too close to it, and one behind it too close as well (tracking detailed design 8.4). So an arm tracked through the head or chest is most often off only in depth. An arm turns within the plane containing it and the camera's axis (+Z), keeping the direction it points on the camera's image (seen from the front) as tracked.
  1. The upper arm turns about the shoulder when it enters the head, or when the elbow enters the torso. The forearm does not turn: it keeps its direction and moves forward or back with the elbow. Folded arms with the elbow in the chest bring the elbow forward.
  2. The forearm turns about the elbow when the forearm or hand still enters the head or torso. With the elbow beside the body and the forearm across the front of the torso, as with a hand on the belly, only the forearm comes forward. A version that turned the whole arm at the shoulder when the forearm and hand entered the torso swung it forward for a hand on the belly (22–60° on the three samples, reproduced); now the shoulder stays and the forearm comes 31–37° forward. On the user's "hands folded in front of the chest", the turn averaged 10–18° (15–33° before), 35–40° in 2D, with no jump over 5° in a frame.
- Front or back: when the limb's end (the elbow for the upper arm, the wrist for the forearm) is in front of the center of the part entered, the limb turns forward; when behind, back. The side chosen is kept until the end moves more than 5 cm to the other side (`ArmClearance.ClearanceSides`, carried from frame to frame by the caller). Only when that side cannot clear does the limb turn to the other side, which is then remembered. The least angle that clears is found by halving (12 times).
- 2D (tracking detailed design 8.6): the body has no depth to tell front from back by. At the user's request (2026-10-24), the arms always go in front of the body (`frontOnly`, passed by `AvatarService` from tracking's options): they turn only forward, whatever side was remembered. On the three samples, the elbow of folded arms came 14.5–20.5 cm in front of the shoulder. Without it, on the sample with the deepest torso it went 10.8 cm behind.
- Limits: up to 60°, and no closer than 5° to the camera's axis (past it, the limb would point the other way on the image). When neither side clears, the limb turns to the limit toward the first side. A head the shoulder is in (1), or a head or torso part the elbow is in (2), is left out, since no turn could take the arm out of it.
- Timing (`ClearanceMotion`): the turn added (the rotation from the tracked one) moves toward its target with a time constant of 0.08 s. The tracked motion of the arm is followed without delay; only the turn added eases in and out. Within 0.01° it settles exactly.
- History (2026-10-24): at first, the arm turned the least angle among away from the center, up, down, and to either side. In the user's screen recording, forearms folded in front of the face were turned up and ones in front of the chest down, unlike the tracking. The user's four recordings were replayed on the three samples and compared (`VrmRecordingTests.Body_OnTheRecordings_ClearanceMovesTheArmsSmoothly`, Explicit):
  - The least angle in any direction: nearly all of the turn showed on the image (4.5–22.9° on average).
  - Weighing turns that change the direction on the image 2–4 times: the deviation fell a little, but the choice flipped and the arm jumped up to 28–69° in a frame.
  - Depth only, choosing front or back by the least angle: no deviation on the image, but with the hands behind the head the side flipped and the arm jumped up to 90°.
  - The current method: 0.0–0.4° on the image. Jumps over 5° in a frame: none in six recordings, one or two in two (up to 8°), and six (up to 14°) for hideri-san with the hands behind the head. What stays inside is shallow, needing 0.4–4.3° more on average to come out (up to 42° at the moment the side changes).
- The head and the torso's parts move with the torso's rotation and the neck's and head's turn (`FaceTarget.Head`).
- `AvatarController` applies it to `BodyMotion`'s output every frame. `AvatarController.Body` (the avatar drawn in the skeleton comparison on the Diagnostics screen) is the pose after it.
- Known limits: in poses depth alone cannot clear, such as a forearm crossing over the head, the arm stays inside. When the side changes, the arm moves to the other side over 0.1–0.2 s. The arms are not kept out of each other, the hips, or the legs.

### 7.7 The Hands and the Forearms' Twist (HandRetargeting)

Step 6, part 1 (2026-10-24), for the elbow that seemed to bend the wrong way (chapter 17). Until then the forearm kept the upper arm's twist and the hand the forearm's direction, so the palm never turned.

- The palm (`HandPose.PalmRotation`: the fingers' direction and the back of the hand, in person space) becomes the hand's rotation in the avatar root's space. In the reference pose the palm is down (the back of the hand up) and the fingers point along the arm. In a mirror the person's other hand is used, reflected through X = 0.
- The hand takes the palm's whole turn (its depth is set from the forearm's in Tracking, tracking detailed design 8.4).
- The twist is measured from neutral: the back of the hand along the elbow's hinge axis (with the thumb up when the forearm points forward from an arm at the side). In the reference pose (arm out sideways, palm down, the elbow bending forward) palm down is neutral too. Supination (turning the palm up) is positive, and a reading more than 150° toward pronation is read as supination (palms are often turned up on an arm held out, rarely turned far down; folded in front of the chest, pronation read up to about 115° on the recordings).
- The forearm and wrist turn up to ±90° from neutral; a twist past that is left out (the shoulder is not turned). A version that also turned the shoulder to show the palm's whole turn was tried, but on straight arms it twisted the upper arm's sleeve far, looking less natural than the person's arm (the user's screen recording, 2026-10-24). So with an arm held out and the palm turned straight up, the avatar's goes as far as thumb up.
- The forearm's bone takes half of the forearm's twist (`ForearmTwistShare`), and the wrist the rest. On models without twist bones the forearm's bone turns the whole forearm mesh, so all of it at the forearm wrings the elbow, and all of it at the hand wrings the wrist.
- At the wrap of the reading (210° of supination, 150° of pronation) it could be either way round, so within 30° of it the twist fades smoothly back to neutral, and a reading crossing it does not jump.
- A hand bent at the wrist until its back lies along the forearm has no twist to read, and the reading scatters. When the back of the hand crosses the forearm (the sine of the angle between them) less than 0.2 the twist is not read, and from 0.5 it is read fully (smoothly in between).
- The shown palm turns no faster than 540° a second (`HandMotion.MaxDegreesPerSecond`). Even a palm read the other way round (accepted by Tracking when it lasts three times, tracking detailed design 8.3) turns over about a third of a second instead of flipping in a frame.
- In the user's screen recording (2026-10-24) a hand turned about 180° in a frame. Replaying the recordings in 3D and 2D, in 2D the hand turned up to 178° in a frame, the forearm 120°, and the upper arm 83° (in 3D up to 52°, 65°, and 44°). The causes were the forearm without depth in 2D, whose elbow bend turned over as it swung from up to down (fixed by leaning the wrist forward, tracking detailed design 8.6), and the two above. After the fixes: hands 31–52°, forearms 24–64°, upper arms 34–63°.
- The first version (2026-10-24) took palm down as the end of the forearm's reach, with 180° of supination and 60° of pronation. In the user's screen recording the wrists bent (the hand's depth disagreeing in Tracking), and in "hands folded in front of the chest" 52% of readings stuck at the end of pronation. Measured from neutral, the median there is −89° (palm down, the end of pronation's reach), and what is left out is mostly about 20° or less.
- The hands are inferred apart from the body, so their motion (`HandMotion`) has the face's timing (7.3, 8.4): interpolated between observations, held 0.5 s when lost, back to following the forearm over 1 s, and in again over 0.2 s when found.
- `AvatarController` turns the hands after the body's motion, then keeps the arms out of the head and torso (7.6). Turning the forearm at the elbow turns the hand with it, and the hand's capsule lies along the hand.
- Measured (2026-10-24, the user's recordings, `AvatarRecordingTests.TheElbow_OnEachRecording_BendsWhereAnArmCan`, Explicit):
  - The upper arm's twist set by the elbow's bend plane went past ±90° only in "hands behind the head" (82–83%). That pose really turns the shoulder far outward; turning the forearm's depth over put the hand in front of the head, so it is not a depth error. The elbow's look was put down to the forearm's and hand's twist.
  - Hands seen: "arms forward" 81–84%, "arms forward and sideways" 35–61%, "hands folded in front of the chest" 6–58%, "hands behind the head" 5–10%.
  - The palm's twist (from neutral, supination positive, after the hand's depth correction): a median of 24–27° in "arms forward", 0% past the forearm's reach. On the avatar's left in "arms forward and sideways", 44% went past the forearm's reach (palm up on a straight arm). On the avatar's right in "hands folded in front of the chest", a median of −89°, with more than 10° left out in 51%. The hands' confidence was about 0.9 throughout, so it cannot tell errors apart.
- Fingers (7.5) come in step 6, part 2.

### 7.8 How Fast the Arms Turn (SteadyMotion)

At the last step of the upper body, an arm bone (upper arm, forearm, hand) turns where it points no faster than 720° a second (12° a frame at 60 frames a second), and twists about itself no faster than 360° a second (6° a frame). The torso is shown as it is, without a limit. Steadiness and natural motion come before following every turn (the user's decision, 2026-10-24).

- Measured on the user's recording "1024不自然解消用" (about 60 s, their own avatar Otoha, 3D, movement speed 1, mirrored), replayed at 60 frames a second (`VrmRecordingTests.Body_OnTheRecordings_MovesSteadily`, Explicit). Before the limit, the upper arm turned up to 51°, the forearm 52°, and the hand 129° in a frame, with 18–58 frames past 15° per bone. Most of it was twist (for example 48° on the upper arm, 13° of it in where it points): the upper arm's twist set by the elbow's bend plane turns fast on a nearly straight arm. It was already in tracking's values (after smoothing), not added by turning the hands or keeping the arms out of the head and torso.
- With the limit, on all five recordings no arm bone turns more than 13° in a frame, with no frame past 15°. The shake (how much the turn changes from frame to frame) fell by about 40% (for example the right forearm from 2.10° to 1.29°). Fast movements lag behind only while they last.
- Where the bone points and its twist are limited apart (`SteadyMotion.Limit`, a pure function). The twist changes mostly from noise, so its limit is lower.

---

## 8. State / Lifecycle

### 8.1 AvatarService

- An optional application service, started after Logging, Video, and Stage (not created without Video and Stage). Without Tracking, the avatar is shown in the reference pose.
- At startup it builds the list and starts loading the avatar in use from `avatar-settings.json`. Startup goes on without waiting for the load (not blocking the main thread for long, chapter 9). If the avatar in use is no longer registered, no avatar is in use.
- At shutdown it destroys the avatar's GameObjects, textures, meshes, and materials (using the disposal of UniVRM's `RuntimeGltfInstance`).

### 8.2 Loading

1. `Loading`. `VrmAvatarLoader` reads the file and builds it with UniVRM.
2. Check the rig and expressions. If a required bone (Hips, Spine, Head, left and right upper arms, forearms, hands) is missing, `Error` (`MissingBones`). These are the upper-body bones VRM 1.0 requires. Chest, Upper Chest, and Neck are optional in VRM 1.0, so when one is missing its rotation goes to the nearest bone. While the model is in the reference pose, its body shape (`AvatarBodyShape`, 7.6) is measured.
3. Set the layer to `Avatar` and place it at the spawn point (6.5).
4. `Ready`. Destroy the old avatar, if any.

- If another avatar is chosen while one is loading, the earlier load is canceled. When a swap fails, the old avatar stays `Ready` and `Problem` says what failed.

### 8.3 Per-frame Application

- Before the Avatar Camera renders, in this order (equivalent of `LateUpdate`, after `IFrameLoop`):
  1. Read `ITrackingSource.Current`. Tracking registers with the per-frame work (`IFrameLoop`) before Avatar, so it reads the observation updated in the same frame.
  2. `AvatarController`: the face (`FaceMotion`: interpolation between observations, handling a lost face, `ExpressionMapping`) and the neck and head turn. The upper body (`BodyMotion`: interpolation, returning lost parts, `PoseRetargeting`) is set on the bones, and the neck turns from the top of the torso toward the head (step 5). Before they are set on the bones, the arms are kept out of the head and torso (`ArmClearance`, 7.6).
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
    public FaceTuning Face { get; set; }          // Face tuning (7.4): sensitivity per part and whether winks are allowed; the user's
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
| EditMode | `PoseRetargeting`: from synthetic observations of known poses (arms sideways, down, forward, the elbow bent forward or up, a tilted torso), the bone rotations are as expected. Mirroring, the torso's limits, how shoulders rise, unseen arms. `BodyMotion`: a lost arm holds, then hangs, and comes back; a hidden wrist keeps the forearm's bend. Turning the palm comes in step 6 |
| EditMode (Explicit) | `AvatarRecordingTests`: on the developer's recordings (outside the repository), no rotation is not a number; how often the arms move, how bone lengths vary, and jumps go to the log |
| EditMode | Interpolation (7.3), returning when lost (8.4), blending for `LowConfidence` |
| EditMode | `ExpressionMapping`: response curves, keeping eyes together, distributing mouth shapes, overlap with emotion expressions |
| EditMode | `AvatarImporter`: checking extension, size, and magic; cleaning up the temporary folder; detecting the same hash; reading and writing the profile (keeping unknown fields) |
| EditMode | `AvatarController` driven by fake `IAvatarRig` and `IAvatarExpressions` |
| PlayMode | Loading a real VRM: bones and expressions are present, and destroying leaves nothing behind. Uses a small test model that can be committed, if one exists; otherwise the sample from the user (outside the repository) with `[Explicit]` |
| Hardware | Camera, MediaPipe, and avatar connected and checked visually (demo branch) |
| Performance | Loading time and the longest frame; per-frame application time |

Tests for how fast the arms turn (2026-10-24): `SteadyMotionTests` (EditMode: a turn within the limits is taken whole; where a bone points and its twist are each held to their limits; the torso is not held), `VrmRecordingTests.Body_OnTheRecordings_MovesSteadily` (Explicit: on the developer's recordings, each bone's largest turn in a frame, how often past 15°, the shake, and the worst moments with tracking's turn alone).

Tests for the fingers (2026-10-24): `FingerTests` (EditMode: an open hand is straight; a fist bends each joint within its limit and folds the thumb; the spread is toward the thumb on either hand and within its limit; a hand not seen is not measured; the fingers bend into the palm and the thumb across it on either hand, joints carrying their parent's turn and turning with the hand; a lost hand holds its fingers, then closes them loosely), `VrmLoaderTests.Fingers_CloseTheModelsOwnHand` (PlayMode, Explicit: a fist turns the model's own index finger into its palm), and `VrmRecordingTests.Body_OnTheRecordings_MovesSteadily`, which also measures the fingers.

Tests for the hands and the forearms' twist (2026-10-24): `HandRetargetingTests` (EditMode: a palm down is the reference pose; a palm turned up turns the hand as far as the forearm's reach, a quarter, the forearm's bone half of that, and not the shoulder; on a bent arm a twist past the reach is left out; a palm nearly over is read on the supination side, without a jump at the wrap; how much is seen blends with following the forearm; in a mirror the other hand is used, reflected; a lost hand holds, then follows the forearm, and comes back; `AvatarController` sets the hand and forearm). `ArmClearanceTests`: turning the forearm at the elbow turns the hand too. `VrmLoaderTests.Body_PointsTheModelsOwnArms_AndAppliesWithinItsBudget`: a palm turned up turns the back of the model's hand down. `AvatarRecordingTests.TheElbow_OnEachRecording_BendsWhereAnArmCan` (Explicit) measures the elbow's bend plane and the palm's twist on the recordings.

Tests for keeping the arms out of the head and torso (2026-10-24): `ArmClearanceTests` (EditMode: a forearm across the face turns in depth at the elbow, forward when in front and back when behind; an upper arm toward the head turns back at the shoulder; folded arms come forward; an elbow in the chest comes forward at the shoulder; each keeping the direction seen from the camera; the side kept until the forearm is clearly on the other; the added turn easing in and out; arms clear of the body, or no shape, stay as they are; a shoulder inside the head is not turned; the ellipsoid moves with the head; `AvatarController` sets the pose it shows on the bones; the head's and torso's shapes are measured from mesh points, leaving out hair below the neck), `VrmLoaderTests.Body_KeepsTheArmsOutOfTheHeadAndTorso` (the shape measured on three real models; a forearm across the face kept out of the head on the model's own bones; resting arms not moved; folded arms kept out of the chest), `VrmRecordingTests.Body_OnTheRecordings_ClearanceMovesTheArmsSmoothly` (on the developer's recordings: how often and how far the arms are turned, how much shows on the image, what stays inside, and the jumps added).

Tests made in step 5: `AvatarBodyTests` (EditMode: arms sideways, down, and forward, the elbow bent forward or up, mirroring, the torso's tilt and limit, how shoulders rise, unseen arms, a lost arm holding and then hanging, a hidden wrist keeping the forearm's bend, the rest pose without tracking, `AvatarController` and the neck), `AvatarRecordingTests` (EditMode, Explicit, on the developer's recordings), `VrmLoaderTests.Body_PointsTheModelsOwnArms_AndAppliesWithinItsBudget` (on real models, the arm and forearm bones point as observed, and the application time).

Tests made in step 4: `AvatarFaceTests` (EditMode: response curves, fitting a part convention and the part values (a closed smile that keeps the eyes open, i and e, brows), the Expression Signal (feelings, eyes, mouth, sides, neutral, the same values part by part and whole-face), both eyes and winks, the mouth's shares and a closed mouth, feelings, gaze, tuning, mirroring and limiting the head, interpolation and a lost face, substituting blinks, `AvatarController`), `AvatarServiceTests` (the head turn from tracking, the mirroring option, applying and saving tuning), `VrmLookTests` (from a share of the eye range to angles), `VrmLoaderTests.Face_TurnsTheHead_MovesTheFace_AndAppliesWithinItsBudget` (head, expressions, gaze, and application time on real models). UI: `AvatarCardPanelTests` (the face tuning Card) and `LiveSetupPanelTests` (movement speed).

Tests made in step 3: `AvatarCoreTests`, `AvatarImportTests`, `AvatarServiceTests` (EditMode, fake loader), `VrmLoaderTests` (PlayMode, `[Explicit]`; samples are read from the folder in the `VV_SAMPLES_DIR` environment variable, else `samples` next to the repository, by `VrmSamples`, which the tests on the samples share). The measurements on the developer's recordings with the samples are in `VrmRecordingTests` (PlayMode, `[Explicit]`), which replays a recording as the app runs it: corrections, 2D flattening when asked, smoothing, then 60 frames a second. The setup screen's Card: `AvatarCardPanelTests` (UI PlayMode).

---

## 15. Performance

- Per-frame application (`Avatar/Apply`) targets 0.3 ms or less (measured on the development PC and recorded here).
- A single frame while loading targets 50 ms or less, so switching avatars during a stream does not visibly freeze the image.
- SpringBone load depends on the model. It is measured and recorded.
- Measured (2026-10-20, development PC, `VrmLoaderTests`): the two VRM 1.0 samples (14 MB, 17 MB) loaded in 0.33–0.37 s, and the VRM 0.x sample (15 MB) in 0.58 s including the conversion to 1.0. The longest frame while loading was 46–47 ms for each. Per-frame application (`Avatar/Apply`) was measured in step 4.
- Measured (2026-10-20, development PC, `VrmLoaderTests`): per-frame application, the face computation and UniVRM's processing (SpringBone included) together, took a median of 0.031–0.033 ms, a 95th percentile of 0.035–0.038 ms, and at most 0.044–0.146 ms on the three samples. Moving the face part by part (2026-10-21), it took a median of 0.034–0.035 ms, a 95th percentile of 0.038–0.040 ms, and at most 0.057–0.130 ms. Within the 0.3 ms target.
- Measured (2026-10-22, step 5): with the upper body added, observations of the face only took a median of 0.038–0.039 ms. With the body moving too (the synthetic motion of raising and waving an arm), a median of 0.041 ms, a 95th percentile of 0.044–0.049 ms, and at most 0.062–2.808 ms (2.8 ms only on one sample's first frame). Within the target.
- Measured (2026-10-24, up to the arms' turning limit): per-frame application with the body moving took a median of 0.065 ms and a 95th percentile of 0.075 ms.
- Measured (2026-10-24, the hands and the forearms' twist): per-frame application with the body moving took a median of 0.080–0.093 ms and a 95th percentile of 0.114–0.124 ms. Within the target.
- Measured (2026-10-24, keeping the arms out of the head and torso): the body shape is measured once at load, in 13.4–30.8 ms on the three samples. The longest frame while loading was 47–48 ms, within the 50 ms target. With the arms kept out of the head and torso, per-frame application with the body moving (the synthetic motion of raising and waving an arm, which passes near the head) took a median of 0.084–0.092 ms and a 95th percentile of 0.113–0.121 ms (a median of 0.041 ms before). Searching all five directions to the same precision took a median of 0.146 ms and a 95th percentile of 0.24 ms, so a direction that does not clear at the least angle found so far is ruled out with one check.
- Measured shapes (same day): the head ellipsoid's half sizes were 7.4–10.2 cm across, 9.0–13.0 cm up and down, and 9.3–10.7 cm front to back. The torso parts' half sizes were 6.8–13.5 cm across and 7.3–10.6 cm front to back. The upper arm's thickness (radius) was 3.7–5.3 cm. Resting arms (70°) touched no torso on any of the three and were not moved. Folded arms came out of the chest by turning the whole arm 23–41° forward.

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
│  ├─ Pose/                 AvatarRestPose, HeadPose, PoseRetargeting, BodyTarget, ArmSide, AvatarBodyShape, ArmClearance, PlacedPart, HandRetargeting,
│  │                        FingerPose, FingerRetargeting
│  ├─ Expression/           ExpressionCurve, ExpressionMapping, FaceFeatures, ExpressionController, ExpressionSignal, FaceTarget, FaceTuning, FacePart, FacePartProfile
│  └─ Runtime/              AvatarController, FaceMotion, BodyMotion, HandMotion, ClearanceMotion, SteadyMotion, MotionTiming,
│                           ObservationClock, FrameClock
├─ Vrm/                                       VirtualVessel.Avatar.Vrm (the only assembly that references UniVRM)
│  ├─ VrmAvatarLoader.cs
│  ├─ VrmAvatarModel.cs     IAvatarModel; turns UniVRM's own update off and runs it in Apply
│  ├─ VrmAvatarRig.cs       IAvatarRig on the control rig
│  ├─ VrmBodyShape.cs       Measures the body shape right after loading (7.6)
│  ├─ VrmAvatarExpressions.cs
│  ├─ VrmAvatarFaceShapes.cs  IAvatarFaceShapes: blend shapes by name, written after UniVRM's processing
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
| Setting the arm-down angle in the profile (`pose.armDownAngle`, 6.3) | Not built; the default 70° for now. Added to profile loading and the avatar Card if, for example, a model's arms sink into its body |
| Depth estimation (tracking detailed design 8.4) | Decided: four methods were compared on recordings, and a correction straightening arms that point at the camera was added (tracking detailed design 8.4). The avatar's elbow bend in "arms forward" went from a median of 40° to 6°. Awaiting the camera check. The earlier **known issue** (the user's camera check, 2026-10-22): an arm thrust forward does not straighten. In the recording "arms forward", the elbow bends 33–40° at the median and the wrist is only 36–39 cm ahead of the shoulder for an arm about 43–45 cm long: MediaPipe underestimates the depth. Fixed by working the depth out again from bone lengths (8.4) |
| The forearm's twist (the palm's direction) | Addressed: the hand and forearm turn from the palm (7.7; step 6, part 1, 2026-10-24). Awaiting the camera check. Before: added with the hands in step 6; the forearm kept the upper arm's twist. **Known issue** (same check): the elbow seems to bend the wrong way. The forearm does point at the wrist, so the cause is mainly the twist of the forearm and hand (the palm's direction). On a nearly straight arm, the bend from depth errors also makes the upper arm's twist unsteady, so depth estimation should reduce it too. The order is depth estimation, then step 6 (user's decision, 2026-10-22) |
| Checking the upper body (2026-10-22, the user's camera) | Mirroring has the sides right. The arms with the hands behind the head pass. The arms' direction and bending have the two issues above |
| Arms sinking into the head (2026-10-24, the user's camera) | Addressed: the arms are kept out of the head (7.6). The user checked it with the camera and accepted it (same day) |
| Arms sinking into the torso (arms folded in front of the chest, for example) | Addressed: the arms are kept out of the torso too (7.6; the user's request, 2026-10-24). Awaiting the camera check. The arms are not kept out of each other, the hips, or the legs |
| Perfect Sync (models with the 52 ARKit expressions) | After v0.1.0 |
| Saving the part convention to the Avatar Profile (system design 7.12) | For now it is decided from the shape names each time a model loads (7.4). When a Setup screen lets the user choose the expression mapping, it is decided at registration and saved |
| Puffing the cheeks | MediaPipe's `CheekPuff` hardly responds, and VRM has no standard expression for it. The three samples (VRoid) have neither such an expression nor a blend shape for it. Considered together with Perfect Sync (user's request, 2026-10-21) |
| The cropped video of the face tuning Card | Decided: the user moves it up and down (UI `setup.md` chapter 9; where the face is depends on the avatar's height). Not saved. Choosing the part from the head's position automatically is considered with Camera Points |
| Expression defaults | Decided: the values the user tuned on a camera became the defaults (7.4, 2026-10-21). Someone whose face moves differently changes them on the tuning screen |
| FBX | Starting with the choice of library. After v0.1.0 |
| Screens for adjusting the spawn point and camera position | When designing Stage Scenes and Camera Points |
| Moving settings to Project / Profile | Moved in the refactoring after step 7 (the avatar's motion) (user's decision, 2026-10-22) |

---

## 18. Implementation Order

Tracking and Avatar proceed together in this order. Each step is one branch with its tests and a demo branch to try it (CLAUDE.md 29.2). v0.1.0 is released after step 7.

1. Tracking foundation (Tracking detailed design chapter 18, item 1)
2. MediaPipe and the camera (Tracking detailed design chapter 18, item 2)
3. Loading avatars: adding UniVRM (SBOM), registration, Data Root, `IFileDialog`, Video's avatar layer and Avatar Camera, the default spawn point, the avatar card on the setup screen
4. Face: expressions, gaze, head orientation, including the face tuning screen and ten levels of movement speed (Tracking detailed design 8.2)
5. Upper body: torso, shoulders, arms. Comparing depth estimation (Tracking detailed design 8.4)
6. Fingers: part 1 is the hands and the forearms' twist (7.7), part 2 the fingers (7.5)
7. Tracking settings screen (range choice with "full body: coming soon", smoothing levels, mirroring, reference pose), finishing diagnostics
