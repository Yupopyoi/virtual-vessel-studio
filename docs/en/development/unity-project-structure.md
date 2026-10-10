# Unity Project Structure Conventions

## 1. Purpose

This document defines conventions for the Unity project under `unity/VirtualVesselStudio/`:

- directory structure
- assembly definitions
- namespaces
- references between modules
- test placement

The goal is a structure in which the module division defined in system design 2.4 can be enforced mechanically in the Unity project.

All modules are added according to these conventions.

---

## 2. Directory Structure

Project-owned code and assets are placed under `Assets/VirtualVessel/`.

```text
Assets/
├─ VirtualVessel/
│  ├─ Core/
│  ├─ Application/
│  ├─ ProjectData/
│  ├─ Diagnostics/
│  ├─ UI/
│  ├─ Avatar/
│  ├─ Tracking/
│  ├─ Voice/
│  ├─ Audio/
│  ├─ Capture/
│  ├─ Stage/
│  ├─ Video/
│  ├─ Streaming/
│  ├─ VoiceLab/
│  └─ ExternalServices/
│
├─ ThirdParty/
│  └─ <Name>/
│
├─ Scenes/
│  └─ Persistent.unity
│
└─ Settings/
```

Modules are added when their implementation becomes necessary. Empty module directories are not created in advance.

If an external asset must be imported by a method other than the Unity Package Manager, it is placed unmodified in `Assets/ThirdParty/<Name>/`.

### Rationale

External assets and Unity packages may create directories directly under `Assets/`.

Grouping project-owned code under `Assets/VirtualVessel/` clearly distinguishes project code from external code.

---

## 3. Internal Module Structure

Each module basically has the following structure.

```text
Assets/VirtualVessel/<Module>/
├─ Runtime/
│  ├─ VirtualVessel.<Module>.asmdef
│  └─ ...
│
├─ Editor/
│  ├─ VirtualVessel.<Module>.Editor.asmdef
│  └─ ...
│
└─ Tests/
   ├─ EditMode/
   │  ├─ VirtualVessel.<Module>.Tests.EditMode.asmdef
   │  └─ ...
   │
   ├─ PlayMode/
   │  ├─ VirtualVessel.<Module>.Tests.PlayMode.asmdef
   │  └─ ...
   │
   └─ Performance/
      ├─ VirtualVessel.<Module>.Tests.Performance.asmdef
      └─ ...
```

`Editor/` and `Tests/` are created only when needed.

---

## 4. Assembly Definitions

### 4.1 Naming

| Type | Assembly name |
|---|---|
| Runtime | `VirtualVessel.<Module>` |
| Editor | `VirtualVessel.<Module>.Editor` |
| EditMode test | `VirtualVessel.<Module>.Tests.EditMode` |
| PlayMode test | `VirtualVessel.<Module>.Tests.PlayMode` |
| Performance test | `VirtualVessel.<Module>.Tests.Performance` |

### 4.2 Reference Direction

References between assemblies are allowed only in the following direction.

```text
VirtualVessel.Application
        ↓
Feature Modules (Avatar, Tracking, Voice, ...)
        ↓
VirtualVessel.Diagnostics
        ↓
VirtualVessel.Core
```

- `VirtualVessel.Application` may reference each module as the composition root.
- Feature modules do not reference Application.
- References between feature modules are limited to the data flows defined in the system design (e.g. Avatar → Tracking) and use only the other module's public contracts.
- Circular references are prohibited. Assembly definitions detect them mechanically.
- `VirtualVessel.Core` does not reference any other project assembly.

### 4.3 Scope of the Core Assembly

`VirtualVessel.Core` contains only basic elements that do not belong to a specific module.

Examples:

- time (clock)
- application service lifecycle contract
- main thread dispatch contract
- common result / error representation

Functions or data contracts of a specific module are not moved into Core "because we want to share them."

If Core grows large, it is treated as the same problem as a global manager and reviewed.

`VirtualVessel.Core` uses `noEngineReferences` as far as possible and does not depend on UnityEngine.

### 4.4 Visibility

- Only interfaces, data contracts, commands, and events exposed outside the module are public.
- Implementation classes are internal.
- When tests need internal access, `InternalsVisibleTo` is used.

### Rationale

Assembly definitions and C# access modifiers mechanically guarantee system design 2.4's rule that modules do not freely depend on other modules' internal classes.

---

## 5. Namespaces

Namespaces are basically `VirtualVessel.<Module>` and match the assembly name.

Subdivisions within a module are `VirtualVessel.<Module>.<Area>`.

Examples:

```text
VirtualVessel.Application
VirtualVessel.ProjectData
VirtualVessel.Diagnostics.Logging
```

Namespace names correspond to the module names in system design 2.4. `Project / Data` becomes `ProjectData`.

---

## 6. Test Placement

- Unity tests that belong to a specific module are placed in that module's `Tests/`.
- Tests that do not belong to a specific module are placed in `tests/` at the repository root (system design 3.3, 15.20).
- Tests use the Unity Test Framework (NUnit).
- Performance tests use the Unity Performance Testing Extension (`com.unity.test-framework.performance`) and are placed in each module's `Tests/Performance/`. Because Unity tests can only live inside the Unity project, `tests/performance/` at the repository root holds only hardware CI scripts, baselines for comparison, and result history.

---

## 7. Static State

- As a rule, holding state in static fields is avoided.
- Where necessary, static state is explicitly reset with `[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]`, etc., so that it is initialized correctly even when domain reload is disabled in the Unity Editor's Enter Play Mode settings.

### Rationale

When domain reload is disabled, static state remains from the previous play session and causes defects that are hard to reproduce.

---

## 8. C# Language Features

- Code is written within the C# version and API compatibility level (.NET Standard 2.1) supported by Unity 6.6.
- C# coding conventions follow `CLAUDE.md` and `.editorconfig`.
