---
description: "Use when creating or modifying platform APIs, admin UI, controllers, endpoints, frontend admin screens, or integration surfaces in the MaNoir.Platform repo."
name: "MaNoir.Platform API And UI Boundaries"
---
# API And UI Guidelines

- APIs expose platform capabilities; they do not become the place where deep business rules accumulate.
- Admin UIs consume APIs or public clients, never internal domain implementation.
- `MaNoir.Core.AdminUi` should be treated as the .NET host of the admin experience when a server host exists.
- If several admin hosts need the same .NET bootstrap, prefer a focused platform package such as `MaNoir.Core.AdminUi.Hosting`.
- If several React admin modules need the same UI foundations, prefer a focused npm package such as `MaNoir.Core.AdminUi.Kit` under `ui/`.
- Frontend admin modules should live in `ui/` with explicit functional names such as `MaNoir.Core.AdminUi.Users` or `MaNoir.Core.AdminUi.Settings`.
- `MaNoir.Core.AdminUi.Hosting` stays a technical foundation package for the .NET admin host.
- `MaNoir.Core.AdminUi.Kit` stays a technical foundation package for reusable React/UI components and utilities.
- If a feature needs deployment/runtime control, it does not belong in Core API or Core Admin UI.
