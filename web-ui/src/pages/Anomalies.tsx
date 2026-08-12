import { useState } from 'react'
import { modals } from '@mantine/modals'
import { notifications } from '@mantine/notifications'
import { useAnomalies, useRunCapability, useSnoozeAnomaly } from '@/api/hooks'
import type { Anomaly, HubEventSeverity } from '@/api/types'
import { PageTitle } from '@/components/Layout'
import {
  Badge,
  Button,
  Card,
  Code,
  Group,
  Loader,
  SegmentedControl,
  Stack,
  Text,
  Tooltip,
} from '@/components/ui'
import { formatDateTime } from '@/lib/utils'

const SEVERITY: Record<HubEventSeverity, { label: string; color: string }> = {
  0: { label: 'info', color: 'gray' },
  1: { label: 'avertissement', color: 'yellow' },
  2: { label: 'critique', color: 'red' },
}

/**
 * Les anomalies actives, et leur mise en sommeil.
 *
 * Une anomalie est un **état** qui s'ouvre, dure et se résout (ADR-0005). Cette page montre des
 * conditions en cours, pas un flux d'événements — c'est le journal qui fait cela. Un import
 * bloqué depuis dix heures est une ligne ici, et six cents lignes là-bas.
 */
export function AnomaliesPage() {
  const [scope, setScope] = useState('active')
  const anomalies = useAnomalies(scope === 'all')
  const snooze = useSnoozeAnomaly()

  const hush = (anomaly: Anomaly, hours?: number) =>
    snooze.mutate(
      { key: anomaly.dedupeKey, hours },
      {
        onSuccess: () =>
          notifications.show({
            color: 'blue',
            title: 'Mise en sommeil',
            message: hours
              ? `Réactivée dans ${hours} h si elle dure toujours.`
              : 'Tue jusqu’à ce qu’elle se résolve.',
          }),
      },
    )

  return (
    <>
      <PageTitle
        title="Anomalies"
        subtitle="Des conditions en cours, pas un flux d'événements : chacune n'apparaît qu'une fois, quel que soit le nombre de cycles où elle est observée."
      />

      <SegmentedControl
        value={scope}
        onChange={setScope}
        mb="md"
        data={[
          { value: 'active', label: 'Actives' },
          { value: 'all', label: 'Tout, résolues comprises' },
        ]}
      />

      {anomalies.isLoading && <Loader />}

      {!anomalies.isLoading && anomalies.data?.length === 0 && (
        <Card>
          <Text size="sm" c="dimmed">
            {scope === 'active' ? 'Rien à signaler.' : 'Aucune anomalie enregistrée.'}
          </Text>
        </Card>
      )}

      <Stack gap="sm">
        {anomalies.data?.map((anomaly) => (
          <Card key={anomaly.dedupeKey} padding="sm">
            <Group justify="space-between" align="flex-start" wrap="wrap" gap="sm">
              <Stack gap={4} style={{ flex: 1, minWidth: 260 }}>
                <Group gap="xs" wrap="wrap" align="baseline">
                  <Badge variant="light" color={SEVERITY[anomaly.severity].color}>
                    {SEVERITY[anomaly.severity].label}
                  </Badge>
                  <Text fw={500} size="sm">
                    {anomaly.title}
                  </Text>
                  {anomaly.state === 'Snoozed' && (
                    <Badge variant="outline" color="blue">
                      en sommeil
                      {anomaly.snoozedUntil
                        ? ` jusqu'à ${formatDateTime(anomaly.snoozedUntil)}`
                        : " jusqu'à résolution"}
                    </Badge>
                  )}
                  {anomaly.state === 'Resolved' && (
                    <Badge variant="outline" color="green">
                      résolue
                    </Badge>
                  )}
                </Group>

                {anomaly.body && (
                  <Text size="sm" c="dimmed">
                    {anomaly.body}
                  </Text>
                )}

                <Group gap="xs" wrap="wrap">
                  <Code>{anomaly.type}</Code>
                  <Tooltip label="Nombre de cycles où l'anomalie a été observée" openDelay={400}>
                    <Text size="xs" c="dimmed">
                      {anomaly.occurrences} observation{anomaly.occurrences > 1 ? 's' : ''} ·{' '}
                      {formatElapsed(anomaly.durationSeconds)}
                    </Text>
                  </Tooltip>
                  <Text size="xs" c="dimmed">
                    depuis {formatDateTime(anomaly.openedAt)}
                  </Text>
                </Group>
              </Stack>

              {anomaly.state === 'Open' && (
                <Group gap="xs">
                  {anomaly.type === 'media.import.pending' && anomaly.data?.downloadId && (
                    <ResolveImportButton downloadId={anomaly.data.downloadId} />
                  )}
                  <Button
                    size="compact-xs"
                    variant="default"
                    onClick={() => hush(anomaly, 6)}
                    loading={snooze.isPending}
                  >
                    Ignorer 6 h
                  </Button>
                  <Button
                    size="compact-xs"
                    variant="subtle"
                    onClick={() => hush(anomaly)}
                    loading={snooze.isPending}
                  >
                    Jusqu'à résolution
                  </Button>
                </Group>
              )}
            </Group>
          </Card>
        ))}
      </Stack>
    </>
  )
}

/**
 * Déclenche l'import manuel, avec la confirmation que la capacité exige.
 *
 * La confirmation n'est pas une politesse d'interface : `RequireConfirmation` est déclaré sur
 * la capacité elle-même (ADR-0016), donc un import mal déclenché reste impossible sans intention
 * explicite, quel que soit le canal. Cette modale est la traduction web de cette exigence.
 */
function ResolveImportButton({ downloadId }: { downloadId: string }) {
  const run = useRunCapability()

  const confirm = () =>
    modals.openConfirmModal({
      title: 'Importer manuellement',
      children: (
        <Stack gap="xs">
          <Text size="sm">
            Le fichier sera importé dans la bibliothèque avec la correspondance que le service a
            retenue.
          </Text>
          <Text size="xs" c="dimmed">
            Réversible, mais un import sur le mauvais média se corrige à la main.
          </Text>
        </Stack>
      ),
      labels: { confirm: 'Importer', cancel: 'Annuler' },
      confirmProps: { color: 'orange' },
      onConfirm: () =>
        run.mutate(
          { key: 'media.import.manual', args: { download: downloadId } },
          {
            onSuccess: (result) =>
              notifications.show({
                // Outcome 1 = Accepted : la commande est prise en compte, son aboutissement se
                // constatera au cycle suivant.
                color: result.outcome === 2 ? 'red' : 'blue',
                title: 'Import manuel',
                message: result.message ?? 'Demande transmise.',
                autoClose: 8000,
              }),
            onError: (error) =>
              notifications.show({
                color: 'red',
                title: 'Import manuel',
                message: (error as Error).message,
              }),
          },
        ),
    })

  return (
    <Button size="compact-xs" color="orange" onClick={confirm} loading={run.isPending}>
      Importer
    </Button>
  )
}

function formatElapsed(seconds: number): string {
  if (seconds < 60) return `${seconds} s`
  if (seconds < 3600) return `${Math.floor(seconds / 60)} min`
  if (seconds < 86400) return `${Math.floor(seconds / 3600)} h`
  return `${Math.floor(seconds / 86400)} j`
}
