# Cycle de vie d'un téléchargement

Capturé le 11 août 2026 sur les instances réelles, pendant qu'un film et deux packs de saison
étaient téléchargés puis importés. Anonymisé.

Un instantané unique n'aurait rien montré : les champs qui portent la corrélation ne prennent
leurs valeurs intéressantes qu'en transition. Ces fixtures sont **une séquence**, pas une photo.

| Fichier | Instant | Ce qu'il contient |
|---|---|---|
| `00`, `10` | 21:19 | Files vides — état de repos |
| `01` | 21:25 | Radarr, `status=warning` **dès la première seconde** |
| `11` | 21:26 | Sonarr, même chose, 44 enregistrements |
| `02` | 21:28 | Radarr `downloading/downloading/ok` |
| `12` | 21:28 | Sonarr, **2 packs de saison = 44 enregistrements** |
| `13` | 21:40 | **22 en `downloading` et 22 en `importPending`, simultanément** |
| `14` | 21:40 | Un pack importé : ses 22 enregistrements ont disparu |
| `15` | 21:43 | Le second pack seul, en `importPending` |
| `03`, `16` | 21:37 / 21:43 | Files vides — après import |
| `20`, `21` | | qBittorrent au repos, puis en téléchargement |
| `30`, `31` | | Requêtes Seerr, sans puis avec `downloadStatus` |
| `40`–`43` | 12/08 | **Import bloqué** — file, historique, candidat d'import manuel, torrent |

## L'import bloqué (`40`–`43`)

Capturé le 12 août sur un cas non provoqué : un téléchargement interrompu puis relancé à la main
dans qBittorrent, contournant le pilotage de Radarr. Il apporte la sémantique de
`statusMessages`, jusque-là documentée comme inconnue.

```
trackedDownloadState  : importBlocked      ← valeur jamais observée avant
trackedDownloadStatus : warning
errorMessage          : ""                 ← vide
statusMessages        : ["Found matching movie via grab history, but release was
                          matched to movie by ID. Manual Import required."]
```

**C'est la seule source de l'explication** : l'historique ne porte que `grabbed`, et les journaux
de Radarr n'en gardent aucune trace, à aucun niveau. Le fichier `42` montre par ailleurs que
`/api/v3/manualimport` renvoyait **zéro rejet** — le fichier était importable et n'attendait
qu'une confirmation.

## Le cycle observé

```
warning/downloading/ok  →  downloading/downloading/ok  →  completed/importPending/ok  →  disparition
```

`status` et `trackedDownloadState` sont **deux axes distincts**, et `trackedDownloadStatus` est
le seul qui dise si quelque chose ne va pas.

## Ce que ces fixtures ont appris

Les trois découvertes sont détaillées dans
[ADR-0015](../../../../docs/adr/0015-mediajourney-etat-derive.md) :

1. **Un pack de saison produit 22 enregistrements de file pour un torrent**, avec la `size`
   répétée. Agréger sans regrouper par `downloadId` donne 451 Go au lieu de 20,5 Go.
2. **`status=warning` apparaît dès la première seconde** — « stalled with no connections » avant
   tout contact avec un pair. Un détecteur naïf se déclencherait sur chaque téléchargement.
3. **`importPending` dure moins de cinq secondes** dans le cas nominal, et le film n'y est jamais
   passé. On ne le détecte pas au polling ; le voir est en soi le signal.

## Reproduire

```powershell
./scripts/capture-media-fixtures.ps1 -OutputDirectory ./capture `
    -RadarrUrl … -RadarrApiKey … -SonarrUrl … -SonarrApiKey … `
    -SeerrUrl … -SeerrApiKey … -QBittorrentUrl … -DurationMinutes 45
```

Déclencher un téléchargement pendant que le script tourne. Il n'écrit un fichier que lorsqu'une
signature d'état change.
