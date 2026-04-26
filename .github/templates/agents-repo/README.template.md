# MaNoir.Platform

Ce repository porte une famille d'agents transverses MaNoir.

Il est responsable de l'orchestration entre plusieurs blocs du systeme, sans devenir la source de verite de leurs objets metier.

## Role du repository

Ce repo est destine a contenir principalement :

- un runtime partage dans `packages/` seulement si plusieurs agents en ont reellement besoin ;
- un projet par vrai agent transverse dans `apps/` ;
- des composants d'orchestration et de reaction cross-domain.

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
- les primitives generales du Core ;
- des composants de PlatformOps ;
- des experiences front.
