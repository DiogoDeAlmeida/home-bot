import { useState } from 'react'
import { useJournal } from '@/api/hooks'
import type { HubEventSeverity } from '@/api/types'
import { PageTitle } from '@/components/Layout'
import { Badge, Button, Card, Spinner } from '@/components/ui/primitives'
import { formatDateTime } from '@/lib/utils'

const SEVERITY: Record<HubEventSeverity, { label: string; tone: 'neutral' | 'warn' | 'bad' }> = {
  0: { label: 'info', tone: 'neutral' },
  1: { label: 'anomalie', tone: 'warn' },
  2: { label: 'critique', tone: 'bad' },
}

export function JournalPage() {
  const journal = useJournal()
  const [minimum, setMinimum] = useState<HubEventSeverity>(0)

  const events = journal.data?.filter((event) => event.severity >= minimum) ?? []

  return (
    <>
      <PageTitle
        title="Journal"
        subtitle="Derniers événements publiés par les modules. Diagnostiquer sans ouvrir de session SSH."
      />

      <div className="mb-4 flex gap-2">
        {([0, 1, 2] as HubEventSeverity[]).map((level) => (
          <Button
            key={level}
            size="sm"
            variant={minimum === level ? 'primary' : 'secondary'}
            onClick={() => setMinimum(level)}
          >
            {level === 0 ? 'Tout' : SEVERITY[level].label}
          </Button>
        ))}
      </div>

      {journal.isLoading && <Spinner />}

      {!journal.isLoading && events.length === 0 && (
        <Card>
          <p className="text-sm text-ink-muted">Aucun événement à ce niveau.</p>
        </Card>
      )}

      <div className="space-y-2">
        {events.map((event, index) => (
          <Card key={`${event.dedupeKey ?? event.type}-${event.occurredAt}-${index}`} className="py-3">
            <div className="flex flex-wrap items-baseline gap-2">
              <Badge tone={SEVERITY[event.severity].tone}>{SEVERITY[event.severity].label}</Badge>
              <span className="font-medium">{event.title}</span>
              <code className="font-mono text-xs text-ink-muted">{event.type}</code>
              <span className="ml-auto text-xs text-ink-muted">
                {formatDateTime(event.occurredAt)}
              </span>
            </div>
            {event.body && <p className="mt-1 text-sm text-ink-muted">{event.body}</p>}
          </Card>
        ))}
      </div>

      {events.some((event) => event.dedupeKey) && (
        <p className="mt-4 text-xs text-ink-muted">
          Les anomalies portant une clé de déduplication sont republiées à chaque cycle : c'est
          ainsi que le noyau sait qu'elles durent. Le regroupement en une seule notification
          arrivera avec le moteur d'anomalies.
        </p>
      )}
    </>
  )
}
