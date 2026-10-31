# Tracking Detailed Design

## 1. Purpose

This document defines the detailed design of the Tracking module. The Tracking module takes the movement of a person's face, body, and hands from a camera image and passes it to the Avatar module as a `TrackingFrame` that depends on neither a specific library nor an avatar (system design chapter 8).

The scope built here (v0.1.0):

- Common contracts: `TrackingFrame`, `ITrackingProvider`, tracking status
- MediaPipe provider: camera tracking with the Face / Pose / Hand Landmarkers of MediaPipeUnityPlugin
- Common processing: one coordinate system, confidence and missing data, smoothing
- Tracking camera: reusing the camera input of the Capture module
- Choosing what moves (face, upper body, hands and fingers; full body shown as "coming soon")
- Recording and replay for tuning and evaluation (developer-only)

Out of scope, handled by later designs:

- Other providers such as mocopi (system design 16.3)
- Tracking several people (system design 8.12; the design does not assume a single person)
- Full body (lower body, hip movement, foot contact). The choice is provided in advance (6.5)
- The details of the tracking settings screen (handled in the UI screen specification; chapter 10 lists only the needed items)

Related system design sections:

- 7.6 Pose application and body-size differences, 7.7 Expression control
- Chapter 8 Tracking
- 15.3 Real-time behavior, 15.7 Main-thread load
- 16.3 Adding tracking providers, 16.4 Combining tracking sources

Related detailed designs:

- Avatar detailed design (`avatar.md`): the side that applies `TrackingFrame` to the avatar
- Capture detailed design (`capture.md`): adds the tracking camera source (5.4)

Prototype referenced (outside the repository, CLAUDE.md chapter 12):

- `E:\VirtualVessel\benchmarks\Voom` (MediaPipeUnityPlugin 0.16.0, UniVRM 0.129.1)

### What is taken from the prototype, and what is not

Voom showed that the Face / Pose / Hand Landmarkers of MediaPipeUnityPlugin run in Unity on Windows and that face movement can be applied to a VRM 1.0 avatar. In the user's assessment, however, finger movement and three-dimensional movement (especially toward and away from the camera) were still immature. The product handles it as follows.

| In the prototype | In the product |
|---|---|
| The MediaPipeUnityPlugin Task API (`LIVE_STREAM`), reading the image with `AsyncGPUReadback` | Kept. It is proven to work |
| Computing eyes, mouth, and brows from the positions of face points | Not kept. The product uses the expression values (blendshapes) MediaPipe outputs (6.3). The tuning knowledge of the prototype (how easily the eyes close, keeping both eyes together, and so on) stays as settings of the conversion in Avatar (Avatar detailed design chapter 7) |
| The body was only the chest tilt, smoothed by averaging Euler angles over 30 frames | Not kept. The average suppressed jitter but lagged 0.5–1 s. The product separates the causes of jitter and suppresses each with a low-lag method (chapter 8) |
| Fingers and depth | The prototype is not taken as correct. They are built while being checked both numerically and visually with the evaluation tools (chapter 12) |
| UI handling MediaPipe types directly, printing settings with `Debug.Log` | Not kept. MediaPipe types do not leave the provider |

### Overview

```mermaid
flowchart LR
    Cam["Webcam"] --> CapSrc["Capture: camera source<br/>(WebCamTexture)"]
    CapSrc -- "Texture" --> Reader["Frame reader<br/>(AsyncGPUReadback)"]

    subgraph Tracking["Tracking Module"]
        Reader --> MP["MediaPipe provider<br/>Face / Pose / Hand"]
        MP -- "observation (common coordinates)" --> Slot["Latest observation<br/>(cross-thread hand-off)"]
        Slot --> Filter["Smoothing, missing data"]
        Filter --> Frame["TrackingFrame"]
    end

    Frame --> Avatar["Avatar Module"]
    Settings["tracking-settings.json"] --> Tracking
```

---

## 2. Responsibilities

| Responsibility | Owner |
|---|---|
| Listing, choosing, starting, and stopping the camera; the image texture | Capture (`camera` source, 5.4) |
| Reading the image and passing it to inference | Tracking (MediaPipe provider) |
| Inference (face, body, and hand points and expression values) | Tracking (MediaPipe provider, using MediaPipeUnityPlugin internally) |
| One coordinate system, the definition of left and right, confidence and status | Tracking (conversion in the provider and common processing) |
| Smoothing, outlier removal, holding missing data | Tracking (common processing) |
| Correcting the input (8.5: a facing direction was tried and not adopted) | Tracking (system design 8.11) |
| Fitting the observed pose to the avatar skeleton, converting to expressions, mirroring | Avatar |
| Saving the choice of what moves | Tracking (`tracking-settings.json`) |
| Creating services and the order of startup and shutdown | Application (composition) |

---

## 3. Module Boundaries

- Tracking references Core, Diagnostics, ProjectData, and the public contracts of Capture (`ICaptureSources`, `IVideoCaptureSource`). It does not reference Avatar, Stage, Video, or UI.
- MediaPipeUnityPlugin types (`Mediapipe.*`) are used only inside the MediaPipe provider (a separate assembly, chapter 16). `TrackingFrame` contains no MediaPipe point indices, coordinate systems, or types (system design 8.1).
- Other modules use only `ITrackingSource` (5.1), never a provider directly.
- The names of expression values (6.3) are the 52 names that come from ARKit. They are not a type of a specific library but a widely used list of names for face movements, independent of providers (MediaPipe, a future iPhone, and so on) and of avatars (Perfect Sync models and so on).

---

## 4. Main Classes and Interfaces

| Name | Kind | Summary |
|---|---|---|
| `TrackingFrame` | Class | One observation: body, left and right hands, face, each with its status and time (chapter 6) |
| `BodyJoint` | Enum | Body joints (6.2) |
| `HandJoint` | Enum | Hand joints (21 points, 6.4) |
| `FaceShape` | Enum | Expression values (52, 6.3) |
| `TrackingStatus` | Enum | `Tracked`, `LowConfidence`, `Lost`, `NotSupported`, `Disabled` (6.1) |
| `TrackingRegions` | Flags enum | `Face`, `Body`, `LeftHand`, `RightHand`. The capabilities of a provider and the enabled parts |
| `BodyRange` | Enum | What moves: `UpperBody`, `FullBody` (6.5) |
| `ITrackingProvider` | Interface | The contract of an input method that produces observations (5.2) |
| `ObservationTraits` | Flags enum | What kind of observation a provider makes, to pick the corrections that suit it (8.4): `MonocularDepth`, `None` |
| `IObservationCorrection` | Interface | One correction of observations, with the traits it applies to (`Requires`) (8.4) |
| `ObservationCorrections` | Class | Picks from the corrections (`Catalog`) those that suit the provider's traits, and applies them to a copy of each observation (8.4) |
| `CameraAxisArmStraightening` | Class | Straightens arms that point at the camera (D in 8.4; the computation is a pure function) |
| `ITrackingSource` | Interface | What other modules use: the latest smoothed `TrackingFrame` and the status (5.1) |
| `TrackingService` | Class (`IApplicationService`) | Creates and starts the provider; per-frame processing, settings, metrics |
| `TrackingSettings` | Class | Saved settings (chapter 10) |
| `FrameSlot<T>` | Class | Hands only the latest value across threads (chapter 9) |
| `OneEuroFilter` | Struct | Smoothing whose strength follows speed (8.2, pure computation) |
| `RotationFilter` | Struct | Smoothing for rotations (quaternions) (8.2) |
| `TrackingProcessor` | Class | Common processing from observation to `TrackingFrame` (outliers, smoothing, missing data) |
| `TrackingStateMachine` | Static class | Decides the status of each part (pure functions) |
| `MediaPipeTrackingProvider` | Class (internal) | The MediaPipe provider |
| `MediaPipeConversion` | Static class (internal) | Converts MediaPipe points and coordinates to the common form (pure functions) |
| `ITrackingRecordings` | Interface | The entry point for recording and replay (developer-only, 5.5) |
| `TrackingRecordings`, `TrackingRecorder`, `TrackingRecordingFile`, `TrackingRecordingFormat`, `TrackingReplayProvider` | Class | Recording and replaying observations (developer-only, chapter 12) |

