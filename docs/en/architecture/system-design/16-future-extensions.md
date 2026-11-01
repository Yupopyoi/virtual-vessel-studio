# 16. Future Extension Policy

## 16.1 Basic Policy

This system does not aim to implement every function in the initial version and is designed on the assumption of future feature additions.

However, rather than implementing every function that might be used in the future from the initial stage, the basic policy is

**to provide appropriate boundaries where future changes are expected, so that functions can be added when they become necessary**

The main extension targets assumed are:

- multiple people / multiple avatars
- new tracking methods
- integration of multiple tracking sources
- new 3D model formats
- new capture sources / capture backends
- additional extension bone control methods
- new voice conversion approaches
- Voice Lab feature extensions
- new streaming services
- simultaneous output to multiple streaming destinations
- new video / audio codecs
- new external input devices
- new stage features
- new locales
- UI themes
- plugin mechanism
- external integration API

### Rationale

Predicting future functions and implementing all of them from the initial stage increases complexity that is not needed now.

On the other hand, a tightly coupled structure that assumes only current functions would require a large-scale redesign when adding future functions.

Therefore, the policy adopted is

**implement functions when they become necessary, but provide extensible boundaries in advance**

---

## 16.2 Support for Multiple People and Multiple Avatars

The initial implementation mainly targets use by one person, but extension to a structure that can handle multiple people simultaneously in the future is assumed.

Conceptually, the structure is as follows.

```text
Tracking Person A
      ↓
Avatar Runtime A

Tracking Person B
      ↓
Avatar Runtime B
```

In the future, the structure is expected to handle:

- multiple tracking persons
- multiple Avatar Runtimes
- assignment of persons to avatars
- individual spawn points
- individual voice settings
- individual tracking profiles
- individual camera configurations

### Rationale

Fully implementing multi-person control from the initial implementation increases complexity.

On the other hand, adopting structures in which only one person can exist in the whole system, such as

- a global CurrentAvatar
- a global TrackingFrame

would widen the scope of changes when supporting multiple people later.

Therefore, even if the initial version limits the count to one, the internal representation maintains a structure that can handle multiple instances.

---

## 16.3 Supporting New Tracking Providers

The initial implementation uses MediaPipe as the main tracking method, but different tracking providers can be added in the future.

Examples:

- mocopi
- other motion capture devices
- other camera tracking libraries
- VR devices
- tracking over a network
- smartphone sensors

Added providers convert into common tracking data through the tracking provider boundary defined in Chapter 8.

```text
New Tracking Device
        ↓
New Tracking Provider
        ↓
TrackingFrame
        ↓
Existing Avatar Pipeline
```

### Rationale

Instead of changing the Avatar module for each new tracking method, the existing pipeline can be reused simply by adding a provider.

---

## 16.4 Integrating Multiple Tracking Sources

In the future, configurations that combine multiple tracking sources will be possible.

Example:

```text
Camera
  ↓
Face / Upper Body

mocopi
  ↓
Body

Hand Tracking Device
  ↓
Fingers
```

Extension to an approach that integrates these and handles them as one tracking session is possible.

Between multiple sources,

- time synchronization
- coordinate system unification
- confidence
- priority
- filling in missing data

and so on are handled.

### Rationale

A single provider cannot necessarily capture the body, face, and fingers all with high accuracy.

Combining multiple inputs allows the strengths of each to be used.

---

## 16.5 Supporting New 3D Model Formats

The initially supported formats are VRM 1.0 and later (VRM 0.x is accepted by converting it to 1.0) and FBX.

When adding another 3D avatar format in the future, it is supported by adding an avatar loader as defined in Chapter 7.

Conceptually:

```text
IAvatarLoader
├─ VrmAvatarLoader
├─ FbxAvatarLoader
└─ FutureAvatarLoader
```

A new loader converts model-format-specific information into a common format usable by the Avatar Runtime and the avatar profile.

### Rationale

Not adding model-format-specific processing to existing functions such as pose, expression, and tracking limits the scope of changes when adding new formats.

---

## 16.6 Adding Extension Bone Control Methods

For the extension bones handled by the initial function, more diverse control inputs and motion approaches can be added in the future.

Examples of inputs that could be added:

- voice volume
- pitch
- BGM
- SE
- keyboard shortcuts
- external devices
- streaming events
- network events
- arbitrary parameters

Examples of motion approaches that could be added:

