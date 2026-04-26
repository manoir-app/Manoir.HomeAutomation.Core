---
description: "Use when creating or modifying deployment orchestration, runtime convergence, Kubernetes control, Docker control, control-plane APIs, or Gaia-like components in the MaNoir.Platform repo."
name: "MaNoir.Platform Platform Operations"
---
# Platform Operations Guidelines

- Components here own platform execution and convergence.
- They do not own business truth from domains.
- They may read public contracts from other repos, but should not depend on internal domain implementation.
- Prefer provider-based separation when multiple runtime targets exist.
