# ADR-0019 — Packaging et mise à jour : sonde de santé réelle, rollback corrélé, déclenchement manuel

**Statut :** acceptée — 12 août 2026

## Contexte

Étape 5 du cadrage. Avant de l'écrire, un état des lieux volontairement honnête des quatre
étapes précédentes (voir [CONTRIBUTING.md](../../CONTRIBUTING.md)) a trouvé cinq bugs réels —
tous passaient la CI, aucun n'aurait été vu sans un humain cliquant, tapant, ou ouvrant
réellement le formulaire concerné. Le packaging n'a aucune raison d'échapper à cette même règle,
d'autant qu'il porte le risque le plus élevé du projet : ce script détient les clés de tout le
homelab et remplace un binaire en production.

Trois questions ont été posées en revue, avant l'écriture de quoi que ce soit :

1. Que vérifie `/healthz` **aujourd'hui**, avant de bâtir un rollback dessus ?
2. Un rollback qui remet l'ancien binaire en place, sans toucher à la base — que se passe-t-il
   si la migration a réussi et que l'échec vient d'ailleurs ?
3. Une mise à jour doit-elle pouvoir s'appliquer d'elle-même ?

## Décision 1 — `/healthz` vérifie trois choses, chacune peut seule le faire échouer

**Avant cette étape**, `/healthz` répondait `200` dès que le processus écoutait sur le port —
rien d'autre. Un hub dont la connexion Discord avait échoué en silence répondait quand même
sain. Le seul lien indirect avec la base : l'atteindre impliquait que la migration au démarrage
avait réussi (`Program.cs` fait `return 1` avant que `RunAsync()` ne soit jamais atteint sur un
échec de migration) — ça ne dit rien de l'état une fois le service en route.

Trois vérifications désormais, chacune capable seule de faire échouer la sonde (503, détail en
JSON) :

- **Base** — `CanConnectAsync()` et absence de migration en attente. Un schéma à moitié
  appliqué doit se voir ici, pas seulement au prochain redémarrage manqué.
- **Discord** — via la nouvelle `IDiscordConnectionStatus` (`HomelabHub.Discord`), qui reflète
  l'état réel de `DiscordGatewayService` : `Connected` et `NotConfigured` comptent comme sains,
  `Failed` comme en échec. `Connecting` compte **aussi** comme en échec — un choix qui a une
  conséquence directe : les quelques secondes de poignée de main juste après un redémarrage
  normal rendent `/healthz` momentanément rouge. Absorbé côté scripts (fenêtre de tolérance de
  90 s), pas en assouplissant la sonde — un blocage permanent en `Connecting` (jamais `Failed`,
  jamais `Connected`) doit rester détectable.
- **Modules** — `IModuleRegistry.IsActive("system")`. Ce module n'a aucune configuration requise
  (cadrage §6 : c'est le banc de test de l'abstraction) ; son inactivité ne peut donc venir que
  d'une régression du noyau de modules, jamais d'une clé d'API absente chez l'utilisateur.

## Décision 2 — le rollback restaure le binaire *et* la sauvegarde prise pour cette tentative précise

**Le trou identifié en revue** : un rollback qui repose l'ancien binaire sans coordonner l'état
de la base peut le confronter à un schéma plus récent que ce qu'il sait lire si la migration a
réussi et que l'échec de `/healthz` vient d'ailleurs — remplacer un binaire cassé par un binaire
qui plante contre sa propre base, pire que le point de départ.

`deploy/update.sh` prend donc sa propre sauvegarde, nommément associée à la tentative en cours
(`pre-update-<depuis>-vers-<vers>-<horodatage>.db`), **avant** tout arrêt de service — pas la
sauvegarde automatique que `Program.cs` prend déjà avant une migration (ADR-0007), qui reste
un filet indépendant pour l'usage direct du binaire hors de ce script. Le rollback restaure
*ce fichier précis*, jamais « la sauvegarde la plus récente » au sens large.

