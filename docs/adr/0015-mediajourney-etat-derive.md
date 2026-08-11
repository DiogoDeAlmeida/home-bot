# ADR-0015 — `MediaJourney` est un état dérivé, jamais un état possédé

**Statut :** acceptée — 11 août 2026, **avant** l'écriture de la corrélation

## Contexte

Le module média corrèle trois vues du même objet : la requête Seerr, la file Radarr ou Sonarr,
le torrent qBittorrent. Il est tentant de matérialiser cette corrélation — de tenir une table de
`MediaJourney` que le hub enrichirait au fil des cycles.

Cette décision est prise **avant** d'écrire la corrélation parce qu'elle est irréversible
ensuite : une fois qu'un état est possédé, tout dépend de sa survie.

## Décision

**`MediaJourney` se reconstruit intégralement à chaque cycle, à partir des seuls services.**
Aucune information ne doit exister uniquement dans le hub.

Concrètement, à chaque cycle : lire Seerr, lire les files Radarr et Sonarr, lire qBittorrent,
puis joindre — `media.externalServiceId` et `tmdbId`/`tvdbId` en amont, `downloadId` normalisé
en minuscules en aval. Le snapshot précédent n'est jamais consulté pour produire le suivant.

## Ce que cela achète

- **Un redémarrage du hub ne perd rien** et ne demande aucune réconciliation. Le premier cycle
  reconstruit l'intégralité de l'état.
- **Le report de SQLite devient légitime** ([ADR-0007](0007-migrations-et-sauvegarde-au-demarrage.md)) :
  il n'y a rien à persister. Ce n'est pas une dette, c'est une conséquence de la conception.
- **Les détecteurs restent des projections sans état** ([ADR-0005](0005-anomalies-comme-etats.md)),
  ce qui n'aurait plus été vrai si le parcours accumulait de l'historique. Ils publient des
  `HubEvent` avec leur `DedupeKey` et ne présument rien de la survie d'un état en mémoire ; le
  moteur de déduplication de l'étape 4 tranche.
- Aucune divergence possible entre ce que le hub croit et ce que les services savent. Le hub
  n'a jamais raison contre Radarr.

## Le signal d'alarme

**Si la corrélation exige de mémoriser quelque chose que les services ne savent pas redire, il
faut s'arrêter et le signaler** plutôt que d'ajouter discrètement un champ persistant.

Cela changerait la nature du module et déclencherait le besoin de persistance plus tôt que
prévu. Les cas à surveiller, par ordre de vraisemblance :

- l'instant où une anomalie s'est ouverte, si l'on veut « bloqué depuis N heures » plutôt que
  « bloqué » — un débit nul est lisible dans l'instant, sa durée ne l'est pas ;
- un identifiant de message Discord à retrouver après redémarrage — mais celui-là relève de
  l'adaptateur, pas du parcours média ;
- une release abandonnée dont on voudrait garder la trace après sa disparition de la file.

Le premier est le plus probable et arrivera à l'étape 4. Il ne remet pas en cause cette
décision : c'est le **moteur d'anomalies** qui portera cette durée, pas `MediaJourney`.

## Conséquence sur le modèle

**Amendé le 11 août 2026, après capture d'un cycle complet sur les instances réelles.** La
multiplicité initialement posée était incomplète : il y a un troisième niveau.

```
MediaJourney (1) ── (0..N) DownloadItem ── (1..N) QueueEntry
                             │                       │
                             └ un downloadId          └ un épisode
```

Un pack de saison observé en conditions réelles produit **22 enregistrements de file pour un
seul torrent** — un par épisode, tous portant le même `downloadId` et la **même `size`
répétée**. Deux packs simultanés donnaient 44 enregistrements pour 2 torrents.

**Le regroupement par `downloadId` doit donc être la première opération de la corrélation**,
avant toute agrégation. Mesuré sur ces données :

| Agrégation | Résultat |
|---|---|
| Somme des `size` de tous les enregistrements | 451 022 706 508 octets — **faux, facteur 22** |
| Somme après regroupement par `downloadId` | 20 501 032 114 octets — correct |

Un tableau de bord naïf aurait annoncé 451 Go en cours et 44 téléchargements. Aucun mock écrit à
la main n'aurait révélé ça.

Le parcours reste capable d'exister sans requête amont (import manuel) et sans torrent aval
(média déjà présent) : ces cas tombent d'une jointure qui n'exige rien.

## Deux pièges de détection, constatés et non supposés

**1. `status = warning` dès la première seconde.** Un torrent fraîchement récupéré, avant tout
contact avec un pair, remonte `errorMessage: "The download is stalled with no connections"` —
alors que `trackedDownloadStatus` vaut `ok`. Un détecteur de blocage naïf se déclencherait sur
**chaque nouveau téléchargement**.

Conséquences pour l'étape 4 : l'axe de santé est `trackedDownloadStatus`, pas `status`, qui est
un résumé conflant l'erreur transitoire. Et un téléchargement n'est « bloqué » que s'il a
d'abord progressé (`sizeleft < size`) ou s'il dure depuis un délai de grâce compté à partir de
`added`.

**2. `importPending` dure moins de cinq secondes.** Les deux packs y sont passés entre deux
échantillons espacés de 5 s ; le film n'y a jamais été vu. Avec des hardlinks, l'import est
quasi instantané.

Conséquence : **on ne détecte pas un import bloqué en cherchant `importPending` au polling.**
À 60 secondes d'intervalle, on ne le voit jamais dans le cas nominal. La logique est donc
inversée : *voir* `importPending` sur un cycle est déjà en soi le signal, sans seuil de durée.
Ce qui confirme le choix de câbler l'anomalie sur le déclencheur `Manual Interaction Required`
plutôt que sur un `importPending` qui traîne.

## Corollaire : ne pas s'appuyer sur la corrélation de Seerr

Seerr expose son propre `media.downloadStatus`. Confronté aux mêmes instants :

- il porte **le même défaut de duplication** — 10 entrées pour un seul titre et un seul
  `externalId` ;
- il **diverge de Sonarr** : saison 1 en `warning` avec `sizeLeft == size` côté Seerr, pendant
  que Sonarr téléchargeait activement la saison 2.

Il expose en revanche un champ `downloadId` utile pour relier une requête à un torrent sans
passer par la file. **Commodité, jamais source de vérité** : le hub ne doit avoir raison contre
personne, mais il n'a pas à hériter des approximations d'un tiers.
