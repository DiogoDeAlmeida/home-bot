# Contribuer

> Les tests automatisés prouvent qu'un composant fait ce qu'on lui a demandé ; seule
> l'exécution réelle prouve que la demande était la bonne.

Cinq bugs trouvés en conditions réelles sur une seule tranche (adaptateur Discord, étape 3-4) —
zéro par la suite de tests, pourtant verte à chaque étape : un verrou de sauvegarde qui
s'archivait lui-même, des réponses `Query` qui ne rendaient jamais leur `Payload`, un attribut
HTML natif qui bloquait un formulaire avant même d'atteindre la logique JS déjà correcte, un
identifiant de téléchargement jamais exposé là où deux commandes en avaient besoin. Chacun de
ces cas passait la CI. Aucun n'aurait été vu sans quelqu'un cliquant réellement le bouton,
tapant réellement la commande, ouvrant réellement le formulaire dans un navigateur.

Ce que ça change concrètement :

- **Un vert en CI ferme un ticket, pas une tranche.** Une fonctionnalité qui touche Discord,
  le navigateur, ou un service distant (Radarr, Sonarr, qBittorrent…) n'est terminée qu'après
  avoir été exercée pour de vrai — pas seulement testée en isolation contre un double.
- **Écrire le test qui aurait attrapé le bug n'efface pas le bug qu'il a fallu trouver en
  premier autrement.** Les cinq cas ci-dessus ont chacun leur test de non-régression
  aujourd'hui ; ça ne change rien au fait qu'aucun des cinq n'existait avant la vérification
  réelle qui les a révélés.
- **Avant de bâtir quelque chose sur un signal existant** (une sonde de santé, un statut, une
  métrique), vérifier ce qu'il couvre *réellement* aujourd'hui plutôt que ce que son nom laisse
  supposer. `/healthz` a longtemps voulu dire « le processus répond aux requêtes HTTP », pas
  « le hub va bien » — voir [ADR-0019](docs/adr/0019-packaging-et-mise-a-jour.md).

Le reste des conventions (français pour la documentation, anglais pour le code et les commits,
appels réseau sortants avec délai explicite, pas de secret dans le dépôt…) est dans
[`docs/00-cadrage.md`](docs/00-cadrage.md), section 8.
