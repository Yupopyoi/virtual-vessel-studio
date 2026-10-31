# 15. Non-functional Design

## 15.1 Basic Policy

This system is a real-time application that simultaneously runs 3D avatar control, real-time tracking, voice conversion, video generation, live streaming, etc.

Therefore, beyond each function simply working, the following are treated as important non-functional requirements:

- performance
- real-time behavior
- stability
- failure isolation
- extensibility
- maintainability
- security
- privacy
- data integrity
- compatibility
- observability
- ease of installation in user environments

Each function is not only optimized independently; the use of CPU, GPU, memory, audio, storage, network, etc. across the entire system is considered.

### Rationale

In this system, heavy processing such as

- Unity rendering
- tracking
- RVC
- video encoding
- audio processing
- streaming

runs simultaneously.

If performance is designed only on the basis of individual functions, frame drops or audio dropouts may occur on integration even if each works fine alone.

Therefore, the entire system is treated as one real-time pipeline.

---

## 15.2 Performance

Performance is judged not only by raw processing speed but by whether each runtime pipeline can complete its processing within the required time.

The main monitoring targets are:

- rendering frame time
- tracking processing time
- voice conversion processing time
- capture processing time / capture frame rate
- audio buffer state
- video encoding time
- streaming queue
- memory usage
- GPU usage
- CPU usage

These can be checked from developer diagnostics as needed.

### Rationale

A single metric such as CPU usage cannot judge the quality of real-time processing.

For example, even if CPU usage is low, a momentary delay in just one process can cause audio dropouts.

Therefore, processing time per pipeline is also measured.

---

## 15.3 Real-time Behavior

Real-time behavior is especially emphasized for the following processing:

- tracking
- avatar pose
- face expression
- voice conversion
- audio mixing
- GameCapture / SubScreenCapture
- rendering
- video encoding

For each process, not only the average processing time but also the variation in processing time is considered.

### Rationale

In real-time streaming, even if average performance is sufficient, quality degrades if extremely slow frames recur.

Therefore, the structure allows checking

- average values
- maximum values
- latency distribution
- occurrence of drops

and so on.

---

## 15.4 Real-time Audio Performance

Audio processing is more susceptible than video processing to momentary processing stalls, so real-time behavior is secured with priority.

In the voice conversion pipeline including RVC, end-to-end latency is treated as the main performance metric.

The current design uses **within 200 ms** as one reference for the processing deadline of real-time voice conversion.

However, rather than measuring only the total latency, it can be broken down and measured into

- input buffer
- feature extraction
- pitch estimation
- RVC inference
- post-processing
- output buffer

and so on.

### Rationale

Making the causes of latency identifiable makes it easier to decide which processing to improve when performance degrades.

Also, the structure in which audio processing is not stalled by other UI or scene processing is maintained.

---

## 15.5 Real-time Video Performance

For GameCapture / SubScreenCapture, rendering, video encoding, and streaming, the goal is to continuously maintain the configured frame rate.

When frame generation cannot keep up, that state is detected and

- capture dropped / missed frames
- dropped frames
- encode delay
- streaming queue growth

and so on are recorded in developer diagnostics.

### Rationale

If video processing delays accumulate, the stream may gradually fall behind real time.

Therefore, maintaining real-time behavior is prioritized over a structure that keeps accumulating processing delay.

---

## 15.6 Audio and Video Synchronization

Audio and video are synchronized based on a common time reference.

When latency arises on the audio side due to voice conversion, etc., the A/V synchronization function defined in Chapter 10 delays the video side as needed.

The A/V offset can be measured for the synchronization state.

### Rationale

Even if audio and video are each processed in real time, if their latencies differ, viewers perceive a mismatch between mouth movements and audio.

Therefore, not only individual performance but also the final synchronization quality is treated as a non-functional requirement.

---

## 15.7 Limiting Main Thread Load

Time-consuming processing is not concentrated on the Unity main thread.

Examples:

- file I/O
- downloading external OSS
- starting Python services
- model loading
- dataset processing
- network communication
- long-running analysis

