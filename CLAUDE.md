# Virtual Vessel Studio

This file defines the project-wide rules that Claude Code must follow when working in this repository.

Virtual Vessel Studio is a Windows-first integrated application for VTuber streaming, setup, and content creation.

The main application runtime is built with Unity.

The project includes multiple technical domains, including:

- 3D avatars
- tracking
- real-time voice conversion
- audio
- video capture
- stages and rendering
- streaming and recording
- Voice Lab
- Python-based training, generation, and analysis
- native Windows plugins

Do not treat this repository as a collection of independent experiments.

Changes must preserve the architecture, module boundaries, runtime requirements, and user experience defined by the project documentation.

---

# 1. Source of Truth

Before making a substantial change, read the relevant project documentation.

The primary architecture document is:

```text
docs/ja/architecture/system-design.md
docs/en/architecture/system-design.md
```

The development workflow is:

```text
docs/ja/development/development-workflow.md
docs/en/development/development-workflow.md
```

Module-specific detailed designs are stored under:

```text
docs/ja/detailed-design/
docs/en/detailed-design/
```

Architecture decisions may be stored under:

```text
docs/ja/decisions/
docs/en/decisions/
```

UI design documentation may be stored under:

```text
docs/ja/ui/
docs/en/ui/
```

The priority for project decisions is:

1. Explicit user instructions for the current task
2. Project architecture documentation
3. Approved detailed design
4. This `CLAUDE.md`
5. Existing implementation and tests
6. Historical benchmark or prototype implementations

If the implementation and documentation disagree, do not silently choose one.

Identify the inconsistency and determine whether it is:

- an implementation bug,
- outdated documentation,
- an intentional temporary deviation,
- or a design change requiring user approval.

Do not silently rewrite architecture documentation to justify an implementation.

---

# 2. Project Environment

The project currently targets:

```text
OS: Windows
Unity: Unity 6.6
Primary language: C#
Native code: C / C++
Python: Native Python + venv for development
Container runtime: Not required
```

Docker is not a required development, runtime, or distribution dependency.

Do not introduce Docker as a mandatory dependency unless explicitly approved.

The exact Unity patch version used by the project is defined by:

```text
unity/VirtualVesselStudio/ProjectSettings/ProjectVersion.txt
```

Do not upgrade Unity as part of unrelated work.

Unity upgrades must be handled as explicit maintenance tasks.

---

# 3. Architecture Invariants

The following are project-wide architecture rules.

Do not change them without explicit user approval.

## 3.1 Single-application user experience

Normal users interact with Virtual Vessel Studio as a single application.

Internal implementation may contain:

- Unity runtime code,
- native plugins,
- local services,
- external processes,
- Python runtimes,
- external OSS,

but normal users must not be required to understand or manually manage those components.

Do not expose implementation details such as:

- Python commands,
- venv activation,
- pip commands,
- process IDs,
- internal ports,
- external OSS directory structures,

through normal user workflows.

Developer diagnostics may expose appropriate internal information.

---

## 3.2 Unity-centered runtime

Unity is the center of the normal streaming runtime.

Runtime features include, among others:

- application lifecycle
- avatar control
- tracking
- real-time voice conversion
- audio processing
- capture integration
- stage rendering
- camera management
- video output
- streaming
- recording
- runtime UI
- diagnostics

The normal streaming runtime must not require Python services.

---

## 3.3 Runtime and Setup separation

Separate runtime functionality from setup, generation, training, and analysis.

Python is primarily used for operations such as:

- voice generation
- voice cloning
- RVC training
- voice analysis
- dataset processing
- model conversion
- external component setup

These components are started only when required.

Do not move normal streaming functionality into Python simply because implementation is easier there.

---

## 3.4 Real-time voice conversion

Real-time RVC inference used during streaming runs in the Unity runtime.

Do not make the real-time voice conversion path depend on a Python service.

Performance-sensitive RVC implementation must be treated as real-time code.

Inspect relevant benchmark/reference implementations before replacing proven implementation techniques.

---

## 3.5 External OSS isolation

External OSS such as RVC and VoxCPM2 must be isolated behind project-owned boundaries such as:

- adapters
- wrappers
- launchers
- service interfaces
- conversion layers

