# ADR-0003 — Trois modes d'ingestion, un seul contrat de sortie

**Statut :** acceptée — 11 août 2026

## Contexte

La première version du contrat ne prévoyait que l'interrogation périodique (`IModulePoller`).
Or Seerr, Radarr et Sonarr savent **pousser** par webhook, et Home Assistant expose un flux
WebSocket permanent. Concevoir l'abstraction autour du seul polling aurait imposé de rouvrir
le contrat à l'étape 7, exactement ce que le projet cherche à éviter.

## Décision

Reconnaître **trois** modes d'ingestion, distingués par la question « qui pilote le cycle de
vie ? » :

| Mode | Piloté par | Supervisé par | Exemple |
|---|---|---|---|
| `IModulePoller` | le noyau (minuteur) | le noyau | file Radarr, débits qBittorrent |
| `IModuleWebhookHandler` | le noyau (route HTTP) | le noyau | `Grab`, `Manual Interaction Required` |
| `IModuleConnection` | **le module** (boucle) | le noyau (backoff, santé) | WebSocket Home Assistant |

**Ce qui est unifié, c'est la sortie, pas l'entrée.** Les trois écrivent dans le même
`IModuleState<T>` et publient dans le même flux d'événements. Widgets, SignalR et dashboard
Discord ne savent jamais qui a parlé.

Chercher à unifier l'entrée aurait reproduit l'erreur du modèle de rendu partagé
([ADR-0006](0006-pas-de-modele-de-rendu-partage.md)) : une abstraction qui masque une
différence réelle finit par la faire réapparaître sous forme de cas particuliers.

## Précisions

**Le poller reste la source de vérité.** Un payload webhook Radarr signale qu'il s'est passé
quelque chose, mais ne contient pas l'état de la file. Le motif normal est donc : émettre
l'événement immédiatement, puis renvoyer `WebhookResult.AcceptedAndRefresh()`, qui déclenche un
cycle de poll anticipé et débattu. **Le push donne la latence, le poll donne la vérité** — et
un webhook perdu se répare tout seul au cycle suivant.

C'est ce qui autorise un intervalle sobre (60 s pour le média) sans latence perçue.

**`ReportConnected()` n'est pas cosmétique.** Sans lui, une connexion qui tient dix minutes
puis tombe verrait son backoff croître indéfiniment, alors qu'elle fonctionne l'essentiel du
temps. Il remet le compteur à zéro.

**Nommage.** `IModuleConnection` plutôt que `IModuleStream` : ce que le noyau prend en charge
est un cycle de vie de connexion, pas un flux de données. Accessoirement, l'analyseur CA1711
refuse un type non-`Stream` dont le nom finit par `Stream`, et il a raison.

## Conséquences

- Un module peut combiner les trois modes. Home Assistant utilisera une connexion WebSocket
  **et** un poller REST en repli, sans que le noyau ait à connaître cette nuance.
- Le noyau porte la politique de reconnexion. Un module qui écrit sa propre boucle de
  reconnexion duplique le noyau et lui ment sur son état.