Where possible, processing is separated into asynchronous processing or appropriate worker processing.

### Rationale

If the main thread is occupied for a long time,

- the UI freezes
- rendering stops
- tracking updates stop

and so on.

In the streaming runtime in particular, one heavy process must not stop the entire system.

---

## 15.8 Resource Management

Each function is designed considering the resource usage of CPU, GPU, memory, etc.

For the GPU in particular, it may be used simultaneously by

- Unity rendering
- tracking
- RVC
- video encoding

and so on.

Each function does not keep holding large amounts of GPU resources individually.

### Rationale

Because the GPU is shared for multiple purposes in this system, optimizing a single function alone cannot guarantee the performance of the whole system.

Resource usage can be shown in developer diagnostics as needed.

---

## 15.9 Releasing Unneeded Resources

Unused resources such as the following can be released at appropriate times:

- unused avatars
- unloaded stages
- voice models no longer in use
- diagnostic render targets
- capture textures / native capture resources
- Python services
- temporary audio buffers
- preview resources

### Rationale

In an application used for long streams, even a small resource leak can become a major problem after several hours.

Long-term operation is assumed, not just correct behavior over a short time.

---

## 15.10 Stability

Even if an abnormality occurs in some functions, the entire application is not stopped as far as possible.

For example,

- streaming connection failure
- recording failure
- voice conversion failure
- tracking lost
- Python service stopped

and so on are handled individually.

### Rationale

If the entire application exits during a stream because of the failure of a single function, the impact is large.

Therefore, failures of each module are confined within that module as far as possible.

---

## 15.11 Failure Isolation

Major modules cooperate through clear interfaces and data boundaries rather than directly rewriting internal state.

Conceptually, this is handled as follows.

```text
Tracking Failure
      ↓
Tracking Module Error
      ↓
Avatar Runtime can continue

Streaming Failure
      ↓
Streaming Module Error
      ↓
Avatar / Voice / Recording can continue
```

### Rationale

When modules are tightly coupled, a single exception tends to cascade into multiple functions.

Clarifying responsibility boundaries limits the scope of failure impact.

---

## 15.12 Graceful Degradation

Even when some functions are unavailable, degraded operation is performed where possible.

Examples:

- RVC unavailable → pass-through or mute
- hand tracking unavailable → body tracking only
- face tracking unavailable → body tracking only
- YouTube connection failure → continue local recording
- capture device disconnected → stop only the affected capture source
- diagnostic function failure → normal runtime continues

When the degradation method requires the user's choice, it is configurable.

### Rationale

This avoids a structure in which the application cannot be used unless every function is completely healthy.

---

## 15.13 Recovery

For recoverable failures, recovery methods that do not require restarting the entire application are provided where practical.

Examples:

- streaming reconnection
- tracking provider reinitialization
- audio device reinitialization
- voice model reload
- Python service restart

### Rationale

Even if a temporary failure occurs during a long stream, recovery is possible while limiting the scope of impact as far as possible.

---

## 15.14 Long-term Operation

This system assumes continuous streams of several hours or more.

For long-term operation, the following are considered:

- memory leaks
- GPU memory leaks
- buffer accumulation
- growth in log size
- growth in temporary files
- network reconnection
- device disconnection / reconnection

### Rationale

Problems that cannot be detected in short tests may surface during long-term operation.

Therefore, system tests also include long-run tests.

---

## 15.15 Extensibility

The basic structure is one in which adding new functions does not significantly change existing modules.

Examples:

- new 3D model formats
- new tracking providers
- new voice conversion engines
- new streaming services
- new video encoders
- new locales
- new stage formats
- new external input devices

### Rationale

This system is expected to support more external technologies in the future.

Therefore, the specific technologies currently used are not placed at the center of the system.

---

## 15.16 Interfaces and Adapters

Interfaces or adapters are provided at boundaries with external technologies as needed.

Examples:

```text
IAvatarLoader
ITrackingProvider
IVoiceConverter
IVideoEncoder
IStreamPublisher
```

