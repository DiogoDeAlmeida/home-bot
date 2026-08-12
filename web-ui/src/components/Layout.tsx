import { NavLink, Outlet } from 'react-router'
import { useLogout } from '@/api/hooks'
import { Anchor, Button, Container, Group, Stack, Text, Title } from '@/components/ui'

const NAV = [
  { to: '/', label: 'Tableau de bord', end: true },
  { to: '/modules', label: 'Modules', end: false },
  { to: '/parametres', label: 'Paramètres', end: false },
  { to: '/anomalies', label: 'Anomalies', end: false },
  { to: '/journal', label: 'Journal', end: false },
]

export function Layout() {
  const logout = useLogout()

  return (
    <Container size="lg" py="md">
      <Group
        justify="space-between"
        align="center"
        wrap="wrap"
        pb="md"
        mb="lg"
        style={{ borderBottom: '1px solid var(--mantine-color-default-border)' }}
      >
        <Group gap="lg" wrap="wrap">
          <Text fw={600} size="lg">
            Homelab Hub
          </Text>
          <Group gap="md">
            {NAV.map((item) => (
              <Anchor
                key={item.to}
                component={NavLink}
                to={item.to}
                end={item.end}
                size="sm"
                underline="never"
                c="dimmed"
                style={({ isActive }: { isActive: boolean }) =>
                  isActive ? { color: 'var(--mantine-color-text)', fontWeight: 600 } : undefined
                }
              >
                {item.label}
              </Anchor>
            ))}
          </Group>
        </Group>

        <Button variant="subtle" size="compact-sm" onClick={() => logout.mutate()}>
          Déconnexion
        </Button>
      </Group>

      <Outlet />
    </Container>
  )
}

export function PageTitle({ title, subtitle }: { title: string; subtitle?: string }) {
  return (
    <Stack gap={4} mb="lg">
      <Title order={2} size="h3">
        {title}
      </Title>
      {subtitle && (
        <Text size="sm" c="dimmed">
          {subtitle}
        </Text>
      )}
    </Stack>
  )
}
