# Tracking Processing: Decisions and History

Why tracking's smoothing, outlier removal, depth corrections, and 2D body (tracking detailed design chapters 8 and 9) are as they are: the methods compared, the measurements, and what was tried and dropped. The detailed design keeps only the current behavior and links here. Entries are the design's own text when it was moved here (2026-10-27); newer decisions are added below.

## From [tracking.md](../detailed-design/tracking.md), as of 2026-10-27

### 8. Smoothing and Accuracy

Voom's average was needed to suppress jitter. The product separates the causes of jitter and suppresses each with a low-lag method. Each method becomes the default only after replaying recorded observations and comparing numbers (chapter 12) shows less lag than the Voom method (a 30-frame average) with equal or less jitter (CLAUDE.md chapter 33).

#### 8.1 Causes of Jitter and Countermeasures

| Cause | Countermeasure |
|---|---|
| Small fluctuations of inferred values (moving slightly every frame even when still) | One Euro filter (8.2): strong smoothing when still, weak when moving fast |
| Inference at 30 per second and rendering at 60, so the avatar moves only every other frame and steps are visible | For each rendered frame, interpolate between the two latest observations (Avatar detailed design 7.3). Adds at most one observation interval (about 33 ms) |
| Occasional large outliers (an arm jumping for a moment) | Discard values where a joint moved faster than a person can, or with low confidence, and use the previous value (8.3) |
| Unstable depth (values toward and away from the camera swing widely) | Re-estimate depth from image positions and bone lengths (8.4) |
| Wrong rotation averages (averaging Euler angles breaks across ±180°) | Handle rotations as quaternions; never average Euler angles |

#### 8.2 One Euro Filter