Do not expose external OSS-specific types or assumptions across unrelated modules.

Avoid modifying upstream OSS.

If modification of external OSS is required, obtain user approval first and document:

- the upstream version,
- the patch,
- why the patch is necessary,
- the affected behavior,
- upgrade implications,
- whether the patch can eventually be removed.

---

# 4. Module Boundaries

The system is divided into responsibility-based modules.

Major modules include:

```text
Application
Project / Data
Avatar
Tracking
Voice
Audio
Capture
Stage
Video
Streaming
VoiceLab
ExternalServices
Diagnostics
UI
```

A module owns its internal implementation.

Other modules must not freely access its internal classes.

Prefer explicit boundaries using:

- interfaces
- data contracts
- commands
- events
- services
- runtime state

Avoid giant global managers.

Avoid unnecessary global singletons.

Do not create a new central manager merely to make cross-module access convenient.

---

# 5. Cross-module Data

Do not pass external-library-specific data structures across module boundaries when a project-owned representation is appropriate.

Examples:

```text
MediaPipe data
    ↓
Tracking Provider
    ↓
TrackingFrame
    ↓
Avatar
```

```text
VRM / FBX
    ↓
Skeleton Mapping
    ↓
Common Avatar Representation
```

```text
RVC implementation
    ↓
IVoiceConverter
    ↓
Converted Audio
```

```text
Capture backend
    ↓
Capture Adapter
    ↓
IVideoCaptureSource / IAudioCaptureSource
```

Project-owned contracts should remain as independent as practical from:

- model format,
- tracking library,
- voice engine,
- capture backend,
- streaming provider.

---

# 6. Capture Architecture

Capture is responsible for acquiring media.

Capture implementations may include:

- GameCapture
- SubScreenCapture
- future capture providers

The capture module must not decide where video is rendered in the stage.

The responsibilities are:

```text
Capture
    Acquires video/audio
        ↓
Stage
    Decides where captured video is displayed
        ↓
Video
    Produces the final rendered output
        ↓
Streaming / Recording
```

Capture audio is routed through the Audio module.

Do not connect a capture implementation directly to the final encoder.

GameCapture may provide both video and audio.

SubScreenCapture may initially provide video only.

Keep backend-specific details such as DXGI Desktop Duplication isolated behind the capture abstraction.

---

# 7. Stage and Camera Architecture

Core application systems belong to the persistent application context.

Stage content is loaded separately and must not own core application lifecycle.

The main streaming camera remains application-owned.

A stage may provide data such as:

- Camera Points
- Avatar Spawn Points
- lights
- stage effects
- display surfaces

but should not redefine the application-level camera lifecycle.

Diagnostic cameras must not accidentally become the streaming output.

---

# 8. External Service Management

External services and processes are managed through project-owned lifecycle management.

Feature modules must not independently implement arbitrary process startup and shutdown logic.

The External Service layer is responsible for concerns such as:

- component availability
- service startup
- already-running service detection
- compatibility checks
- process ownership
- port checks
- health checks
- readiness
- shutdown
- failure state

Long-running local services and one-shot external processes are different lifecycle categories and should not be forced into the same behavior.

A process started by Virtual Vessel Studio and a compatible process that was already running must be treated differently.

Do not terminate a process that the application does not own.

---

# 9. Python Development

Python development uses native Python environments.

The normal development model is:

```text
Python
    ↓
venv
    ↓
locked dependencies
    ↓
source code
    ↓
tests / debugging / build
```

Docker is not required.

Do not make Docker the only supported development path.

Each Python component must explicitly define:

- supported Python version,
- dependencies,
- locked dependency versions,
- external OSS version or commit where applicable,
- setup procedure,
- health check or validation procedure.

Do not rely on a developer's global Python environment.

Keep environments isolated when components require incompatible dependencies.

---

# 10. Python Distribution

Developer environments and end-user environments are different concerns.

Developers may use:

```text
Native Python + venv
```

End users must not be required to:

- install Python manually,
- create a venv,
- activate a venv,
- run pip,
- resolve Python dependencies,
- understand Python environment structure.

Production distribution should use build-generated runtime packages.

Conceptually:

```text
Source
+ Python version
+ locked dependencies
+ project code
        ↓
Build
        ↓
Runtime Package
        ↓
Virtual Vessel Studio
        ↓
End User
```

