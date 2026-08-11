import type { ReactNode } from 'react'
import { SimpleGrid } from '@mantine/core'
import { useQuery } from '@tanstack/react-query'
import { api } from '@/api/client'
import { useBackups, useModules } from '@/api/hooks'
import type { SystemSnapshot } from '@/api/types'
import { PageTitle } from '@/components/Layout'
import { Alert, Badge, Card, Code, Group, Loader, Progress, Stack, Text } from '@/components/ui'
import { formatBytes, formatDateTime, formatUptime } from '@/lib/utils'

interface Widget {
  moduleKey: string
  key: string
  title: string
  order: number
  data: unknown
  generatedAt: string
}

/**
 * Miroir web de ce que le bot affichera dans un canal conversationnel.
 *
 * Les widgets sont des **données pures** : c'est ici qu'on décide de leur rendu, et chaque
 * adaptateur décidera du sien de son côté. Il n'y a pas de modèle de présentation partagé
 * (ADR-0006) — duplication assumée, moins coûteuse que l'abstraction qu'elle évite.
 */
export function DashboardPage() {
  const widgets = useQuery({
    queryKey: ['widgets'],
    queryFn: () => api.get<Widget[]>('/api/widgets'),
    refetchInterval: 15_000,
  })
  const modules = useModules()
  const backups = useBackups()

  if (widgets.isLoading) {
    return <Loader />
  }

  const lastBackup = backups.data?.[0]

  return (
    <>
      <PageTitle
        title="Tableau de bord"
        subtitle="Mise à jour automatique toutes les 15 secondes."
      />

      <SimpleGrid cols={{ base: 1, sm: 2 }} spacing="md">
        {widgets.data?.map((widget) => (
          <Card key={widget.key}>
            <Group justify="space-between" align="baseline" mb="sm">
              <Text fw={500}>{widget.title}</Text>
              <Text size="xs" c="dimmed">
                {widget.moduleKey}
              </Text>
            </Group>
            {widget.key === 'system.overview' ? (
              <SystemOverview snapshot={widget.data as SystemSnapshot} />
            ) : (
              <GenericWidget data={widget.data} />
            )}
          </Card>
        ))}

        <Card>
          <Text fw={500} mb="sm">
            Sauvegarde
          </Text>
          {lastBackup ? (
            <Stack gap="xs">
              <Row label="Dernière">{formatDateTime(lastBackup.createdAt)}</Row>
              <Row label="Taille">{formatBytes(lastBackup.sizeBytes)}</Row>
              <Row label="Fichiers">{lastBackup.entryCount}</Row>
              <Row label="Archives">{backups.data?.length}</Row>
            </Stack>
          ) : (
            <Alert color="yellow" variant="light">
              Aucune sauvegarde. Le hub concentre toutes les clés d'API du homelab — en créer une
              depuis les paramètres.
            </Alert>
          )}
        </Card>
      </SimpleGrid>

      <Text size="sm" c="dimmed" mt="xl" mb="sm">
        Modules
      </Text>
      <SimpleGrid cols={{ base: 1, sm: 2 }} spacing="sm">
        {modules.data?.map((module) => (
          <Card key={module.key} padding="sm">
            <Group justify="space-between" wrap="nowrap">
              <Stack gap={2}>
                <Text fw={500}>{module.displayName}</Text>
                <Text size="xs" c="dimmed">
                  {module.blockedReason ?? module.description}
                </Text>
              </Stack>
              <Badge color={module.isActive ? 'green' : 'gray'} variant="light">
                {module.isActive ? 'actif' : 'inactif'}
              </Badge>
            </Group>
          </Card>
        ))}
      </SimpleGrid>
    </>
  )
}

function SystemOverview({ snapshot }: { snapshot: SystemSnapshot }) {
  if (!snapshot.observedAt) {
    return (
      <Text size="sm" c="dimmed">
        Première observation en attente.
      </Text>
    )
  }

  return (
    <Stack gap="xs">
      <Row label="Version">
        <Code>{snapshot.version.split('+')[0]}</Code>
      </Row>
      <Row label="En service depuis">{formatUptime(snapshot.uptime)}</Row>

      {snapshot.volumes.map((volume) => (
        <Stack key={volume.path} gap={4} mt="xs">
          <Group justify="space-between">
            <Text size="xs" c="dimmed">
              {volume.label}
            </Text>
            <Text size="xs" c="dimmed">
              {formatBytes(volume.freeBytes)} libres · {volume.freePercent} %
            </Text>
          </Group>
          <Progress
            value={100 - volume.freePercent}
            color={volume.freePercent < 10 ? 'red' : undefined}
            size="sm"
          />
        </Stack>
      ))}
    </Stack>
  )
}

/** Repli pour tout widget dont le front ne connaît pas encore la forme. */
function GenericWidget({ data }: { data: unknown }) {
  return <Code block>{JSON.stringify(data, null, 2)}</Code>
}

function Row({ label, children }: { label: string; children: ReactNode }) {
  return (
    <Group justify="space-between" align="baseline" gap="md">
      <Text size="sm" c="dimmed">
        {label}
      </Text>
      <Text size="sm" fw={500}>
        {children}
      </Text>
    </Group>
  )
}
