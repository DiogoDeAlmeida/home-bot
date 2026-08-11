/**
 * Façade sur Mantine : inventaire explicite de ce que l'application utilise.
 *
 * Les pages importent d'ici, jamais de `@mantine/core` directement. Ce que cela garantit
 * réellement, ni plus ni moins :
 *
 * - **l'inventaire est visible** — vingt composants, pas cent, et la liste est ici ;
 * - **le rayon d'impact d'un changement de bibliothèque est borné et connu** : ce fichier,
 *   `theme.ts`, et `SchemaForm.tsx`, qui est le seul à connaître Mantine en profondeur.
 *
 * Ce que cela ne garantit pas, et il vaut mieux le dire : les *props* restent celles de
 * Mantine. Une vraie isolation demanderait de redéclarer l'interface de chaque composant —
 * c'est-à-dire d'écrire son propre système de design, exactement le coût qu'on a refusé de
 * payer en abandonnant les primitives à la main.
 */
export {
  Alert,
  Anchor,
  Badge,
  Box,
  Button,
  Card,
  Center,
  Code,
  Container,
  Divider,
  Group,
  Loader,
  Paper,
  Progress,
  SegmentedControl,
  Stack,
  Switch,
  Text,
  Title,
  Tooltip,
} from '@mantine/core'
