# Cadrage — Homelab Hub

**Figé le 11 août 2026**, à l'issue de trois tours d'échange. Les décisions structurantes sont
détaillées dans [`adr/`](adr/) ; ce document donne le contexte et le périmètre.

---

## 1. Le manque constaté

Un homelab familial sur Proxmox VE 9.2.10 (nœud unique, `192.168.1.200`), avec une stack média
complète et fonctionnelle. La chaîne Discord permet de *demander* un média (Doplarr) et d'être
*notifié* quand il devient disponible (webhook Seerr).

**Entre les deux, silence.** Impossible de savoir si un téléchargement a démarré, où il en est,
s'il patine, ou pourquoi rien ne se passe. Plus largement, chaque service a son interface, son
IP et son port, sans point d'entrée commun.

## 2. Environnement cible

| Service | Version | Emplacement |
|---|---|---|
| Seerr | 3.4.1 (post-renommage `seerr-team/seerr`) | `192.168.1.231:5055`, Debian 13 |
| Radarr | **6.3.0.10514** | `192.168.1.233:7878`, LXC 111, Debian 12 |
| Sonarr | **4.0.19.2979** | `192.168.1.232:8989`, LXC 110, Debian 12 |
| Prowlarr | — | LXC 114 |
| qBittorrent | **5.1.0** | `192.168.1.240:8090`, derrière WireGuard/NordLynx |
| Jellyfin | dernière | `192.168.1.208:8096` |
| AdGuard Home | — | LXC 113 |
| WireGuard | — | LXC 115 |
| Doplarr | — | LXC 103 |
| Home Assistant | — | installé, non intégré |

Stockage média : partage CIFS Synology monté sur l'hôte en `/mnt/syno`, redistribué aux LXC par
bind mount. Les hardlinks fonctionnent.

Réseau à plat : tous les LXC sur `vmbr0` en `192.168.1.0/24`, joignables entre eux sans
restriction. Le hub tournera dans un LXC Debian 13 dédié, fuseau `Europe/Paris`.

> **Radarr est en version 6 majeure.** Ne pas se fier à une documentation v5 pour la forme des
> réponses `/api/v3/queue` ou des payloads webhook. Les modèles de désérialisation seront écrits
> contre des réponses réellement capturées sur cette installation, puis figées comme fixtures de
> test. Même prudence sur Sonarr : `/api/v3` est commun aux deux, mais les payloads séries
> diffèrent des payloads films.

## 3. Objectifs

1. **Tableau de bord temps réel** — téléchargements en cours, progression, débit, état d'import,
   blocages. Dans Discord comme dans le navigateur.
2. **Notifications d'anomalie** — pas « événement X », mais « quelque chose ne va pas » :
   téléchargement bloqué, tunnel VPN tombé, disque critique, service injoignable.
3. **Configuration sans toucher au code** — tout depuis l'interface web.
4. **Double interface** — Discord et web exposent les mêmes capacités.
5. **Sauvegarde intégrée** — le hub concentrera toutes les clés d'API du homelab, sur un
   Proxmox qui n'a aujourd'hui aucune sauvegarde automatisée.

### Non fonctionnels

- Déploiement en un one-liner depuis l'hôte Proxmox, à la manière des Helper-Scripts.
- Mise à jour intégrée depuis les releases GitHub.
- Modulaire : ajouter un domaine ne doit pas exiger de modifier le noyau — et ce n'est pas une
  intention, c'est un test qui casse la CI ([ADR-0010](adr/0010-frontieres-de-projets.md)).
- Local d'abord : aucune dépendance cloud, aucune donnée hors du LAN.

### Hors périmètre v1

Multi-utilisateur à rôles fins · exposition Internet (l'accès distant passe par le WireGuard
existant) · remplacement de Doplarr, qui continue de tourner en parallèle · application mobile.

## 4. Usage

- **Discord** : le foyer, deux personnes. Lectures ouvertes à tous, modifications réservées au
  rôle `hub-admin`. Serveur unique, commandes enregistrées en guild.
- **Interface web** : une seule personne. Un compte admin unique suffit — et évite d'écrire une
  gestion d'utilisateurs.
- **Volume** : un à cinq téléchargements simultanés en pointe. Dimensionner pour la sobriété, pas
  pour la charge : ce LXC tourne 24/7 sur un serveur domestique.
- **Tolérance au bruit : faible.** Être notifié quand ça va mal, pas quand ça va bien.

## 5. Architecture

> Une seule couche métier, des adaptateurs minces.

```
Adaptateur Discord ─┐                  ┌─ Poller     (interrogation périodique)
                    ├─ Noyau ─ Modules ─┼─ Webhook    (push HTTP entrant)
Adaptateur REST   ──┘                  └─ Connexion  (WebSocket longue durée)
```

Une fonctionnalité s'écrit une fois, sous forme de **capacité**, et les adaptateurs l'exposent.
Les trois modes d'ingestion ont des cycles de vie différents mais **une seule sortie** : le
snapshot du module et son flux d'événements.

Le bot Discord tourne comme service d'arrière-plan dans le même processus que l'API web. **Un
binaire, une unité systemd** — c'est ce qui rend le packaging LXC simple.

Voir [ADR-0002](adr/0002-modules-statiques-activation-runtime.md),
[ADR-0003](adr/0003-trois-modes-ingestion.md), [ADR-0010](adr/0010-frontieres-de-projets.md).

