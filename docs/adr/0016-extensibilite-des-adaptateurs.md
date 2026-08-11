# ADR-0016 — Extensibilité des adaptateurs : ce qui a été corrigé, ce qui reste dû

**Statut :** acceptée — 11 août 2026, avant l'écriture du module média

## Contexte

Question posée : si un second adaptateur conversationnel apparaît dans six mois, qu'est-ce qui
casse ? Le socle tient — autorisation côté noyau, widgets en DTO purs, capacités agnostiques —
mais des noms de plateforme avaient fui dans le contrat.

Le principe qui guide l'arbitrage : **corriger ce qui touche `Abstractions`, différer ce qui
reste interne à un projet d'adaptateur.** Ce qui est dans `Abstractions` est imposé à tous les
modules ; le changer plus tard signifie reprendre chaque module écrit entre-temps. Ce qui vit
dans `HomelabHub.Discord` ne coûte qu'une réécriture locale, et sera de toute façon mieux conçu
avec un second cas d'usage sous les yeux.

## Ce qui a été corrigé maintenant

### 1. Quatre noms de plateforme dans le contrat

L'inventaire s'est révélé plus large que le seul `DiscordBinding` :

| Avant | Après |
|---|---|
| `DiscordBinding(SubGroup, Name, Ephemeral, RequireConfirmation)` | `CommandBinding(params string[] Path) { PrivateReply }` |
| `CapabilityExposure.Rest \| Discord` | `CapabilityExposure.Api \| Chat` |
| `InvocationSource.Rest / DiscordCommand / DiscordComponent` | `Api / ChatCommand / ChatButton` |
| `WidgetDescriptor.ShowOnDiscordDashboard` | `ShowOnChatDashboard` |

Le quatrième n'avait été repéré ni dans l'analyse initiale ni dans la revue : il vivait dans
`Abstractions/Dashboard`, loin des capacités.

### 2. La forme du nom de commande

Trois options ont été pesées.

**Le mapping déclaré par l'adaptateur** — l'intuition la plus séduisante : « une capacité n'a
pas à savoir qu'un jour on la joindra par Telegram ». Écartée, parce qu'elle **inverse la
dépendance**. L'adaptateur devrait tenir une table de toutes les clés de capacités de tous les
modules ; ajouter un module obligerait à modifier le projet Discord, et « écrire une capacité
suffit à obtenir une commande » cesserait d'être vrai.

**Une collection indexée par adaptateur** sur le descripteur — `Bindings["discord"] = …`.
Écartée aussi : la capacité continue de nommer Discord, en chaîne plutôt qu'en type. On déplace
le problème sans le résoudre, et on perd la validation typée.

**Retenue : un chemin de commande neutre**, relatif au module, que chaque adaptateur projette
dans sa syntaxe.

```csharp
Command: new CommandBinding("queue", "pause")
//  Discord  → /media queue pause      (commande, groupe, sous-commande)
//  Telegram → /media_queue_pause
//  CLI      → media queue pause
```

**La capacité dit comment elle s'appelle, pas comment chaque plateforme l'épelle.** Aucune
plateforme n'est nommée, la découverte automatique reste vraie, et la validation typée demeure.

### 3. `RequireConfirmation` remonte sur la capacité

Il était porté par le binding, donc conditionné à l'existence d'une commande. Or « cette
opération est destructrice » est une propriété de l'opération, pas du canal qui l'invoque : une
suppression déclenchée depuis l'API mérite la même confirmation que depuis un bouton. Le champ
est désormais sur `CapabilityDescriptor`, et chaque adaptateur choisit comment demander la
confirmation.

## Ce qui n'avait pas besoin d'être corrigé

**L'autorisation ne fuit pas.** L'intuition initiale — « l'autorisation s'appuie sur un ID de
rôle Discord, il manque un principal indépendant de la plateforme » — ne correspond pas au code.
`CapabilityInvocation` porte `ActorId` (chaîne opaque) et `IsAdministrator` (booléen). Le noyau
ne connaît aucun rôle : **l'adaptateur tranche et transmet un verdict**. L'identifiant de rôle
Discord n'existe nulle part ; il vivra dans la configuration de l'adaptateur, à sa place.

Un principal multi-plateforme deviendrait nécessaire pour reconnaître la même personne sur deux
canaux. C'est du multi-utilisateur — une fonctionnalité hors périmètre v1, pas une fuite
d'abstraction. La distinction compte : on ne paie pas aujourd'hui pour une fonctionnalité qu'on
a explicitement exclue.

## Dette assumée, et son chemin de migration

### La profondeur de commande est validée par le noyau

`CapabilityValidator` refuse un chemin de plus de deux segments — la contrainte de Discord, qui
plafonne à trois niveaux commande comprise. C'est une limite de plateforme appliquée par le
noyau : une fuite, assumée tant qu'il n'y a qu'un adaptateur.

**Migration :** à l'étape 3, l'adaptateur Discord valide ses propres contraintes au démarrage —
profondeur, quotas de commandes, longueurs. Le noyau ne garde que le générique : forme des noms,
unicité, cohérence entre exposition et commande, ordre des paramètres. Les constantes sont déjà
isolées et commentées dans `CapabilityValidator`, la bascule est mécanique.

### Le flux de confirmation et le message de tableau de bord persistant

Ils n'existent pas encore. Écrits à l'étape 3, ils vivront dans `HomelabHub.Discord`. Un second
adaptateur les dupliquerait.

**Migration :** au moment où un second adaptateur apparaît, extraire la partie commune —
machine à états de la confirmation, réconciliation du message persistant avec son identifiant
en base — dans `HomelabHub.Core`, en laissant le rendu et les identifiants de contrôles dans
chaque adaptateur.

**Pourquoi ne pas le faire maintenant :** l'abstraction serait écrite contre un seul cas
d'usage, donc calquée sur Discord, donc fausse pour le suivant. Le coût de l'extraction plus
tard est un refactoring local à un projet ; le coût d'une mauvaise abstraction maintenant est un
contrat à défaire.

## Ce qui rendait déjà l'ajout d'un adaptateur possible

Pour mémoire, et parce que ces décisions sont ce qui limite la dette ci-dessus :

- **l'autorisation est décidée par le noyau** ([ADR-0004](0004-autorisation-cote-noyau.md)) —
  un nouvel adaptateur n'a aucune règle de sécurité à réimplémenter ;
- **les widgets sont des DTO purs** ([ADR-0006](0006-pas-de-modele-de-rendu-partage.md)) — il
  n'y a pas de modèle de rendu à contorsionner ;
- **les capacités sont découvertes**, pas déclarées adaptateur par adaptateur ;
- **les modules ne référencent qu'`Abstractions`** ([ADR-0010](0010-frontieres-de-projets.md)) —
  un adaptateur ne peut pas devenir une dépendance des modules par accident.
