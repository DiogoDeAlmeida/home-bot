# Homelab Hub

Point d'entrée unifié d'un homelab familial sous Proxmox : un service auto-hébergé qui
agrège les services existants et expose leurs capacités **à la fois** via un bot Discord et
une interface web locale.

> **État : étape 1 quasi complète.** Le noyau, le module `system`, la sauvegarde intégrée et
> l'interface web fonctionnent, derrière un compte administrateur. Reste SQLite et EF Core, qui
> arriveront avec les anomalies persistantes ([ADR-0007](docs/adr/0007-migrations-et-sauvegarde-au-demarrage.md)).
> Ensuite : le module média (étape 2), puis l'adaptateur Discord (étape 3).

## Pourquoi

La chaîne Discord existante permet de *demander* un média et d'être *notifié* quand il est
disponible. Entre les deux, silence : impossible de savoir si un téléchargement a démarré, où
il en est, ou pourquoi rien ne se passe. Plus largement, chaque service a son interface, son
IP et son port, sans point d'entrée commun.

## Principe d'architecture

Une seule couche métier, plusieurs adaptateurs minces. Une fonctionnalité s'écrit une fois,
sous forme de **capacité**, et les adaptateurs Discord et REST l'exposent.

```
Adaptateur Discord ─┐                  ┌─ Poller     (interrogation périodique)
                    ├─ Noyau ─ Modules ─┼─ Webhook    (push HTTP entrant)
Adaptateur REST   ──┘                  └─ Connexion  (WebSocket longue durée)
```

Les trois modes d'ingestion ont des cycles de vie différents mais **une seule sortie** : le
snapshot du module et son flux d'événements. Les consommateurs ne savent jamais d'où vient la
donnée.

## Structure

| Chemin | Rôle |
|---|---|
| `src/HomelabHub.Abstractions` | Contrats. Zéro dépendance projet. **La seule chose qu'un module référence.** |
| `src/HomelabHub.Core` | Noyau : registre, exécution des capacités, configuration, anomalies |
| `src/HomelabHub.Infrastructure` | EF Core, protection des données, clients HTTP résilients |
| `src/HomelabHub.Modules.System` | Module système — banc de test de l'abstraction |
| `src/HomelabHub.Modules.Media` | Module média — Seerr, Radarr, Sonarr, qBittorrent |
| `src/HomelabHub.Discord` | Adaptateur Discord |
| `src/HomelabHub.Host` | Racine de composition. Seul projet exécutable. |
| `web-ui/` | Front React + TypeScript + Vite |
| `docs/adr/` | Décisions d'architecture, datées et motivées |

La règle « un module ne référence que `Abstractions` » est vérifiée par un test exécuté en CI
([ADR-0010](docs/adr/0010-frontieres-de-projets.md)). Ce n'est pas une convention : c'est une
build qui casse.

## Prérequis

- **SDK .NET 10** (LTS). La version exacte est épinglée dans `global.json`.
- **Node.js 22 LTS** pour l'interface.

## Développer

```bash
dotnet build && dotnet test
dotnet run --project src/HomelabHub.Host      # API sur http://localhost:8080

cd web-ui && npm install && npm run dev        # interface sur http://localhost:5173
```

En développement, le serveur Vite proxifie `/api` vers le Host. En production, le front est
buildé dans `wwwroot` et servi par ce même Host : une seule origine, donc aucun CORS.

```bash
dotnet publish src/HomelabHub.Host -c Release  # build le front automatiquement
dotnet build -c Release -p:SkipWebUi=true      # ou l'ignorer si Node est absent
```

## API

Toute l'API est verrouillée tant que `POST /api/setup` n'a pas défini de mot de passe
administrateur, puis exige un cookie de session.

| Route | Rôle |
|---|---|
| `GET /healthz` | Sonde de disponibilité, anonyme |
| `GET`/`POST /api/setup` | Assistant de premier démarrage |
| `POST /api/auth/login` · `/logout` · `GET /me` | Session administrateur |
| `GET /api/modules` | Modules, activation, configuration complète ou non |
| `POST /api/modules/{clé}/enabled` | Activer ou désactiver, sans redémarrage |
| `GET`/`PUT /api/modules/{clé}/config` | Schéma et valeurs, secrets masqués en lecture |
| `GET /api/modules/{clé}/health` | Sonde du module |
| `GET`/`PUT /api/settings` | Réglages du hub — même schéma, même formulaire que pour un module |
| `GET /api/widgets` | Widgets des modules actifs, données pures |
| `GET /api/backups` | Archives présentes |
| `GET /api/capabilities` | Capacités exposées en REST |
| `POST /api/capabilities/{clé}` | Exécuter une capacité |
| `GET /api/journal` | Derniers événements |

## Documentation

- [Cadrage](docs/00-cadrage.md) — genèse, arbitrages, périmètre, roadmap
- [Ajouter un module](docs/02-ajouter-un-module.md) — le guide qui sert de test à l'abstraction
- [Décisions d'architecture](docs/adr/) — le *pourquoi* de chaque choix structurant

## Licence

À définir avant la première publication.