A runtime package may use an appropriate packaging approach depending on the component.

Possible approaches include:

- standalone executable/bundle,
- portable or embedded Python runtime,
- component-specific packaged runtime.

Do not assume a single-file executable is always the best packaging format.

Large ML components containing PyTorch, CUDA-related libraries, dynamic imports, or large dependency trees may use directory-based portable runtime packages.

Models and datasets should normally remain separate from the executable/runtime package when appropriate.

Runtime packages should be version-identifiable and diagnosable.

Packaging details belong in the relevant detailed design.

---

# 11. Repository Structure

The product repository is:

```text
virtual-vessel-studio/
```

Expected high-level structure:

```text
virtual-vessel-studio/
├─ CLAUDE.md
├─ README.md
├─ .editorconfig
├─ .gitignore
├─ .gitattributes
│
├─ .github/
├─ docs/
├─ unity/
├─ native/
├─ services/
├─ tools/
└─ tests/
```

Responsibilities:

## `unity/`

Unity application.

## `native/`

Project-owned native Windows code and native plugins.

## `services/`

Project-owned Python services, adapters, wrappers, launchers, and related service integration code.

Do not copy complete third-party OSS repositories into this directory without an explicit design reason.

## `tools/`

Development, build, setup, conversion, and maintenance tooling.

## `tests/`

Tests that do not naturally belong inside a specific component or Unity assembly.

## `docs/`

Project documentation.

Directory structure should reflect architecture responsibilities.

Do not create directories or abstraction layers only for appearance.

---

# 12. External Benchmarks and Prototype References

Historical benchmarks and prototypes may exist outside this repository.

Typical local reference directories may include:

```text
E:\VirtualVessel\VCBM
E:\VirtualVessel\GameCaptureUnityPlugin
E:\VirtualVessel\SubScreenCapturePrototype
E:\VirtualVessel\OtherBenchmarks
```

These are reference snapshots.

They are not part of the Virtual Vessel Studio product repository.

Do not assume:

- they are Git repositories,
- their Git history exists,
- their Git history matters,
- they should be copied into the product repository.

Their purpose is to preserve useful implementation and experimental knowledge.

When relevant, inspect them to understand:

- proven algorithms,
- latency behavior,
- threading,
- buffering,
- memory allocation,
- native interop,
- GPU/CPU transfer behavior,
- copy behavior,
- device I/O,
- error handling,
- previous performance results,
- experimental findings.

Do not blindly copy a benchmark implementation into the product.

Adapt validated techniques to the current Virtual Vessel Studio architecture.

When intentionally replacing a performance-sensitive technique proven by a benchmark, provide a reason and run a comparable benchmark when practical.

If a relevant external benchmark is unavailable, do not invent its contents.

State that it was not inspected.

---

# 13. Standard Development Workflow

For substantial changes, follow this sequence.

## Step 1: Understand the task

Identify:

- goal,
- scope,
- non-goals,
- affected modules,
- acceptance criteria.

Do not begin by editing files without understanding the intended outcome.

## Step 2: Read documentation

Read:

1. this `CLAUDE.md`,
2. relevant architecture documentation,
3. relevant detailed design,
4. relevant architecture decisions.

## Step 3: Inspect the implementation

Inspect:

- existing implementation,
- public interfaces,
- data contracts,
- lifecycle,
- tests,
- diagnostics,
- configuration,
- persistence behavior.

## Step 4: Inspect reference implementations when relevant

For performance-sensitive or historically validated functionality, inspect relevant benchmark/prototype snapshots when available.

## Step 5: Plan

For substantial work, create a concise implementation plan before editing.

Identify:

- files to change,
- interfaces affected,
- tests required,
- performance risks,
- migration risks,
- documentation changes.

## Step 6: Detailed design

Create or update detailed design when the work introduces meaningful module behavior or architecture.

## Step 7: Implement

Make the smallest coherent change that satisfies the task.

Do not perform unrelated refactoring unless necessary.

## Step 8: Build and test

Run relevant:

- compile/build checks,
- unit tests,
- integration tests,
- Unity tests,
- native builds,
- Python tests,
- performance benchmarks.

