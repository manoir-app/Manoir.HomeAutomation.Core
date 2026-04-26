---
description: "Use when creating or modifying API, admin UI, controllers, endpoints, or back-office screens in the {{DOMAIN_NAME}} domain repo."
name: "{{DOMAIN_NAME}} API And UI Boundaries"
---
# API And UI Guidelines

- API and admin UI expose and operate the domain, but do not become the owner of deep business rules.
- UI consumes API or public clients, not internal domain implementation.
- If a behavior feels cross-domain, stop and check whether it belongs in an agent instead.