## 6. Module Média — le point dur

L'intérêt du module est de **corréler** trois vues du même objet : la requête Seerr, la file
Radarr/Sonarr, le torrent qBittorrent.

**Clés de jointure :**

- Seerr → *arr : `media.externalServiceId` et `media.tmdbId` / `tvdbId` ;
- *arr → qBittorrent : `downloadId`, qui est le **hash du torrent en majuscules** là où
  qBittorrent le renvoie en minuscules. Normaliser la casse.

**Le modèle ne peut pas être « une ligne = un média »** :
`MediaJourney (1) ── (0..N) DownloadItem`, avec un parcours capable d'exister sans requête amont
et sans torrent aval.

Cinq cas limites déterminent la forme du modèle, écrits comme tests avant le code de
corrélation dans `tests/HomelabHub.Modules.Media.Tests` :

1. requête de saison résolue en pack unique ;
2. requête de saison résolue en N épisodes séparés ;
3. import manuel sans requête Seerr amont ;
4. média déjà présent, marqué disponible sans téléchargement ;
5. release remplacée par une meilleure — l'anomalie de l'ancien torrent doit se clore.

**qBittorrent 5.1.0 :** un seul utilisateur WebUI, pas de compte dédié possible. Le client
implémente le flux cookie complet (`/api/v2/auth/login` → `SID`, relogin sur 401/403) avec
identifiants en configuration, sans dépendre du `AuthSubnetWhitelist` actuellement actif, et
envoie un `Referer` correct — les protections CSRF sont supposées aux valeurs par défaut.

> **Diagnostic à connaître.** qBittorrent tourne sous `qbtuser` (uid 999), tout son trafic
> sortant est routé dans un tunnel WireGuard avec kill switch. Une règle de routage de priorité
> 100 exclut `192.168.0.0/16` du tunnel : c'est elle qui garde la WebUI joignable depuis le LAN.
> Si l'API devient subitement injoignable, commencer par là.

## 7. Roadmap

| Étape | Contenu | État |
|---|---|---|
| **0** | Squelette, contrats, CI, ADR | **fait** |
| **1** | Socle : journalisation, SQLite, configuration chiffrée, registre de modules, authentification, sauvegarde, **module `system`** comme banc de test, front minimal | **fait** |
| **2** | Module média : clients, corrélation, snapshot, REST + SignalR | **fait** |
| **3** | Dashboard Discord : message persistant, boutons, slash commands | **fait, vérifié en conditions réelles** (ADR-0008) |
| **4** | Notifications : détecteurs, anomalies, journal consultable | **fait** |
| **5** | Packaging : publication self-contained, GitHub Actions, scripts LXC, mise à jour | à venir |
| **6+** | Module Home Assistant, module Proxmox, absorption de Doplarr | |

Le verrou de premier démarrage, prévu à l'étape 1, a en réalité été livré à l'étape 3
([ADR-0018](adr/0018-verrou-de-premiere-instance.md)) — un incident en conditions réelles
(message Discord dédoublé par deux instances du hub tournant sur le même répertoire) l'a fait
remonter en priorité plutôt que de rester une case cochée trop tôt sur la foi d'une bonne
intention.

L'abstraction de modules est fusionnée dans l'étape 1 plutôt que traitée séparément : écrire le
socle avant de poser le contrat, c'est concevoir en aveugle. Le module `system` — réel, trivial,
utile en production — confronte le contrat à une implémentation avant qu'il ne soit figé.

Chaque étape doit laisser le projet déployable et utilisable.

## 8. Conventions

- **Français** pour la documentation et l'interface ; **anglais** pour le code et les commits.
- Commits conventionnels (`feat:`, `fix:`, `chore:`…).
- Tout appel réseau sortant : délai d'attente explicite, gestion d'erreur, jamais d'exception
  non capturée. **Un service injoignable dégrade l'affichage, il ne fait pas tomber le hub.**
- Logs structurés, niveau configurable depuis l'interface. Rétention 14 jours ou 100 000 lignes,
  premier atteint, purge quotidienne.
- Pas de secret dans le dépôt, pas de secret dans les logs.

## 9. Ce qui reste à fournir

| Manquant | Bloque | Échéance |
|---|---|---|
| **Clés d'API Radarr, Sonarr et Seerr** | étape 2 | **fourni** — capturé et anonymisé |
| Jeton Discord, serveur, salon dashboard, rôle hub-admin | étape 3 | **fourni** — adaptateur câblé, voir ci-dessous |
| IP + jeton longue durée Home Assistant | étape 6+ | non pressé |
| Champs Username/Password du webhook Radarr | rien | second facteur optionnel |

La machine de développement est sur le LAN (`192.168.1.17`) et joint les quatre services : les
fixtures de test seront capturées sur les instances réelles, puis anonymisées.

Guild Discord : `905758180364128256`. Le premier identifiant noté ici
(`1328010940041400370`) était erroné — l'application du bot n'y a jamais été invitée, ce qui a
coûté deux allers-retours d'invitation avant que l'erreur ne soit débusquée par un appel REST
direct (`GET /users/@me/guilds`) plutôt que par une supposition. Salon du tableau de bord :
`1537098548254998588`. Rôle `hub-admin` : `1537098626076254258`.
