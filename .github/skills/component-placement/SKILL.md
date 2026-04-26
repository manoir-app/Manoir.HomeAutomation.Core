---
name: component-placement
description: 'Decide where a new MaNoir component belongs. Use when placing a new service, API, UI, agent, deployment tool, communication component, Gaia-like runtime controller, shared package, or uncertain feature into the right repo or architectural family.'
argument-hint: 'Describe the component, what it does, what it depends on, and whether it owns business truth, orchestration, UI, or runtime control.'
user-invocable: true
---

# Component Placement

Use this skill when a new component appears and its placement is unclear.

## Goal

Choose the right architectural family before code is created, so MaNoir does not drift back into a functional monolith.

## Families

Evaluate the component against these families:

1. Platform Core
Used for transverse primitives without which the rest cannot exist.

2. Communication Hub
Used for ingestion, normalization, correlation, deduplication, and routing of external signals.

3. Business Domain
Used when the component owns a specific business truth.

4. Platform Operations
Used for deployment, runtime convergence, Kubernetes or Docker control, control plane, and technical platform state.

5. Operational Agents
Used for orchestration across multiple blocks without owning their canonical truth.

6. Composed Experiences
Used for dashboards, tablet experiences, front shells, and cross-capability user experiences.

7. Lab / Quarantine
Used only when no stable placement is clear yet.

## Procedure

1. Summarize the component in one sentence.
2. Identify whether it owns business truth, runtime control, orchestration, UI composition, or external signal handling.
3. Decide which family is the primary owner.
4. Propose the target repo family.
5. State what the component must not absorb.
6. If placement remains unclear, explicitly recommend quarantine instead of silent expansion of an existing repo.

## Output Format

Provide:

- recommended family;
- recommended repo name pattern;
- why it belongs there;
- what it must not absorb;
- whether it needs `Contracts`, `Client`, `Api`, `AdminUi`, or no public package at all.

## References

- [Architecture](../../../ARCHITECTURE.md)
- [Repository README](../../../README.md)