---

## 5. Public API

### 5.1 ITrackingSource

```csharp
public interface ITrackingSource
{
    IReadOnlyObservable<TrackingState> State { get; }          // Stopped, Starting, Running, Unavailable, Error
    IReadOnlyObservable<TrackingProblem> Problem { get; }      // Problem shown to the user (chapter 11)
    TrackingRegions Supported { get; }                         // Parts the provider can track
    IReadOnlyObservable<TrackingOptions> Options { get; }      // What the user chose (chapter 10)
    double ObservationsPerSecond { get; }                      // Camera images inferred over the last second (shown on the setup screen)

    // The latest smoothed observation, updated every frame on the main thread. Its contents are
    // overwritten by the next update, so callers read it within that frame and do not keep it
    // (the same object is returned each time to avoid allocations).
    TrackingFrame Current { get; }

    void Start();
    void Stop();
    void SetOptions(TrackingOptions options);                  // A change of parts takes effect at once

}
```

`TrackingOptions` gathers what the user chose into one value (immutable: `With…` makes a changed value, given whole to `SetOptions`). New choices change neither the interface nor how saving compares.

```csharp
public readonly struct TrackingOptions
{
    TrackingRegions Regions;            // Parts inferred
    BodyRange BodyRange;                // What moves (full body is coming soon and falls back to the upper body, 6.5)
    int Smoothing;                      // Smoothing level, 1 to 10 (8.2)
    bool Mirror;                        // Mirroring (chapter 10)
    BodyDepth BodyDepth;                // The body in 3D or 2D (8.6)
    PoseModel PoseModel;                // Lite, Full, Heavy; a change restarts a running provider (chapter 10)
}
```

- All operations and notifications happen on the main thread.
- `TrackingService` updates `Current` every frame (`IFrameLoop`). Avatar reads it at the equivalent of `LateUpdate` (Avatar detailed design 8.3).
- One `ITrackingSource` is created per person. For several people, there will be one source per person (system design 8.12). It is not a singleton.

### 5.2 ITrackingProvider

```csharp
public interface ITrackingProvider : IDisposable
{
    TrackingRegions Supported { get; }
    ObservationTraits Traits { get; }                          // What kind of observation, to pick the corrections that suit it (8.4); MediaPipe is MonocularDepth
    IReadOnlyObservable<TrackingState> State { get; }          // The same states for sources and providers
    void Start(TrackingOptions options);                       // Returns without waiting; the result comes through State
    void SetOptions(TrackingOptions options);                  // Changes them while running; inference for disabled parts stops
    void Stop();
    void Tick();                                               // Every frame on the main thread; advances reading the input

    // Copies the latest observation into the given object. Returns false when there is no new observation.
    // Copying into the caller's object avoids a per-frame allocation (exception to CLAUDE.md 15.1, chapter 23).
    bool TryTakeLatest(TrackingFrame into);
}
```

- A provider outputs observations as a `TrackingFrame` before smoothing (chapter 6; the same type before and after smoothing, so it is only copied). Provider differences (coordinate system, point indices, left and right) are converted to the common form inside the provider before output (system design 8.8).
- A new provider (mocopi and so on) is added by implementing this contract only. Stating its observation's traits (`Traits`) correctly gets it only the corrections that suit it (8.4).

### 5.3 TrackingService

- An optional application service, started after Logging, metrics, and Capture. It is not created when Capture is not running.
- At startup it reads `tracking-settings.json` and creates the provider. The MediaPipe provider is created only when the plugin is installed (`#if VV_MEDIAPIPE` in the composition, 16.2). Without the plugin the project still runs; starting tracking gives `Error` (`StartFailed`), and the developer log points to the setup script.
- If it was running, it starts again at the next launch (the same as Capture).

### 5.4 Tracking Camera (Addition to Capture)

Capture got a third source, the tracking webcam `camera`. It opens with `WebCamCaptureSource` (`WebCamTexture`), the same as GameCapture (Capture detailed design 8.5).

| Item | Value |
|---|---|
| Id, kind | `camera` (`CaptureService.TrackingCameraId`), `CaptureSourceKind.Camera` |
| Until a device is chosen | Not opened (`DeviceNotChosen`), for the same reason as Capture detailed design 6.3 |
| Requested resolution, frame rate | 1280×720, 30 fps (enough as inference input, with a small readback cost). `CameraWidth` and others in `capture-settings.json` |
| The same device twice | `game` and `camera` do not open the device the other has open (`WebCamTexture` cannot open one device twice). Shown as `Error` (`DeviceInUse`) |
| Telling new images | `IVideoCaptureSource.FrameCount` and `LastFrameTimestamp` (added to Capture). The same image is not inferred twice, and the image time becomes the observation time |
| Display on the stream | None. Stage creates no screen for sources of the `Camera` kind, so the user's face or room never appears on the stream unintentionally (chapter 13) |
| Setup screen | No capture Card. The device is chosen in the tracking Card (UI `setup.md` chapter 4) |
| Flip | Capture's flip (`CaptureFlip`) is not used. Left and right are decided by the Tracking coordinate definition (6.1) and Avatar mirroring |

### 5.5 ITrackingRecordings (Developer-only)

The entry point for recording and replay (chapter 12), used by the Diagnostics screen (Developer Mode only). Everything is called on the main thread.