External OSS is also handled basically through adapters / wrappers.

### Rationale

This prevents changes in external technologies from spreading to the entire system.

---

## 15.17 Resilience to External OSS Updates

Custom modification of external OSS itself is avoided as far as possible.

For RVC, etc., the following defined in Chapter 13 are used:

- version pinning
- adapters
- version separation of source + venv
- parallel installation of new versions
- rollback

### Rationale

Heavily modifying external OSS makes following upstream updates difficult.

A clear boundary between external code and project-owned code is maintained.

---

## 15.18 Maintainability

Responsibilities are separated per function, and processing is not concentrated in giant managers or controllers.

The direction of dependencies between modules is made clear.

As far as possible,

- core data
- interfaces
- runtime
- setup
- adapters
- UI

and so on are separated.

### Rationale

This makes it easier to understand the scope of impact when adding or fixing functions.

---

## 15.19 Code Readability

Code is valued not only for working but for allowing its responsibilities and design intent to be understood later.

In particular, necessary comments and documentation are left for

- class responsibilities
- public APIs
- complex control
- non-obvious processing
- boundaries with external OSS

### Rationale

As OSS, the code may be read by multiple developers, and continuous changes by automated development agents are also expected.

---

## 15.20 Testability

Each module is structured to be testable independently as far as practical.

Tests are broadly classified as follows.

```text
tests/
├─ unit/
├─ integration/
├─ system/
└─ performance/
```

Tests that naturally belong to a specific component or Unity assembly may be placed with that component. The tree above shows where other tests are placed (see 3.3).

### Unit Test

Verifies individual processing.

### Integration Test

Verifies cooperation between multiple modules.

### System Test

Verifies, as the actual Unity application, an integration of

- tracking
- avatar
- voice
- streaming

and so on.

### Performance Test

Measures the performance of real-time processing, such as RVC latency, capture performance, frame time, and long-run operation.

In addition to the performance regression tests in 15.21, hardware-dependent measurements run in the nightly / hardware CI (self-hosted runner) defined in the development workflow.

Because Unity tests can only be placed inside the Unity project, the performance tests that run in Unity are placed in each module's `Tests/Performance/`. `tests/performance/` holds the scripts for hardware CI, baselines for comparison, and result history. Historical benchmark / prototype implementations are kept outside the repository as references.

### Rationale

A structure that can only be verified with the entire Unity application makes isolating the causes of failures difficult.

Separating performance measurement from correctness tests also allows normal PR CI and performance measurements that require real hardware to run separately.

---

## 15.21 Performance Regression Tests

For processing where real-time behavior is important, not only correct behavior but also performance degradation can be detected.

Examples:

- voice conversion latency
- tracking processing time
- render frame time
- video encoding time

### Rationale

Refactoring or adding functions may worsen only the processing time while the results stay the same.

In a real-time application, performance degradation is also treated as a regression.

---

## 15.22 Basic Security Policy

The basis is not to trust external input or secrets.

Examples:

- project import
- avatar files
- stages
- audio files
- external component downloads
- streaming credentials
- Python processes
- local APIs

Necessary validation is performed on input values and files.

### Rationale

This application is distributed as OSS, and users may load files obtained from outside.

This prevents invalid or corrupted input from destabilizing the entire system.

---

## 15.23 Protecting Secrets

The following information is treated as secret:

- stream keys
- OAuth tokens
- API keys
- other credentials

Secrets are not output to

- normal JSON
- logs
- diagnostics exports
- displays intended for screenshots
- the Git repository

For storage, the OS credential store, etc. defined in Chapter 6 is used.

### Rationale

In OSS development, logs and configuration files may be attached to issues, etc.

Separating secrets from normal data reduces the risk of leakage.

---

## 15.24 Safety of External Downloads

When external components such as RVC are obtained automatically, the source and version are managed in a manifest.

Verification with hashes, etc. is performed as needed.

### Rationale

With automatic setup, users are not aware of what is actually being obtained.

Therefore, the application can confirm that it is obtaining the intended component.

