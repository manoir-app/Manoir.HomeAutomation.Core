# {{REPO_NAME}} - Project Guidelines

## Scope

This repository hosts the `{{DOMAIN_NAME}}` business domain of MaNoir, together with one or more local agents tightly coupled to that domain (for example a home-automation domain hosting its own scripting/scene agent).

It is allowed to contain:

- the domain business logic;
- public contracts of the domain;
- the domain API;
- the domain admin UI;
- one or more local agents that only serve this domain.

It must not become the default location for:

- platform-wide transverse primitives;
- Communication Hub ingestion or correlation responsibilities;
- PlatformOps or deployment control logic;
- cross-domain orchestration agents (an agent that coordinates several domains belongs in a dedicated agents-repo instead);
- composed front experiences.

## Architecture

This repo owns business truth for `{{DOMAIN_NAME}}`, and the runtime behavior of its local agent(s).

Use these rules:

- Domain logic stays in the domain, not in API, UI, agent, or orchestration code.
- Admin UI, API, and local agents all consume the domain through the same explicit application surfaces, never through internal domain implementation.
- If a local agent starts coordinating more than this one domain, move it to a dedicated agents-repo instead of growing it here.
- If a feature owns runtime control or deployment behavior, it belongs in PlatformOps.
- If a concern is shared across all domains, challenge whether it belongs in Core instead.

## Packaging

Keep these rules:

- publish `{{PACKAGE_PREFIX}}.Contracts` when cross-repo public contracts are needed;
- publish `{{PACKAGE_PREFIX}}.Client` only when there is a real external consumption need;
- keep `Domain`, `Api`, `AdminUi`, and local agent implementations internal by default.

Do not publish internal implementation packages for convenience.

## Repository Layout

Prefer this root layout vocabulary:

- `apps/` for executable projects: `{{PACKAGE_PREFIX}}.Api`, `{{PACKAGE_PREFIX}}.AdminUi`, and each local agent (for example `{{PACKAGE_PREFIX}}.Agents.<AgentName>`);
- `packages/` for `{{PACKAGE_PREFIX}}.Domain`, `{{PACKAGE_PREFIX}}.Contracts`, and reusable libraries;
- `ui/` for frontend admin modules such as `{{PACKAGE_PREFIX}}.AdminUi.<Feature>` and shared frontend code;
- `tests/` for test projects.

Do not invent competing root folders such as `bo/`, `pages/`, `frontend/`, or `backend`.

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
