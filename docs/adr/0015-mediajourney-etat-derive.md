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

`MediaJourney (1) ── (0..N) DownloadItem`, avec un parcours capable d'exister sans requête amont
(import manuel) et sans torrent aval (média déjà présent). Ces cas ne sont pas des exceptions à
traiter : ils tombent naturellement d'une jointure qui n'exige rien.