## Step 9: Self-review

Review the complete diff.

Check:

- architecture,
- module boundaries,
- API surface,
- correctness,
- error handling,
- cleanup/disposal,
- cancellation,
- diagnostics,
- security,
- privacy,
- performance,
- test coverage,
- documentation.

## Step 10: Update documentation

Update Japanese and English documentation as required.

## Step 11: Report

Clearly report:

- changes,
- design decisions,
- tests,
- benchmarks,
- documentation,
- unresolved issues.

---

# 14. Detailed Design Policy

Do not manually pre-design every private method.

Detailed design should describe meaningful module-level implementation decisions.

For substantial module work, create or update:

```text
docs/ja/detailed-design/<module>.md
docs/en/detailed-design/<module>.md
```

A detailed design should normally cover:

1. Purpose
2. Responsibilities
3. Module boundaries
4. Main classes and interfaces
5. Public API
6. Data models and contracts
7. State and lifecycle
8. Important sequences
9. Threading / async behavior
10. Configuration
11. Storage
12. Error handling and recovery
13. Logging and diagnostics
14. Testing strategy
15. Performance constraints
16. Directory / namespace structure
17. Open issues

Do not over-specify trivial private implementation details before coding.

Detailed design should guide implementation without becoming a duplicate of source code.

---

# 15. C# Coding Style

Project-owned C# code follows the .NET Runtime coding style:

```text
https://github.com/dotnet/runtime/blob/main/docs/coding-guidelines/coding-style.md
```

The repository `.editorconfig` is the machine-readable project configuration.

Important rules include:

- four spaces,
- no tabs for C# indentation,
- Allman-style braces,
- explicit accessibility modifiers,
- `System.*` using directives first,
- C# keywords such as `int`, `string`, and `float`,
- `nameof(...)` where appropriate.

Private/internal instance fields use:

```csharp
private AudioSource _audioSource;
```

Private/internal static fields use:

```csharp
private static AudioManager s_instance;
```

Thread-static fields use the `t_` prefix where applicable.

Do not introduce a different naming convention in individual modules.

Use language features and APIs that are compatible with the Unity project's supported C#/.NET environment.

---

# 16. Source Code Language

All developer-facing source-code content must be English.

This includes:

- identifiers,
- comments,
- XML documentation,
- TODO comments,
- FIXME comments,
- test names,
- test descriptions,
- assertion messages,
- developer-facing exception messages,
- developer logs,
- diagnostic messages.

Do not add Japanese code comments.

Comments should primarily explain:

- why the code exists,
- why a non-obvious technique is necessary,
- important constraints,
- performance reasoning,
- lifecycle reasoning,
- compatibility assumptions.

Do not add comments that merely restate obvious code.

Example:

Bad:

```csharp
// Increment the counter.
counter++;
```

Useful:

```csharp
// Keep the sequence monotonic so dropped capture frames can be detected by diagnostics.
_sequence++;
```

User-facing Japanese text is not considered developer-facing source text.

User-facing text must use localization resources instead of being hardcoded into logic.

---

# 17. Native C/C++ Code

Native code is used when required for Windows APIs, performance, device access, or Unity native integration.

Examples include:

- Direct3D,
- DXGI,
- capture devices,
- native audio/video integration.

Keep native implementation details behind narrow managed interfaces.

Pay particular attention to:

- ownership,
- lifetime,
- COM release,
- thread shutdown,
- synchronization,
- buffer ownership,
- copying,
- device loss,
- failure cleanup.

Do not leak raw native ownership responsibilities into unrelated Unity modules.

Avoid changing proven native hot paths without inspecting the relevant benchmark/prototype implementation first.

---

# 18. Unity Rules

Unity is the main application platform.

Keep Unity-generated directories out of source control according to `.gitignore`.

Version-control required project files such as:

- Assets
- Packages
- ProjectSettings
- `.meta` files

Do not modify:

```text
Library
Temp
Logs
UserSettings
```

as if they were project source.

Do not commit generated IDE files.

Do not change the Unity editor version as part of unrelated work.

Avoid placing unrelated responsibilities in MonoBehaviour classes merely because they are easy to access from Unity.

Separate:

- domain logic,
- application logic,
- Unity lifecycle integration,
- presentation/UI,

