# ADR-0009 — Échange atomique sans verrou pour `IModuleState<T>`

**Statut :** acceptée — 11 août 2026

## Contexte

`IModuleState<T>.Mutate(s => s.With(change))` est une séquence lecture-modification-écriture.
Trois écrivains peuvent la déclencher simultanément sur le même snapshot
([ADR-0003](0003-trois-modes-ingestion.md)) : un poller sur son minuteur, un webhook sur une
requête entrante, une connexion sur son propre fil d'exécution.

Sans sémantique explicite, deux mises à jour concurrentes s'écrasent silencieusement. Le
symptôme serait une progression de téléchargement qui recule par intermittence — le genre de
bug qu'on ne reproduit jamais volontairement.

## Décision

**Échange atomique par `Interlocked.CompareExchange` avec réessai.** Pas de verrou.

```
boucle :
    ancien   ← Volatile.Read(référence)
    nouveau  ← update(ancien)
    si nouveau est ancien   →  sortir, aucun changement publié
    si CompareExchange(référence, nouveau, ancien) == ancien  →  publier, sortir
    sinon  →  recommencer
```

**Pourquoi pas un verrou par module.** Une connexion WebSocket peut rester longtemps dans son
gestionnaire ; un poller pourrait alors attendre derrière elle. L'échange atomique ne bloque
jamais et ne peut pas interbloquer. Le coût est un réessai sous contention, négligeable au
volume visé (un à cinq téléchargements simultanés).

## Contrat imposé à l'appelant

**La fonction passée à `Mutate` doit être pure.** Elle peut être invoquée plusieurs fois si un
autre écrivain gagne la course. Elle ne doit donc ni écrire en base, ni publier d'événement, ni
incrémenter un compteur externe. Elle prend l'ancien snapshot, en renvoie un nouveau, rien
d'autre.

Les effets de bord se placent **après** le retour de `Mutate`, sur le résultat publié.

C'est la contrainte principale de cette décision, et la raison pour laquelle elle est
documentée sur l'interface elle-même et pas seulement ici.

## Conséquences

- Les snapshots sont des `record` immuables. `With(...)` produit une nouvelle instance.
- Renvoyer l'instance reçue est le moyen explicite de dire « rien de neuf » : aucun rendu
  Discord ni aucune trame SignalR n'est émis. C'est ce qui évite d'éditer le message de
  dashboard toutes les 60 secondes pour rien.
- La diffusion réseau est débattue par le noyau, hors du chemin d'écriture.
