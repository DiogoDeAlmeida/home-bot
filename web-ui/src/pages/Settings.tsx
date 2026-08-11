import { modals } from '@mantine/modals'
import { notifications } from '@mantine/notifications'
import {
  keys,
  useBackups,
  useCapabilities,
  useConfigSurface,
  useRunCapability,
  useSaveConfig,
} from '@/api/hooks'
import type { CapabilitySummary } from '@/api/types'
import { PageTitle } from '@/components/Layout'
import { SchemaForm } from '@/components/SchemaForm'
import {
  Alert,
  Badge,
  Button,
  Card,
  Code,
  Group,
  Loader,
  Stack,
  Text,
} from '@/components/ui'
import { formatBytes, formatDateTime } from '@/lib/utils'

/**
 * Réglages du hub et sauvegardes.
 *
 * Le formulaire est **le même composant** que celui de la page Modules : le noyau décrit ses
 * réglages avec la primitive des modules, sous le préfixe réservé `hub.` (ADR-0013).
 */
export function SettingsPage() {
  const surface = useConfigSurface('/api/settings', keys.settings)
  const save = useSaveConfig('/api/settings', keys.settings)

  return (
    <>
      <PageTitle
        title="Paramètres"
        subtitle="Réglages du hub lui-même. Même schéma, même formulaire que pour un module."
      />

      <Stack gap="md">
        <Card>
          {surface.isLoading && <Loader size="sm" />}
          {surface.data && (
            <SchemaForm
              surface={surface.data}
              onSave={(values) => save.mutate(values)}
              saving={save.isPending}
              saved={save.isSuccess}
              error={save.isError ? (save.error as Error).message : undefined}
            />
          )}
        </Card>

        <BackupsCard />
      </Stack>
    </>
  )
}

function BackupsCard() {
  const backups = useBackups()
  const capabilities = useCapabilities()
  const run = useRunCapability()

  const createBackup = capabilities.data?.find((c) => c.key === 'system.backup.create')

  const launch = (capability: CapabilitySummary) => {
    const execute = () =>
      run.mutate(
        { key: capability.key },
        {
          onSuccess: (result) =>
            notifications.show({
              color: result.outcome === 0 ? 'green' : 'yellow',
              title: capability.displayName,
              message: result.message ?? 'Terminé.',
            }),
          onError: (error) =>
            notifications.show({
              color: 'red',
              title: capability.displayName,
              message: (error as Error).message,
            }),
        },
      )

    // RequireConfirmation est désormais porté par la capacité, pas par le canal (ADR-0016) :
    // l'interface web demande donc la même confirmation qu'un bouton dans un salon.
    if (!capability.requireConfirmation) {
      execute()
      return
    }

    modals.openConfirmModal({
      title: capability.displayName,
      children: <Text size="sm">{capability.description}</Text>,
      labels: { confirm: 'Confirmer', cancel: 'Annuler' },
      onConfirm: execute,
    })
  }

  return (
    <Card>
      <Group justify="space-between" align="flex-start" wrap="wrap" gap="md" mb="md">
        <Stack gap={4} style={{ flex: 1, minWidth: 260 }}>
          <Text fw={500}>Sauvegardes</Text>
          <Text size="sm" c="dimmed">
            Une archive unique par sauvegarde : base, keyring et configuration. Restaurer la base
            sans son keyring rendrait tous les secrets illisibles.
          </Text>
        </Stack>
        {createBackup && (
          <Button onClick={() => launch(createBackup)} loading={run.isPending}>
            Sauvegarder maintenant
          </Button>
        )}
      </Group>

      {backups.isLoading && <Loader size="sm" />}
      {backups.data?.length === 0 && (
        <Alert color="yellow" variant="light">
          Aucune archive pour l'instant.
        </Alert>
      )}

      {backups.data && backups.data.length > 0 && (
        <Stack gap="xs">
          {backups.data.map((archive) => (
            <Group key={archive.fileName} gap="md" wrap="wrap">
              <Code>{archive.fileName}</Code>
              <Text size="sm" c="dimmed">
                {formatDateTime(archive.createdAt)}
              </Text>
              <Badge variant="light" color="gray">
                {formatBytes(archive.sizeBytes)}
              </Badge>
              <Text size="xs" c="dimmed">
                {archive.entryCount} fichiers
              </Text>
            </Group>
          ))}
        </Stack>
      )}
    </Card>
  )
}
