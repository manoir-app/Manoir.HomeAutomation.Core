---
description: "Use when creating or modifying API, admin UI, back-office UI, controllers, endpoints, frontend admin screens, or integration surfaces in MaNoir. Covers API/UI boundaries and forbidden dependencies."
name: "MaNoir API And UI Boundaries"
---
# API And UI Guidelines

- APIs expose platform capabilities; they do not become the place where deep business rules accumulate.
- Admin UIs and back-office UIs consume APIs or public clients, never internal domain implementation.
- `MaNoir.X.AdminUi` should be treated as the .NET host of the admin experience when a server host exists.
- If several admin hosts need the same .NET bootstrap, prefer a focused platform package such as `MaNoir.Core.AdminUi.Hosting` instead of copying host plumbing across repos.
- If several React admin modules need the same UI foundations, prefer a focused npm package such as `MaNoir.Core.AdminUi.Kit` under `ui/` instead of copying components and hooks across repos.
- Frontend admin modules should live in `ui/` with explicit functional names such as `MaNoir.X.AdminUi.Users` or `MaNoir.X.AdminUi.Settings`.
- Prefer multiple focused frontend modules over a single oversized admin SPA when the domain grows.
- A UI may compose existing capabilities, but it must not become the canonical owner of business state.
- `MaNoir.Core.AdminUi.Hosting` stays a technical foundation package for the .NET admin host, not a place for domain screens or workflows.
- `MaNoir.Core.AdminUi.Kit` stays a technical foundation package for reusable React/UI components and utilities, not a place for domain screens or domain rules.
- If an API or UI needs behavior that feels like orchestration, verify whether that logic belongs in an agent or in Platform Operations instead.
- If a feature needs deployment/runtime control, it does not belong in Core API or Core Admin UI.

Use [ARCHITECTURE.md](../../ARCHITECTURE.md) as the source of truth for repo boundaries.