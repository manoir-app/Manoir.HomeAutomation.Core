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
