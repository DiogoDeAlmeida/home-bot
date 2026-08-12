# ADR-0018 — Un verrou de première instance, pas un fichier PID

**Statut :** acceptée — 12 août 2026

## Contexte

Un message de tableau de bord Discord s'est dédoublé sans qu'aucun bug d'édition n'en soit la
cause. En creusant : deux instances du hub tournaient en même temps sur le même répertoire
`.local`, chacune ignorant l'existence de l'autre, chacune éditant sa propre idée de « le »
message. Six processus `dotnet` actifs sur la machine de développement au moment de l'incident —
des sessions de débogage VS Code (Shift+F5) accumulées sans être fermées au fil des tranches
précédentes.

Le tableau de bord dupliqué n'était que le symptôme visible. La configuration JSON, le keyring
Data Protection et la base SQLite auraient pu tout aussi bien diverger en silence, sans qu'aucun
message d'erreur ne le signale jamais — c'est le genre de corruption d'état partagé qui ne se
découvre qu'au pire moment.

## Décision

**Un handle de fichier exclusif** (`FileShare.None` sur `hub.lock`, dans le répertoire de
données), pris comme premier accès à ce répertoire — avant le keyring, la configuration, la
base — et tenu pour toute la durée du processus.

Une seconde instance qui tente le même `FileStream` reçoit une `IOException`, traduite en
`SingleInstanceLockException` avec un message explicite : quoi vérifier (session de débogage
oubliée, processus orphelin), pas seulement que ça a échoué. `Program.cs` l'attrape avant que la
journalisation structurée n'existe — `stderr` brut, que systemd capture aussi bien qu'un logger.

## Pourquoi pas un fichier PID

Un PID mémorisé sur disque peut survivre au processus qui l'a écrit — un crash, un `kill -9` —
et bloquerait alors un redémarrage parfaitement légitime tant que personne ne l'efface à la
main. Vérifier qu'un PID est encore vivant avant de décider est en plus intrinsèquement
« TOCTOU » : rien n'empêche un autre PID de réutiliser le même numéro entre la vérification et
l'usage.

Un verrou tenu par le système d'exploitation n'a aucun de ces deux problèmes : il est libéré à
l'instant où le processus qui le tient se termine, quelle que soit la façon dont il se termine.
Aucun nettoyage à écrire, aucune fenêtre de course à raisonner.

## Pourquoi ça marche pareil sur le LXC de production

`FileShare.None` est traduit par .NET en `flock()` sur Linux depuis .NET Core 2.1 : le même code
protège aussi bien le LXC Debian que ce poste de développement, sans branche spécifique à la
plateforme.

## Vérifié en conditions réelles

Deux instances du vrai binaire lancées coup sur coup sur le même `.local` : la première démarre
normalement, la seconde quitte immédiatement avec le code 1 et le message attendu sur `stderr`,
la première reste intacte. Un arrêt forcé (`Stop-Process -Force`, l'équivalent d'un crash) libère
le verrou assez vite pour qu'une instance suivante démarre sans délai d'attente ni intervention.

## Conséquences

- Le doublon déjà posté dans le salon Discord a été supprimé à la main — ce n'était pas un bug
  d'édition, donc rien dans le code ne le nettoyait de lui-même.
- Aucun réglage nouveau : le verrou ne se désactive pas, il n'y a pas de cas où deux instances
  sur le même répertoire seraient voulues.
