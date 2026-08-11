# ADR-0001 — .NET 10 LTS, publication self-contained non découpée

**Statut :** acceptée — 11 août 2026

## Contexte

Le service tourne dans un LXC Debian 13 aux ressources modestes (2 vCPU, 1 Go de RAM). Le
déploiement doit être aussi simple que celui d'un Helper-Script Proxmox : un one-liner qui
crée le container et installe le service, sans dépendance à installer manuellement.

## Décision

**Runtime : .NET 10**, seule version LTS active. Support jusqu'au 14 novembre 2028. .NET 8 et
9 sortent de support le 10 novembre 2026 — commencer dessus serait démarrer avec trois mois
d'avance sur une migration.

**Publication : self-contained, single-file, ReadyToRun, `linux-x64`.**

**Explicitement écartés :**

- `PublishTrimmed` — EF Core, Discord.Net et Serilog reposent sur la réflexion. Le découpage
  supprime des types résolus dynamiquement, et l'échec survient à l'exécution, pas au build :
  c'est le pire des compromis.
- `PublishAot` — même cause, en pire. C'était le seul argument de NetCord contre Discord.Net
  (voir [ADR-0008](0008-discord-net.md)) ; il tombe de lui-même.
- `EnableCompressionInSingleFile` — le gain de taille se paie par une extraction sur disque à
  chaque démarrage.
- `InvariantGlobalization` — l'interface est en français et doit formater dates et nombres
  correctement.

## Conséquences

- **Le binaire pèsera 90 à 130 Mo.** C'est le prix du « aucun runtime à installer ». Chaque
  release GitHub porte un asset de cette taille.
- **`libicu` devient une dépendance obligatoire du container.** Sans elle, un binaire .NET
  self-contained plante au premier lancement. Le script d'installation doit l'installer
  explicitement — c'est le premier piège du packaging Linux sur ce projet.
- L'empreinte mémoire attendue (120 à 200 Mo de RSS) tient largement dans 1 Go.
- `global.json` épingle le SDK : le poste de développement et la CI compilent avec la même
  version, sans dérive silencieuse.
