# MaNoir.Platform

Ce repository porte une famille d'experiences composees MaNoir.

Il est responsable de l'assemblage de plusieurs capacites du systeme pour produire une experience utilisateur coherente, sans devenir la source de verite du metier sous-jacent.

## Role du repository

Ce repo est destine a contenir principalement :

- `apps/{{PACKAGE_PREFIX}}.Shell/` comme host web .NET principal de l'experience
- `ui/{{PACKAGE_PREFIX}}.Shared/` pour le frontend partage
- `ui/{{PACKAGE_PREFIX}}.<Feature>/` pour les modules frontend composes
- `packages/{{PACKAGE_PREFIX}}.Client/` seulement si un SDK experience partage est justifie

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
- les composants de PlatformOps ;
- la corrélation des signaux externes du Communication Hub.

## Documentation

Completer ce README avec :

- les experiences exposees ;
- les modules frontend servis par le host ;
- les API et clients publics consommes ;
- les limites explicites entre composition d'experience et logique metier.
