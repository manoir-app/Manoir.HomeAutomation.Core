---
description: "Use when creating or reviewing public contracts, DTOs, events, commands, identifiers, or public package boundaries in the MaNoir.Platform platform repo."
name: "MaNoir.Platform Contracts Boundaries"
applyTo: "**/*Contracts*/**"
---
# Contracts Guidelines

- Keep contracts stable because they are cross-repo commitments.
- Contracts describe public exchange, not internal implementation.
- Keep `MaNoir.Core.Contracts` focused on transverse public primitives.
- Keep `MaNoir.CommunicationHub.Contracts` focused on public interaction, signal, and routing contracts.
- Do not leak persistence concerns or internal service abstractions into contracts.
