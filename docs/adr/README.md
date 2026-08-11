# Décisions d'architecture

Un fichier par décision structurante : le contexte, l'arbitrage, et surtout **ce qu'on a
écarté et pourquoi**. Ce dernier point est le seul qui a de la valeur dans six mois, quand la
tentation reviendra.

Une décision ne se modifie pas : elle se remplace par une nouvelle qui la supersède.

| # | Décision | Statut |
|---|---|---|
| [0001](0001-dotnet-10-self-contained.md) | .NET 10 LTS, publication self-contained non découpée | Acceptée |
| [0002](0002-modules-statiques-activation-runtime.md) | Modules statiques, activation ≠ injection de dépendances | Acceptée |
| [0003](0003-trois-modes-ingestion.md) | Trois modes d'ingestion, un seul contrat de sortie | Acceptée |
| [0004](0004-autorisation-cote-noyau.md) | Autorisation côté noyau, pas côté Discord | Acceptée |
| [0005](0005-anomalies-comme-etats.md) | Une anomalie est un état, pas un événement | Acceptée |
| [0006](0006-pas-de-modele-de-rendu-partage.md) | Pas de modèle de rendu partagé entre Discord et le web | Acceptée |
| [0007](0007-migrations-et-sauvegarde-au-demarrage.md) | Migrations et sauvegarde appliquées par l'application | Acceptée |
| [0008](0008-discord-net.md) | Discord.Net plutôt que NetCord | Acceptée |
| [0009](0009-concurrence-du-snapshot.md) | Échange atomique sans verrou pour `IModuleState<T>` | Acceptée |
| [0010](0010-frontieres-de-projets.md) | Frontières de projets, garanties par un test | Acceptée |
| [0011](0011-options-dynamiques-differees.md) | `OptionsFrom` dans le contrat, résolution différée | Acceptée |
| [0012](0012-authentification-des-webhooks.md) | Jeton de webhook en en-tête, URL en repli | Acceptée |
| [0013](0013-schema-partage-modules-et-hub.md) | Le hub décrit ses réglages avec la primitive des modules | Acceptée |
| [0014](0014-demander-une-sauvegarde-nest-pas-la-piloter.md) | Demander une sauvegarde n'est pas la piloter | Acceptée |
| [0015](0015-mediajourney-etat-derive.md) | `MediaJourney` est un état dérivé, jamais possédé | Acceptée |
| [0016](0016-extensibilite-des-adaptateurs.md) | Extensibilité des adaptateurs : corrigé, et dette assumée | Acceptée |
