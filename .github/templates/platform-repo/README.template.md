# MaNoir.Platform

Ce repository porte la famille plateforme transverse de MaNoir.

Il est responsable du socle `Core`, du `CommunicationHub`, et des surfaces techniques publiques necessaires aux autres repos. Il ne doit pas devenir un melange flou entre Core, PlatformOps, domaines metier et experiences front.

## Role du repository

Ce repo est destine a contenir principalement :

- `packages/MaNoir.Core/`
- `packages/MaNoir.Core.Contracts/`
- `packages/MaNoir.Core.Client/` seulement si un client public est reellement utile
- `apps/MaNoir.Core.Api/`
- `apps/MaNoir.Core.AdminUi/` comme host web .NET du back-office transverse
- `packages/MaNoir.Core.AdminUi.Hosting/` pour les bases .NET du host AdminUi
- `ui/MaNoir.Core.AdminUi.Kit/` pour les composants React/UI reutilisables
- `packages/MaNoir.CommunicationHub/`
- `packages/MaNoir.CommunicationHub.Contracts/`
- `packages/MaNoir.CommunicationHub.Client/` seulement si un client public est reellement utile
- `apps/MaNoir.CommunicationHub.Api/` seulement si une surface API est necessaire

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

- la verite metier des grands domaines ;
- les composants de PlatformOps et de control plane ;
- les agents transverses ;
- les experiences front composees ;
- un ensemble flou de composants "communs" sans frontiere claire.

## Documentation

Completer ce README avec :

- les primitives transverses effectivement portees par `MaNoir.Core` ;
- le role exact du `CommunicationHub` ;
- les surfaces publiques publiees ou non ;
- les dependances autorisees pour les autres familles de repos.
