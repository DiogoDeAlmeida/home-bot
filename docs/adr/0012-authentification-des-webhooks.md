# ADR-0012 — Jeton de webhook en en-tête, URL en repli

**Statut :** acceptée — 11 août 2026

## Contexte

Une première version de cette décision plaçait le jeton dans l'URL, au motif que Radarr et
Sonarr ne permettaient pas d'en-têtes personnalisés sur leurs connexions Webhook.

**Cette contrainte est levée.** La fonctionnalité a été implémentée dans les deux projets
(Sonarr PR #7371, portée vers Radarr PR #10651). Radarr 6.3.0 et Sonarr 4.0.19 exposent
désormais, sur la connexion Webhook, des champs Username, Password et Headers en paires
clé/valeur.

Un secret dans une URL fuit par les journaux d'accès, l'historique, les en-têtes `Referer` et
la configuration d'un éventuel proxy inverse. Dès qu'un en-tête est possible, il est préféré.

## Décision

**Route propre, jeton en en-tête :**

```
POST /api/webhooks/{moduleKey}/{hook}
X-Hub-Token: <jeton>
```

**Repli conservé** pour les services incapables d'envoyer un en-tête : jeton en dernier segment
d'URL. La vérification côté noyau est identique dans les deux cas ; seule la source du jeton
diffère. Le contrat accepte les deux, le module n'en voit aucun.

**Basic auth** utilisable en second facteur, mais **non imposée** : un jeton comparé en temps
constant suffit.

## Garanties portées par le noyau

- un jeton par module, généré au premier démarrage, régénérable depuis l'interface ;
- comparaison en temps constant ;
- **le jeton n'apparaît jamais dans les journaux** — l'URL est expurgée avant écriture, ce qui
  vaut surtout pour le mode dégradé ;
- route exemptée du cookie d'authentification, mais **refusée tant que l'assistant de premier
  démarrage n'est pas terminé** ;
- limitation de débit ;
- les en-têtes d'authentification sont retirés de `WebhookRequest.Headers` avant remise au
  module.

## Conséquence sur le comportement

Un webhook n'est jamais l'unique source de vérité : il déclenche un événement et, le plus
souvent, un cycle de poll anticipé ([ADR-0003](0003-trois-modes-ingestion.md)). Une notification
perdue — jeton invalide, service redémarré, hub arrêté — se répare au cycle suivant sans
intervention.

`WebhookResult.Ignored` renvoie tout de même un 200 au service appelant : Radarr désactive une
connexion qui échoue trop souvent, et on ne veut pas perdre l'intégration à cause d'un payload
inattendu.