---

## 15.25 Exposure Scope of Local Services

Python local services, etc. are, as a rule, not made accessible from external networks.

Unless necessary, they listen only on localhost.

### Rationale

There is normally no need to expose training / analysis services to the LAN or the internet.

The attack surface is not increased unnecessarily.

---

## 15.26 Basic Privacy Policy

This system handles data important to users, such as

- camera video
- screen video obtained by GameCapture / SubScreenCapture
- microphone audio
- voice datasets
- voice models
- tracking data

The basis is not to send these externally unintentionally.

### Rationale

Real camera video and voice datasets in particular have a large impact if sent externally.

Functions that can be processed locally are processed locally as the basis.

---

## 15.27 Privacy of the Diagnostic Camera

The diagnostic camera and camera input video are structurally separated from the streaming pipeline as defined in Chapters 10 and 14.

### Rationale

This prevents real camera video or tracking debug displays from being sent to YouTube by an operational mistake.

The separation is achieved by the pipeline structure, not merely by a setting.

---

## 15.28 Privacy of Diagnostic Information

Developer diagnostics and diagnostics exports do not output the following as-is:

- stream keys
- OAuth tokens
- API keys
- passwords
- unnecessary camera images
- unnecessary microphone audio

For necessary paths, etc., masking is considered in the future where they may contain personal information.

### Rationale

When attaching diagnostic information to an OSS issue, users may publish it without checking the contents in detail.

---

## 15.29 Data Integrity

When updating important data such as projects, profiles, and the Voice Lab database, approaches that are as resistant to corruption as possible are adopted even if the application exits midway.

For configuration file updates, as needed,

```text
Temporary File
      ↓
Validation
      ↓
Atomic Replace
```

and similar approaches are used.

### Rationale

If the application exits while writing a configuration file, even the original data may be lost.

---

## 15.30 Safety of Backup and Migration

Processing that changes large amounts of data, such as migration and Data Root moves, does not immediately discard existing data.

The switch to new data happens after conversion, copying, and validation are complete.

### Rationale

This prevents a migration failure from losing even existing data that was working correctly.

---

## 15.31 Compatibility

Even after application updates, the structure allows projects, profiles, etc. from older versions to be loaded as far as practical.

The schema versions and migration defined in Chapter 6 are used.

### Rationale

A structure that requires recreating existing projects with every application update places a large burden on users.

---

## 15.32 Compatibility with External Environments

The following environmental differences are considered:

- GPU
- GPU driver
- display resolution
- audio devices
- camera devices
- capture boards
- display / monitor configuration
- Desktop Duplication support
- CUDA environment
- Python environment
- external OSS versions

When using functions that exist only in specific environments, capability is checked at startup or during setup.

### Rationale

A configuration that worked in the development environment is not necessarily usable in every user environment.

Rather than failing only when a function is used, problems are detected in advance where practical.

---

## 15.33 Capability Detection

The availability of hardware and runtime functions can be detected.

Examples:

- GPU availability
- hardware video encoder
- camera
- microphone
- capture board
- SubScreenCapture / Desktop Duplication
- tracking provider
- Python service
- external component

The UI does not simply let unavailable functions fail but shows them as states such as

- unavailable
- not set up
- not supported

### Rationale

Explicitly managing environment-dependent functions makes it easier for users to understand the cause of problems.

---

## 15.34 Observability

Important internal states of the system are observable from developer diagnostics.

Examples:

- frame time
- tracking FPS
- tracking confidence
- voice latency
- audio buffers
- encoder time
- dropped frames
- A/V offset
- memory
- GPU
- external service state

### Rationale

Problems in real-time systems often do not appear as simple exceptions.

Making internal state observable makes it easier to analyze performance problems and temporary anomalies.

---

## 15.35 Logging

Logs have at least the following levels.

```text
Trace
Debug
Information
Warning
Error
Critical
```

In the normal runtime, debug logs are not output in larger volumes than necessary.

The level of detail can be changed through Developer Mode, etc.

### Rationale

