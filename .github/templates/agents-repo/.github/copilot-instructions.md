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
