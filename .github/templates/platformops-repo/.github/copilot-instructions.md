# MaNoir.Platform - Project Guidelines

## Scope

This repository hosts the Platform Operations family of MaNoir.

It is allowed to contain:

- deployment orchestration;
- runtime convergence;
- Kubernetes and Docker control components;
- control-plane APIs;
- technical platform state.

It must not become the default location for:

- business domain truth;
- platform-wide transverse primitives better owned by Core;
- composed front experiences;
- Communication Hub external signal correlation.

## Architecture

Use these rules:

- PlatformOps owns runtime control, not business state.
- A Gaia-like component belongs here by default.
- If a feature changes where or how the platform runs, it is probably PlatformOps.
- If a feature owns business truth, it belongs elsewhere.

## Packaging

Keep these rules:

- publish `{{PACKAGE_PREFIX}}.Contracts` only when control-plane contracts must be consumed from other repos;
- keep providers and runtime implementation internal by default;
- avoid publishing implementation-heavy packages for convenience.

## Repository Layout

Prefer this root layout vocabulary:

- `apps/` for executable control-plane surfaces;
- `packages/` for reusable providers, contracts, and libraries;
- `ui/` for SPA-based operations frontends when they exist;
- `ops/` for deployment artifacts owned by the repo.