where practical.

---

# 19. UI Rules

Use the established project UI architecture.

Prefer Unity UI Toolkit unless an approved design requires another approach.

Do not hardcode user-facing text.

Use Unity Localization.

At minimum, support:

- Japanese
- English

Reuse project UI components and design patterns where available.

Keep setup workflows separate from live/runtime workflows where appropriate.

Significant UI changes require visual review.

Review:

- spacing,
- alignment,
- hierarchy,
- localization,
- overflow,
- different text lengths,
- enabled/disabled states,
- loading states,
- error states.

Do not expose developer-only implementation details in normal UI.

---

# 20. Logging and Diagnostics

User-facing errors and developer diagnostics are separate concerns.

Normal users should receive:

- understandable problem descriptions,
- recovery guidance where possible,
- actionable status.

Developer diagnostics should contain enough technical context to investigate failures.

Never log:

- passwords,
- API keys,
- OAuth tokens,
- stream keys,
- credentials,
- private secrets.

Do not log private audio or video content by default.

Avoid high-frequency logging in:

- frame loops,
- audio callbacks,
- tracking loops,
- capture loops,
- rendering loops.

Prefer:

- state-change logging,
- rate-limited logging,
- aggregated repeated errors,
- developer trace modes.

Include useful version information where relevant.

---

# 21. Error Handling

Failures should degrade gracefully where possible.

A failure in Voice Lab or a Python component should not unnecessarily prevent normal streaming with already prepared assets.

External process failures must provide useful diagnostics.

Do not hide exceptions without explanation.

Do not turn recoverable errors into application crashes when a module can safely enter a degraded or unavailable state.

Always consider resource cleanup after failure.

---

# 22. Testing Rules

New behavior requires appropriate tests.

Bug fixes should include regression tests when practical.

Use the appropriate level:

- unit test,
- integration test,
- system test,
- performance test.

Prefer testing meaningful behavior and module contracts over trivial implementation details.

Important integration paths include examples such as:

```text
Tracking → Avatar
Voice → Audio
Capture → Stage
Capture Audio → Audio
Audio → Streaming
Project → Runtime
Voice Lab → External Service
```

Never claim a test was executed if it was not executed.

If a required test cannot be run, explicitly state:

- what was not run,
- why,
- what risk remains.

---

# 23. Performance Rules

Virtual Vessel Studio contains real-time paths.

Treat the following as performance-sensitive unless proven otherwise:

- audio callbacks,
- real-time voice conversion,
- tracking processing,
- capture,
- video transfer,
- rendering,
- encoding,
- native interop.

In hot paths, avoid unnecessary:

- allocations,
- garbage generation,
- blocking calls,
- locks,
- copies,
- CPU/GPU transfers,
- filesystem access,
- network access,
- logging.

Do not optimize blindly.

Measure before and after meaningful performance changes.

Historical benchmarks are evidence, not architecture.

Preserve the behavior that a benchmark proved while adapting the implementation to the current product architecture.

---

# 24. Resource Lifetime

Explicitly manage lifecycle for resources such as:

- native handles,
- COM objects,
- GPU resources,
- textures,
- audio devices,
- capture devices,
- processes,
- services,
- files,
- sockets,
- cancellation tokens.

Initialization and shutdown behavior must be clear.

Support cancellation for long-running operations when practical.

Avoid background tasks that outlive the owning module unintentionally.

---

# 25. Project Data and Compatibility

Project files, profiles, configuration, and persistent data are user assets.

Do not make breaking changes casually.

When persistent schemas evolve:

- use explicit schema versions,
- provide migration when appropriate,
- preserve backward compatibility where practical,
- document breaking changes.

A change that intentionally breaks existing project compatibility requires user approval.

Do not silently discard unknown or old data simply because the current implementation does not recognize it.

---

# 26. Security and Privacy

Security and privacy are design concerns.

Do not:

- commit secrets,
- log secrets,
- hardcode credentials,
- expose private user media unnecessarily,
- terminate unrelated user processes,
- trust arbitrary local services based only on an open port.

Validate external service identity when applicable.

Treat downloaded or external components as dependencies with explicit versions and provenance.

Security/privacy trade-offs require explicit consideration and may require user approval.

---

