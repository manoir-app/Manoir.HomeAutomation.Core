---
description: "Use when creating or reviewing public contracts, DTOs, events, commands, identifiers, or public package boundaries in the {{DOMAIN_NAME}} domain repo."
name: "{{DOMAIN_NAME}} Contracts Boundaries"
applyTo: "**/*Contracts*/**"
---
# Contracts Guidelines

- Keep contracts stable because they are cross-repo commitments.
- Contracts describe public exchange, not internal domain implementation.
- Prefer explicit public DTOs, events, commands, and identifiers.
- Do not leak persistence concerns or internal service abstractions into contracts.
