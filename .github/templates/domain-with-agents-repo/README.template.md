# {{REPO_NAME}}

Ce repository porte le domaine `{{DOMAIN_NAME}}` de MaNoir, ainsi qu'un ou plusieurs agents locaux propres a ce domaine.

Il est responsable de la verite metier du domaine, de ses contrats publics, de son API, de son eventuel back-office, et du comportement de ses agents locaux. Il ne doit pas devenir un point d'accumulation de logique transverse qui releve du Core, du Communication Hub, de PlatformOps, ou des experiences composees. Si un agent commence a coordonner plusieurs domaines, il doit migrer vers un repo agents dedie plutot que de grossir ici.

## Role du repository

Ce repo est destine a contenir principalement :

- `packages/{{PACKAGE_PREFIX}}.Domain/`
- `packages/{{PACKAGE_PREFIX}}.Contracts/`
- `apps/{{PACKAGE_PREFIX}}.Api/`
- `apps/{{PACKAGE_PREFIX}}.AdminUi/` comme host web .NET du back-office
- `apps/{{PACKAGE_PREFIX}}.Agents.<AgentName>/` pour chaque agent local strictement mono-domaine
- `ui/{{PACKAGE_PREFIX}}.AdminUi.Shared/` pour le frontend partage
- `ui/{{PACKAGE_PREFIX}}.AdminUi.<Feature>/` pour les modules frontend d'administration

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
- des agents transverses multi-domaines (a deplacer vers un repo agents dedie) ;
- des experiences front composees.

## Documentation

Completer ce README avec :

- la responsabilite du domaine ;
- ses objets majeurs ;
- ses surfaces publiques ;
- ses agents locaux et ce qu'ils orchestrent ;
- ses dependances autorisees.

Si le back-office utilise React ou Vue, documenter quels modules frontend sont servis par `{{PACKAGE_PREFIX}}.AdminUi`.
