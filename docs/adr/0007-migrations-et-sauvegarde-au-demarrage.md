# ADR-0007 — Migrations et sauvegarde appliquées par l'application

**Statut :** acceptée — 11 août 2026

## Contexte

Le cadrage prévoyait un script de mise à jour qui « applique les migrations EF Core ». Avec
quoi ? `dotnet ef` est un outil du SDK .NET, absent d'un container qui ne contient qu'un binaire
self-contained. Le script n'a aucun moyen de l'invoquer.

Second point, plus grave : ce hub concentrera toutes les clés d'API du homelab, sur un Proxmox
qui n'a aujourd'hui **aucune sauvegarde automatisée**.

## Décision

**L'application applique ses propres migrations au démarrage**, dans cet ordre strict :

1. sauvegarde de la base (`VACUUM INTO`, cohérente sans arrêter le service) ;
2. `Database.Migrate()` ;
3. en cas d'échec : **refus de démarrer**, avec un message explicite dans le journal systemd.

Un service qui ne monte pas est un incident visible et réparable. Un service qui monte sur une
base à moitié migrée est une corruption silencieuse.

## Sauvegarde intégrée, dès l'étape 1

- capacité `system.backup.create`, exposée en REST uniquement
  ([ADR-0004](0004-autorisation-cote-noyau.md)) ;
- **archive unique contenant la base, le keyring Data Protection et la configuration** ;
- déclenchement automatique avant toute migration et avant toute mise à jour, avec restauration
  si la mise à jour échoue ;
- rétention configurable.

## Pourquoi l'archive unique n'est pas un détail

Sur Linux, il n'y a pas de DPAPI : le keyring Data Protection est un dossier de fichiers XML
**en clair** dans `data/keys/`.

Deux conséquences à assumer explicitement :

1. Le chiffrement des secrets en base protège contre « quelqu'un a récupéré le fichier `.db` ».
   Il ne protège **pas** contre « quelqu'un a accès au système de fichiers ».
2. **Une base restaurée sans son keyring rend tous les secrets définitivement illisibles.**

C'est le piège classique de Data Protection sur Linux. Produire une archive unique rend
l'erreur structurellement impossible, plutôt que de compter sur la vigilance au moment où on
restaure — c'est-à-dire au pire moment.

## Conséquences

- Le script de mise à jour vérifie `/healthz` après redémarrage avant de déclarer la mise à
  jour réussie.
- Un rollback de version applicative ne suffit pas si la migration a modifié le schéma : la
  restauration se fait depuis l'archive.
