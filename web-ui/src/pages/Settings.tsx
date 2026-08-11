import {
  keys,
  useBackups,
  useCapabilities,
  useConfigSurface,
  useRunCapability,
  useSaveConfig,
} from '@/api/hooks'
import { PageTitle } from '@/components/Layout'
import { SchemaForm } from '@/components/SchemaForm'
import { Alert, Badge, Button, Card, Spinner } from '@/components/ui/primitives'
import { formatBytes, formatDateTime } from '@/lib/utils'

/**
 * Réglages du hub et sauvegardes.
 *
 * Le formulaire est **le même composant** que celui de la page Modules : le noyau décrit ses
 * réglages avec la primitive des modules, sous le préfixe réservé `hub.` (ADR-0013). Aux yeux
 * de l'interface, c'est un pseudo-module ; dans le contrat, il n'en est pas un.
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

      <div className="space-y-4">
        <Card>
          {surface.isLoading && <Spinner />}
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
      </div>
    </>
  )
}

function BackupsCard() {
  const backups = useBackups()
  const capabilities = useCapabilities()
  const run = useRunCapability()

  const createBackup = capabilities.data?.find((c) => c.key === 'system.backup.create')

  return (
    <Card>
      <div className="mb-4 flex flex-wrap items-center justify-between gap-3">
        <div>
          <h2 className="font-medium">Sauvegardes</h2>
          <p className="mt-1 text-sm text-ink-muted">
            Une archive unique par sauvegarde : base, keyring et configuration. Restaurer la base
            sans son keyring rendrait tous les secrets illisibles.
          </p>
        </div>
        {createBackup && (
          <Button
            onClick={() => run.mutate({ key: createBackup.key })}
            disabled={run.isPending}
          >
            {run.isPending ? 'Sauvegarde…' : 'Sauvegarder maintenant'}
          </Button>
        )}
      </div>

      {run.data && (
        <div className="mb-4">
          <Alert tone={run.data.outcome === 0 ? 'ok' : 'warn'}>{run.data.message}</Alert>
        </div>
      )}
      {run.isError && (
        <div className="mb-4">
          <Alert tone="bad">{(run.error as Error).message}</Alert>
        </div>
      )}

      {backups.isLoading && <Spinner />}
      {backups.data?.length === 0 && (
        <Alert tone="warn">Aucune archive pour l'instant.</Alert>
      )}

      {backups.data && backups.data.length > 0 && (
        <ul className="divide-y divide-border-subtle text-sm">
          {backups.data.map((archive) => (
            <li key={archive.fileName} className="flex flex-wrap items-baseline gap-3 py-2">
              <code className="font-mono text-xs">{archive.fileName}</code>
              <span className="text-ink-muted">{formatDateTime(archive.createdAt)}</span>
              <Badge>{formatBytes(archive.sizeBytes)}</Badge>
            </li>
          ))}
        </ul>
      )}
    </Card>
  )
}
