# ADR-0004 — Autorisation côté noyau, pas côté Discord

**Statut :** acceptée — 11 août 2026

## Contexte

Exigence initiale : les capacités de lecture sont ouvertes à tous les membres, les capacités
de modification réservées à un rôle `hub-admin` — « en se servant des permissions par défaut
des commandes Discord plutôt que d'un contrôle maison ».

Deux constats rendent cette approche impraticable.

**1. `default_member_permissions` ne descend pas aux sous-commandes.** Discord ne permet de
régler les permissions que sur la commande racine. Or `/media queue list` (lecture, ouverte) et
`/media queue pause` (modification, restreinte) partagent la racine `/media`. Discord ne sait
pas les séparer.

**2. Les composants de message n'ont aucune permission.** Le dashboard porte des boutons
« pause » et « relancer ». Discord n'offre strictement aucun mécanisme de contrôle d'accès sur
les boutons : n'importe quel membre voyant le message peut cliquer.

Le second constat est décisif. **Une vérification côté code est obligatoire de toute façon.**
Ajouter en plus un mécanisme natif pour les seules slash commands créerait deux sources de
vérité, dont celle qui protège vraiment n'est pas celle qu'on croit.

## Décision

Une racine de commande par module. **L'autorisation est une affaire de noyau**, dérivée de
`CapabilityKind`, appliquée en un seul endroit à trois surfaces :

```
Capability.Kind == Mutation
        ├──▶ slash command  : vérification du rôle hub-admin avant exécution
        ├──▶ bouton         : même vérification, même code, sur le custom_id
        └──▶ API web        : admin unique déjà authentifié ⇒ toujours autorisé
```

Le rôle `hub-admin` est un identifiant stocké en configuration.

`default_member_permissions` reste **inutilisé** : la racine contient des lectures ouvertes à
tous, et ne rien y poser évite d'avoir à reconfigurer les surcharges dans l'interface Discord
après chaque réenregistrement des commandes.

## Décision liée : `CapabilityExposure`

`Kind` gouverne *qui* peut appeler. Il fallait aussi gouverner *où* une capacité apparaît.

`system.backup.create` produit une archive contenant le keyring, donc toutes les clés d'API du
homelab en clair une fois déchiffrées. Cette capacité ne doit jamais transiter par Discord,
quel que soit son `Kind`.

D'où `CapabilityExposure` (indicateurs binaires, `Rest | Discord`), indépendant de `Kind`. Le
validateur de démarrage échoue si un `DiscordBinding` est déclaré alors que l'exposition exclut
Discord : deux déclarations qui se contredisent doivent casser bruyamment, pas être arbitrées
en silence.

## Conséquences

- Un membre non autorisé reçoit un message éphémère explicite plutôt qu'une commande invisible.
  Plus clair pour un usage familial.
- Les `Mutation` sont journalisées avec l'identité de l'appelant (`CapabilityInvocation.ActorId`).
- `RequireConfirmation` reste disponible **en plus** du contrôle de rôle, pour les opérations
  destructrices.
