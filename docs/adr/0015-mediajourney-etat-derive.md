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

> ### Pourquoi la règle qui suit paraîtra fausse
>
> **Règle : voir `importPending` sur un cycle de polling est en soi le signal d'anomalie.
> Il n'y a pas de seuil de durée.**
>
> Cette formulation est contre-intuitive au point qu'un lecteur futur la prendra pour un bug et
> la « corrigera » en ajoutant un seuil. Voici le raisonnement, pour qu'il ne le fasse pas.
>
> L'intuition dit : « `importPending` est un état transitoire normal, donc il faut mesurer
> depuis combien de temps il dure avant de crier ». C'est le raisonnement qu'on applique à un
> téléchargement bloqué, et il est juste **là-bas**.
>
> Il est faux ici parce que la fenêtre nominale — moins de cinq secondes — est **plus courte que
> l'intervalle d'échantillonnage**, qui est de soixante secondes. Un import qui se passe bien
> n'est donc jamais observé du tout. La probabilité de tomber dessus par hasard est d'environ un
> cycle sur douze, et elle tend vers zéro à mesure que l'import est rapide.
>
> Autrement dit : le polling ne voit `importPending` que quand il *persiste*. Le seuil de durée
> est déjà appliqué, gratuitement, par l'échantillonnage lui-même. En rajouter un explicite
> reviendrait à l'appliquer deux fois et à retarder la détection sans rien gagner.
>
> **Ce qui invaliderait ce raisonnement**, et devrait alors faire réintroduire un seuil : un
> intervalle de polling descendu à quelques secondes, ou un stockage sans hardlinks — copie
> réelle entre volumes distincts — où l'import légitime durerait des minutes.

Ce raisonnement confirme le choix de câbler l'anomalie sur le déclencheur
`Manual Interaction Required` plutôt que sur un `importPending` qui traîne.

**3. Méfiance à étendre à `statusMessages`.** `errorMessage` de Radarr n'est pas une erreur : c'est
un état transitoire que l'API expose sans le qualifier. Rien ne disait que `statusMessages` se
comportait mieux, et il est resté inexploité jusqu'à ce qu'un cas réel soit observé.

### Ce cas a eu lieu — 12 août 2026

Un téléchargement interrompu puis relancé à la main dans qBittorrent, en contournant le
pilotage de Radarr, a produit un import bloqué durable :

```
status                : completed
trackedDownloadState  : importBlocked      ← valeur jamais observée jusque-là
trackedDownloadStatus : warning
errorMessage          : ""                 ← vide
statusMessages        : [{
    title    : <nom de la release>,
    messages : ["Found matching movie via grab history, but release was matched
                to movie by ID. Manual Import required."]
}]
```

**Trois sources vérifiées, une seule parle :**

| Source | Contenu |
|---|---|
| `errorMessage` | vide |
| `/api/v3/history?downloadId=` | `grabbed` seulement, aucun événement terminal |
| Journaux Radarr, tous niveaux, 24 h | 19 lignes sur ce titre, **toutes en `info`** et de routine |
| `statusMessages` | **la seule explication existante** |

### Décision : restituer, jamais interpréter

`statusMessages` est désormais **repris mot pour mot** dans le corps de l'anomalie. Sans lui,
l'utilisateur lirait « l'import n'a pas abouti » sans savoir quoi faire ; avec lui, il lit
« Manual Import required » et sait où aller.

Il n'est en revanche **jamais analysé** : pas de recherche de motif, pas de classification. Les
raisons tiennent en trois points.

1. **Un échantillon ne fait pas une taxonomie.** Un seul cas observé, en dix-huit jours de
   fonctionnement.
2. **C'est de la prose anglaise produite par le service**, susceptible de changer de formulation
   entre deux versions, voire d'être traduite.
3. **La gravité est déjà portée ailleurs**, par `trackedDownloadStatus`. Analyser le texte
   n'apporterait qu'une seconde source de vérité, en plus fragile.

Le champ `title` d'un `statusMessage` est le nom de la release, redondant avec celui de l'entrée
de file : **ce n'est pas une catégorie d'erreur**, et le prendre pour telle serait le premier
faux pas d'une interprétation mécanique.

### La voie structurée existe, pour plus tard

`/api/v3/manualimport?downloadId=` renvoie les candidats à l'import avec un tableau
`rejections` **structuré**. Dans le cas observé il était **vide**, et le film correctement
identifié : le fichier était importable, il n'attendait qu'une confirmation humaine.

C'est là qu'il faudra regarder le jour où l'on voudra qu'une décision soit automatisable — et
non dans la prose de `statusMessages`.

### Effet de bord vérifié

Le torrent était à 100 % en `stalledUP` depuis des heures, donc « inactif » au sens de
qBittorrent. Le détecteur de blocage de téléchargement **ne se déclenche pas** pour autant : il
exige l'état `Downloading`. Confondre les deux produirait deux anomalies pour un seul problème.

## L'état terminal vient de l'historique, pas de la file

Le cycle observé se termine par la **disparition** de l'entrée de file. Or une entrée disparaît
aussi bien après un import réussi qu'après une suppression manuelle ou un échec. Sans source
complémentaire, un `MediaJourney` resterait indéfiniment indéterminé après cette disparition —
et la tentation serait alors de mémoriser son dernier état connu, ce qui contredirait tout cet ADR.

**`/api/v3/history` répond, et l'API filtre côté serveur :**

```
GET /api/v3/history?downloadId=A52239F7…   →  2 enregistrements
     grabbed                 2026-08-11T19:25:19Z
     downloadFolderImported  2026-08-11T19:37:01Z
```

Les types d'événements pertinents sont `grabbed`, `downloadFolderImported`, `downloadFailed` et
`downloadIgnored` ; les deux derniers ne se sont pas produits pendant la capture et restent à
observer. La duplication par épisode s'y retrouve à l'identique — 44 événements pour un pack de
saison — donc le même regroupement par `downloadId` s'applique.

**Cela ne fragilise pas la décision, cela la renforce :** l'état terminal reste *dérivé*, lu
chez le service, jamais mémorisé par le hub. Un redémarrage continue de ne rien perdre.

**Conséquence sur l'implémentation :** l'historique est une source de la corrélation dès le
premier jour, pas un ajout ultérieur. Une page récente d'historique est lue une fois par cycle
et indexée par `downloadId` — et non une requête par parcours, qui ferait N appels par cycle.

## Corollaire : ne pas s'appuyer sur la corrélation de Seerr

Seerr expose son propre `media.downloadStatus`. Confronté aux mêmes instants :

- il porte **le même défaut de duplication** — 10 entrées pour un seul titre et un seul
  `externalId` ;
- il **diverge de Sonarr** : saison 1 en `warning` avec `sizeLeft == size` côté Seerr, pendant
  que Sonarr téléchargeait activement la saison 2.

Il expose en revanche un champ `downloadId` utile pour relier une requête à un torrent sans
passer par la file. **Commodité, jamais source de vérité** : le hub ne doit avoir raison contre
personne, mais il n'a pas à hériter des approximations d'un tiers.
