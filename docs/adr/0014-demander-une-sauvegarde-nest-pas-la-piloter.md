# ADR-0014 — Demander une sauvegarde n'est pas la piloter

**Statut :** acceptée — 11 août 2026
**Remplace** une première version qui plaçait `IHubBackupService` dans `Abstractions`.

## Contexte

Le module `system` porte la capacité `system.backup.create`. Comme tout module, il ne référence
que `HomelabHub.Abstractions` (ADR-0010). Il fallait donc que le service de sauvegarde y soit
déclaré — c'est ce qui a été fait dans un premier temps, sous couvert de « service de
plateforme », au même titre que `IEventPublisher`.

**C'était une erreur, et elle contredisait une décision prise deux jours plus tôt.**

`system.backup.create` est restreinte à `CapabilityExposure.Rest` précisément parce que
l'archive contient le keyring Data Protection, donc de quoi déchiffrer toutes les clés d'API du
homelab (ADR-0004). Interdire ce déclenchement depuis Discord tout en rendant le service
résoluble par n'importe quel module rouvrait l'accès par une autre porte. L'asymétrie entre
« interdit à Discord » et « ouvert à tout module » est indéfendable.

## Décision

Séparer l'**intention** du **pilotage**.

| | Où | Qui peut l'obtenir |
|---|---|---|
| `IBackupRequester<TModule>` | `Abstractions` | n'importe quel module |
| `IHubBackupService` | `Core` | le noyau et le Host, jamais un module |

Un module dit « je voudrais une sauvegarde, pour telle raison ». **Le noyau décide** : il applique
l'anti-rebond, journalise l'appelant et le motif, et renvoie `Created`, `Throttled` ou `Failed`.

Le paramètre de type suit la convention déjà posée par `IModuleConfiguration<TModule>` : il donne
au noyau l'identité de l'appelant sans que le module ait à la déclarer, donc sans qu'il puisse
mentir dessus.

Le module `system` n'est pas privilégié : il passe par le même `IBackupRequester` que les autres.

## Conséquences assumées

**`system.backup.list` disparaît**, et `SystemSnapshot` perd son champ `LastBackup`. Énumérer les
archives est du pilotage, pas une intention. Ces informations sont servies par
`GET /api/backups`, une route du noyau — cohérent avec ADR-0013, qui fait déjà du noyau un
pseudo-module aux yeux de l'interface.

**L'anti-rebond est global, pas par module.** Ce qui compte est l'espace disque et le coût
d'écriture, pas l'identité du demandeur : trois modules qui demandent chacun une sauvegarde dans
la même minute n'en justifient qu'une. L'intervalle minimal est un réglage du hub
(`hub.backup.minimumInterval`, cinq minutes par défaut).

**Un échec rend le jeton d'anti-rebond.** Sinon une sauvegarde ratée condamnerait le hub à rester
sans sauvegarde pendant tout l'intervalle — précisément au moment où quelque chose ne va pas.

## Ce que la décision ne prétend pas être

Ce n'est pas un bac à sable. Un module est du code de première main compilé dans le même binaire :
il peut lire le disque directement s'il le veut. `IHubPlatform` expose d'ailleurs toujours les
chemins des répertoires, parce que le module `system` en a besoin pour mesurer l'espace libre.

Ce que le contrat empêche, c'est l'**accident** et la **dérive** : qu'une opération sensible
devienne banale parce qu'elle était à portée de main, et que la politique du noyau — anti-rebond,
journalisation, décision — soit contournée sans que personne l'ait voulu.
