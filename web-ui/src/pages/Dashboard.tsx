import { useQuery } from '@tanstack/react-query'
import { api } from '@/api/client'
import { useBackups, useModules } from '@/api/hooks'
import type { SystemSnapshot } from '@/api/types'
import { PageTitle } from '@/components/Layout'
import { Alert, Badge, Card, Spinner } from '@/components/ui/primitives'
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
 * Miroir web de ce que le bot affichera dans Discord.
 *
 * Les widgets sont des **données pures** : c'est ici qu'on décide de leur rendu, et l'adaptateur
 * Discord décidera du sien de son côté. Il n'y a pas de modèle de présentation partagé
 * (ADR-0006) — c'est une duplication assumée, moins coûteuse que l'abstraction qu'elle évite.
 */
export function DashboardPage() {
  const widgets = useQuery({
    queryKey: ['widgets'],
    queryFn: () => api.get<Widget[]>('/api/widgets'),
    refetchInterval: 15_000,
  })
  const modules = useModules()
  const backups = useBackups()

  if (widgets.isLoading) return <Spinner />

  const lastBackup = backups.data?.[0]

  return (
    <>
      <PageTitle
        title="Tableau de bord"
        subtitle="Mise à jour automatique toutes les 15 secondes."
      />

      <div className="grid gap-4 sm:grid-cols-2">
        {widgets.data?.map((widget) => (
          <Card key={widget.key}>
            <div className="mb-3 flex items-baseline justify-between gap-2">
              <h2 className="font-medium">{widget.title}</h2>
              <span className="text-xs text-ink-muted">{widget.moduleKey}</span>
            </div>
            {widget.key === 'system.overview' ? (
              <SystemOverview snapshot={widget.data as SystemSnapshot} />
            ) : (
              <GenericWidget data={widget.data} />
            )}
          </Card>
        ))}

        <Card>
          <h2 className="mb-3 font-medium">Sauvegarde</h2>
          {lastBackup ? (
            <dl className="space-y-2 text-sm">
              <Row label="Dernière">{formatDateTime(lastBackup.createdAt)}</Row>
              <Row label="Taille">{formatBytes(lastBackup.sizeBytes)}</Row>
              <Row label="Fichiers">{lastBackup.entryCount}</Row>
              <Row label="Archives">{backups.data?.length}</Row>
            </dl>
          ) : (
            <Alert tone="warn">
              Aucune sauvegarde. Le hub concentre toutes les clés d'API du homelab — en créer une
              depuis les paramètres.
            </Alert>
          )}
        </Card>
      </div>

      <h2 className="mt-8 mb-3 text-sm font-medium text-ink-muted">Modules</h2>
      <div className="grid gap-3 sm:grid-cols-2">
        {modules.data?.map((module) => (
          <Card key={module.key} className="flex items-center justify-between gap-3 py-3">
            <div>
              <p className="font-medium">{module.displayName}</p>
              <p className="text-xs text-ink-muted">{module.blockedReason ?? module.description}</p>
            </div>
            <Badge tone={module.isActive ? 'ok' : 'neutral'}>
              {module.isActive ? 'actif' : 'inactif'}
            </Badge>
          </Card>
        ))}
      </div>
    </>
  )
}

function SystemOverview({ snapshot }: { snapshot: SystemSnapshot }) {
  if (!snapshot.observedAt) {
    return <p className="text-sm text-ink-muted">Première observation en attente.</p>
  }

  return (
    <dl className="space-y-2 text-sm">
      <Row label="Version">
        <code className="font-mono text-xs">{snapshot.version.split('+')[0]}</code>
      </Row>
      <Row label="En service depuis">{formatUptime(snapshot.uptime)}</Row>

      {snapshot.volumes.map((volume) => (
        <div key={volume.path} className="pt-2">
          <div className="flex justify-between text-xs text-ink-muted">
            <span>{volume.label}</span>
            <span>
              {formatBytes(volume.freeBytes)} libres · {volume.freePercent} %
            </span>
          </div>
          <div className="mt-1 h-1.5 overflow-hidden rounded-full bg-surface-muted">
            <div
              className={
                volume.freePercent < 10 ? 'h-full bg-red-500' : 'h-full bg-accent'
              }
              style={{ width: `${100 - volume.freePercent}%` }}
            />
          </div>
        </div>
      ))}
    </dl>
  )
}

/** Repli pour tout widget dont le front ne connaît pas encore la forme. */
function GenericWidget({ data }: { data: unknown }) {
  return (
    <pre className="overflow-x-auto rounded bg-surface-muted p-3 text-xs text-ink-muted">
      {JSON.stringify(data, null, 2)}
    </pre>
  )
}

function Row({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div className="flex items-baseline justify-between gap-3">
      <dt className="text-ink-muted">{label}</dt>
      <dd className="font-medium">{children}</dd>
    </div>
  )
}
