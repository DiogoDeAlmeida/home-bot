# web-ui

Interface web du hub. Vite + React + TypeScript + React Router + TanStack Query + Tailwind 4.

```bash
npm install
npm run dev        # http://localhost:5173, proxifie /api vers le Host sur :8080
npm run build      # sortie dans ../src/HomelabHub.Host/wwwroot
```

## La pièce centrale

[`src/components/SchemaForm.tsx`](src/components/SchemaForm.tsx) génère un formulaire à partir
d'un schéma servi par le serveur. **Ajouter un module ne demande aucune ligne de TypeScript.**

Le même composant sert la configuration d'un module et les réglages du hub : le noyau décrit ses
réglages avec la primitive des modules, sous le préfixe réservé `hub.`
([ADR-0013](../docs/adr/0013-schema-partage-modules-et-hub.md)).

Deux règles gouvernent la soumission :

- **seuls les champs modifiés partent** — un secret arrive masqué (`••••••1234`) ; le réémettre
  écraserait la vraie valeur par des points de suspension ;
- **un champ vidé part explicitement à `null`**, ce qui supprime la clé côté serveur.

`OptionsFrom` — options résolues à l'exécution — est présent dans le contrat mais **pas encore
résolu** ([ADR-0011](../docs/adr/0011-options-dynamiques-differees.md)) : le formulaire rend une
saisie libre et le dit explicitement plutôt que d'afficher un champ texte inexpliqué là où on
attend une liste.

## Composants d'interface

`src/components/ui/primitives.tsx` suit les conventions shadcn/ui — mêmes noms, même `cn()`,
mêmes variantes via `class-variance-authority` — mais les composants sont écrits à la main. Pour
cinq écrans d'administration, le CLI shadcn entraînerait une douzaine de dépendances Radix pour
des boutons et des champs de saisie. La structure reste compatible : le jour où un vrai menu
déroulant accessible sera nécessaire, on dépose le composant shadcn correspondant à côté.

## Intégration au build .NET

`dotnet publish -c Release` déclenche `npm run build` via une cible MSBuild du projet Host, et
embarque `wwwroot` dans la publication. `-p:SkipWebUi=true` court-circuite l'étape quand Node
n'est pas disponible. En Debug, la cible ne s'exécute pas : le front se lance à part.
