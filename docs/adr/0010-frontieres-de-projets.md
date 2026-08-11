# ADR-0010 — Frontières de projets, garanties par un test

**Statut :** acceptée — 11 août 2026

## Contexte

Le cadrage proposait un découpage en sept projets, dont `Application` et `Infrastructure`
séparés selon l'usage de la Clean Architecture.

Ce découpage-là paie en équipe et avec plusieurs cibles de présentation. Ici, la frontière qui
fait réellement un travail est ailleurs : **contrats ↔ modules ↔ adaptateurs**.

## Décision

- `Application` **disparaît**, fusionné dans `Core`.
- `Abstractions` **apparaît** : aucune référence projet, rien hors `Microsoft.Extensions.*`.
  C'est la seule chose qu'un module a le droit de connaître.
- `Web` devient `Host` : racine de composition, seul projet exécutable, seul endroit qui
  connaisse à la fois Discord et l'API.

Règle de dépendances :

```
Modules.*  ──▶  Abstractions          (et rien d'autre du projet)
Core       ──▶  Abstractions
Infra      ──▶  Abstractions, Core
Discord    ──▶  Abstractions, Core
Host       ──▶  tout
```

## La règle est vérifiée, pas seulement énoncée

« Si intégrer Home Assistant demande de modifier le noyau, c'est que l'abstraction est ratée. »
Cette phrase n'a de valeur que si quelque chose l'applique. Une convention orale se viole en
trois mois, sans mauvaise foi : on ajoute une référence pour débloquer un cas, et la frontière
disparaît.

`tests/HomelabHub.Architecture.Tests` fait échouer la CI sur violation, par deux vérifications
complémentaires :

1. **les `ProjectReference` déclarés dans les `.csproj`** — c'est l'intention, et cela attrape
   même une référence inutilisée que le compilateur éliderait ;
2. **les références présentes dans les assemblys compilés** — c'est le fait.

**Pourquoi pas NetArchTest.** La règle porte sur des références entre projets, pas sur des
relations entre types : une soixantaine de lignes sans dépendance supplémentaire suffisent.
NetArchTest deviendra utile le jour où il faudra des règles plus fines — interdire un espace de
noms, imposer une direction entre couches à l'intérieur du noyau.

Le test a été vérifié dans les deux sens : il passe sur la structure actuelle, et il échoue
quand on ajoute délibérément une référence de `Modules.Media` vers `Core`. Un garde-fou qu'on
n'a jamais vu échouer ne garde rien.

## Conséquences

- Toute dépendance ajoutée à `Abstractions` est imposée à tous les modules futurs. C'est
  pourquoi le test contrôle aussi cette liste.
- Changer cette règle demande un nouvel ADR, pas une ligne de `.csproj`.