# 27. Documentation Rules

Project documentation is maintained in Japanese and English.

The directory structure is parallel:

```text
docs/ja/...
docs/en/...
```

Documents representing the same subject should use matching relative paths.

For example:

```text
docs/ja/detailed-design/capture.md
docs/en/detailed-design/capture.md
```

When a project document is created or changed:

1. update the Japanese version,
2. update the English version,
3. verify semantic consistency.

Both versions must represent the same:

- architecture decisions,
- interfaces,
- constraints,
- versions,
- file paths,
- diagrams,
- behavior.

Do not finish a documentation-related task with only one language updated unless the user explicitly requests a temporary exception.

`CLAUDE.md` itself is an operational instruction file for development agents and may remain English-only.

---

# 28. Documentation Synchronization

Documentation must describe the implemented system.

Update documentation when changing:

- architecture,
- module responsibilities,
- interfaces,
- data contracts,
- persistent data formats,
- external components,
- setup procedures,
- runtime packaging,
- user workflows,
- major UI behavior,
- directory structure.

Do not leave known documentation drift for a later unspecified task.

If documentation cannot be updated within the current task, report the mismatch explicitly.

---

# 29. Git Workflow

Git history should reflect the actual development process.

Work must be divided into small, reviewable, logically coherent steps.

Do not accumulate a large amount of unrelated implementation before committing.

---

## 29.1 Branch Policy

Do not perform normal feature development directly on `main`.

Before starting implementation work, create or switch to a dedicated short-lived task branch.

Typical branch prefixes are:

```text
feature/<task-name>
fix/<task-name>
refactor/<task-name>
docs/<task-name>
chore/<task-name>
```

Examples:

```text
feature/capture-source-abstraction
feature/voice-model-management
fix/external-service-startup
refactor/audio-routing
docs/english-architecture
```

One branch should represent one clearly defined task or closely related unit of work.

If a task becomes too broad, split it into multiple branches or explicitly defined follow-up tasks.

Do not mix unrelated features, fixes, refactoring, or documentation work into the same branch unless they are required to complete the same logical task.

Before modifying files, confirm the current branch.

If the current branch is `main`, create an appropriate task branch before modifying any tracked file.

The only exceptions are:

- operations that do not change repository contents, such as GitHub repository settings, branch protection rules, labels, or issue management,
- work that the user has explicitly instructed to perform directly on `main` for the current task.

Documentation, `CLAUDE.md`, configuration, and repository-structure changes are not exceptions and use a task branch.

---

## 29.2 Task Decomposition

Before implementation, divide substantial work into small logical steps.

Each step should have a clear purpose and an independently understandable result.

Prefer development sequences such as:

```text
Define interface and contract tests
    ↓
Add core implementation with unit tests
    ↓
Add integration with integration tests
    ↓
Add diagnostics
    ↓
Update documentation
```

rather than implementing the entire subsystem as one large change.

Each step includes the tests that verify it. Do not defer all tests to a final step.

A development step should be small enough that:

- its purpose is easy to explain,
- its diff can be reviewed independently,
- failures can be isolated,
- the code can be reverted without discarding unrelated work,
- the associated verification can be clearly identified.

Do not artificially split changes into meaningless tiny commits.

The goal is small logical units, not commits for every file or every few lines.

---

## 29.3 Commit Policy

Create commits at verified logical checkpoints.

A commit should represent a coherent unit of work whose relevant behavior has been checked.

Examples of appropriate commit boundaries include:

```text
Add capture source interfaces
Add GameCapture adapter implementation
Add capture lifecycle tests
Add capture diagnostics
Update capture detailed design
```

A commit should not contain multiple unrelated purposes.

Before committing:

1. Review `git status`.
2. Review the relevant diff.
3. Confirm that only intended files are included.
4. Run the checks appropriate for that logical unit.
5. Confirm that the code is in a usable intermediate state.
6. Stage only the intended files.
7. Review the staged diff.
8. Create the commit.

Do not knowingly commit code that is in the middle of an incomplete edit, does not compile where compilation is expected, or breaks already working behavior without an explicit reason.

The repository should remain reasonably usable at each commit boundary.

---

## 29.4 Verification Before Commit

The verification required before each commit depends on the change.

