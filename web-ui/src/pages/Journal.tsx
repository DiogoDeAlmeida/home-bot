import { useState } from 'react'
import { useJournal } from '@/api/hooks'
import type { HubEventSeverity } from '@/api/types'
import { PageTitle } from '@/components/Layout'
import {
  Badge,
  Card,
  Code,
  Group,
  Loader,
  SegmentedControl,
  Stack,
  Text,
} from '@/components/ui'
import { formatDateTime } from '@/lib/utils'

const SEVERITY: Record<HubEventSeverity, { label: string; color: string }> = {
  0: { label: 'info', color: 'gray' },
  1: { label: 'anomalie', color: 'yellow' },
  2: { label: 'critique', color: 'red' },
}

export function JournalPage() {
  const journal = useJournal()
  const [minimum, setMinimum] = useState('0')

  const threshold = Number(minimum) as HubEventSeverity
  const events = journal.data?.filter((event) => event.severity >= threshold) ?? []

  return (
    <>
      <PageTitle
        title="Journal"
        subtitle="Derniers événements publiés par les modules. Diagnostiquer sans ouvrir de session SSH."
      />

      <SegmentedControl
        value={minimum}
        onChange={setMinimum}
        mb="md"
        data={[
          { value: '0', label: 'Tout' },
          { value: '1', label: 'Anomalies' },
          { value: '2', label: 'Critiques' },
        ]}
      />

      {journal.isLoading && <Loader />}

      {!journal.isLoading && events.length === 0 && (
        <Card>
          <Text size="sm" c="dimmed">
            Aucun événement à ce niveau.
          </Text>
        </Card>
      )}

      <Stack gap="xs">
        {events.map((event, index) => (
          <Card key={`${event.dedupeKey ?? event.type}-${event.occurredAt}-${index}`} padding="sm">
            <Group gap="xs" wrap="wrap" align="baseline">
              <Badge variant="light" color={SEVERITY[event.severity].color}>
                {SEVERITY[event.severity].label}
              </Badge>
              <Text fw={500} size="sm">
                {event.title}
              </Text>
              <Code>{event.type}</Code>
              <Text size="xs" c="dimmed" ml="auto">
                {formatDateTime(event.occurredAt)}
              </Text>
            </Group>
            {event.body && (
              <Text size="sm" c="dimmed" mt={4}>
                {event.body}
              </Text>
            )}
          </Card>
        ))}
      </Stack>

      {events.some((event) => event.dedupeKey) && (
        <Text size="xs" c="dimmed" mt="md">
          Les anomalies portant une clé de déduplication sont republiées à chaque cycle : c'est
          ainsi que le noyau sait qu'elles durent. Le regroupement en une seule notification
          arrivera avec le moteur d'anomalies.
        </Text>
      )}
    </>
  )
}
