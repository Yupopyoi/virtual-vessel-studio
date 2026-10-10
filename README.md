# Virtual Vessel Studio

Virtual Vessel Studio is a Windows-first integrated application for VTuber streaming, setup, and content creation.

It combines 3D avatars, tracking, real-time voice conversion, audio, video capture, stages, streaming, recording, and Voice Lab into a single application built on Unity.

> Status: early development. The repository currently contains the project foundation and design documents.

## Repository layout

```text
virtual-vessel-studio/
├─ docs/       Project documentation (Japanese: docs/ja, English: docs/en)
├─ unity/      Unity application (unity/VirtualVesselStudio)
├─ native/     Project-owned native Windows code and plugins
├─ services/   Project-owned Python services, adapters, and launchers
├─ tools/      Development, build, setup, and maintenance tooling
└─ tests/      Tests that do not belong to a specific component
```

## Requirements

- Windows
- Unity 6.6 (the exact version is defined in `unity/VirtualVesselStudio/ProjectSettings/ProjectVersion.txt`)

Open `unity/VirtualVesselStudio` from Unity Hub.

## Documentation

- System design: `docs/ja/architecture/system-design.md`
- Development workflow: `docs/ja/development/development-workflow.md`

English versions are maintained under `docs/en/` with the same relative paths.

Development agents and contributors should read `CLAUDE.md` before making changes.

## License

Virtual Vessel Studio is licensed under the [Apache License 2.0](LICENSE). See [NOTICE](NOTICE) for attribution.

The name "Virtual Vessel Studio" and its logos are not covered by the license. See [TRADEMARKS.md](TRADEMARKS.md).

Third-party components, models, and runtimes distributed with the application are subject to their own licenses.
