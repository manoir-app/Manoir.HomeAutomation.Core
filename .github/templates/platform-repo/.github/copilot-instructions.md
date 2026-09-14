# MaNoir.Platform - Project Guidelines

## Scope

This repository hosts the platform foundation of MaNoir.

It is allowed to contain:

- the transverse `MaNoir.Core` foundation;
- `MaNoir.Core` public contracts and clients when justified;
- the Core API and admin UI;
- the `MaNoir.CommunicationHub` and its public contracts and clients when justified;
- narrowly scoped platform UI foundations such as `MaNoir.Core.AdminUi.Hosting` and `MaNoir.Core.AdminUi.Kit`.

It must not become the default location for:

- business domain logic;
- cross-domain operational agents;
- composed front experiences;
- platform control-plane and deployment components.

## Architecture

Use these rules:

- Core provides transverse primitives, not hidden business logic.
- Communication Hub ingests, normalizes, correlates, and routes external signals, but does not own final business meaning.
- Platform UI foundations stay technical and do not absorb domain workflows.
- Business domains own their business truth.
- Platform operations own deployment and runtime control, not business state.
- UIs consume public surfaces and must not access internal domain implementation.

## Packaging

Keep these rules:

- publish `MaNoir.Core.Contracts` and `MaNoir.CommunicationHub.Contracts` when public contracts are needed;
- publish `MaNoir.Core.Client` or `MaNoir.CommunicationHub.Client` only when there is a real cross-repo consumption need;
- publish a narrowly scoped technical foundation package only when it supports a stable cross-repo platform concern, for example `MaNoir.Core.AdminUi.Hosting`;
- publish a shared frontend package under `ui/` only when it carries stable cross-repo React/UI foundations, for example `MaNoir.Core.AdminUi.Kit`;
- keep `Api`, `AdminUi`, and implementation packages internal by default.

Do not introduce vague shared packages such as `Common`, `Shared`, or `Utils` as a dumping ground.

## Repository Layout

Prefer a stable repository root layout:

- `.github/` for Copilot and repository automation;
- `docs/` for repository-specific documentation;
- `eng/` for build, tooling, and engineering scripts;
- `apps/` for executable or host projects;
- `packages/` for reusable or publishable packages;
- `ui/` for SPA and frontend projects;
- `tests/` for test projects;
- `ops/` for deployment and runtime artifacts when the repo owns them.

Keep these rules:

- every project folder should match the exact project name;
- back-office UI is always named `AdminUi`, never `Bo`, `BackOffice`, `Ui`, or `Pages`;
- do not create competing root folders such as `bo/`, `pages/`, `frontend/`, `backend/`, `api/`, `domain/`, or `services`;
- if a UI project embeds a frontend app, keep its framework-specific structure inside the project folder, not at repository root.

## Plugin catalog manifest (manoir.plugin.yaml)

- `manoir.plugin.yaml` at this repo's root, once created from this template, is the active source manifest. It is not dead and must never be deleted.
- Transformation flow — do not assume, this is what actually happens end to end:

```
this repo's manoir.plugin.yaml (PluginManifest, authored by hand)
        │  CI job "publish-plugin-catalog" in .github/workflows/build.yml
        │  invokes manoir-app/manoir-plugin-action
        ▼
Manoir.PluginCatalog/plugins/<Root>/<Category>/<PluginId>/plugin.yaml (AvailablePlugin, generated)
   + deploy/docker-compose.yml (copied from deployment.artifacts[].path)
   + readme.md (copied from README.md)
```

- It is transformed by the external GitHub Action `manoir-app/manoir-plugin-action` (not vendored inside this repo) into a lightweight `plugin.yaml` (`AvailablePlugin` format) plus companion artifacts, pushed to a dedicated branch in `Manoir.PluginCatalog`.
- Never change this manifest's shape or the publish pipeline without first checking `manoir-plugin-action`'s behavior; a change here likely requires a coordinated change there.
- The action does not currently copy or generate any catalog images; do not assume images are propagated automatically.
- The `catalog.contributions` block of `manoir.plugin.yaml` only produces a lightweight `announcedContributions` summary (kind + labels) in the catalog. It is not parsed by any .NET code. The real admin navigation source of truth is a hand-written `XxxPluginDescriptorProvider.cs` class; keep both in sync manually.
- If you are not sure whether this manifest is still used, whether it is safe to change, or what a given field does, STOP and ask the user to confirm instead of assuming it is dead code.