Examples include:

- compilation,
- unit tests,
- integration tests,
- Unity EditMode tests,
- Unity PlayMode tests,
- Python tests,
- native builds,
- manual runtime checks,
- focused performance benchmarks.

Run the smallest sufficient verification for the current logical unit.

Do not delay all verification until the end of the entire branch.

When practical, detect problems immediately after the step that introduced them.

If a check cannot be run, explicitly record that fact before committing and describe the remaining risk.

Never claim that a check passed if it was not actually executed.

---

## 29.5 Commit Size

Prefer multiple small, meaningful commits over one large final commit.

A typical substantial task may contain several commits such as:

```text
feat: add capture source contracts
feat: add game capture adapter
test: add capture lifecycle tests
feat: add capture diagnostics
docs: add capture detailed design
```

Each commit should be understandable from its message and diff.

Avoid commits such as:

```text
feat: implement capture module
```

when that commit contains hundreds of unrelated design, implementation, test, UI, and documentation changes that could have been separated into meaningful checkpoints.

Also avoid meaningless fragmentation such as:

```text
fix typo
update file
more changes
test
final fix
```

Commit history should communicate how the feature was built.

---

## 29.6 Commit Messages

Commit messages must be written in English.

Prefer Conventional Commit-style prefixes where appropriate:

```text
feat:
fix:
refactor:
test:
docs:
chore:
perf:
build:
ci:
```

Examples:

```text
feat: add capture source abstraction
feat: add GameCapture adapter
test: add capture source lifecycle tests
perf: reduce capture frame copies
fix: handle capture device disconnect
refactor: isolate external service lifecycle
docs: add capture detailed design
chore: update editor configuration
```

Messages should describe the logical result of the commit rather than the mechanical action performed.

---

## 29.7 Claude Code Commit Authority

Claude Code may create local commits as part of an approved implementation task.

Claude Code should create commits when a logical unit of work:

- is complete,
- has been reviewed,
- has received the appropriate verification,
- and is useful as an independent development checkpoint.

Claude Code does not need to ask for approval before every local commit when the user has already delegated implementation of the task.

However, Claude Code must not create commits merely to save temporary or broken intermediate states.

If a commit contains known limitations, failed checks, or intentionally incomplete behavior, make that clear to the user.

---

## 29.8 Push Policy

Remote push is user-controlled.

Claude Code must not execute:

```text
git push
```

without explicit user approval.

This includes:

- the first push of a branch,
- subsequent pushes,
- force pushes,
- pushing tags.

When the branch is ready to push, stop and report:

- the current branch,
- commits that would be pushed,
- a short summary of each commit,
- verification performed,
- known issues or unverified items.

Then wait for explicit user approval before pushing.

Example:

```text
Branch:
feature/capture-source-abstraction

Commits:
1. feat: add capture source contracts
2. feat: add GameCapture adapter
3. test: add capture lifecycle tests

Verification:
- Unity compilation: passed
- EditMode tests: passed
- Hardware capture test: not run

Ready to push when approved.
```

Only after explicit approval may Claude Code perform the push.

---

## 29.9 Prohibited Git Operations

Claude Code must not perform the following without explicit user approval:

- `git push --force`
- `git push --force-with-lease`
- destructive history rewriting
- interactive rebase of already shared commits
- deleting remote branches
- deleting tags
- `git reset --hard`
- discarding uncommitted user changes
- overwriting unrelated user work
- merging into `main`
- merging or closing a Pull Request

Do not use destructive Git operations merely to simplify implementation.

Preserve user work.

---

## 29.10 Final Branch Review

Before declaring a branch complete:

1. Review the complete branch diff against its base branch.
2. Confirm that no unrelated changes are included.
3. Run the required final tests.
4. Run applicable performance checks.
5. Update required Japanese and English documentation.
6. Check for temporary debug code.
7. Check for secrets or local environment paths.
8. Review commit history for logical structure.

If commits have become confusing during development, propose cleanup before push.

Do not rewrite already published history without explicit approval.

---

# 30. Pull Requests

A Pull Request represents a completed reviewable branch.

Do not merge directly into `main` as part of normal feature development.

A Pull Request should clearly explain:

