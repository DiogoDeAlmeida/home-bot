# ADR-0002 — Modules statiques, activation ≠ injection de dépendances

**Statut :** acceptée — 11 août 2026

## Contexte

Le cadrage initial proposait un `IHubModule` portant `ConfigureServices(IServiceCollection,
IConfiguration)`, et exigeait qu'un module puisse être activé et désactivé depuis l'interface
web « sans redémarrage si possible ».

Ces deux exigences sont incompatibles : **le conteneur d'injection de dépendances de .NET est
immuable après `Build()`**. Un module ne peut pas enregistrer ses services au moment où
l'utilisateur coche une case.

## Décision

Séparer trois choses que le cadrage confondait :

| | Quand | Modifiable à chaud |
|---|---|---|
| Enregistrement dans le conteneur | au démarrage, pour **tous** les modules, activés ou non | non |
| Activation | état runtime lu par le noyau | **oui** |
| Configuration (URL, clé d'API, intervalles) | état runtime | **oui**, via `IOptionsMonitor` |

Un module désactivé est un module dont le noyau ne démarre pas les sources d'ingestion, ne
publie pas les capacités et ne route pas les widgets. Le comportement observable est exactement
celui voulu — « ni commande, ni endpoint, ni widget » — sans toucher au conteneur.

Corollaire : `CheckHealthAsync` **sort** de `IHubModule`. La sonde a besoin des clients résolus
par le conteneur, alors que `IHubModule` est instancié avant que le conteneur existe. Elle
devient `IModuleHealthCheck`, enregistrée via `AddHealthCheck<T>()`.

Corollaire : `Version` **disparaît** de `IHubModule`. Tant que les modules sont des projets
référencés statiquement, leur version est celle de l'application.

## Alternatives écartées

**Chargement dynamique de plugins** (`AssemblyLoadContext`) — apporterait un vrai besoin de
versionner les modules, mais impose l'isolation des dépendances, le déchargement et le
versionnage du contrat. Coût considérable, bénéfice nul tant qu'une seule personne écrit les
modules. À reconsidérer si quelqu'un d'autre en écrit un.

## Conséquences

- Ajouter un module reste une modification du code source et un redéploiement. Assumé.
- L'interface web peut activer, désactiver et reconfigurer sans redémarrage — ce qui était le
  besoin réel derrière la demande initiale.
