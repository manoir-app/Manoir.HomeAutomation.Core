---
description: "Use when creating or modifying shells, dashboards, tablets, frontend experience modules, composed UI routing, or experience-facing admin screens in the MaNoir.Platform repo."
name: "MaNoir.Platform Experience UI Boundaries"
---
# Experience UI Guidelines

- Experience hosts and modules compose existing public capabilities; they do not own canonical business state.
- `apps/{{PACKAGE_PREFIX}}.Shell/` is the preferred .NET host when a server host exists.
- Frontend modules should remain focused and named by experience surface or feature.
- Prefer several focused modules in `ui/` over a single oversized frontend bundle.
- If a UI starts carrying business truth, stop and move that responsibility back to the owning repo.
