# ADR-0008 — Discord.Net plutôt que NetCord

**Statut :** acceptée — 11 août 2026

## Contexte

Deux bibliothèques Discord sérieuses existent en .NET.

| | Version | Date | Maturité |
|---|---|---|---|
| **Discord.Net** | 3.20.1 | 7 juin 2026 | stable de longue date |
| NetCord | 1.0.0-beta.11 | 8 juillet 2026 | encore en bêta |

L'argument principal de NetCord est la prise en charge de Native AOT : démarrage plus rapide,
empreinte mémoire réduite.

## Décision

**Discord.Net 3.20.1.**

L'argument AOT ne s'applique pas : EF Core interdit déjà l'AOT sur ce projet
([ADR-0001](0001-dotnet-10-self-contained.md)). Le seul avantage différenciant de NetCord est
donc inutilisable ici, et il resterait à accepter une bêta sur un projet destiné à être
maintenu plusieurs années.

Discord.Net cible .NET 8 et plus, avec une cible .NET 10 dédiée depuis la 3.19, et Components v2
est stable depuis la 3.18.

## Conséquences

- L'adaptateur reste confiné dans `HomelabHub.Discord`. Aucun type Discord.Net ne remonte dans
  `Abstractions` ni dans `Core` : un changement de bibliothèque resterait local.
- Les commandes sont enregistrées **en guild** (effet immédiat) et non en global (jusqu'à une
  heure de propagation).
- L'autorisation n'est pas implémentée dans cet assembly : elle appartient au noyau
  ([ADR-0004](0004-autorisation-cote-noyau.md)).