```csharp
public interface ITrackingRecordings
{
    IReadOnlyObservable<RecordingActivity> Activity { get; }                       // Idle, Recording, Replaying
    IReadOnlyObservable<IReadOnlyList<TrackingRecordingInfo>> Recordings { get; }  // newest first
    string ActiveId { get; }                       // The recording being recorded or replayed, or null
    double RecordedSeconds { get; }                // Length of the recording in progress
    Task RefreshAsync(CancellationToken cancellationToken);
    void StartRecording(string description);       // Throws while tracking is not running
    void StopRecording();
    Task StartReplayAsync(string id, CancellationToken cancellationToken);
    void StopReplay();
    Task DeleteAsync(string id, CancellationToken cancellationToken);
    Task DeleteAllAsync(CancellationToken cancellationToken);
    void StopAll();                                // When Developer Mode turns off, for example
}

public sealed class TrackingRecordingInfo
{
    string Id;                 // The file name (12.3)
    string Description;
    DateTime StartedAtUtc;
    double Seconds;
    long Bytes;
    bool Readable;             // False for an unknown version or a broken header
}
```

- For recording and replay, `TrackingService` has two members used only inside its assembly: one that receives observations (before smoothing), and one that temporarily replaces the provider with replay. Other modules cannot see them.

---

## 6. Data Model

### 6.1 TrackingFrame

```text
TrackingFrame
├─ Sequence            Increases with each observation
├─ Body                BodyPose (6.2)
├─ LeftHand            HandPose (6.4; the user's own left hand)
├─ RightHand           HandPose
└─ Face                FacePose (6.3)

Common to each part:
├─ Status              Tracked / LowConfidence / Lost / NotSupported / Disabled
├─ Confidence          0–1
└─ Timestamp           Time of the image frame the part came from (seconds, monotonic since application start)
```

- Each part has its own time and status. Face, body, and hands come from separate inferences, so the source image frame can differ between parts (9.2).
- Left and right are the **user's own** left and right, not the image's. Whether to mirror is decided by Avatar (Avatar detailed design 6.6).
- The coordinate system is the "person space", oriented like a Unity avatar: origin at the midpoint of the hips, +Y up, +Z the person's front (toward the camera), +X the person's right. The unit is meters. It does not depend on the camera position or field of view.
- `Status`: `Tracked` means the value is usable. `LowConfidence` means there is a value with low confidence (Avatar uses it less strongly). `Lost` means it was lost (the value is the last usable one). `NotSupported` means the provider cannot track it. `Disabled` means the user turned it off.

### 6.2 BodyPose

| Item | Content |
|---|---|
| Joints | `BodyJoint`: `Head` (center of the head), `LeftEar`, `RightEar`, `Nose`, `Neck` (midpoint of the shoulders), `LeftShoulder`, `RightShoulder`, `LeftElbow`, `RightElbow`, `LeftWrist`, `RightWrist`, `Hips` (origin), `LeftHip`, `RightHip`, `LeftKnee`, `RightKnee`, `LeftAnkle`, `RightAnkle`, `LeftFoot`, `RightFoot` |
| Per joint | Position (person space), confidence (0–1), `Tracked`/`LowConfidence`/`Lost` |
| Image position | Each joint also keeps its position in the image (0–1), used for depth estimation (8.4) |

- Below the knees is used only for full body (`BodyRange.FullBody`). For the upper body, the values are present but Avatar does not use them.
- Of the 33 MediaPipe Pose points, the finger points (17–22) are not used. Fingers come from the Hand Landmarker.

### 6.3 FacePose

| Item | Content |
|---|---|
| Which face | Up to two faces are found, and the one whose nose tip is nearest the body's nose is the person's. If all are far (more than 12% of the image width), the face is lost, so a face on a poster behind the person is not picked up. Without the body, the most confident face is used |
| Head rotation | Rotation (person space), from MediaPipe's facial transformation matrix. More stable than computing it from Pose points |
| Head position | Position relative to the camera (meters), used for leaning and moving closer |
| Expression values | 0–1 per `FaceShape`: the 52 that come from ARKit (`EyeBlinkLeft`, `JawOpen`, `MouthSmileLeft`, and so on). Values that cannot be tracked (`TongueOut` with MediaPipe) are `NotSupported` |
| Gaze | Also gives the direction (angles) of each eye from the expression values (`EyeLookIn/Out/Up/Down`) |

- Left and right of expression values are also the user's own. The left and right in MediaPipe's names may be based on the image; the provider's conversion checks this and aligns them (made into a test at implementation time).

### 6.4 HandPose

| Item | Content |
|---|---|
| Joints | `HandJoint`: 21 points — wrist, thumb (CMC, MCP, IP, tip), and the other four fingers (MCP, PIP, DIP, tip) |
| Positions | Person space. Combines the finger shape relative to the wrist (the 3D points of the Hand Landmarker) with the wrist position (Pose wrist) |
| Palm orientation | A rotation from the wrist and the bases of the index and little fingers |
| Image positions | The 21 points' positions in the image (0–1). Shown over the camera image on the setup screen and used to decide left and right |
| Deciding left and right | MediaPipe's handedness depends on image orientation and can flicker between frames. The product assigns a detected hand to **whichever Pose wrist is nearer** (two hands take the pairing with the smaller total distance, so they never land on the same side). Handedness is used only when the body is not seen; it assumes a mirrored image, so on an unmirrored camera "Right" is the person's left hand. Hands away from the body are not left out: hands and body come from separate inferences, often of different image frames, so a moving hand looks a frame's movement away from the body's wrist. Leaving such hands out made moving hands flicker (tried in step 2 and undone, 2026-10-19) |

### 6.5 What Moves (BodyRange)

| Value | Content | v0.1.0 |
|---|---|---|
| `UpperBody` | Head, neck, torso, shoulders, arms. Fixed below the hips (streaming seated) | Default. Available |
| `FullBody` | Adds hip movement and rotation, legs, and foot contact | Shown as "coming soon" and cannot be chosen |

- Face (expressions, gaze) and hands are enabled or disabled separately from the range (`TrackingRegions`). Inference for disabled parts does not run (to reduce load).
- To widen the range, add a value to `BodyRange` and add its application in Avatar. `TrackingFrame` holds the full-body joints from the start.

---

## 7. State / Lifecycle

### 7.1 TrackingSource State

| State | Meaning |
|---|---|
| `Stopped` | Stopped |
| `Starting` | Preparing the camera or inference (loading models can take a few seconds) |
| `Running` | Producing observations (still `Running` when no one is in view; parts become `Lost`) |
| `Unavailable` | No camera chosen or the camera is not found. Follows the Capture state and continues automatically when it returns |
| `Error` | Inference cannot start (missing models, the plugin cannot load, and so on) |

### 7.2 Starting and Stopping

1. `Start()`: starts the Capture `camera` source. The tracking camera is used only for tracking, so it starts and stops together with tracking.
2. When camera images arrive, the provider loads models (without blocking the main thread, chapter 9) and starts inference. `Running`.
3. `Stop()`: stops inference, discards pending readbacks without waiting, and releases buffers and models.

### 7.3 Status of Each Part

`TrackingStateMachine` (pure functions) decides the status of each part from whether there is an observation, its confidence, and elapsed time.

