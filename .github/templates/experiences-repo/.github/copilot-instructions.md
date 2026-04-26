# MaNoir.Platform - Project Guidelines

## Scope

This repository hosts composed user experiences for MaNoir.

It is allowed to contain:

- experience shell hosts;
- composed frontend modules;
- experience-specific shared frontend assets;
- thin integration glue needed to assemble public capabilities.

It must not become the default location for:

- canonical business truth;
- platform-wide transverse primitives;
- deployment and runtime control-plane logic;
- external signal ingestion or correlation.

## Architecture

Use these rules:

- Experience repositories assemble existing capabilities; they do not redefine business truth.
- `{{PACKAGE_PREFIX}}.Shell` is the preferred .NET host when the experience has a server host.
- Frontend modules should live in `ui/` with explicit names such as `{{PACKAGE_PREFIX}}.Dashboard` or `{{PACKAGE_PREFIX}}.Tablet`.
- Prefer several focused frontend modules over one oversized frontend bundle when the experience surface grows.
- If a feature starts owning business rules instead of composing public ones, it belongs elsewhere.

## Packaging

Keep these rules:

- publish a client package only if several experiences truly share one;
- keep host and frontend implementation internal by default;
- avoid turning the repo into a general-purpose utility library.

## Repository Layout

Prefer this root layout vocabulary:

- `apps/` for executable experience hosts;
- `packages/` for reusable experience packages when justified;
- `ui/` for frontend modules and shared frontend code;
- `tests/` for test projects.