- Chooses the strength of a low-pass filter each time from how fast the value changes. Slow movement is smoothed strongly, fast movement catches up quickly. Two parameters: the minimum cutoff (`MinCutoff`) and how much speed relaxes it (`Beta`).
- Applied per axis for positions and per value for expression values. For rotations (head orientation and so on), the same idea is applied with spherical interpolation, using the angle of the quaternion difference as the speed (`RotationFilter`).
- The joints of the body, and those of each hand, are smoothed together (`LinkedPositionFilter`, 2026-10-24): every joint moves the same share of the way each observation, with the cutoff of the fastest seen joint. The same low-pass filter on every joint keeps the shape, so a straight arm stays straight while it moves. With a cutoff per joint and axis, the slower elbow lagged behind the wrist, and an arm raised and lowered straight was shown bent (the user's screen recording). Joints not seen (`BodyPose.IsSeen`, 8.4), such as legs below the camera, do not set the cutoff, since their guesses jump about. The beta is half that of `Position` (`SmoothingProfile.Joints`), because the fastest joint in three axes is faster than one joint along one axis. Measured on the user's recordings at level 1 (`RecordedSmoothingTests`; the bend beyond any the arm showed in the second before): on "1024不自然解消用", observations bent past 15° went from 88 to 13 with the lag unchanged (383 ms) and the shake a little lower (1.07° to 0.99° per observation); on "腕を前へ伸ばす" from 24 to 0. The torso now shares the arms' cutoff while they move, and shook 0.18° a frame instead of 0.10° on the avatar.
- Each hand's joints use the same filter, but never steadier than 2 Hz while still (`SmoothingProfile.Hands`, `MinHandCutoff`, 2026-10-25). Fingers change shape by a few centimeters, too slowly to quicken the filter, so at the steady levels a hand's shape barely followed. At level 1 (0.003 Hz while still), a thumb tucked into a rock-paper-scissors fist for over a second was never seen tucked on the user's recording made for the hands, and the fingers stayed bent in an open hand. At 2 Hz it was seen in 121 of 128 observations (87 at 0.5 Hz, 108 at 1 Hz), 0.07 s late, while a finger's angle changed a median of 0.30° per observation (0.17°). Only the fingers' angles come from these positions; the palm and the arms have their own filters.
- Uses the time of the image frame, not the processing time, so uneven frame intervals do not change the smoothing strength.
- Parameters are per part (fast for the face, slow for the torso, and so on). The user sees ten levels of "movement speed" (1: steadiest, 10: fastest) (chapter 10, UI `setup.md` chapter 4). Each level changes the parameters from the standard set measured in step 1 toward the smooth set by the same ratio per level (half the ratio between the two sets). The smooth set falls at level 9 and the standard set at 11, so levels 1 to 10 are all on the steadier side. The default is 5 (standard). Face values are never slower than the smooth set, so blinks and the mouth keep up.
- History: at first the three sets (smooth, standard, responsive) were levels 3, 5, and 7 with 5 as the default (it was 7 for a while after a camera check, and went back to 5 after more checks, 2026-10-21). The arms were steadier at the former 3, so it became the standard (version 3). Then, with lag of about 300 ms acceptable and steadiness mattering more, the level 1 of that time became the standard (version 4; both the user's decisions, 2026-10-24). No requirement caps the lag (the 80 ms of chapter 15 is the processing delay from a video frame to `Current`, not smoothing lag).
- The computation is a pure function (it takes the state as an argument and returns a new state) and is checked in EditMode with inputs and outputs only.
- The speed of a rotation is smoothed as an angular velocity with a direction (a vector). Smoothing only the size of the angle (always positive) does not let shakes in opposite directions cancel, so a still value looks like it is moving (the first implementation shook about three times as much as the average when still).

##### Measured Results (Step 1, 2026-10-18)

Synthetic motion (an angle with 2° noise at 30 per second: still for 3 s, raised 90° in 0.3 s, held, waved ±30° at 0.5 Hz), compared over 20 noise seeds (`OneEuroComparisonTests`, `SmoothingProfile`).

| Method | Jitter when still (RMS of the per-frame change) | Lag | Error while waving slowly |
|---|---|---|---|
| Voom's method (30-frame average) | 0.097° | 483 ms | 23.2° |
| Smooth (MinCutoff 0.05 Hz, Beta 0.003) | 0.057° | 99 ms | 9.6° |
| Standard (0.1 Hz, 0.005) | 0.088° | 66 ms | 7.5° |
| Responsive (0.2 Hz, 0.01) | 0.160° | 56 ms | 5.2° |

- Standard is as steady as the average or steadier when still, with about 1/7 of the lag. A single noise draw can favor either method by about a third, so the test compares the average of 10 seeds.
- Positions (joints) use provisional values with beta converted at 1 cm ≈ 2° for an arm (Standard: 0.1 Hz, 1.0). For an arm movement with 1 cm noise, the angle jitter went from 4.7° to 0.13° with 100 ms lag. Face values are provisional too and are tuned in steps 2–5 on movements recorded with a camera (chapter 17).

##### Measuring the Ten Levels (step 4, 2026-10-21)

The same synthetic motion, compared over 10 noise seeds (`FilterTests.EachLevel_IsLessSteady_ButLessBehind_ThanTheOneBelow`). Lag is measured in steps of one observation (33 ms). Avatar's interpolation between observations adds one observation on top (Avatar detailed design 7.3).

| Level | Still jitter | Lag |
|---|---|---|
| 1 | 0.041° | 133 ms |
| 2 | 0.049° | 133 ms |
| 3 (formerly smooth) | 0.059° | 100 ms |
| 4 | 0.073° | 100 ms |
| 5 (formerly standard, default) | 0.091° | 67 ms |
| 6 | 0.121° | 67 ms |
| 7 (formerly responsive) | 0.162° | 60 ms |
| 8 | 0.217° | 33 ms |
| 9 | 0.290° | 33 ms |
| 10 | 0.386° | 33 ms |

##### After Shifting the Levels (2026-10-24, version 3)

The user found the arms steadier at the former level 3 and asked for it as the standard, with steadier levels added. The levels moved two steps: the three measured sets (smooth, standard, responsive) are levels 5, 7, and 9, and levels 1 to 4 extend past them toward steadier by the same ratio. The synthetic motion (as above), and the right upper arm's lag on the user's recordings (`RecordedSmoothingTests`, Explicit; 30 observations a second):

| Level | Still jitter | Lag (synthetic) | Lag (recording "arms forward and sideways") | Lag (recording "arms forward") |
|---|---|---|---|---|
| 1 | 0.029° | 177 ms | 257 ms | 165 ms |
| 2 | 0.035° | 167 ms | | |
| 3 | 0.041° | 133 ms | 202 ms | 129 ms |
| 4 | 0.049° | 133 ms | | |
| 5 (standard, default; formerly 3) | 0.059° | 100 ms | 147 ms | 92 ms |
| 6 | 0.073° | 100 ms | | |
| 7 (formerly 5) | 0.091° | 67 ms | 110 ms | 74 ms |
| 8 | 0.121° | 67 ms | | |
| 9 (formerly 7) | 0.162° | 60 ms | 74 ms | 37 ms |
| 10 | 0.217° | 33 ms | | |

##### After Making the Standard Steadier, up to About 300 ms Lag (2026-10-24, version 4)

The levels moved four more steps (version 3's 1 is the standard 5). Lag on the recordings was measured again, searching up to 30 observations (one second). Avatar's interpolation between observations adds one observation (33 ms) to it.

| Level | Still jitter | Lag (synthetic) | Lag (recording "arms forward and sideways") | Lag (recording "arms forward") |
|---|---|---|---|---|
| 1 | 0.014° | 303 ms | 460 ms | 312 ms |
| 2 | 0.017° | 267 ms | | |
| 3 | 0.021° | 233 ms | 349 ms | 221 ms |
| 4 | 0.025° | 200 ms | | |
| 5 (standard, default; version 3's 1) | 0.029° | 177 ms | 276 ms | 165 ms |
| 6 | 0.035° | 167 ms | | |
| 7 | 0.041° | 133 ms | 221 ms | 129 ms |
| 8 | 0.049° | 133 ms | | |
| 9 (the smooth set) | 0.059° | 100 ms | 165 ms | 92 ms |
| 10 | 0.073° | 100 ms | | |

- Tried and dropped (2026-10-24): an "ultra fast" mode apart from the levels. Smoothing was weakened to four times the responsive set (the right upper arm lagged 0–37 ms on the recordings and kept about 70% of the raw shake), and Avatar stopped interpolating between observations, showing each at once. The user checked it with the camera and dropped it as far too unsteady.

#### 8.3 Outlier Removal

- The palm's direction likewise: a value turned faster than 1800° a second (60° in an observation at 30 a second) is not used, and the third in a row is accepted (`TrackingProcessor.MaxPalmDegreesPerSecond`). On the user's recordings the palm turned a median of 3–6° per observation, and when read the other way round it jumped past 90° up to 174°, often coming back within 2–5 observations (2026-10-24).
- For each joint, a value whose speed from the previous value exceeds a limit (for example 8 m/s for a wrist) is not used that time. If it keeps exceeding (a really fast movement), it is accepted from the third time.
- Points whose confidence (MediaPipe visibility, presence) is below the threshold are `LowConfidence`, and smoothing gets stronger.

#### 8.4 Depth (Toward and Away From the Camera) and Correcting Observations

With a single camera, depth is less certain than image position, and least certain for limbs pointing at the camera. In the user's camera check (2026-10-22), an arm thrust forward did not straighten on the avatar.

##### Methods Compared (Step 5, 2026-10-22)

On the user's three recordings (chapter 12), the median elbow bend (left arm / right arm) and the upper arm's jumps of more than 30° in one observation (before smoothing, all three recordings) were compared. In "arms forward" the arms are straight, so less bend is more correct; in the other two the arms really bend.

| Method | Arms forward | Hands clasped in front of the chest | Hands behind the head | Jumps |
|---|---|---|---|---|
| A: world landmarks as they are | 33° / 40° | 65° / 81° | 76° / 86° | 1 |
| B: x and y as they are, depth rebuilt from bone length (`√(L² − d²)`, its sign from z with hysteresis; the first plan) | 31° / 42° | 67° / 92° | 79° / 82° | 5 |
| C: x and y from the 2D image detections, scaled to meters by the shoulder width, depth from bone length | 78° / 59° | 103° / 117° | 122° / 124° | 2 |
| **D: only when both the upper arm and the forearm point toward the camera, move the elbow toward the line from the shoulder to the wrist** | **6° / 0°** | 65° / 81° | 76° / 81° | 3 |

- B did not help because the world landmarks' x and y themselves bend the arm; correcting depth alone does not straighten it.
- In C, an arm pointing at the camera looks short in the image, so small detection errors became large bends.
- The bend appears when both the upper arm and the forearm point toward the camera (0.73–0.93 along its axis in "arms forward", 0.3–0.5 with the hands clasped).
- **D is used** (user's decision, 2026-10-22). The elbow starts moving when how far both parts point toward the camera (the smaller cosine to its axis) reaches 0.5, and the arm is fully straight at 0.8, blended smoothly in between. The shoulder and the wrist stay, so the hand does not move. An arm that really bends while pointing at the camera is straightened too; that direction is a single camera's weakest anyway, and showing it straight was preferred. Nothing is corrected while the shoulder, elbow, or wrist is unseen (visibility below 0.5).
- With D, arms near straight left the bend plane undefined and the upper arm's twist unsteady (jumps rose to 5, mostly twist). Avatar raised the bend at which the twist follows the bend plane from a sine of 0.10–0.35 to 0.25–0.50, bringing them to 3 (avatar detailed design 7.2).

##### How Observations Are Corrected (Extensibility)

Corrections are common Tracking Processing, not per input method (provider) (system design 8.10, 8.11). So that inputs such as mocopi can be added later without changing them, they are split as follows.

- **A provider states what kind of observation it makes (`ObservationTraits`)** and holds no corrections. MediaPipe is `MonocularDepth`: depth estimated from one camera's image, least reliable for limbs pointing at the camera. An input that observes with sensors on the body, such as mocopi, has no camera axis, so it is `None` and gets no camera corrections.
- **Each correction is an `IObservationCorrection`**, with the traits it applies to (`Requires`). For now there are two, `CameraAxisArmStraightening` (D) and `HandDepthFromForearm` (below), applied in that order.
- **`ObservationCorrections` picks from the list of corrections (`Catalog`) those that suit the active provider's traits.** `TrackingService` corrects a copy of the provider's observation and passes it to smoothing (8.2). It picks again when the provider changes (when a replay starts and ends).
- **Recordings (chapter 12) keep the observation before correction.** A replay picks the same corrections again from the recorded input's traits, so corrections can change and be compared without filming again. The recording's header keeps the input's traits (12.3).
- A new correction only implements `IObservationCorrection` and joins the `Catalog`. An input that needs a new trait adds one value to `ObservationTraits`.
- Whether a joint is seen is decided once, in `BodyPose.IsSeen`, used by the corrections and by Avatar alike: its visibility at least 0.5 (`BodyPose.MinVisibility`) and its position inside the camera image (0 to 1). MediaPipe also guesses joints outside the image and may call them visible: on the user's recordings, a wrist outside the image was visible past 0.5 in up to all of those frames, and moved the avatar's arm where the person's was not (the user's report, 2026-10-24). Such a joint is not seen, as a hidden one is.

##### Giving the Hand the Forearm's Depth (HandDepthFromForearm, step 6, 2026-10-24)

- The hands and the body are inferred apart, each guessing its own depth, and they disagree. On the user's recordings the hand's direction (wrist to the base of the middle finger) was a median of 24–63° off the forearm's in 3D, but only 4–26° across and up (as seen from the front). The avatar's wrists bent where the person's did not (the user's screen recording).
- The hand keeps its bend from the forearm as seen from the front (across and up), and leans toward or away from the camera as the forearm does. The back of the hand is kept at right angles to that. The angle across and up is taken from the 3D points' X and Y, so the image's aspect ratio is not needed.
- When the hand shows less than 0.1 of its length from the front, its bend there is not used; at 0.3 it is used fully (smoothly in between). A hand pointing at the camera looks short from the front, and its direction there is unreliable.
- Nothing is corrected when the elbow, the wrist, or the hand is not seen.

For fingers, the joint **angles** (the angle between neighboring bones) are primary. Angles do not depend on hand size or distance from the camera, and depth errors affect them less than positions. Finger flexion is limited to 0 to about 90°, and spreading to a small range (Avatar detailed design 7.5).

#### 8.5 Reference Pose (Input Correction, System Design 8.11)

Not adopted (the user's decision, 2026-10-26). Step 7 tried taking the person's front with the camera to one side: "Face the front" took how far the person was turned (from the shoulders, or the head in 2D) and turned every observation back by it about the vertical before smoothing. The avatar then faced forward, but as soon as the person moved, the torso and the head leaned to one side and the arms pointed off (the user's video). A camera to one side sees little of the far side of the body, and its depth errors lie along its own axis; turning the observation back moves those errors into the body's left, right, and lean. Turning cannot add what the camera does not see.

- The camera goes in front of the person, such as on top of the monitor; the README says so.
- Not done either: bone lengths and a reference head position (the depth correction of 8.4 does not need them).
- Correction to fit the avatar's body (different arm lengths and so on) is done by Avatar (system design 7.6).

#### 8.6 The Body in 2D (BodyDepth)

The camera sees across and up well and depth poorly (8.4). For when depth errors make the arms point unsteadily, 2D can be chosen, using only the movement seen from the front (the user's request, 2026-10-24).

- `TrackingOptions.BodyDepth`: `Tracked` (3D, the default) or `Flat` (2D). Switched on the tracking Card of the setup screen (UI `setup.md` chapter 4). Saved as `bodyDepth` in `tracking-settings.json` (0: 3D, 1: 2D).
- In 2D, after the corrections (8.4) and before smoothing, every body joint is put at the hips' depth (0 in person space) (`BodyFlattening`), except the elbows, which go 10 cm in front, and the wrists 5 cm further. With the forearm level with the elbow, a forearm swung from up to down passed straight through the line of the upper arm, its bend turned over at once, and the arm twisted up to 120° in a frame on the recordings replayed in 2D; leaning a little forward, it bends forward on the way, as in 3D. The body moves only across and up: the torso's lean and twist, and arms reaching forward or back, are gone.
- The arms always pass in front of the body (the user's request, 2026-10-24). With the elbows and wrists at the body's depth too, keeping the avatar's arms out of the torso (avatar detailed design 7.6) moved them behind it. 10 cm in front leans a hanging upper arm about 20° forward; that much depth is accepted (the user's judgment). On some builds the arm is still behind the torso's center, so in 2D Avatar also turns the arms only forward.
- Like the corrections, it works on a copy, and recordings (chapter 12) keep what was observed. A switch glides through the smoothing.
- The face (head turn, expressions) and the hands are not changed.
- An arm pointing at the camera looks short from the front, and its direction is easily set by the observation's jitter. In 2D this cannot be helped.
- Keeping the avatar's arms out of the head and torso (avatar detailed design 7.6) moves them in depth, so it works in 2D too.

### 9.4 Background Removal (Tried and Removed)

Against body points being pulled toward things in the background, cutting the person out (Selfie Segmentation) and painting the background gray before finding the face, body, and hands was tried (2026-10-19). With a real camera it made mistakes more frequent, so it was removed (the user's judgment).

- The person mask is coarse (144×256); the tips of hands and arms were likely painted as background, which disturbed the points. It also added about 15 ms of lag on average (chapter 15).
- At the same time, leaving out hands away from the body was tried and removed because hands flickered (6.4).
- When background mistakes are taken up again, options such as widening the mask around the hands or using it only for the body inference are compared on recorded movements (chapter 12) before deciding.
