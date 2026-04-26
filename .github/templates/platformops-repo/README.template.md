# MaNoir.Platform

Ce repository porte les composants de `Platform Operations` de MaNoir.

Il est responsable du deployment, de la convergence runtime, du control plane, et plus generalement du pilotage technique de la plateforme. Il ne doit pas devenir un nouveau Core transverse ni un domaine metier cache.

## Role du repository

Ce repo est destine a contenir principalement :

- `packages/{{PACKAGE_PREFIX}}.Core/`
- `packages/{{PACKAGE_PREFIX}}.Contracts/`
- `apps/{{PACKAGE_PREFIX}}.Api/` si une surface de pilotage est necessaire
- `packages/{{PACKAGE_PREFIX}}.Provider.Kubernetes/`
- `packages/{{PACKAGE_PREFIX}}.Provider.Docker/`
- `ui/{{PACKAGE_PREFIX}}.AdminUi/` si une interface d'exploitation est une SPA
- `apps/{{PACKAGE_PREFIX}}.AdminUi/` si cette interface est une application web .NET server-side

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

- la verite metier des domaines ;
- les primitives transverses generales du Core ;
- les dashboards et experiences front ;
- la corrélation des signaux externes du Communication Hub.
