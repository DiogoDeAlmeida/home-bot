# ADR-0013 — Le hub décrit ses réglages avec la primitive des modules

**Statut :** acceptée — 11 août 2026

## Contexte

`ModuleConfigSchema` décrit la configuration d'un module et permet au front de générer le
formulaire sans code dédié. Mais le hub lui-même a des réglages : rétention des sauvegardes,
anti-rebond, niveau de journalisation, et demain le jeton Discord et le salon du tableau de bord.

Ces réglages n'appartenaient à aucun schéma. La rétention vivait sous une clé
`hub.backup.retention` écrite en dur, hors de toute description — donc invisible du générateur
de formulaire, donc destinée à une page Paramètres écrite à la main.

## Décision

**Même primitive, porteur différent.**

```
ConfigSchema<TSelf>              ← toutes les méthodes AddUrl, AddSecret, AddDuration…
├── ModuleConfigSchema           ← clés préfixées par la clé du module
└── HubConfigSchema              ← clés préfixées par « hub. », préfixe réservé
```

Le paramètre de type ne sert qu'à préserver le chaînage fluide dans les classes dérivées. Les
deux produisent des `ConfigField` strictement identiques — un test le vérifie, parce qu'une
réutilisabilité affirmée et jamais exercée ne vaut rien.

Côté serveur, `ConfigSurface` projette et écrit indifféremment les deux :
`/api/modules/{clé}/config` et `/api/settings` partagent le même code, au préfixe près. Côté
front, **un seul générateur de formulaire**.

**Aux yeux de l'interface, le noyau est un pseudo-module. Dans le contrat, il n'en est pas un** :
il n'implémente pas `IHubModule`, n'a ni capacité, ni cycle d'ingestion, ni activation.

## Le préfixe `hub` est réservé

`AddHubCore` refuse un module dont la clé serait `hub` : il écraserait la rétention des
sauvegardes ou le niveau de journalisation. L'échec est au démarrage, comme le reste des
validations de déclaration.

## Ce que la décision apporte au-delà de l'économie de code

Elle force à valider la primitive **avant** que quatre clients de services ne s'appuient dessus.
Si `ConfigSchema` a un défaut de conception, on le découvre avec le module `system` et ses trois
champs, pas avec le module média déjà écrit.

C'est le même raisonnement qui a fait écrire le module `system` avant l'interface web.

## Conséquences

- Ajouter un réglage au hub ne demande aucune ligne de TypeScript, exactement comme pour un module.
- `hub.logging.level` est appliqué à chaud par un délégué de filtrage évalué à chaque appel de
  journalisation : passer en `Debug` depuis l'interface est instantané, sans SSH ni redémarrage.
- La page Paramètres et la page Modules partagent leur composant de formulaire.
