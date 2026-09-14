# MaNoir Repository Templates

Ce dossier contient des starters minimaux pour ouvrir de nouveaux repositories MaNoir sans repartir de zero a chaque fois.

Le but n'est pas de fournir un squelette complet de code, mais de fournir un point de depart propre pour :

- le cadrage du repo ;
- les frontieres d'architecture ;
- les customisations Copilot ;
- les conventions de base.

## Principe

Chaque template doit etre traite comme un point de depart intentionnel, pas comme un copier-coller aveugle.

Avant de l'utiliser :

1. choisir la famille du repo ;
2. remplacer les placeholders ;
3. supprimer ce qui n'est pas necessaire le premier jour ;
4. garder seulement les projets et les instructions justifies par le besoin reel.

## Templates disponibles

- `platform-repo/` : pour le socle plateforme transverse, le Core, et le CommunicationHub ;
- `domain-repo/` : pour un grand domaine metier sans agent local ;
- `domain-with-agents-repo/` : pour un domaine metier qui heberge aussi un ou plusieurs agents locaux qui lui sont propres (ex: un domaine domotique avec son propre agent de scripting/scenes) — c'est probablement le cas le plus frequent ;
- `agents-repo/` : pour un repo d'agents transverses multi-domaines.
- `experiences-repo/` : pour les shells, dashboards, tablettes, et autres experiences composees.

Il n'y a volontairement pas de template `platformops-repo/` : PlatformOps est un socle technique unique (Manoir.Ops/Gaia), pas une famille de repos reproductible.

`domain-with-agents-repo/` embarque aussi de vrais fichiers de depart executables, pas seulement du texte, car ce sont exactement les pieces qui ont fait perdre des heures de debug sur un vrai nouveau repo : `manoir.plugin.yaml`, `.github/workflows/build.yml` (avec le job `publish-plugin-catalog`), `deploy/docker-compose.yml`, un `Program.cs` d'Admin UI cable sur `MaNoir.Core.AdminUi.Hosting`, et un couple `vite.config.ts`/`runtimeConfig.ts` suivant la convention build relatif + `<base href>`. Remplacer chaque `{{PLACEHOLDER}}` (y compris dans les noms de dossiers/fichiers) avant utilisation.


## Usage recommande

1. Partir du template le plus proche.
2. Copier le contenu dans le nouveau repo.
3. Remplacer les placeholders comme `MaNoir.Platform`, `{{DOMAIN_NAME}}`, `{{PACKAGE_PREFIX}}`.
4. Relire les frontieres du repo avant de creer les projets.
5. Ajuster ou supprimer les instructions trop larges pour le repo cree.

## Structure racine recommandee

Les templates MaNoir partent de la meme structure racine :

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

Regles de nommage :

- les applications executables sont rangees sous `apps/<ExactProjectName>/` ;
- les librairies et packages sont ranges sous `packages/<ExactProjectName>/` ;
- les SPA et frontends web sont ranges sous `ui/<ExactProjectName>/` ;
- les tests sont ranges sous `tests/<ExactProjectName>.Tests/` ;
- le back-office s'appelle toujours `AdminUi` ;
- on n'utilise pas de dossiers racine concurrents comme `bo/`, `pages/`, `frontend/`, ou `backend/`.

## Regles

- un template doit aider a demarrer vite, pas figer trop tot une structure inutile ;
- un template ne remplace pas [ARCHITECTURE.md](../../ARCHITECTURE.md) ;
- si un nouveau repo ne rentre dans aucun template, il faut d'abord clarifier sa famille plutot que forcer un starter inadapté.

## Completion future

Si besoin, ce dossier pourra accueillir ensuite :

- des checklists de publication de packages ;
- des squelettes `.csproj` ou solution si cela devient utile.