| Condition | Status |
|---|---|
| Observed, confidence at or above the threshold (default 0.5) | `Tracked` |
| Observed, below the threshold | `LowConfidence` |
| No observation for more than 0.2 s | `Lost` (the last value is kept) |
| Turned off by the user | `Disabled` |

- How values return when lost (holding, easing back to the rest pose) is decided by Avatar (Avatar detailed design 8.4). Tracking passes "since when it has been lost".
- So that the status does not flicker on every brief loss (1–2 frames), it moves to `Lost` by time and back to `Tracked` after two consecutive observations.

---

## 8. Smoothing and Accuracy

The causes of jitter are separated and each is suppressed with a low-lag method. A method becomes the default only after replaying recorded observations and comparing numbers (chapter 12) shows less lag than Voom's 30-frame average with equal or less jitter (CLAUDE.md chapter 33). What was compared, tried, and dropped is in [decisions/tracking-processing.md](../decisions/tracking-processing.md).

### 8.1 Causes of Jitter and Countermeasures

| Cause | Countermeasure |
|---|---|
| Small fluctuations of inferred values | One Euro filter (8.2): strong smoothing when still, weak when moving fast |
| Inference at 30 per second and rendering at 60 | For each rendered frame, interpolate between the two latest observations (Avatar detailed design 7.3) |
| Occasional large outliers | Discard values that moved faster than a person can, and use the previous value (8.3) |
| Unstable depth | Correct observations where a single camera is weakest (8.4), or use the body in 2D (8.6) |
| Wrong rotation averages | Handle rotations as quaternions; never average Euler angles |

### 8.2 One Euro Filter

- Chooses the strength of a low-pass filter each time from how fast the value changes: two parameters, the minimum cutoff (`MinCutoff`) and how much speed relaxes it (`Beta`). It uses the time of the image frame, not the processing time.
- Positions are filtered per axis and expression values per value. Rotations use the same idea with spherical interpolation, the speed being the angular velocity as a vector (`RotationFilter`), so shakes in opposite directions cancel.
- The joints of the body, and those of each hand, are smoothed together (`LinkedPositionFilter`): every joint moves the same share of the way each observation, with the cutoff of the fastest seen joint, so a straight arm stays straight while it moves. Joints not seen (`BodyPose.IsSeen`, 8.4) do not set the cutoff. The beta is half that of positions (`SmoothingProfile.Joints`).
- Each hand's joints are never smoothed steadier than 2 Hz while still (`SmoothingProfile.Hands`, `MinHandCutoff`), so a hand's shape follows at every level. Only the fingers' angles come from these positions; the palm and the arms have their own filters.
- Parameters are per part. The user chooses one of ten levels of "movement speed" (1: steadiest, 10: fastest; chapter 10, UI `setup.md` chapter 4). Each level changes the parameters from the standard set toward the smooth set by the same ratio per level. The smooth set falls at level 9 and the standard set at 11, so all levels are on the steadier side. The default is 5, about 300 ms of lag on the user's recordings: steadiness matters more than lag (the user's decision). Face values are never slower than the smooth set, so blinks and the mouth keep up.

| Level | Still jitter (synthetic motion) | Lag (synthetic) |
|---|---|---|
| 1 | 0.014° | 303 ms |
| 3 | 0.021° | 233 ms |
| 5 (default) | 0.029° | 177 ms |
| 7 | 0.041° | 133 ms |
| 9 (the smooth set) | 0.059° | 100 ms |

- The computation is pure (it takes the state and returns a new one) and is tested with inputs and outputs only (`FilterTests`, `RecordedSmoothingTests`).
- No requirement caps the smoothing lag (the 80 ms of chapter 15 is the processing delay to `Current`).

### 8.3 Outlier Removal

- For each joint, a value whose speed from the previous value exceeds a limit (for example 8 m/s for a wrist) is not used that time. If it keeps exceeding (a really fast movement), it is accepted from the third time.
- The palm's direction likewise: a value turned faster than 1800° a second is not used, and the third in a row is accepted (`TrackingProcessor.MaxPalmDegreesPerSecond`).
- Points whose confidence is below the threshold are `LowConfidence`, and smoothing gets stronger.
- Dropped values are counted (`Tracking/Outliers`, chapter 11).

### 8.4 Depth (Toward and Away From the Camera) and Correcting Observations

With a single camera, depth is less certain than image position, and least certain for limbs pointing at the camera.

- **Straightening arms that point at the camera** (`CameraAxisArmStraightening`): only when both the upper arm and the forearm point toward the camera, the elbow moves toward the line from the shoulder to the wrist. It starts when how far both point toward the camera (the smaller cosine to its axis) reaches 0.5, and the arm is fully straight at 0.8, blended in between. The shoulder and wrist stay. Nothing is corrected while the shoulder, elbow, or wrist is unseen. An arm that really bends toward the camera is shown straight too; that direction is a single camera's weakest (the user's decision).
- **Giving the hand the forearm's depth** (`HandDepthFromForearm`): the hand keeps its bend from the forearm as seen from the front, and leans toward or away from the camera as the forearm does, the back of the hand at right angles to that. Below 0.1 of its length seen from the front, its bend there is not used; from 0.3, fully. Nothing is corrected when the elbow, wrist, or hand is not seen.
- Finger angles (the angle between neighboring bones) are primary for fingers: they depend neither on hand size nor on distance, and depth errors affect them less (Avatar detailed design 7.5).

#### How Observations Are Corrected (Extensibility)

Corrections are common Tracking Processing, not per input method (system design 8.10, 8.11).

- **A provider states what kind of observation it makes (`ObservationTraits`)** and holds no corrections. MediaPipe is `MonocularDepth`. An input with sensors on the body, such as mocopi, is `None` and gets no camera corrections.
- **Each correction is an `IObservationCorrection`**, with the traits it applies to (`Requires`). For now: `CameraAxisArmStraightening`, then `HandDepthFromForearm`.
- **`ObservationCorrections` picks from the `Catalog` those that suit the active provider's traits.** `TrackingService` corrects a copy of the observation and passes it to smoothing; it picks again when the provider changes (a replay starts or ends).
- **Recordings (chapter 12) keep the observation before correction**, and a replay picks the same corrections from the recorded traits, so corrections can change and be compared without filming again.
- A new correction implements `IObservationCorrection` and joins the `Catalog`.
- Whether a joint is seen is decided once, in `BodyPose.IsSeen`, for the corrections and for Avatar: visibility at least 0.5 (`BodyPose.MinVisibility`) and its position inside the camera image (0 to 1). MediaPipe also guesses joints outside the image and may call them visible; such a joint is not seen.

### 8.5 Reference Pose (Input Correction, System Design 8.11)

