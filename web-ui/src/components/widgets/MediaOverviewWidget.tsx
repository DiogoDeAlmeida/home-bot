import { Alert, Badge, Group, Progress, Stack, Text, Tooltip } from '@/components/ui'
import type { JourneyState, JourneySummary, MediaOverview } from '@/api/types'
import { formatBytes, formatDuration, formatSpeed } from '@/lib/utils'

const STATE: Record<JourneyState, { label: string; color: string }> = {
  0: { label: 'demandé', color: 'gray' },
  1: { label: 'téléchargement', color: 'blue' },
  2: { label: 'import', color: 'violet' },
  3: { label: 'disponible', color: 'green' },
  4: { label: 'échec', color: 'red' },
  5: { label: 'indéterminé', color: 'orange' },
}

/**
 * Rendu web du widget média.
 *
 * **Premier vrai test d'ADR-0006.** Le module fournit des données brutes déjà triées et
 * bornées ; c'est ici, et seulement ici, qu'on décide de leur apparence. L'adaptateur d'un
 * canal conversationnel rendra les mêmes données autrement — en embed, avec ses contraintes de
 * longueur — et cette duplication est assumée.
 *
 * Le repli générique clé/valeur reste en place pour les widgets inconnus : c'est un filet, pas
 * une sortie finale.
 */
export function MediaOverviewWidget({ data }: { data: MediaOverview }) {
  if (!data.observedAt) {
    return (
      <Text size="sm" c="dimmed">
        Première observation en attente.
      </Text>
    )
  }

  return (
    <Stack gap="sm">
      {data.unavailableSources.length > 0 && (
        <Alert color="red" variant="light" p="xs">
          <Stack gap={2}>
            {data.unavailableSources.map((source) => (
              <Text key={source} size="xs">
                {source}
              </Text>
            ))}
          </Stack>
        </Alert>
      )}

      <Group gap="lg" wrap="wrap">
        <Stat label="En cours" value={data.downloading} />
        <Stat label="Import" value={data.importing} />
        <Stat
          label="À voir"
          value={data.needsAttention}
          color={data.needsAttention > 0 ? 'orange' : undefined}
        />
        <Stat label="Débit" value={formatSpeed(data.downloadSpeed)} />
      </Group>

      {data.bytesTotal > 0 && (
        <Text size="xs" c="dimmed">
          {formatBytes(data.bytesRemaining)} restants sur {formatBytes(data.bytesTotal)}
        </Text>
      )}

      {data.top.length === 0 ? (
        <Text size="sm" c="dimmed">
          Rien en cours. {data.totalJourneys} média{data.totalJourneys > 1 ? 's' : ''} suivi
          {data.totalJourneys > 1 ? 's' : ''}.
        </Text>
      ) : (
        <Stack gap="sm">
          {data.top.map((journey) => (
            <JourneyRow key={journey.key} journey={journey} />
          ))}
        </Stack>
      )}
    </Stack>
  )
}

function JourneyRow({ journey }: { journey: JourneySummary }) {
  const state = STATE[journey.state]

  return (
    <Stack gap={4}>
      <Group justify="space-between" gap="xs" wrap="nowrap" align="baseline">
        <Tooltip label={journey.title ?? journey.key} openDelay={400} multiline maw={420}>
          <Text size="sm" fw={500} lineClamp={1} style={{ flex: 1, minWidth: 0 }}>
            {journey.title ?? journey.key}
          </Text>
        </Tooltip>
        <Badge
          size="xs"
          variant="light"
          color={journey.needsAttention ? 'orange' : state.color}
        >
          {journey.needsAttention ? 'à voir' : state.label}
        </Badge>
      </Group>

      <Progress
        value={journey.progress * 100}
        color={journey.needsAttention ? 'orange' : state.color}
        size="sm"
      />

      <Group justify="space-between" gap="xs">
        <Text size="xs" c="dimmed">
          {(journey.progress * 100).toLocaleString('fr-FR', { maximumFractionDigits: 1 })} %
          {journey.episodeCount > 0 && ` · ${journey.episodeCount} épisodes`}
          {journey.downloadCount > 1 && ` · ${journey.downloadCount} torrents`}
        </Text>
        <Text size="xs" c="dimmed">
          {formatSpeed(journey.downloadSpeed)}
          {journey.estimatedTimeLeft && ` · ${formatDuration(journey.estimatedTimeLeft)}`}
        </Text>
      </Group>
    </Stack>
  )
}

function Stat({
  label,
  value,
  color,
}: {
  label: string
  value: string | number
  color?: string
}) {
  return (
    <Stack gap={0}>
      <Text size="xs" c="dimmed">
        {label}
      </Text>
      <Text size="lg" fw={600} c={color}>
        {value}
      </Text>
    </Stack>
  )
}
