# scripts

Déploiement LXC. **Non écrits — étape 6.**

## Contenu prévu

| Fichier | Rôle |
|---|---|
| `ct/homelabhub.sh` | Crée le container Proxmox et pilote l'installation |
| `install/homelabhub-install.sh` | Installe le binaire, crée l'utilisateur système, pose l'unité systemd |
| `homelabhub-update.sh` | Compare à la dernière release GitHub, sauvegarde, remplace, redémarre |

Structure calquée sur les Proxmox VE Helper-Scripts. `homelabhub-update` est exposé sous
`/usr/bin/update` par lien symbolique : l'ergonomie habituelle est préservée, sans qu'un
binaire nommé `update` ne squatte le `PATH` global.

## Points à ne pas rater

- **`libicu` est une dépendance obligatoire.** Un binaire .NET self-contained plante au
  premier lancement sans elle, et l'interface est en français : pas d'`InvariantGlobalization`
  ([ADR-0001](../docs/adr/0001-dotnet-10-self-contained.md)).
- **Fuseau `Europe/Paris`** posé par le script.
- **Les migrations sont appliquées par l'application au démarrage**, jamais par `dotnet ef` —
  le SDK .NET n'est pas installé dans le container
  ([ADR-0007](../docs/adr/0007-migrations-et-sauvegarde-au-demarrage.md)).
- **Sauvegarde = base + keyring, dans une archive unique.** Restaurer une base sans son
  keyring Data Protection rend tous les secrets illisibles. L'archive unique rend l'erreur
  impossible.
- Le service tourne sous un utilisateur dédié non privilégié ; les données persistantes vivent
  dans `/opt/homelabhub/data`, jamais écrasées par une mise à jour ; la configuration dans
  `/etc/homelabhub/`.
