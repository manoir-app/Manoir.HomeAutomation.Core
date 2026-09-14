# MaNoir.Platform - Project Guidelines

## Scope

This repository hosts a family of MaNoir cross-domain operational agents.

It is allowed to contain:

- orchestration logic across several blocks;
- reactive or scheduled workflows;
- agent-specific runtime composition;
- technical coordination code for agents.

It must not become the default location for:

- canonical business truth;
- transverse platform primitives;
- deployment and runtime control-plane logic;
- composed front experiences.

## Architecture

Use these rules:

- Agents orchestrate and react, but do not become the canonical owner of business state.
- Agents depend on public contracts and clients, not internal implementation from other repos.
- If an agent is strictly mono-domain, challenge whether it belongs in the domain repo instead.
- If a feature controls platform runtime or deployment, it belongs in PlatformOps, not here.

## Packaging

Keep these rules:

- publish packages only when a real shared runtime or public contract exists;
- keep agent implementation internal by default;
- avoid turning this repo into a utility library for the rest of the platform.

## Repository Layout

Prefer this root layout vocabulary:

- `apps/` for agent executables;
- `packages/` for shared runtime or reusable agent libraries;
- `tests/` for test projects;
- `ops/` if the repo owns agent deployment artifacts.

## Plugin catalog manifest (manoir.plugin.yaml)

- `manoir.plugin.yaml` at this repo's root, once created from this template, is the active source manifest. It is not dead and must never be deleted.
- This repo is backend-only (no admin UI), so its manifest typically has no `deployment.adminUi` block, but it still needs `deployment.group` and `deployment.artifacts` so Gaia can deploy it.
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
- If you are not sure whether this manifest is still used, whether it is safe to change, or what a given field does, STOP and ask the user to confirm instead of assuming it is dead code.