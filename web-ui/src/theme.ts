import { createTheme } from '@mantine/core'

/**
 * Thème du hub. Volontairement minuscule : Mantine porte déjà le mode clair/sombre, les
 * échelles d'espacement et les couleurs. Tout ce qu'on ajoute ici est du CSS qu'il faudra
 * maintenir — c'est précisément ce qu'on cherchait à ne plus faire.
 */
export const theme = createTheme({
  primaryColor: 'indigo',
  defaultRadius: 'md',
  fontFamily: 'ui-sans-serif, system-ui, "Segoe UI", sans-serif',
  headings: { fontWeight: '600' },

  components: {
    Card: { defaultProps: { withBorder: true, padding: 'lg' } },
    Paper: { defaultProps: { withBorder: true } },
    Button: { defaultProps: { variant: 'filled' } },
  },
})