- animation
- physics
- procedural motion
- timeline
- script control
- event-driven

### Rationale

Extension bones have more freedom of use than standard humanoid bones.

Without fixing them to a specific control method, new input signals and motion approaches can be added in the future.

---

## 16.7 Supporting New Voice Conversion Approaches

The initial runtime uses RVC, but another real-time voice conversion engine can be added in the future.

The voice converter boundary defined in Chapter 9 is used.

```text
IVoiceConverter
├─ RvcVoiceConverter
├─ PassThroughVoiceConverter
└─ FutureVoiceConverter
```

Even when a new voice conversion approach is added, existing pipelines such as

- audio input
- post-processing
- audio mixing
- streaming

are reused as far as possible.

### Rationale

Voice conversion technology is likely to keep changing.

RVC itself is not the central concept of the system but one of the voice conversion engines currently used.

---

## 16.8 Extending Voice Lab

Voice Lab is also not a fixed UI dedicated to RVC and VoxCPM2.

The structure allows the following to be added in the future:

- new voice generation engines
- new voice clone approaches
- new training approaches
- new audio evaluation metrics
- automatic dataset evaluation
- model comparison
- automatic parameter search
- hyperparameter search
- training condition recommendations
- comparison with past training runs

The internal implementation of each step can be extended while maintaining the basic workflow

```text
Generate
   ↓
Clone / Select
   ↓
Dataset
   ↓
Train
   ↓
Evaluate
   ↓
Register
```

### Rationale

Even if voice generation and training technologies change, the user's goal of creating a voice model does not change much.

---

## 16.9 Supporting New Streaming Services

The initial target is YouTube Live, but the stream publisher is abstracted so that other streaming destinations can be added in the future.

Conceptually:

```text
IStreamPublisher
├─ YouTubePublisher
├─ FuturePublisherA
└─ FuturePublisherB
```

The

- rendering
- encoding
- A/V synchronization
- recording

of the streaming pipeline are kept independent of streaming services as far as possible.

### Rationale

This avoids rebuilding the entire video pipeline to add a streaming service.

---

## 16.10 Simultaneous Output to Multiple Streaming Destinations

In the future, a structure that sends one streaming video to multiple publishers may be considered.

Conceptually:

```text
Encoded Stream
      ↓
Stream Distribution
   ├─ Publisher A
   └─ Publisher B
```

A structure that allows choosing as needed among

- common encoding
- per-destination encoding
- per-destination bitrate
- per-destination resolution

and so on is considered.

### Rationale

Requirements may differ per streaming service, so room is left for per-destination adjustment rather than simple duplication.

---

## 16.11 Codec and Encoder Extension

New codecs and hardware backends can be added to the video and audio encoders in the future.

Examples:

- H.264
- H.265
- AV1
- other future codecs

Hardware encoders are also confined inside the encoder implementation without making the entire pipeline depend on a specific GPU vendor.

### Rationale

Codecs and hardware encoders may change in the future.

Therefore, the structure allows only encoders to be added or replaced without changing other parts of the streaming pipeline.

---

## 16.12 Extending Stage Features

The following stage features are expected to be added in the future:

- user-created stages
- stage packages
- externally distributed stages
- stage animation
- interactive objects
- stage-specific effects
- weather
- time of day
- dynamic lighting
- event triggers

However, the structure does not allow stages to freely operate on the inside of the application.

Operations that a stage requests from the system go through published interfaces, events, or commands.

### Rationale

When handling externally created stages, the boundary between stage scripts and the system itself is important.

The structure in which adding stages does not directly change the internal state of tracking, voice, etc. is maintained.

---

## 16.13 Supporting External Input Devices

In the future, operation devices other than the keyboard can be used.

Examples:

- Stream Deck-type devices
- MIDI controllers
- game controllers
- smartphones
- network controllers
- other shortcut devices

The command boundary defined in Chapter 14 is used.

```text
UI
Keyboard
External Device
Network
      ↓
Command
      ↓
Runtime
```

### Rationale

This avoids reimplementing runtime processing for each input method.

---

## 16.14 Extending UI Locales

The initial UI languages are

- Japanese
- English

Future languages are supported by adding a string table for the locale as defined in Chapter 14.

The structure does not require changing runtime processing or screen logic when adding a new language.

As needed, per locale,

- font assets
- theme style sheets
- text direction
- layout

