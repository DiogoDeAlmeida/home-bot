# Déploiement — Homelab Hub

Étape 5 du cadrage. Voir [ADR-0019](adr/0019-packaging-et-mise-a-jour.md) pour les décisions et
leurs raisons ; ce document est la procédure.

> **Non vérifié en conditions réelles au moment de l'écriture.** Compilé, relu, mais jamais
> exécuté sur un vrai LXC — exactement le genre d'écart que [CONTRIBUTING.md](../CONTRIBUTING.md)
> demande de ne pas ignorer. **Avant tout LXC de production**, exercer la chaîne complète
> (création, installation, premier démarrage, mise à jour, rollback provoqué) sur un LXC jetable
> créé pour l'occasion. Cette page sera mise à jour une fois cette vérification faite.

## 1. Cible

LXC Debian 13 dédié, réseau à plat (cadrage §2). `curl` est le seul prérequis avant le premier
lancement — le script vérifie et installe le reste lui-même.

## 2. Disposition sur disque

```
/opt/homelabhub/
├── current -> releases/v0.2.0/      # lien symbolique, repointé à chaque mise à jour
├── releases/
│   ├── v0.1.0/                      # anciennes versions, conservées pour rollback
│   └── v0.2.0/
│       ├── homelabhub               # binaire self-contained (ADR-0001)
│       ├── wwwroot/                 # interface web, servie depuis le disque
│       └── appsettings.json
└── data/                            # jamais touché par une mise à jour
    ├── homelabhub.db
    ├── keys/                        # clés Data Protection
    └── backups/
        └── pre-update-*.db          # une sauvegarde par tentative de mise à jour

/etc/homelabhub/
└── hub.json                         # configuration chiffrée (ADR unrelated à cette étape)
```

`data/` et `hub.json` vivent hors de `releases/`, donc hors du chemin qu'une mise à jour
remplace — c'est ce qui rend le rollback binaire sans effet sur eux.

## 3. Installation initiale

```bash
curl -fsSL https://raw.githubusercontent.com/DiogoDeAlmeida/home-bot/main/deploy/install.sh | sudo bash
```

Ce que fait [`deploy/install.sh`](../deploy/install.sh), dans l'ordre :

1. Vérifie et installe les dépendances (`curl`, `tar`, `jq`, `sqlite3`, `libicu-dev`) — jamais
   supposées présentes.
2. Crée l'utilisateur système dédié `homelabhub` (sans shell, sans domicile créé).
3. Télécharge la dernière release (ou celle passée en argument), vérifie sa somme de contrôle.
4. Vérifie les bibliothèques natives du binaire lui-même (`ldd`), pas seulement les paquets
   Debian censés les fournir.
5. Installe et active l'unité systemd.
6. Attend `/healthz` jusqu'à 90 s avant de déclarer l'installation réussie.

