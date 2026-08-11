# ADR-0005 — Une anomalie est un état, pas un événement

**Statut :** acceptée — 11 août 2026

## Contexte

Le cadrage demandait des « notifications intelligentes », dont « téléchargement bloqué depuis
N heures ».

Traité comme un événement, un poller à 60 secondes produit **240 messages Discord** pour une
anomalie qui dure quatre heures. La tolérance au bruit annoncée est faible : ce comportement
rendrait le système inutilisable dès la première nuit.

Une anomalie n'est pas un fait ponctuel. C'est une **condition qui s'ouvre, dure, et se
résout**.

## Décision

Le noyau maintient une table d'anomalies actives, indexée par `HubEvent.DedupeKey`, avec un
cycle de vie explicite :

```
        ┌─────────────────────────────────────┐
        ▼                                     │
    ouverte ──────▶ en sommeil ───(échéance)──┘
        │               │
        └───────┬───────┘
                ▼
            résolue
```

**Notification aux seules transitions.** Republier le même `DedupeKey` à chaque cycle est le
comportement attendu d'un détecteur : c'est ainsi que le noyau sait que l'anomalie dure
toujours. Il n'en découle aucune notification.

Une anomalie est **résolue** quand elle cesse d'être republiée pendant un délai de grâce, ce
qui déclenche une notification de clôture.

## Mise en sommeil

Deux formes, toutes deux demandées :

- **Six heures**, avec réarmement automatique à l'échéance.
- **Jusqu'à résolution**, pour les anomalies connues et acceptées : réarmement seulement après
  un passage effectif par l'état résolu.

## Détection : préférer le signal à la déduction

Radarr et Sonarr émettent un déclencheur `Manual Interaction Required`. C'est exactement le
signal « un import est bloqué et attend une décision humaine ».

**Câbler l'anomalie sur ce déclencheur plutôt que la déduire d'un `importPending` qui traîne.**
Un seuil temporel sur un état intermédiaire produit des faux positifs sur les gros fichiers et
des faux négatifs sur les petits ; le service concerné, lui, sait.

## Conséquences

- Le modèle de données porte une table d'anomalies dès l'étape 1, bien avant que les détecteurs
  existent (étape 5). Le rétro-adapter aurait coûté une migration pénible.
- Les `HubEventSeverity.Info` ne sont jamais poussés dans Discord : ils alimentent le journal
  consultable dans l'interface.
