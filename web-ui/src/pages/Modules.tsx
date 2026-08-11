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
import {
  Alert,
  Badge,
  Button,
  Card,
  Code,
  Divider,
  Group,
  Loader,
  Stack,
  Switch,
  Text,
} from '@/components/ui'

const HEALTH: Record<HealthState, { label: string; color: string }> = {
  0: { label: 'inconnu', color: 'gray' },
  1: { label: 'opérationnel', color: 'green' },
  2: { label: 'dégradé', color: 'yellow' },
  3: { label: 'en panne', color: 'red' },
  4: { label: 'désactivé', color: 'gray' },
}

export function ModulesPage() {
  const modules = useModules()

  if (modules.isLoading) return <Loader />

  return (
    <>
      <PageTitle
        title="Modules"
        subtitle="Activation, santé et configuration. Le formulaire est généré depuis le schéma déclaré par chaque module."
      />
      <Stack gap="md">
        {modules.data?.map((module) => <ModuleCard key={module.key} module={module} />)}
      </Stack>
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
      <Group justify="space-between" align="flex-start" wrap="wrap" gap="md">
        <Stack gap={4} style={{ flex: 1, minWidth: 240 }}>
          <Group gap="xs" wrap="wrap">
            <Text fw={500}>{module.displayName}</Text>
            <Code>{module.key}</Code>
            <Badge variant="light" color={module.isActive ? status.color : 'gray'}>
              {module.isActive ? status.label : 'inactif'}
            </Badge>
          </Group>
          <Text size="sm" c="dimmed">
            {module.description}
          </Text>
          <Text size="xs" c="dimmed">
            {module.capabilities} capacité{module.capabilities > 1 ? 's' : ''} · {module.pollers}{' '}
            poller{module.pollers > 1 ? 's' : ''}
            {module.webhooks.length > 0 && ` · webhooks : ${module.webhooks.join(', ')}`}
          </Text>
        </Stack>

        <Group gap="sm">
          <Switch
            aria-label={`Activer ${module.displayName}`}
            checked={module.enabled}
            disabled={setEnabled.isPending}
            onChange={(event) => setEnabled.mutate(event.currentTarget.checked)}
          />
          <Button variant="default" size="compact-sm" onClick={() => setOpen((value) => !value)}>
            {open ? 'Fermer' : 'Configurer'}
          </Button>
        </Group>
      </Group>

      {module.blockedReason && (
        <Alert color="yellow" variant="light" mt="sm">
          {module.blockedReason}
        </Alert>
      )}

      {module.isActive && health.data && health.data.services.length > 0 && (
        <Stack gap={4} mt="sm">
          {health.data.services.map((service) => (
            <Group key={service.name} justify="space-between" gap="md">
              <Group gap="xs">
                <Badge size="xs" variant="light" color={HEALTH[service.state].color}>
                  {HEALTH[service.state].label}
                </Badge>
                <Text size="xs">{service.name}</Text>
              </Group>
              <Text size="xs" c="dimmed">
                {service.message}
              </Text>
            </Group>
          ))}
        </Stack>
      )}

      {open && (
        <>
          <Divider my="lg" />
          <ModuleConfig moduleKey={module.key} />
        </>
      )}
    </Card>
  )
}

function ModuleConfig({ moduleKey }: { moduleKey: string }) {
  const path = `/api/modules/${moduleKey}/config`
  const queryKey = keys.moduleConfig(moduleKey)
  const surface = useConfigSurface(path, queryKey)
  const save = useSaveConfig(path, queryKey)

  if (surface.isLoading) return <Loader size="sm" />
  if (!surface.data) {
    return (
      <Alert color="red" variant="light">
        Configuration illisible.
      </Alert>
    )
  }
  if (surface.data.fields.length === 0) {
    return (
      <Text size="sm" c="dimmed">
        Ce module n'expose aucun réglage.
      </Text>
    )
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
