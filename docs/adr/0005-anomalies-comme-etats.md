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

Une anomalie est **résolue** quand elle cesse d'être republiée, ce qui déclenche une
notification de clôture.

## Un détecteur est une projection sans état, pas un émetteur

C'est le corollaire qui fait tenir tout le reste, et il mérite d'être explicite.

Un détecteur ne dit jamais « ferme telle anomalie ». Il repart du snapshot courant à chaque
cycle et republie **l'ensemble de ce qui va mal en ce moment**. Le noyau compare au cycle
précédent et en déduit les ouvertures et les clôtures.

Conséquence pratique, sur le cas « release remplacée par une meilleure » : quand Radarr
abandonne une release, l'ancien `downloadId` disparaît de la file, donc du snapshot, donc de
ce que le détecteur republie. L'anomalie se clôt d'elle-même. **Aucune correspondance
`DedupeKey` → `DownloadItem` n'est à tenir**, ni par le module, ni par le contrat. Un module
qui tiendrait cette table dupliquerait le noyau et finirait par diverger de lui.

## La réconciliation est portée par un cycle réussi

La règle « ce qui n'est plus republié est résolu » n'est correcte que si l'absence est
significative. Si `PollAsync` lève une exception à mi-parcours, une partie des anomalies
disparaîtrait et serait déclarée résolue à tort — un service injoignable se traduirait par une
salve de « tout va bien » au lieu d'une alerte.

Le noyau possède les bornes du cycle : il ouvre la fenêtre en appelant `IModulePoller.PollAsync`
et la ferme **au retour sans exception**. La réconciliation n'a lieu qu'alors. En cas d'échec,
les anomalies actives restent ouvertes en l'état.

Aucun changement de contrat n'est nécessaire : `IEventPublisher` reste inchangé, et les modules
n'ont rien à savoir de ce mécanisme.

> **Point ouvert (étape 4).** Un module qui n'aurait que des webhooks, sans aucun poller, n'a
> pas de cycle, donc pas de fenêtre de réconciliation. Aucun module prévu n'est dans ce cas —
> Home Assistant conserve un poller REST de repli. Si le cas se présente, la réponse sera une
> publication d'ensemble explicite, pas un délai de grâce temporel.

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
