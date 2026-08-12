# ADR-0017 — La table d'anomalies est le premier état réellement possédé

**Statut :** acceptée — 12 août 2026

## Contexte

[ADR-0007](0007-migrations-et-sauvegarde-au-demarrage.md) reportait délibérément SQLite : la
configuration est un dictionnaire sans relation, et [ADR-0015](0015-mediajourney-etat-derive.md)
impose que les parcours média soient **dérivés**, reconstruits à chaque cycle. Rien de tout cela
ne justifiait EF Core.

[ADR-0005](0005-anomalies-comme-etats.md) a changé cela sans le dire tout de suite. Le moteur
d'anomalies tient une information que **personne d'autre ne sait redire** :

- depuis quand une anomalie est ouverte ;
- combien de cycles elle a traversés ;
- qu'elle a été mise en sommeil, et jusqu'à quand.

Radarr sait qu'un import est bloqué. Il ne sait pas depuis quand le *hub* le signale, ni que
l'exploitant a demandé le silence jusqu'à demain matin. Tant que la table vivait en mémoire,
chaque redémarrage rouvrait tout et renotifiait tout — comportement assumé faute de mieux, sur
le principe que renotifier est bruyant mais qu'oublier serait dangereux.

## Décision

**La table d'anomalies et le journal sont persistés dans SQLite.** C'est le premier état que le
hub possède ; les autres restent dérivés.

Le noyau ne connaît ni SQLite ni EF Core. Il connaît `IAnomalyStore` et `IJournalStore`, dont
l'implémentation par défaut est en mémoire — un noyau monté seul, en test, reste fonctionnel
sans base ni montage.

### Ce qui entre en base, et ce qui n'y entre pas

| Donnée | En base ? | Pourquoi |
|---|---|---|
| Anomalies | oui | Aucun service amont ne sait redire « ouverte depuis dix heures ». |
| Journal | oui | Un historique consultable, avec la rétention promise au cadrage. |
| Configuration | non | Dictionnaire sans relation, déjà chiffré dans un JSON atomique. |
| Parcours média | **non** | Dérivé (ADR-0015). Le persister ferait diverger le hub de ses sources. |

La dernière ligne est la règle qu'il faut tenir : persister un snapshot média rendrait ADR-0015
faux en pratique tout en le laissant vrai sur le papier.

## Le piège que la persistance introduit

Une anomalie ne se referme que parce que son module **cesse de la republier** lors d'un cycle
réussi. Si le module a disparu du binaire ou a changé de clé, plus personne ne la republiera —
et donc plus personne ne pourra la résoudre. Elle resterait ouverte pour toujours, visible dans
l'interface, sans aucune action possible.

C'est un défaut que la version en mémoire n'avait **pas**, puisqu'elle repartait de zéro. La
durabilité ne s'obtient donc pas en ajoutant simplement une base : il faut une réconciliation.

Au démarrage, `AnomalyEngine.Hydrate` recharge la table et **clôt d'office** les anomalies dont
la clé de module ne figure plus au catalogue, sans émettre de transition : personne ne veut une
salve de « résolu » dans Discord à chaque mise à jour qui retire un module. La ligne est
conservée pour l'historique jusqu'à la purge.

Distinction volontaire : **un module simplement désactivé n'est pas orphelin.** Sa clé est
toujours au catalogue, ses anomalies l'attendent, et le réactiver reprend exactement où il en
était.

## Les instants sont stockés en ticks UTC, pas en texte

SQLite n'a pas de type date. Le provider écrirait un `DateTimeOffset` sous forme de texte avec
son décalage — et **refuse alors de traduire la moindre comparaison**, parce que l'ordre
lexicographique de deux instants notés dans des fuseaux différents ne correspond pas à leur
ordre chronologique.

Toute la rétention repose sur des comparaisons de dates. Sans conversion, elle ne s'exécuterait
pas du tout : `Where(j => j.OccurredAt < cutoff)` lève à l'exécution.

Un entier de ticks UTC est comparable, indexable et exact. Le décalage d'origine est **perdu**,
volontairement : le hub raisonne en UTC de bout en bout et n'affiche l'heure de Paris qu'au
dernier moment.

## `VACUUM INTO`, et pourquoi une copie de fichier ne suffit pas

La base tourne en mode WAL : les lectures de l'interface ne bloquent plus l'écriture d'un cycle
d'ingestion, et inversement.

Conséquence directe, observée sur l'instance de développement : après quelques minutes,
`homelabhub.db` faisait **4 Ko** et `homelabhub.db-wal` **165 Ko**. Copier le seul fichier
principal aurait produit une archive qui s'ouvre, se restaure, et ne contient rien.

`BackupService` écarte donc `.db`, `-wal` et `-shm` de la copie brute, et ajoute à leur place un
instantané obtenu par `VACUUM INTO` — qui prend un verrou de lecture, écrit une base complète et
compactée, et n'a besoin d'aucune coordination avec les écrivains.

Cela vaut pour **toutes** les sauvegardes, pas seulement celle qui précède une migration.

## Séquence de démarrage

Celle d'ADR-0007, désormais câblée :

1. s'il existe une base **et** des migrations en attente : sauvegarde, dont le motif nomme les
   migrations concernées ;
2. `Database.Migrate()`, puis `PRAGMA journal_mode=WAL` ;
3. en cas d'échec : journal critique et **code de sortie 1**, sans démarrer ;
4. `HydrateHubState()` — avant le premier cycle d'ingestion, sans quoi une anomalie rechargée
   après son propre cycle serait vue comme nouvelle, et renotifiée.

La condition « base existante **et** migrations en attente » n'est pas cosmétique : archiver à
chaque démarrage ferait tourner la rétention à vide et chasserait les archives qui comptent —
celles d'avant les migrations.

## Rétention

Deux bornes, la première atteinte l'emportant : **14 jours ou 100 000 lignes**, purge
quotidienne, réglable depuis l'interface sous le préfixe réservé `hub.`
([ADR-0013](0013-schema-partage-modules-et-hub.md)).

L'âge seul ne suffit pas — un module bavard produit cent mille lignes en deux jours. Le nombre
seul ne suffit pas non plus — un hub tranquille garderait des traces d'il y a six mois.

Les anomalies **résolues** suivent la même fenêtre d'âge. Les autres ne sont jamais purgées :
une anomalie ouverte depuis trois semaines est exactement ce qu'il faut garder.

## Conséquences

- Un redémarrage ne renotifie plus. Vérifié sur l'instance réelle : `Occurrences=9` à cheval sur
  deux exécutions, `OpenedAt` figé, `LastSeenAt` qui avance.
- Une base indisponible **dégrade** le hub — il oublie l'heure d'ouverture — mais ne l'arrête
  pas. C'est quand la machine va mal qu'on a besoin qu'elle continue de regarder.
- Les migrations sont appliquées par l'application, jamais par `dotnet ef`, absent du LXC.
- Le journal enregistre une ligne par republication. Une anomalie persistante y écrit donc
  ~1 440 lignes par jour à elle seule : la borne des 100 000 lignes sera atteinte par la
  répétition, pas par la variété. À réexaminer si l'historique doit couvrir 14 jours réels.
