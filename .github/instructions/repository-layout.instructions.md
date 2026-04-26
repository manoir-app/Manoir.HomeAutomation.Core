---
description: "Use when creating or reorganizing repository folders, project layout, solution structure, source folders, test folders, or deciding how to name UI/admin folders in a MaNoir repo."
name: "MaNoir Repository Layout"
---
# Repository Layout Guidelines

- Use a stable root layout vocabulary: `.github/`, `docs/`, `eng/`, `apps/`, `packages/`, `ui/`, `tests/`, `ops/`.
- Place executable projects under `apps/<ExactProjectName>/`.
- Place reusable or publishable packages under `packages/<ExactProjectName>/`.
- Place SPA and frontend projects under `ui/<ExactProjectName>/`.
- Place test projects under `tests/<ExactProjectName>.Tests/`.
- Use `AdminUi` as the only accepted suffix for back-office UI projects.
- Treat `apps/MaNoir.X.AdminUi/` as the preferred .NET host when the admin surface has a server host.
- Place frontend admin modules under `ui/MaNoir.X.AdminUi.<Feature>/` and optional shared frontend code under `ui/MaNoir.X.AdminUi.Shared/`.
- Do not create competing root folders such as `bo/`, `pages/`, `frontend/`, `backend/`, `api/`, `domain/`, or `services/`.
- If a UI project contains a framework-specific internal layout, keep it inside the project folder.

When in doubt, prefer one more project folder under `apps/`, `packages/`, or `ui/` rather than inventing a new root convention.
