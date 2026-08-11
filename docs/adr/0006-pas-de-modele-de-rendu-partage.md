# ADR-0006 — Pas de modèle de rendu partagé entre Discord et le web

**Statut :** acceptée — 11 août 2026

## Contexte

Le cadrage prévoyait que les modules exposent « des widgets de dashboard : des blocs de données
que les deux interfaces savent rendre ».

L'intention est bonne — écrire une fois, afficher partout — mais les deux cibles n'ont aucune
primitive de mise en page commune. Un embed Discord, c'est 25 champs, 6000 caractères, un
markdown restreint et aucun contrôle de disposition. React n'a aucune de ces limites, et en a
d'autres.

Une abstraction de rendu partagée mène à l'un de deux échecs :

- **le plus petit dénominateur commun** — un dashboard web indigent, contraint par Discord ;
- **un mini-langage de mise en page maison** — on maintient un moteur de rendu au lieu d'un
  homelab.

## Décision

Un widget expose des **données typées et sérialisables, sans information de présentation**
(`WidgetPayload.Data`). Chaque adaptateur possède son propre rendu.

Côté Discord, un rendu générique clé/valeur sert de repli ; un module peut fournir un rendu
dédié quand la lisibilité le justifie. Cela représente quelques dizaines de lignes par module.

**C'est une duplication assumée**, et elle coûte moins cher que l'abstraction qu'elle évite.

## Conséquences

- `CapabilityResult.Payload` suit la même règle : données brutes, mise en forme par
  l'adaptateur.
- Le dashboard Discord n'affiche pas tout. `WidgetDescriptor.ShowOnDiscordDashboard` sélectionne
  l'essentiel — le message permanent doit rester lisible sur mobile. Le détail passe par
  `/media queue list` ou par l'interface web.
- Un même widget peut donc paraître plus riche sur le web que dans Discord. C'est voulu.
