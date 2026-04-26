# MaNoir.Platform - Project Guidelines

## Scope

This repository hosts the `{{DOMAIN_NAME}}` business domain of MaNoir.

It is allowed to contain:

- the domain business logic;
- public contracts of the domain;
- the domain API;
- the domain admin UI;
- a local domain agent only if it is strictly mono-domain.

It must not become the default location for:

- platform-wide transverse primitives;
- Communication Hub ingestion or correlation responsibilities;
- PlatformOps or deployment control logic;
- cross-domain orchestration agents;
- composed front experiences.

## Architecture

This repo owns business truth for `{{DOMAIN_NAME}}`.

Use these rules:

- Domain logic stays in the domain, not in API, UI, or orchestration code.
- Admin UI and API consume the domain through explicit application surfaces.
- If a feature owns runtime control or deployment behavior, it belongs in PlatformOps.
- If a feature only orchestrates several domains, it belongs in an agents family, not here.
- If a concern is shared across all domains, challenge whether it belongs in Core instead.

## Packaging

Keep these rules:

- publish `{{PACKAGE_PREFIX}}.Contracts` when cross-repo public contracts are needed;
- publish `{{PACKAGE_PREFIX}}.Client` only when there is a real external consumption need;
- keep `Domain`, `Api`, `AdminUi`, and local agent implementation internal by default.

Do not publish internal implementation packages for convenience.

## Repository Layout

Prefer this root layout vocabulary:

- `apps/` for executable projects like `{{PACKAGE_PREFIX}}.Api`;
- `packages/` for `{{PACKAGE_PREFIX}}.Domain`, `{{PACKAGE_PREFIX}}.Contracts`, and reusable libraries;
- `apps/{{PACKAGE_PREFIX}}.AdminUi/` for the .NET admin host when a server host exists;
- `ui/` for frontend admin modules such as `{{PACKAGE_PREFIX}}.AdminUi.<Feature>` and shared frontend code;
- `tests/` for test projects.

Do not invent competing root folders such as `bo/`, `pages/`, `frontend/`, or `backend`.