### Écarté : corréler via les journaux du binaire

Une option envisagée était de laisser `Program.cs` créer sa sauvegarde comme d'habitude et de
faire lire au script son nom de fichier dans `journalctl` (la ligne « Sauvegarde de sécurité :
{File} »). Écartée : ça fait dépendre la correction du rollback du texte exact d'un message de
log, potentiellement multi-ligne selon le formateur de console actif, et rejouable seulement si
personne ne reformule jamais cette phrase. Le script possède et nomme sa propre sauvegarde à la
place — aucune dépendance à un format de sortie qui n'a pas vocation à être un contrat.

## Décision 3 — la mise à jour reste un geste explicite ; seul le signalement est automatique

Aucune mise à jour ne s'applique d'elle-même — `deploy/update.sh` est toujours lancé à la main.
`SystemPoller` vérifie périodiquement (`system.update.checkIntervalHours`, 12 h par défaut) si
une version plus récente existe sur GitHub et le signale comme n'importe quelle anomalie
(ADR-0005) : ouverte tant qu'une version plus récente existe, refermée d'elle-même une fois la
mise à jour faite, notifiée dans Discord par l'infrastructure déjà en place.

### Écarté : un mécanisme de notification dédié

Poster directement dans Discord depuis un script ou un service séparé aurait dupliqué une
plomberie déjà écrite, testée en conditions réelles, et qui sait déjà comment s'ouvrir, se
répéter sans bruit, et se refermer. Réutiliser `HubEvent` → `AnomalyEngine` →
`IAnomalyNotifier` coûte une vingtaine de lignes dans `SystemPoller` plutôt qu'un nouveau canal.

### Écarté : vérifier à la cadence du poller de disque

`SystemModule` n'a qu'un seul poller enregistré ; en ajouter un second sous la même clé de
module aurait fait courir deux cycles indépendants sous le même verrou de réconciliation
(`AnomalyEngine.BeginCycle`/`CompleteCycle`, indexé par module et non par poller) — chacun
aurait pu clore à tort les anomalies observées par l'autre. La vérification GitHub vit donc dans
le même `SystemPoller.PollAsync` que l'espace disque, avec son propre débit interne
(`UpdateCheckIntervalHoursKey`) : l'appel à GitHub n'a lieu que si l'intervalle est écoulé, mais
le résultat connu est republié à chaque cycle comme l'exige ADR-0005.

## Vérifié en conditions réelles

**Pas encore.** Compilé et relu, jamais exécuté sur un vrai LXC — voir
[docs/03-deploiement.md](../03-deploiement.md). Avant tout LXC de production, la chaîne complète
(création, installation, premier démarrage, mise à jour, rollback provoqué délibérément) doit
être exercée sur un LXC jetable créé pour l'occasion. Cette section sera mise à jour à l'issue,
avec les écarts trouvés — il y en aura probablement, si la tendance des quatre étapes
précédentes se confirme.

## Conséquences

- `Directory.Build.props` portait une `RepositoryUrl` erronée (`github.com/diogo/homelab-hub`,
  jamais le vrai dépôt) — corrigée au passage vers `DiogoDeAlmeida/home-bot`, seul endroit d'où
  `deploy/install.sh` et `release.yml` tirent cette information.
- `deploy/update.sh` n'est pas distribué dans l'archive de release (ce n'est pas un artefact
  publié) : `install.sh` le dépose à côté du binaire, et chaque mise à jour réussie le remplace
  par la copie correspondant à la version qu'elle vient de déployer.
- Le one-liner depuis l'hôte Proxmox (`pct create` + injection du script, à la manière des
  Helper-Scripts — objectif non fonctionnel du cadrage §3) reste hors périmètre : ce qui est
  livré ici s'exécute *dans* un LXC déjà créé. Candidat naturel pour une prochaine tranche, une
  fois cette chaîne prouvée en conditions réelles.