Not adopted (the user's decision, 2026-10-26). Taking the person's front with the camera to one side was tried in step 7 and made the body lean once the person moved: a camera to one side sees too little of the body, and turning its observations back moves its depth errors into the lean. The camera goes in front of the person instead (the README says so). Bone lengths and a reference head position are not needed by 8.4. Correction to fit the avatar's body is done by Avatar (system design 7.6).

### 8.6 The Body in 2D (BodyDepth)

For when depth errors make the arms point unsteadily, 2D uses only the movement seen from the front.

- `TrackingOptions.BodyDepth`: `Tracked` (3D, the default) or `Flat` (2D), on the tracking Card (UI `setup.md` chapter 4), saved as `bodyDepth` (0: 3D, 1: 2D).
- In 2D, after the corrections (8.4) and before smoothing, every body joint is put at the hips' depth (0 in person space) (`BodyFlattening`), except the elbows, 10 cm in front (`ArmsForwardMeters`), and the wrists 5 cm further (`ForearmForwardMeters`). The arms then always pass in front of the body, and a forearm swung from up to down bends forward on the way, as in 3D. The torso's lean and twist, and arms reaching forward or back, are gone.
- In 2D, Avatar also turns the arms only forward when keeping them out of the torso (avatar detailed design 7.6).
- Like the corrections, it works on a copy, and recordings keep what was observed. A switch glides through the smoothing.
- The face and the hands are not changed.
- An arm pointing at the camera looks short from the front, and its direction is easily set by jitter; in 2D this cannot be helped.

---

## 9. Threading / async

### 9.1 Flow

```mermaid
sequenceDiagram
    participant Main as Main thread
    participant GPU as GPU (AsyncGPUReadback)
    participant MP as MediaPipe thread

    Main->>GPU: Copy a new camera image flipped upright, request the readback
    GPU-->>Main: Pixels (into a free buffer)
    Main->>MP: DetectAsync (Face / Pose / Hand; the same buffer, not copied)
    MP-->>MP: Inference
    MP->>MP: Convert the result to the common form and put it in FrameSlot
    MP-->>Main: Release the images (deleter) → the buffer is free
    Main->>Main: Every frame: take the latest from FrameSlot, smooth it, update Current
```

- Only new images (`FrameCount` changed) are read. One read at a time, and MediaPipe holds at most two camera images. Images beyond that are skipped, not queued (`Tracking/Dropped`), so lag does not grow.
- The read flips the image on the GPU: Unity textures start at the bottom row and MediaPipe images at the top one. Images wider than 1280 px are scaled down.
- The buffers (`ImageBufferPool`, three) go through taking (reserving) → a readback → being referenced by MediaPipe images. MediaPipe images reference a buffer without copying it. MediaPipe reads a buffer after `DetectAsync` returns, so a buffer is reused only after every image made over it has been released (counted through the plugin's deleter). The deleter receives only the pixel pointer, so the pool keeps a table from pointer to buffer. If a buffer is not given back within 2 s, the developer log records it and the buffer waits, rather than overwriting memory MediaPipe may still read.
- MediaPipe results are delivered on MediaPipe's threads. They are converted to the common form there and put in `FrameSlot`. No Unity API is called there. Failures are counted and logged on the main thread, gathered, at most every 10 s.
- `FrameSlot<T>` cycles three objects (being written, latest, being read). Only a short lock, no waiting (the same idea as Capture detailed design 8.2). Objects are reused; nothing is allocated per observation. Face, body, and hands (both) have separate slots.
- Assigning hands to left and right and leaving out faces away from the body (6.3, 6.4) happen on the main thread, with the latest body.
- Smoothing and status decisions run on the main thread once per frame.
- The plugin allocates images and packets each time. Those allocations are the plugin's; our processing allocates nothing per frame.

### 9.2 Per-part Times

Face, body, and hand inferences run in parallel and their results arrive separately. `TrackingFrame` holds the latest observation of each part with its own time (the time of the source image frame). Avatar interpolates using the per-part times.

### 9.3 Loading Models

- Reading the models (`.bytes`) and creating the tasks happen on the thread pool. Models are given to the plugin as bytes; the plugin's asset loading is not used.
- If tracking stops while loading, the tasks are closed when they finish being created.
- On the development PC, the first result came 0.69 s after starting (chapter 15).

### 9.4 Background Removal

Not used. Cutting the person out of the background before inference was tried and removed: with a real camera it made mistakes more frequent ([decisions/tracking-processing.md](../decisions/tracking-processing.md)).

---

## 10. Settings

```csharp
public sealed class TrackingSettings
{
    public string Provider { get; set; } = "mediapipe";
    public TrackingRegions Regions { get; set; } = TrackingRegions.All;   // Enabled parts: face, body, both hands
    public BodyRange BodyRange { get; set; } = BodyRange.UpperBody;
    public int SmoothingLevel { get; set; } = 5;                          // Smoothing level, 1 to 10 (8.2); from version 4, 5 is version 3's 1
    public PoseModel PoseModel { get; set; } = PoseModel.Full;             // Lite, Full, Heavy (load and accuracy); one of the options
    public bool Mirror { get; set; } = true;                              // Mirroring
    public BodyDepth BodyDepth { get; set; } = BodyDepth.Tracked;          // The body in depth: 3D (0) or 2D (1) (8.6); added in version 3 without a step
    public bool Running { get; set; }                                     // Whether it was started; restored at the next launch
}
```

- What the user chose is read and written as `TrackingOptions` (`GetOptions`, `With`). It is saved only when the choices or whether tracking was started change.
- Saved as the project's tracking profile, `Profiles/Tracking/<ProfileId>.json` in the Data Root (`ConfigFileStore`; project data detailed design 6), in the format of `tracking-settings.json`. Until the refactoring after step 7 it was `tracking-settings.json` in the settings folder; the first project took a copy of it (project data detailed design 8).
- The camera device choice is saved in `capture-settings.json` (`camera` source).
- Mirroring (user's decision, 2026-10-19): when on, the camera image and points on the setup screen are flipped left to right, like a mirror. The avatar follows the same setting, moving its left hand for the user's right (Avatar detailed design 6.6). Tracking holds it as one setting so the preview and the avatar never disagree. `Current` keeps the person's own sides whatever the setting.
- The Pose model is one of the options (step 7): Lite or Full. Heavy is not offered (`TrackingOptions.Available`): on the user's camera it tracked worse than Full (2026-10-26), being slower, with the smoothing, outlier limits, and depth corrections all tuned on Full. A saved Heavy reads as Full. The heavy model file is not shipped (2026-10-27): `tools/deps/mediapipe.json` no longer lists it, and the fetch script removes model files that are not listed. The provider loads it when it starts, so a change while tracking restarts the provider; the camera keeps running. Before step 7 it was fixed when the provider was created, and a change took effect only after restarting the app.
- Version 2 (step 4): the three-level `smoothing` (0 to 2) became the ten-level `smoothingLevel` (1 to 10). Version 1 files are moved over when read. Version 1 had no screen to choose a level, so standard (1) was never the user's choice and becomes the default, 5. Smooth (0) and responsive (2) came only from editing the file by hand, so they keep their measured values as 3 and 7. Other values and unknown fields are kept.
- Version 4 (2026-10-24): the levels moved four more steps (8.2). A version 3 level becomes the level four steps up, keeping the body's motion. Version 3's standard 5, the untouched default, becomes the new standard 5. Version 3's 6 to 10 all become 10.
- Version 3 (2026-10-24): the levels moved two steps (8.2). A version 2 level becomes the level two steps up, keeping its motion. Version 2's standard 5, though, was the untouched default nearly every time, and the user asked for the former 3 as the standard, so it becomes the new standard 5. Version 2's 9 and 10 both become 10. Version 1 files go through version 2.

---

## 11. Logging and Diagnostics

- State changes, the provider, model kinds and versions, and model loading time are logged at Information. Nothing is logged per frame.
- The following are recorded in performance metrics (`IPerformanceMetrics`) and appear in the Diagnostics screen table.

| Metric | Kind | Content |
|---|---|---|
| `Tracking/face.Inference`, `body.Inference`, `hands.Inference` | Timer | From handing over the image to the result (including waiting) |
| `Tracking/Latency` | Timer | From the image frame time to the update of `Current` |
| `Tracking/Readback` | Timer | Image readback time |
| `Tracking/Dropped` | Counter | Images not inferred because MediaPipe was still busy with earlier ones |
| `Tracking/Outliers` | Counter | Joint positions and palm turns dropped as moving faster than a person can (8.3) |

- Values discarded as outliers (8.3), counted by `TrackingProcessor.Outliers`, go to the counter `Tracking/Outliers` (step 7).

- Problems shown to the user (`TrackingProblem`): `CameraNotChosen` (choose a camera), `CameraNotFound`, `CameraInUse` (the same camera as the game screen), `StartFailed` (tracking could not start), `NoPerson` (no one is in view; status display only). Plugin or model paths are not shown to the user.
- The setup screen overlays points and bones on the camera image so the user can check that tracking works (never on the stream).

---

## 12. Recording and Replay (Developer-only)

Tools for choosing the smoothing and depth methods by comparison rather than by feel. A movement filmed with the camera is recorded and replayed any number of times, and only the method changes between comparisons, in numbers and by eye.

### 12.1 Parts

| Name | Summary |
|---|---|
| `TrackingRecordingFormat` | Converts one observation and the file's header to and from bytes (pure functions) |
| `TrackingRecordingFile` | Reads the header and length for the list, and the whole file for replay |
| `TrackingRecorder` | Writes observations to the file being recorded; writing happens on another thread |
| `TrackingRecordings` (`ITrackingRecordings`, 5.5) | Starts and stops recording and replay, lists, and deletes. Used by the Diagnostics screen |
| `TrackingReplayProvider` | Replays read observations as an `ITrackingProvider` (step 1) |
| `TrackingMetrics`, `SyntheticMotion` | Evaluation values (12.6) and synthetic movements (step 1) |

### 12.2 Recording

- Only the provider's observations (`TrackingFrame` before smoothing) are recorded. **Images are not recorded.**
- Recording happens only when a developer starts it on the Diagnostics screen (shown only in Developer Mode, UI foundation detailed design 7.7). The design first enabled it with a launch argument, but changes are usually tried in the Editor, which Unity Hub launches, so it is operated from the screen instead (user's decision, 2026-10-22). It is never available from the normal screens.
- Recording is possible only while tracking runs, and not during replay.
- Before starting, an optional description can be given (for example "arms forward", up to 100 characters). It is kept inside the file, not in the file name.
- A recording is at most 5 minutes long (about 15 MB) and stops on its own after that.
- It also stops when tracking stops, when the application exits, and when Developer Mode is turned off.
- Each observation is turned into bytes on the main thread (about 1.7 KB, in reused buffers) and handed to the writing thread. The main thread never writes to the file.

### 12.3 File

- Location: `Cache/TrackingRecordings/` in the Data Root. In system design 6.24, Cache is for temporary data that can be made again. A recording cannot be made again (only filmed again), but it is treated as **developer data that may disappear at any time** and kept there (user's decision, 2026-10-22).
- No database is used. One file holds one recording, with the description and the like at the start of the file. There are a few dozen recordings at most, nothing is queried or related to other data, and the database and the files cannot drift apart (2026-10-22).
- Name: `yyyyMMdd-HHmmss.vvtrack` from the UTC time recording started. If the name exists, `-2`, `-3`, and so on are added.
- Format (little-endian):

```text
Header
├─ Identifier "VVTR" (4 bytes)
├─ Format version (int32; 2 for now)
├─ Size of one observation (int32, bytes)
├─ When recording started (int64, UTC ticks)
├─ Parts the provider can track (TrackingRegions, int32)
├─ What kind of observation the provider makes (ObservationTraits, int32; from version 2)
└─ Description (int32 byte count + UTF-8)
Observations × count (all the same size)
├─ Sequence (int64), Time (double)
├─ Body: status, confidence, time, 20 joints' positions, image positions, and confidences
├─ Left hand, right hand: status, confidence, time, 21 points' positions and image positions, palm rotation
└─ Face: status, confidence, time, head rotation and position, 52 values, supported values (bits), each eye's gaze
```

- All observations have the same size, so their count follows from the file size and the length from the first and last observations' times. The list reads only the header and the first and last observations.
- A file whose end is missing, for example after a crash, is used up to the last complete observation.
- Version 2 added the observation's traits (8.4). Version 1 files still read; every version 1 recording was made with MediaPipe, so they read as `MonocularDepth`.
- Files of an unknown version or with a broken header are listed as "cannot be read", not deleted (CLAUDE.md chapter 25). They can be deleted.
- A format change raises the version and either keeps reading the old one or shows that it cannot be read.

### 12.4 Replay

- A recording is chosen from the list and replayed. During replay, the camera and the provider stop, and the recording plays in a loop through `TrackingReplayProvider`. Stopping returns to the state before replay: the camera and the provider if tracking was running.
- Replay works while tracking is stopped, to reproduce a movement without a camera. Starting tracking for replay is not saved. Stopping tracking during replay stops the replay too.
- The processing and settings, such as smoothing, apply during replay as usual, so only the method being compared changes on the same movement.
- The file is read on another thread (about 15 MB for 5 minutes).
- Turning Developer Mode off stops replay too.

### 12.5 Keeping and Deleting

- Nothing is deleted automatically. Recordings serve as baselines for comparisons over several days, so only a developer deletes them (user's decision, 2026-10-22).
- One recording or all of them can be deleted, each after a confirmation dialog (Danger).
- A recording being replayed or recorded cannot be deleted; it is stopped first.
- Only `.vvtrack` files in the recordings folder whose names fit 12.3 are deleted. No path outside the folder is made from a name in the list.

### 12.6 Evaluation Values

Pure functions computed in EditMode (`TrackingMetrics`).

| Value | Meaning |
|---|---|
| Jitter | Size of joint angle changes during still intervals (RMS) |
| Lag | During fast movements, the time until the smoothed value catches up with the raw value (from cross-correlation) |
| Bone-length stability | Variation of bone lengths; an indicator of depth estimation quality |
| Joint-limit violations | Number of times a joint limit was exceeded |

### 12.7 The Repository and Developers' Recordings

- Recordings committed to the repository are only synthetic movements made for tests (fixed movements with added noise). A recording a developer makes of themself can be personal information including the face's movement, so it stays on that developer's machine and out of the repository (chapter 13).
- The Data Root is normally outside the repository (`%LOCALAPPDATA%`). In case a launch argument points it inside, `.gitignore` excludes `*.vvtrack`.

---

## 13. Security and Privacy

- The camera image shows the user's face and room. It never leaves the process and is not written to files, logs, or diagnostics. Reading for inference happens only inside reused buffers.
- The tracking camera image is not put on the stream (5.4).
- The skeleton comparison on the developer Diagnostics screen (UI foundation detailed design 7.7) shows the camera image with the face covered, and covers the whole image while the face's place is not known. The image is not recorded either.
- Observations (face points, expression values, body points) can also contain personal characteristics. Recording (chapter 12) happens only when a developer starts it on the Diagnostics screen, and goes to `Cache/TrackingRecordings/` in the Data Root. What a recording holds is not written to logs or included in diagnostics exports, and never leaves the PC. While recording, the Diagnostics screen shows "Recording" with an icon and text. Logs carry only the file name, the length, and the count.
- MediaPipe runs inference inside the process and does not communicate over the network. Model files are included in the distribution and are not downloaded at run time. The versions and hashes (SHA-256) of models and the plugin are pinned in the SBOM and the setup script (16.2).
- Inference failures (invalid input, plugin exceptions) are caught inside the provider and become `Error`; they do not crash the application.

---

## 14. Testing Strategy

| Kind | Target |
|---|---|
| EditMode | `OneEuroFilter`, `RotationFilter`: jitter decreases on a constant value, time to catch up with a step input, changing time intervals, rotations across ±180° |
| EditMode | `MediaPipeConversion`: the coordinate system (conversion to person space, left/right, up/down, toward the camera), point mapping, left/right assignment independent of handedness. Checked with synthetic data of known poses |
| EditMode | Depth estimation (8.4): depth from apparent length and bone length, sign hysteresis, re-choosing by joint limits |
| EditMode | `TrackingStateMachine`, outlier removal, `FrameSlot` (including hand-off across threads) |
| EditMode | Evaluation values (chapter 12): with synthetic movements, smoothing lag and jitter are within targets. The Voom method (30-frame Euler average) is implemented for comparison, and the product method has less lag on the same input |
| EditMode | `TrackingService` driven by the replay provider: start/stop, enabling/disabling parts, switching the range, saving settings |
| PlayMode | The plugin's test images (`lenna.png`, `thumb-up.jpg`, not copied into the repository) are given to `MediaPipeTrackingProvider` as a still camera: a face is found (only when the image is upright), it looks forward, a hand is found, and results keep coming (buffers are given back). Compiled only when the plugin is installed |
| Hardware | Face, body, and hands are tracked with a real camera, and left and right are right (checked by winking). Checked visually on a demo branch |
| Performance | `MediaPipeProviderTests.AllTasks_Timing` (`[Explicit]`): observations and their age with the three inferences together (chapter 15) |

- `tools/unity/run-tests.ps1` runs only the project's test assemblies under `Assets/VirtualVessel`. The plugin brings its own tests, and one crashes the Editor in batch mode. `[Explicit]` tests carry the `Explicit` category and run only when named.

---

## 15. Performance

- Targets (checked on the development PC, results recorded here):
  - Inferring face, body, and both hands together keeps up with the camera at 30 per second.
  - The median lag from the image frame to the update of `Current` (`Tracking/Latency`) is 80 ms or less.
  - Main-thread work (requesting readbacks, receiving, smoothing) is 0.5 ms or less per frame, with no per-frame allocation.
- On Windows, MediaPipe inference runs on the CPU (GPU paths are limited to Android and similar). If the targets are not met, the options are chosen by measurement:
  - Use the lighter Pose model (Lite).
  - Infer hands every other time (if hands affect perceived motion less than the face).
  - Compare with the Holistic Landmarker, which combines the three inferences (experimental in plugin 0.16).
  - Later, build a provider that infers on the GPU with Unity Inference Engine (formerly Sentis) (within system design 16.3).
- Inference runs on MediaPipe's threads and never makes the main thread wait.

#### Measured Results (Step 2, 2026-10-19, development PC)

`MediaPipeProviderTests.AllTasks_Timing`: a still image (512×512) given as a 30 fps camera, with Face, Pose (Full), and Hand together.

| Value | Result |
|---|---|
| From start to the first result | 0.69 s |
| Results | 90.3 per second (three parts per 30 fps image) |
| Age of an observation when taken | 13 ms on average, 92 ms at most |

- The three inferences together kept 30 fps. Switching to the Holistic Landmarker or inferring hands less often is not needed now.

A record of measuring with background removal (9.4, tried and removed), under the same conditions (2026-10-19).

| Background removal | From start to the first result | Results | Age of an observation |
|---|---|---|---|
| Off | 0.24 s | 89.8 per second | 15 ms on average, 57 ms at most |
| On | 0.23 s | 90.1 per second | 30 ms on average, 65 ms at most |

- Background removal added about 15 ms on average.
- Values with a real camera (1280×720) are recorded from `Tracking/Latency` and the others in the demo.

---

## 16. Directory / Namespace and Dependencies

### 16.1 Directory

```text
Assets/VirtualVessel/Tracking/
├─ Runtime/                                    VirtualVessel.Tracking
│  ├─ TrackingFrame.cs        TrackingFrame, TrackedPart, BodyPose, HandPose, FacePose
│  ├─ TrackingTypes.cs        BodyJoint, HandJoint, FaceShape, TrackingStatus, TrackingRegions, BodyRange, TrackingState, TrackingProblem, SmoothingLevel
│  ├─ ITrackingSource.cs      ITrackingSource, ITrackingProvider
│  ├─ TrackingService.cs      TrackingService (replay in TrackingService.Replay.cs)
│  ├─ TrackingStatusRules.cs  The state and problem shown to the user
│  ├─ TrackingOptions.cs      TrackingOptions (what the user chose)
│  ├─ TrackingSettings.cs     TrackingSettings, PoseModel
│  ├─ Processing/             OneEuroFilter (with PositionFilter, RotationFilter), SmoothingProfile, OutlierGate, TrackingProcessor, TrackingStateMachine, EventRate, ObservationCorrections (with IObservationCorrection), CameraAxisArmStraightening, HandDepthFromForearm, BodyFlattening
│  ├─ Threading/FrameSlot.cs
│  ├─ Recording/              ITrackingRecordings, TrackingRecordings, TrackingRecorder, TrackingRecordingFile, TrackingRecordingFormat (step 5), TrackingReplayProvider, TrackingMetrics, SyntheticMotion
│  └─ MediaPipe/MediaPipeConversion.cs   MediaPipe points to the common form (pure functions; no plugin types, so it lives here and is tested without the plugin)
├─ MediaPipe/                                  VirtualVessel.Tracking.MediaPipe (the only assembly that references MediaPipeUnityPlugin; compiled only with the plugin, through `VV_MEDIAPIPE`)
│  ├─ MediaPipeTrackingProvider.cs   Starting and stopping, and the flow from image to result
│  ├─ MediaPipeTasks.cs              Creating, calling, and closing tasks (the part that uses the plugin's task API)
│  ├─ MediaPipeResults.cs            Copying the plugin's results into arrays; choosing the person's face and hands among candidates (FacesObservation, HandsObservation)
│  ├─ MediaPipeTracking.cs           Creation for the composition root
│  ├─ CameraFrameReader.cs           Flipping on the GPU and AsyncGPUReadback
│  └─ ImageBufferPool.cs             Buffers not reused until MediaPipe releases them
└─ Tests/
   ├─ EditMode/
   └─ PlayMode/                                Only with the plugin (`VV_MEDIAPIPE`)
```

References: Core, Diagnostics, ProjectData, Capture. The MediaPipe assembly also references MediaPipeUnityPlugin (`Mediapipe.Runtime`) and uses `allowUnsafeCode` (to take the buffers' pointers).

### 16.2 Adding MediaPipeUnityPlugin

- The version is pinned to v0.16.3 (MediaPipe v0.10.22). It was confirmed to compile and run on Unity 6.6 (2026-10-19).
- The plugin is the `.tgz` published in the GitHub releases (about 290 MB) and is not committed, because it would weigh down Git history.
  - `tools/deps/fetch-mediapipe.ps1` downloads the pinned `.tgz` into `build/deps/`, checks its SHA-256, and unpacks it as an embedded package at `unity/VirtualVesselStudio/Packages/com.github.homuler.mediapipe/` (outside Git's control). Unity finds packages under Packages without a `manifest.json` entry, so a working copy without the plugin still opens (only the MediaPipe assembly is not compiled).
  - `packages-lock.json` lists it as an embedded package (version `file:`). The repository checks compare an embedded package with the version in `tools/deps/mediapipe.json`.
  - Local machines and the release script (`tools/release/build-release.ps1`) use the same script, the same idea as the native plugin (the DLL is not in Git and is produced by a procedure).
- The models (`face_landmarker_v2_with_blendshapes`, `pose_landmarker_lite` / `full`, `hand_landmarker`) are the ones in the plugin package. Their SHA-256 values are pinned in `tools/deps/mediapipe.json`; the script checks them and copies the files to `Assets/StreamingAssets/Tracking/MediaPipe/` (outside Git's control; part of the build's StreamingAssets).
- The distribution includes only the Windows x64 native library (`mediapipe_c.dll`), per the plugin's import settings.
- Licenses: the plugin is MIT; MediaPipe and the models are Apache-2.0. The libraries built into the plugin's native library (Abseil, OpenCV, Protocol Buffers, TensorFlow Lite, XNNPACK, and others, all permissive) are listed in the plugin's `Third Party Notices.md`, which the release package includes. Recorded in the SBOM (CLAUDE.md chapter 35).

#### Preparing for Updates

MediaPipeUnityPlugin keeps being updated (about nine months from v0.16.0 to v0.16.3). To make following updates easy:

- The version, download URL, and SHA-256 are written in one place, `tools/deps/mediapipe.json`. The script, CI, and the SBOM check read these values. An update is one change (a `chore:` branch) that changes these values and the SBOM.
- Only the MediaPipe assembly uses plugin types, and within it, the part that calls the plugin (creating tasks, `DetectAsync`, receiving results) is separate from the part that converts results to the common form (`MediaPipeConversion`). When the plugin API changes, the fix is limited to the part that calls the tasks.
- `MediaPipeConversion` is a pure function that takes arrays of points (positions, confidence), not plugin types. Its tests keep working when the plugin API changes.
- Model files have their own hashes, separate from the plugin's. If updating the plugin changes a model, the script stops and says so, so a model never changes unnoticed.
- Checking an update: EditMode and PlayMode tests (face, body, and hands found on still images), the `AllTasks_Timing` measurement, and a visual check with a camera are compared before and after the update, and the results go in the pull request.
- The update procedure is in development workflow 14.1.

---

## 17. Open Issues

| Item | Plan |
|---|---|
| The depth estimation method (8.4) compared with world landmarks | Decided: four methods were compared on recordings, and the correction straightening arms that point at the camera (D) was chosen (8.4; user's decision, 2026-10-22) |
| An arm out sideways read as forward | Not addressed. In the user's screen recording (2026-10-23), seen in the skeleton comparison (UI foundation detailed design 7.7), arms held straight out sideways were observed turned 71–72° forward. The arm's length in the image cannot tell forward from sideways (perspective cancels the shortening), so another cue is needed. The method is to be considered later (user's decision, 2026-10-24) |
| Default smoothing parameters | In step 4 the user can choose one of ten levels, with 5 as the default (8.2). On 2026-10-24 the levels moved two steps and then four more, making a level steady within about 300 ms lag the standard 5 (user's decision). The ratios between the parts' parameters are revisited with the body's movement |
| Switching to the Holistic Landmarker | Judged unnecessary (the three inferences together kept 30 fps, chapter 15). Revisited if a real camera is not enough |
| Left and right of MediaPipe's face values (6.3) | Assumed to be the person's sides (`MediaPipeTracking.FaceSidesMirrored = false`). Checked by winking in the demo |
| Gaze angle range (`MaxGazeDegrees`, 25°) | Avatar does not use the gaze angles of `FacePose`; it takes the face values (`EyeLook*`) as a share of the model's eye range (Avatar detailed design 7.4). So this value does not affect the avatar. To be decided when something needs the angles |
| Distance at which a face counts as away from the body (12% of the image width) | Provisional. Revisited if mistakes remain with a real camera |
| Adding full body (`FullBody`) | After v0.1.0 |
| Combining lip sync from voice with face tracking | When designing Voice / Audio |
| Moving tracking settings to Project / Profile | Done (2026-10-27): the project's tracking profile (project data detailed design) |

---

## 18. Implementation Order

Each step is one branch with its tests and a demo branch to try it (CLAUDE.md 29.2). The order together with Avatar is in Avatar detailed design chapter 18.

1. Tracking foundation: `TrackingFrame`, contracts, `OneEuroFilter`, `RotationFilter`, `FrameSlot`, status decisions, evaluation values and replay (synthetic movements)
2. MediaPipe: adding the plugin and models (SBOM), the Capture `camera` source, `TrackingService`, `MediaPipeTrackingProvider`, the camera and point display on the setup screen, measuring load and lag (done)