and so on can be changed.

### Rationale

The localization function is not an implementation dedicated to Japanese and English; supported languages can be increased by adding localization resources.

---

## 16.15 Extending UI Themes

Using UI Toolkit theme style sheets and the design system, UI themes can be added in the future.

Examples:

- dark theme
- light theme
- high contrast theme
- custom theme

Implementing many themes in the initial implementation is not required.

### Rationale

If UI components use the common design system, the visual design can be changed without changing each screen individually.

---

## 16.16 Plugin Mechanism

In the future, a structure that allows some functions of this system to be added as plugins is considered.

The following are assumed as plugin candidates:

- tracking providers
- voice converters
- stream publishers
- external devices
- audio processors
- stage features
- developer tools

Conceptually:

```text
Application Core
       ↓
Plugin API
   ├─ Tracking Plugin
   ├─ Voice Plugin
   ├─ Streaming Plugin
   └─ Device Plugin
```

A complete plugin SDK is not required in the initial implementation.

The boundaries between internal modules are stabilized first and published as an external plugin API when it becomes necessary.

### Rationale

Building a general-purpose plugin system from the initial stage has large design and compatibility management costs.

It is safer to stabilize internal extension points first and then publish only the necessary scope.

---

## 16.17 Plugin Version Compatibility

If a plugin mechanism is introduced in the future, the plugin API version is made explicit.

Conceptual example:

```text
Plugin
├─ PluginId
├─ PluginVersion
├─ RequiredApiVersion
└─ Capabilities
```

The structure does not load incompatible plugins as-is.

### Rationale

This prevents old plugins from behaving unexpectedly inside the application when the plugin API changes due to updates of the application itself.

---

## 16.18 External Integration API

In the future, providing an API that allows external applications to operate this system is considered.

Examples:

- avatar switching
- stage switching
- camera switching
- SE playback
- BGM operation
- expression operation
- obtaining runtime state

Operations from the external API also use existing runtime operation paths such as the commands defined in Chapter 14.

```text
External Application
        ↓
External API
        ↓
Command
        ↓
Runtime
```

### Rationale

Reusing the existing runtime operation paths rather than creating operation processing dedicated to the external API unifies behavior.

---

## 16.19 Safety of the External API

If an external integration API is provided, it is not exposed unconditionally to external networks.

The initial state is localhost only, and when access from external networks is allowed, explicit settings and the necessary authentication methods are used.

### Rationale

A convenient external operation API is at the same time an attack surface.

The exposure scope is controlled according to the security policy in Chapter 15.

---

## 16.20 Extending Automation

In the future, a structure that can run automations combining multiple operations is considered.

Example:

```text
Scene Change Command
   ├─ Stage switching
   ├─ Camera switching
   ├─ BGM switching
   └─ Effect playback
```

Also,

- stream start
- stream end
- stage change
- specific shortcuts
- external events

and so on can be used as triggers.

### Rationale

If individual functions are unified as commands, it is easy to develop effects and automation that combine multiple commands.

---

## 16.21 Extending Project Features

For the projects defined in Chapter 6, the following can be added in the future:

- project templates
- project duplication
- project sharing
- project packages
- read-only projects
- project diffs
- profile sharing

### Rationale

As settings and assets increase, the burden of building a new project from scratch grows.

Mechanisms that allow existing projects to be reused can be added.

---

## 16.22 Asset Distribution and Sharing

In the future, a structure that allows the following assets to be shared with other users is considered:

- stages
- profiles
- UI themes
- extension motion settings
- audio settings
- other presets

Shared data has metadata such as

- version
- author
- compatibility
- dependencies

Secrets and personal data are not included in shared packages.

### Rationale

Allowing users and developers to share settings and assets as OSS increases the extensibility of the system.

---

## 16.23 Sharing Extension Motion

In the future, motion settings for extension bones can be saved and shared as presets.

For example, a structure that can save motion profiles tuned per model for

- ear motion
- tail motion
- wing motion
- ahoge
- accessories

and so on is assumed.

### Rationale

The motion of extension bones differs by user preference and model structure.

Separating motion settings from the model data itself makes tuning, reuse, and sharing easier.

---

## 16.24 Extending Developer Tools

For developer / diagnostics, the following can be added in the future:

- performance profiler
- tracking visualizer
- audio analyzer
- stream statistics
- runtime object inspector
- extension motion debugger
- plugin inspector

