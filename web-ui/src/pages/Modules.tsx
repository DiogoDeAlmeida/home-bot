import { useState } from 'react'
import {
  keys,
  useConfigSurface,
  useModuleHealth,
  useModules,
  useSaveConfig,
  useSetModuleEnabled,
} from '@/api/hooks'
import type { HealthState, ModuleSummary } from '@/api/types'
import { PageTitle } from '@/components/Layout'
import { SchemaForm } from '@/components/SchemaForm'
import { Alert, Badge, Button, Card, Spinner, Toggle } from '@/components/ui/primitives'

const HEALTH: Record<HealthState, { label: string; tone: 'neutral' | 'ok' | 'warn' | 'bad' }> = {
  0: { label: 'inconnu', tone: 'neutral' },
  1: { label: 'opérationnel', tone: 'ok' },
  2: { label: 'dégradé', tone: 'warn' },
  3: { label: 'en panne', tone: 'bad' },
  4: { label: 'désactivé', tone: 'neutral' },
}

export function ModulesPage() {
  const modules = useModules()

  if (modules.isLoading) return <Spinner />

  return (
    <>
      <PageTitle
        title="Modules"
        subtitle="Activation, santé et configuration. Le formulaire est généré depuis le schéma déclaré par chaque module."
      />
      <div className="space-y-4">
        {modules.data?.map((module) => <ModuleCard key={module.key} module={module} />)}
      </div>
    </>
  )
}

function ModuleCard({ module }: { module: ModuleSummary }) {
  const [open, setOpen] = useState(false)
  const setEnabled = useSetModuleEnabled(module.key)
  const health = useModuleHealth(module.key, module.isActive)

  const status = health.data ? HEALTH[health.data.state] : HEALTH[0]

  return (
    <Card>
      <div className="flex flex-wrap items-start justify-between gap-4">
        <div className="min-w-0 flex-1">
          <div className="flex flex-wrap items-center gap-2">
            <h2 className="font-medium">{module.displayName}</h2>
            <code className="font-mono text-xs text-ink-muted">/{module.key}</code>
            <Badge tone={module.isActive ? status.tone : 'neutral'}>
              {module.isActive ? status.label : 'inactif'}
            </Badge>
          </div>
          <p className="mt-1 text-sm text-ink-muted">{module.description}</p>
          <p className="mt-2 text-xs text-ink-muted">
            {module.capabilities} capacité{module.capabilities > 1 ? 's' : ''} · {module.pollers}{' '}
            poller{module.pollers > 1 ? 's' : ''}
            {module.webhooks.length > 0 && ` · webhooks : ${module.webhooks.join(', ')}`}
          </p>
        </div>

        <div className="flex items-center gap-3">
          <Toggle
            label={`Activer ${module.displayName}`}
            checked={module.enabled}
            disabled={setEnabled.isPending}
            onCheckedChange={(value) => setEnabled.mutate(value)}
          />
          <Button variant="secondary" size="sm" onClick={() => setOpen((value) => !value)}>
            {open ? 'Fermer' : 'Configurer'}
          </Button>
        </div>
      </div>

      {module.blockedReason && (
        <div className="mt-3">
          <Alert tone="warn">{module.blockedReason}</Alert>
        </div>
      )}

      {module.isActive && health.data && health.data.services.length > 0 && (
        <ul className="mt-3 space-y-1 text-xs text-ink-muted">
          {health.data.services.map((service) => (
            <li key={service.name} className="flex items-baseline justify-between gap-3">
              <span>
                <Badge tone={HEALTH[service.state].tone}>{HEALTH[service.state].label}</Badge>{' '}
                {service.name}
              </span>
              <span>{service.message}</span>
            </li>
          ))}
        </ul>
      )}

      {open && (
        <div className="mt-5 border-t border-border-subtle pt-5">
          <ModuleConfig moduleKey={module.key} />
        </div>
      )}
    </Card>
  )
}

function ModuleConfig({ moduleKey }: { moduleKey: string }) {
  const path = `/api/modules/${moduleKey}/config`
  const queryKey = keys.moduleConfig(moduleKey)
  const surface = useConfigSurface(path, queryKey)
  const save = useSaveConfig(path, queryKey)

  if (surface.isLoading) return <Spinner />
  if (!surface.data) return <Alert tone="bad">Configuration illisible.</Alert>
  if (surface.data.fields.length === 0) {
    return <p className="text-sm text-ink-muted">Ce module n'expose aucun réglage.</p>
  }

  return (
    <SchemaForm
      surface={surface.data}
      onSave={(values) => save.mutate(values)}
      saving={save.isPending}
      saved={save.isSuccess}
      error={save.isError ? (save.error as Error).message : undefined}
    />
  )
}
