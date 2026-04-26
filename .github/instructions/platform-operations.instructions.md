---
description: "Use when discussing deployment, runtime convergence, Kubernetes, Docker orchestration, control plane, platform runtime management, Gaia-like components, or deciding whether code belongs in PlatformOps instead of Core."
name: "MaNoir Platform Operations Placement"
---
# Platform Operations Placement

- Components that deploy, converge, supervise, or pilot the platform runtime belong to Platform Operations.
- Typical examples are Kubernetes control, Docker control, deployment convergence, runtime configuration, technical platform state, and control-plane APIs.
- These concerns do not belong in Core, business domains, or composed front experiences.
- A Gaia-like component is a PlatformOps component by default.
- If a feature changes where or how the platform runs, treat it as PlatformOps unless proven otherwise.

See the Platform Operations sections in [ARCHITECTURE.md](../../ARCHITECTURE.md).