### Rationale

As functions increase, the means of checking internal state also need to be extended.

Developer tools are also treated as extensible functions separated from the normal runtime.

---

## 16.25 AI-assisted Setup

In the future, functions that automate or assist part of the configuration work may be considered.

Examples:

- avatar bone mapping candidates
- extension bone classification
- tracking adjustment value recommendations
- voice model comparison assistance
- training parameter recommendations
- UI setting assistance

However, automatic results are not applied unconditionally; users can check and correct the results.

### Rationale

As configuration items increase, the burden of setup work also increases.

Automation reduces the burden while maintaining a structure in which users make the final decisions.

---

## 16.26 Backward Compatibility

Even when adding functions in the future, existing

- projects
- profiles
- avatars
- voice models
- stages
- training history

remain usable as far as practical.

The schema versions and migration approach defined in Chapter 6 are used.

### Rationale

This avoids feature additions making existing user data unusable.

---

## 16.27 Deprecation

If it becomes necessary to retire old functions or interfaces in the future, the basis is not to delete them immediately but to treat them as deprecated for a certain period.

As needed,

- deprecation warnings
- migration methods
- alternative functions
- the version planned for removal

are made explicit.

### Rationale

As OSS, external plugins and user data may exist, and sudden removal has a large impact.

---

## 16.28 Separating Extension Functions from the Core

Even when adding future functions, functions are not concentrated in the application core.

Conceptually, the following is maintained.

```text
Application Core
      ↓
Stable Interfaces
      ↓
Feature Modules
      ↓
Adapters / Plugins / External Services
```

### Rationale

If dependencies on the core increase with each function added, eventually all modules end up mutually dependent.

The core is limited to stable common functions as far as possible.

---

## 16.29 Deciding Whether to Adopt Future Functions

Future functions are not added merely because they can be implemented.

When adding, at least the following are checked:

- usefulness
- implementation and maintenance cost
- runtime load
- UI complexity
- security
- privacy
- external dependencies
- impact on the existing design

### Rationale

The goal is not to increase the number of functions itself but to maintain usability and maintainability as an integrated environment.

---


## 16.30 Extending Capture Sources and Capture Backends

The initial implementation provides GameCapture using a capture board and SubScreenCapture using DXGI Desktop Duplication.

In the future, video input approaches such as the following can be added using the same capture source boundary:

- window capture
- desktop capture with a different method
- Spout input
- network video input
- other video devices / capture backends

Even when adding a new capture approach, backend-specific APIs are not exposed to the higher-level stage or video pipeline.

```text
New Capture Backend
        ↓
Capture Adapter
        ↓
IVideoCaptureSource / IAudioCaptureSource
        ↓
Existing Stage / Audio / Video Pipeline
```

### Rationale

Capture technology differs in implementation approach depending on the OS, GPU API, device, etc.

Maintaining the capture source boundary allows the existing stage display, audio mixing, and streaming pipeline to be reused even when input technologies are added or changed.

---

## 16.31 Design Principles for Future Extensions

Future feature additions follow these basic principles.

1. Do not over-implement future functions from the initial stage.
2. However, provide clear extension boundaries where future changes are expected.
3. Even if the initial implementation is for one person, do not fix the internal structure to one person.
4. Add tracking methods as providers.
5. Allow multiple tracking sources to be integrated in the future.
6. Add 3D model formats as loaders.
7. Allow new input signals and motion approaches to be added to extension bone control.
8. Maintain a structure in which the voice conversion engine is replaceable.
9. Do not fix Voice Lab to specific AI models.
10. Add streaming services as publishers.
11. Make encoders / codecs replaceable.
12. Do not let stages depend directly on application internals.
13. New operation inputs use the command boundary.
14. Add new locales as localization resources.
15. Add UI themes through the design system.
16. Publish the plugin mechanism gradually after internal extension points are stable.
17. The external API reuses existing command paths.
18. Do not carelessly expose the external API to networks.
19. Extend in the direction of enabling reuse and sharing of projects and assets.
20. Maintain backward compatibility with existing data as far as practical.
21. Provide migration and a deprecation period when retiring old functions.
22. Do not bloat the core; implement added functions as independent modules.
23. When adding new functions, also evaluate performance, security, privacy, and UI complexity.
24. Base final decisions on adopting functions on actual usefulness.
25. Add new video input approaches as capture sources / adapters.
