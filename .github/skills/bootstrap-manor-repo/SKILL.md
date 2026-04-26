---
name: bootstrap-manor-repo
description: 'Bootstrap a new MaNoir repository cleanly. Use when creating a new domain repo, platform repo, PlatformOps repo, agents repo, experiences repo, or when deciding which projects, packages, and instructions should exist from day one.'
argument-hint: 'Describe the new repo purpose, its family, and the first capabilities it must support.'
user-invocable: true
---

# Bootstrap MaNoir Repo

Use this skill to define the minimal clean starting structure for a new MaNoir repository.

## Goal

Create only the projects, packages, and instructions that are justified on day one, while keeping repo boundaries aligned with MaNoir architecture.

## Procedure

1. Identify the repo family:
   Platform, Business Domain, Platform Operations, Operational Agents, Experiences, or Lab.
2. State the repository responsibility in one short paragraph.
3. List what this repo is allowed to contain.
4. List what this repo must not absorb.
5. Propose the minimal repository root layout.
6. Propose the minimal internal project structure.
7. Decide which packages, if any, should be published:
   usually `Contracts`, sometimes `Client`, rarely anything else.
8. Propose the minimal customizations to create:
   `copilot-instructions.md`, targeted `.instructions.md`, and optional skills.
9. Call out the first architectural risks for this repo.

Use this default root layout unless a strong reason exists to change it:

```text
/
  .github/
  docs/
  eng/
   apps/
   packages/
   ui/
  tests/
   ops/
```

## Default Starting Patterns

### Platform repo

- `MaNoir.Core`
- `MaNoir.Core.Contracts`
- `MaNoir.Core.Client` only if justified
- `MaNoir.Core.Api`
- `MaNoir.Core.AdminUi`
- `MaNoir.Core.AdminUi.Hosting` only if several admin hosts share the same .NET host foundation
- `MaNoir.Core.AdminUi.Shared` for shared React/UI foundations when justified
- `MaNoir.CommunicationHub`
- `MaNoir.CommunicationHub.Contracts`
- `MaNoir.CommunicationHub.Client` only if justified
- `MaNoir.CommunicationHub.Api` only if an API surface is required

### Business domain repo

- `MaNoir.X.Domain`
- `MaNoir.X.Contracts`
- `MaNoir.X.Api`
- `MaNoir.X.AdminUi`
- `MaNoir.X.AdminUi.<Feature>` for frontend admin modules when needed
- `MaNoir.X.AgentLocal` only if clearly needed

### Platform Operations repo

- `MaNoir.PlatformOps.Core`
- `MaNoir.PlatformOps.Contracts`
- `MaNoir.PlatformOps.Provider.Kubernetes`
- `MaNoir.PlatformOps.Provider.Docker`
- `MaNoir.PlatformOps.Api` only if a control surface is required
- `MaNoir.PlatformOps.AdminUi` only if an operations UI is required

### Agents repo

- a runtime project only if several agents truly share one
- one project per real agent unit inside the same repo family

### Experiences repo

- `MaNoir.Experience.Shell` as the preferred .NET host
- `MaNoir.Experience.Shared` for shared frontend assets when justified
- `MaNoir.Experience.<Feature>` for focused frontend modules
only create the ones that are immediately needed

## Output Format

Return:

- recommended repo name;
- repository family;
- minimal project list;
- packages to publish and packages to keep internal;
- customizations to create;
- main drift risks to watch.

## References

- [Architecture](../../../ARCHITECTURE.md)
- [Repository README](../../../README.md)