```text
Summary
Related issue
Design
Changes
Commits
Tests
Benchmarks
UI verification
Compatibility / Migration
Documentation
Remaining issues
```

The PR description must reflect checks that were actually performed.

Do not describe tests, benchmarks, or runtime validation as successful unless they were executed.

Push, Pull Request creation, and merge remain user-controlled unless explicitly delegated.

Claude Code may prepare:

- the proposed PR title,
- the proposed PR description,
- the branch summary,
- the verification summary,

before asking the user whether to push or create the Pull Request.

---

# 31. Changes Requiring User Approval

Do not make the following changes silently.

Obtain user approval before:

- changing major architecture,
- moving major responsibilities between modules,
- introducing a major external dependency,
- changing the main Unity/runtime boundary,
- making Python required for normal streaming,
- making Docker required,
- directly patching external OSS,
- replacing a benchmark-proven performance approach without justification,
- breaking persistent project/data compatibility,
- changing security or privacy behavior materially,
- substantially changing a major user workflow,
- changing the selected core technology for an established subsystem,
- performing a major Unity version upgrade.

When approval is required, explain:

- what would change,
- why,
- alternatives,
- migration impact,
- performance impact where relevant.

---

# 32. Decisions Claude Code May Make

Claude Code does not need user approval for every internal implementation detail.

Within approved architecture and task scope, Claude may decide reasonable details such as:

- private method structure,
- internal helper classes,
- internal DTO implementation,
- local refactoring,
- test organization,
- small internal interface design,
- naming consistent with project conventions,
- error handling implementation,
- cancellation implementation,
- internal state-machine implementation.

Do not escalate trivial implementation choices.

Use engineering judgment while respecting the architecture.

---

# 33. Changes to Existing Proven Code

Before rewriting working performance-sensitive code:

1. understand the current implementation,
2. understand what previous benchmarks proved,
3. identify critical implementation characteristics,
4. preserve required behavior,
5. measure the replacement.

Pay particular attention to:

- threading,
- buffering,
- allocation,
- copies,
- SIMD,
- native calls,
- GPU/CPU boundaries,
- blocking behavior,
- scheduling,
- latency.

Do not label existing benchmark code as obsolete simply because it looks unconventional.

Unconventional code may exist for measured performance reasons.

---

# 34. Refactoring

Refactor when it improves maintainability, correctness, architecture, or testability.

Do not refactor merely to make code stylistically different.

Avoid combining large refactoring with unrelated feature changes.

When refactoring performance-sensitive code, verify that performance has not regressed.

Maintain public behavior unless the task explicitly changes it.

---

# 35. Dependency Policy

Do not add a dependency merely to avoid implementing a small amount of straightforward code.

Before adding a substantial dependency, consider:

- maintenance,
- license,
- package size,
- runtime cost,
- security,
- Unity compatibility,
- Windows compatibility,
- update policy,
- OSS distribution.

Major new dependencies require user approval.

Pin external component versions where reproducibility matters.

Do not automatically track upstream latest versions for critical components.

---

# 36. Completion Criteria

A task is not complete merely because the code compiles.

For applicable work, completion includes:

```text
Implementation
    ↓
Build
    ↓
Tests
    ↓
Benchmark
    ↓
Self Review
    ↓
Documentation
    ↓
Final Report
```

Only perform the steps relevant to the task, but do not skip required verification silently.

---

# 37. Completion Report

At the end of substantial work, report using this structure:

## Changes

What was changed.

## Design decisions

Important implementation decisions and why they were made.

## Tests

What tests were executed and their results.

Explicitly identify tests that were not run.

## Benchmarks

Performance measurements, if applicable.

State when benchmarks were not applicable.

## Documentation

Documents created or updated.

Confirm Japanese/English synchronization where applicable.

## Remaining issues

Known limitations, unresolved risks, follow-up work, or intentionally deferred items.

---

# 38. General Principle

The repository is the project's long-term memory.

Do not rely on chat history as the only source of important engineering decisions.

When a decision affects future development, encode it in the appropriate place:

```text
CLAUDE.md
architecture documentation
detailed design
decision record
tests
configuration
```

The goal is for a future contributor or development agent to understand:

- what the system does,
- why it is structured this way,
- what must not be broken,
- how to verify a change,

by reading the repository itself.