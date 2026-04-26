# MaNoir.Platform

Ce repository porte le domaine `{{DOMAIN_NAME}}` de MaNoir.

Il est responsable de la verite metier du domaine, de ses contrats publics, de son API, et eventuellement de son back-office. Il ne doit pas devenir un point d'accumulation de logique transverse qui releve du Core, du Communication Hub, de PlatformOps, ou des experiences composees.

## Role du repository

Ce repo est destine a contenir principalement :

- `packages/{{PACKAGE_PREFIX}}.Domain/`
- `packages/{{PACKAGE_PREFIX}}.Contracts/`
- `apps/{{PACKAGE_PREFIX}}.Api/`
- `apps/{{PACKAGE_PREFIX}}.AdminUi/` comme host web .NET du back-office
- `ui/{{PACKAGE_PREFIX}}.AdminUi.Shared/` pour le frontend partage
- `ui/{{PACKAGE_PREFIX}}.AdminUi.<Feature>/` pour les modules frontend d'administration
- `apps/{{PACKAGE_PREFIX}}.AgentLocal/` seulement si un agent est strictement mono-domaine

## Structure racine recommandee

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

## Ce que ce repository ne doit pas devenir

Ce repo ne doit pas absorber :

- des primitives transverses de plateforme ;
- des composants de corrélation ou d'ingestion externe qui relevent du Communication Hub ;
- des composants de deployment ou de control plane ;
- des agents transverses multi-domaines ;
- des experiences front composees.

## Documentation

Completer ce README avec :

- la responsabilite du domaine ;
- ses objets majeurs ;
- ses surfaces publiques ;
- ses dependances autorisees.

Si le back-office utilise React ou Vue, documenter quels modules frontend sont servis par `{{PACKAGE_PREFIX}}.AdminUi`.
