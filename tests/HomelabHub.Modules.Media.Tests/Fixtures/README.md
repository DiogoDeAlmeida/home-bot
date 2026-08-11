# Fixtures

Réponses **capturées sur les instances réelles** du homelab le 11 août 2026, puis anonymisées.
Pas des mocks écrits à la main : Radarr est en majeure 6, et une forme de réponse supposée
d'après la documentation v5 est exactement le genre d'erreur qui coûte une soirée.

| Service | Version |
|---|---|
| Radarr | 6.3.0.10514 |
| Sonarr | 4.0.19.2979 |
| Seerr | 3.4.1 |
| qBittorrent | 5.1.0 (WebAPI 2.11.4) |

## Anonymisation

Produite par [`scripts/anonymize-fixtures.ps1`](../../../../scripts/anonymize-fixtures.ps1), qui
opère **uniquement par substitution de texte**. Il ne parse jamais le JSON et ne le resérialise
jamais.

> **Leçon payée.** Une première version passait par `ConvertFrom-Json` / `ConvertTo-Json` de
> PowerShell, qui **déballe silencieusement les tableaux à un seul élément** :
> `"records": [ { … } ]` devenait `"records": { … }`. Les fixtures restaient du JSON valide mais
> ne représentaient plus ce que les services renvoient — c'est-à-dire qu'elles ne servaient plus
> à rien. Les tests de contrat l'ont attrapé ; sans eux, la corrélation aurait été écrite contre
> des données fausses.

## Ce que l'anonymisation a retiré

Titres de médias, chemins de fichiers, URL d'indexeurs et de trackers, adresses IP du LAN, noms
d'instances. Les identifiants publics — `tmdbId`, `tvdbId` — sont conservés : ce sont des
références vers des bases publiques, ils ne révèlent rien de l'installation.

**Les hashs de torrents ont été réécrits, pas supprimés.** Un infohash identifie une release
précise ; le laisser reviendrait à publier la bibliothèque. La réécriture est **stable** : un
même hash réel donne toujours le même hash synthétique, dans tous les fichiers. La jointure que
les tests doivent exercer est donc préservée.

## Ce que ces captures ont déjà prouvé

**La clé de corrélation, sur données réelles :** `downloadId` côté Radarr et Sonarr est le hash
SHA-1 du torrent en **majuscules**, sur 40 caractères ; `hash` côté qBittorrent est le même en
**minuscules**. Quatre correspondances sur cinq entrées d'historique testées — la cinquième
n'avait plus de torrent, ce qui est précisément le cas « parcours sans torrent aval ».

**Les catégories qBittorrent** utilisées par l'installation : `radarr`, `tv-sonarr`, et une
catégorie vide (import manuel).

**BitTorrent v2 :** aucun torrent v2 au moment de la capture — `hash` et `infohash_v1`
coïncident partout. Les champs `infohash_v1` et `infohash_v2` existent néanmoins, et le client
devra joindre sur `hash` avec repli sur `infohash_v1`.

## Limite connue

**Les files Radarr et Sonarr étaient vides** au moment de la capture : `records: []`. Seule
l'enveloppe de pagination est donc figée, pas la forme d'un enregistrement de file — qui est
justement ce que la corrélation consomme.

À recapturer quand un téléchargement sera actif. En attendant, `radarr-history.json` et
`sonarr-history.json` documentent la forme réelle de `downloadId` et des données de grab.
