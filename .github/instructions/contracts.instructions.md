---
description: "Use when creating, updating, or reviewing Contracts packages, public DTOs, public commands, events, identifiers, or cross-repo package boundaries in MaNoir."
name: "MaNoir Contracts Boundaries"
applyTo: "**/*Contracts*/**"
---
# Contracts Guidelines

- Contracts expose public exchange models, not internal implementation models.
- Keep contracts stable and explicit because they are versioned cross-repo commitments.
- Prefer simple DTOs, commands, events, identifiers, and enums that describe public behavior.
- Do not leak internal services, persistence concerns, or domain-only helper types into Contracts.
- Breaking changes in Contracts require deliberate versioning and should be called out clearly.
- If a type is useful only inside the repo, keep it out of Contracts.

See [ARCHITECTURE.md](../../ARCHITECTURE.md) for the packaging and dependency rules.