Always outputting large volumes of logs increases storage usage and I/O load.

On the other hand, detailed logs are needed for failure analysis, so they are switched according to purpose.

---

## 15.36 Log Rotation

Log files are prevented from growing without limit.

Approaches such as the following can be used:

- rotation by file size
- rotation by date
- number of retained generations
- automatic deletion of old logs

### Rationale

This prevents logs alone from exhausting storage through long sessions or long-term use.

---

## 15.37 Ease of Installation

General users are not required, as part of normal use, to perform

- Git operations
- installing Python / choosing a Python version
- creating / activating a venv
- pip install
- internal RVC settings
- port settings

and so on.

Required Python components are provided as built runtime packages, and the structure allows them to be obtained, verified, and registered from the application's setup function.

### Rationale

Even though the system uses multiple technologies internally, the basis is not to expose that complexity to general users.

---

## 15.38 Minimizing Runtime Dependencies

The streaming runtime can operate with as few external dependencies as possible.

In particular, even if the Python environment for Voice Lab is unavailable, the structure keeps normal streaming possible using already registered

- avatars
- stages
- voice models
- projects

### Rationale

This prevents problems in the setup or training environment from making even normal streaming impossible.

---

## 15.39 Maintaining Quality with Claude Code

In development with Claude Code, existing non-functional requirements are checked when adding functions.

In particular, the following are review targets:

- module boundaries
- main thread blocking
- resource leaks
- error handling
- security
- secret output
- localization
- performance
- tests
- documentation

Necessary rules are written in `CLAUDE.md` and each design document.

### Rationale

Even in automated development, it is necessary to prevent non-functional requirements from degrading while only functional requirements are satisfied.

---

## 15.40 Quality Checks by the User

Final quality is not judged only by automated tests and review by Claude Code.

In the actual usage environment, the user also checks

- voice quality
- tracking quality
- UI
- latency
- streaming
- stability

and so on.

For functions where perceived quality is especially important, human evaluation is included in the final judgment.

### Rationale

Audio quality, the naturalness of the avatar, the feel of UI operation, etc. cannot be fully evaluated with numerical metrics alone.

---

## 15.41 Non-functional Tests

Before release, the following are verified as needed:

- long-run operation
- memory usage
- GPU memory usage
- voice latency
- frame rate
- A/V sync
- continuous acquisition with GameCapture / SubScreenCapture
- behavior on capture device / monitor disconnection
- streaming reconnection
- device disconnection / reconnection
- project migration
- Data Root move
- external OSS setup
- Japanese / English UI
- non-output of secrets

### Rationale

Simple normal-path functional tests alone cannot detect problems caused by actual long streams or environmental differences.

---

## 15.42 Basic Principles of Non-functional Design

This system adopts the following basic principles for non-functional requirements.

1. Consider the real-time performance of the whole system, not each function alone.
2. Protect time-constrained processing such as audio, tracking, and rendering from heavy processing on the main thread.
3. Isolate failures per module.
4. Perform degraded operation as far as possible when some functions fail.
5. Prevent resource leaks on the assumption of long streams.
6. Use external technologies through interfaces / adapters.
7. Minimize changes to external OSS itself.
8. Treat performance regressions as defects too.
9. Prevent corruption of projects and configuration data.
10. Do not output secrets to normal settings, logs, or diagnostic information.
11. Protect the privacy of camera, microphone, datasets, etc.
12. Do not expose local services to external networks unnecessarily.
13. Maintain compatibility with old projects and profiles through migration.
14. Handle hardware / device differences with capability detection.
15. Make internal state sufficiently observable through developer diagnostics.
16. Do not accumulate logs without limit.
17. Do not require general users to deal with internal complexity such as Git or Python environment setup.
18. The normal streaming runtime remains usable even if the training environment is unavailable.
19. Include non-functional requirements in reviews even when Claude Code implements.
20. Perceived quality is also checked by the user.
21. Treat capture processing load, frame drops, and device disconnection as non-functional requirements.
22. Confine capture-backend-specific constraints inside capability detection and adapters.
