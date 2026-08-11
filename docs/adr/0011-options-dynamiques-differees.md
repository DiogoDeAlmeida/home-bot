# ADR-0011 — `OptionsFrom` dans le contrat, résolution différée

**Statut :** acceptée — 11 août 2026

## Contexte

`ModuleConfigSchema` était purement statique. Confronter le contrat au cas Home Assistant
*avant* de le figer a révélé que c'était insuffisant.

Home Assistant expose des milliers d'entités. On ne peut pas demander de taper
`sensor.salon_temperature` à la main dans un champ texte : il faut une liste alimentée à
l'exécution en interrogeant le service.

Et ce n'est pas un cas isolé :

- le rôle `hub-admin` ([ADR-0004](0004-autorisation-cote-noyau.md)) — la liste des rôles ne
  peut venir que de l'API Discord ;
- le dossier racine ou le profil de qualité Radarr ;
- le salon du dashboard Discord.

Sans cet exercice, un schéma statique aurait été figé et le manque découvert à l'étape 7.

## Décision

**Le contrat porte `ConfigField.OptionsFrom` dès maintenant. Le front ne le résout pas en v1.**

- `OptionsFrom` désigne la clé d'une capacité de lecture renvoyant la liste des options.
- `DependsOn` désigne les champs qui doivent être renseignés avant que cette résolution soit
  possible — on ne peut pas lister les entités Home Assistant avant d'en connaître l'URL et le
  jeton.
- En attendant, le formulaire affiche une saisie libre. Les quelques identifiants concernés
  seront saisis à la main.

## Pourquoi différer

Résoudre dynamiquement suppose un formulaire **progressif** — certains champs n'apparaissent
qu'une fois d'autres validés —, la gestion des dépendances entre champs, et celle de l'état
« pas encore configurable ». C'est le genre de chantier qui fait gonfler l'étape 1 jusqu'à ce
que plus rien ne soit déployable, alors qu'aucun module de la v1 n'en a besoin.

Deux minutes de saisie manuelle, une fois, contre une semaine de front générique.

**Ce qui compte est que le schéma n'ait pas à changer plus tard.** Le champ existe, sa sémantique
est fixée ; seule son implémentation front est repoussée.

## Conséquences

- Le générateur de formulaire ignore `OptionsFrom` en v1 et rend un champ texte.
- La résolution sera implémentée quand un module en aura réellement besoin — vraisemblablement
  avec le module Home Assistant.
- Les capacités servant de source d'options sont de simples capacités de lecture : aucun
  mécanisme particulier à prévoir côté serveur.