À l'issue, l'interface web est accessible sur `http://<IP-du-LXC>:8080` pour la configuration
initiale (compte admin, clés d'API, Discord).

## 4. Mise à jour — toujours manuelle

**Aucune mise à jour ne s'applique d'elle-même.** Le hub détient les clés de tout le homelab ;
une mise à jour ratée en silence sur un service de cette nature est le genre d'incident qu'on
préfère ne jamais avoir à raconter. `SystemPoller` vérifie périodiquement (par défaut toutes les
12 h, `system.update.checkIntervalHours`) si une version plus récente existe sur GitHub, et le
signale comme n'importe quelle autre anomalie — dans Discord, dans le journal — sans jamais rien
déclencher.

```bash
sudo /opt/homelabhub/update.sh              # dernière version
sudo /opt/homelabhub/update.sh v0.3.0       # version précise
```

`update.sh` n'est pas dans l'archive de release : `install.sh` le dépose à côté du binaire lors
de l'installation initiale, et chaque mise à jour réussie le remplace par la copie correspondant
à la version qu'elle vient de déployer — l'outil reste toujours aligné sur ce qu'il gère.

Ce que fait [`deploy/update.sh`](../deploy/update.sh) :

1. Télécharge et vérifie la nouvelle version **pendant que l'ancienne tourne encore** — un
   échec de téléchargement ou de somme de contrôle n'interrompt rien.
2. Rafraîchit l'unité systemd depuis le tag ciblé et recharge systemd — avant l'arrêt, pas
   après : une version qui compte sur une directive nouvelle doit la trouver déjà en place à son
   premier démarrage.
3. Sauvegarde la base (`sqlite3 ... VACUUM INTO`) dans un fichier nommé pour cette tentative
   précise : `pre-update-<depuis>-vers-<vers>-<horodatage>.db`.
4. Arrête le service, repointe `current` vers la nouvelle version, redémarre.
5. Attend `/healthz` jusqu'à 90 s.
6. **Sain** → conserve les 3 dernières versions et les 5 dernières sauvegardes de mise à jour,
   purge le reste.
   **Dégradé ou indisponible** → rollback automatique : restaure *cette sauvegarde précise* (pas
   « la plus récente » au sens large) et repointe `current` vers la version précédente. Si le
   rollback lui-même échoue, le script s'arrête et demande une intervention manuelle plutôt que
   de tenter autre chose — voir ADR-0019, section rollback.

Rollback manuel, hors d'une mise à jour en cours :

```bash
sudo /opt/homelabhub/update.sh --rollback
```

## 5. `/healthz`

Trois vérifications, chacune pouvant seule faire échouer la sonde (503, détail en JSON) :

| Vérification | Sain quand | Source |
|---|---|---|
| Base | Lisible **et** sans migration en attente | `IDbContextFactory<HubDbContext>` |
| Discord | `Connected` ou `NotConfigured` | `IDiscordConnectionStatus` |
| Modules | Module `system` actif | `IModuleRegistry` |

**`Connecting` compte comme dégradé**, y compris juste après un démarrage normal — la poignée
de main Discord prend quelques secondes. C'est pour ça que les scripts d'installation et de
mise à jour tolèrent 90 s (30 tentatives, 3 s d'intervalle) avant de conclure à un échec réel.

## 6. Réglages : à chaud, ou redémarrage requis

Trouvé en conditions réelles sur le LXC jetable : la configuration Discord (jeton, serveur,
salon, rôle) semblait ne rien faire une fois enregistrée — en réalité, `DiscordGatewayService`
la lit une seule fois, à son démarrage, jamais rechargée pendant que le processus tourne.
Contrairement à la plupart des autres réglages, qui sont relus à chaque cycle et prennent donc
effet sans redémarrage.

| Réglage | À chaud ? |
|---|---|
| Niveau de journalisation | Oui — appliqué à l'enregistrement (`PUT /api/settings`) |
| Rétention et intervalle des sauvegardes | Oui — relu à chaque sauvegarde demandée |
| Rétention et intervalle de purge du journal | Oui — relu à chaque cycle de `RetentionService` |
| Intervalle de vérification de nouvelle version | Oui — relu à chaque cycle de `SystemPoller` |
| Seuils et intervalle du module `system` | Oui — relu à chaque cycle |
| URL, clé d'API, intervalle du module `media` (Radarr, Sonarr, Seerr, qBittorrent) | Oui — un client HTTP neuf est construit à chaque cycle, à partir de la configuration courante |
| **Jeton, serveur, salon, rôle Discord** | **Non — lu une seule fois au démarrage de `DiscordGatewayService`** |

Le formulaire des paramètres porte cette information sur chacun des quatre champs Discord
concernés. Pour l'appliquer sans SSH : la capacité `hub.service.restart` (« Redémarrer le
service », sous les réglages dans l'interface, `/hub service restart` depuis Discord), qui
demande confirmation puisqu'elle interrompt le service en cours quelques secondes.

Reconnecter le client Gateway à chaud plutôt que redémarrer le processus reste possible en
théorie, mais c'est une vraie complexité (ADR-0019 ne le retient pas pour cette étape) — le
redémarrage, lui, est déjà couvert : `deploy/systemd/homelabhub.service` porte `Restart=always`
précisément pour qu'un arrêt volontaire (`hub.service.restart`) reparte tout seul, pas seulement
un plantage.

## 7. Publication d'une release

```bash
git tag v0.3.0
git push origin v0.3.0
```

[`release.yml`](../.github/workflows/release.yml) publie un binaire self-contained linux-x64
(`dotnet publish ... -p:Version=0.3.0`), l'archive avec l'interface web construite, calcule sa
somme de contrôle, et crée la release GitHub avec les deux fichiers en pièces jointes. La CI
(`ci.yml`) a déjà validé le commit avant que le tag n'y soit posé — cette étape ne rejoue aucun
test, elle publie ce qui a déjà été vérifié.

## 8. Ce qui reste hors de cette étape

- **Le one-liner depuis l'hôte Proxmox** (`pct create` + injection de `install.sh`, à la manière
  des Helper-Scripts) n'est pas écrit : `deploy/install.sh` s'exécute *dans* un LXC déjà créé,
  pas depuis l'hôte. Créer le LXC reste un geste manuel pour l'instant — candidat naturel pour
  une prochaine tranche, une fois cette chaîne prouvée.
- Aucune restauration de base n'est exposée depuis l'interface : `deploy/update.sh` restaure au
  niveau fichier, en dehors du hub, précisément parce qu'un rollback doit fonctionner même quand
  le binaire qu'on restaure ne démarre pas.
