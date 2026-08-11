# web-ui

Interface web du hub. **Non initialisée — étape 1.**

## Stack retenue

Vite + React + TypeScript + React Router + TanStack Query + Tailwind + shadcn/ui.

TanStack Query gère le cache et l'invalidation ; SignalR pousse les invalidations plutôt que
les données elles-mêmes, ce qui évite de dupliquer la logique de fraîcheur.

## Pièce centrale

Le **générateur de formulaire à partir de `ModuleConfigSchema`**. C'est lui qui rend le
système réellement extensible : ajouter un module ne doit demander aucune ligne de TypeScript.

Il consomme les champs décrits côté serveur (`ConfigField`) et rend le composant
correspondant. Le champ `OptionsFrom` — options résolues à l'exécution — est présent dans le
contrat mais **délibérément non résolu en v1**
([ADR-0011](../docs/adr/0011-options-dynamiques-differees.md)) : le front affiche une saisie
libre, et la résolution dynamique viendra quand un module en aura réellement besoin.

## Prérequis

Node **20.19+** ou **22.12+** — Vite 7 ne prend plus en charge Node 20.11.

## Build

La sortie sera injectée dans `src/HomelabHub.Host/wwwroot/` par une cible MSBuild, pour que
`dotnet publish` produise un binaire unique servant l'interface en statique.
