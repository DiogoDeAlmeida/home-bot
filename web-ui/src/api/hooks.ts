import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from './client'
import type {
  Anomaly,
  BackupArchive,
  CapabilityResult,
  CapabilitySummary,
  ConfigSurface,
  HubEvent,
  ModuleHealth,
  ModuleSummary,
} from './types'

export const keys = {
  setup: ['setup'] as const,
  me: ['me'] as const,
  modules: ['modules'] as const,
  moduleConfig: (key: string) => ['modules', key, 'config'] as const,
  moduleHealth: (key: string) => ['modules', key, 'health'] as const,
  settings: ['settings'] as const,
  capabilities: ['capabilities'] as const,
  backups: ['backups'] as const,
  journal: ['journal'] as const,
  anomalies: ['anomalies'] as const,
}

// ── Installation et session ──────────────────────────────────────────────────────

export const useSetupState = () =>
  useQuery({
    queryKey: keys.setup,
    queryFn: () => api.get<{ configured: boolean; minimumPasswordLength: number }>('/api/setup'),
    retry: false,
  })

export const useSession = () =>
  useQuery({
    queryKey: keys.me,
    queryFn: () => api.get<{ authenticated: boolean; name: string | null }>('/api/auth/me'),
    retry: false,
  })

export function useCompleteSetup() {
  const client = useQueryClient()
  return useMutation({
    mutationFn: (password: string) => api.post<{ configured: boolean }>('/api/setup', { password }),
    onSuccess: () => client.invalidateQueries(),
  })
}

export function useLogin() {
  const client = useQueryClient()
  return useMutation({
    mutationFn: (password: string) => api.post('/api/auth/login', { password }),
    onSuccess: () => client.invalidateQueries(),
  })
}

export function useLogout() {
  const client = useQueryClient()
  return useMutation({
    mutationFn: () => api.post('/api/auth/logout'),
    onSuccess: () => client.invalidateQueries(),
  })
}

// ── Modules ─────────────────────────────────────────────────────────────────────

export const useModules = () =>
  useQuery({ queryKey: keys.modules, queryFn: () => api.get<ModuleSummary[]>('/api/modules') })

export const useModuleHealth = (key: string, enabled = true) =>
  useQuery({
    queryKey: keys.moduleHealth(key),
    queryFn: () => api.get<ModuleHealth>(`/api/modules/${key}/health`),
    enabled,
    refetchInterval: 30_000,
  })

export function useSetModuleEnabled(key: string) {
  const client = useQueryClient()
  return useMutation({
    mutationFn: (enabled: boolean) => api.post(`/api/modules/${key}/enabled`, { enabled }),
    onSuccess: () => {
      void client.invalidateQueries({ queryKey: keys.modules })
      void client.invalidateQueries({ queryKey: keys.moduleHealth(key) })
    },
  })
}

// ── Configuration : modules et hub partagent la même forme (ADR-0013) ───────────

export const useConfigSurface = (path: string, queryKey: readonly unknown[]) =>
  useQuery({ queryKey, queryFn: () => api.get<ConfigSurface>(path) })

export function useSaveConfig(path: string, queryKey: readonly unknown[]) {
  const client = useQueryClient()
  return useMutation({
    mutationFn: (values: Record<string, string | null>) => api.put<void>(path, values),
    onSuccess: () => {
      void client.invalidateQueries({ queryKey })
      void client.invalidateQueries({ queryKey: keys.modules })
    },
  })
}

// ── Capacités, sauvegardes, journal ─────────────────────────────────────────────

export const useCapabilities = () =>
  useQuery({ queryKey: keys.capabilities, queryFn: () => api.get<CapabilitySummary[]>('/api/capabilities') })

export function useRunCapability() {
  const client = useQueryClient()
  return useMutation({
    mutationFn: ({ key, args }: { key: string; args?: Record<string, unknown> }) =>
      api.post<CapabilityResult>(`/api/capabilities/${key}`, args ?? {}),
    onSuccess: () => {
      // Une capacité peut avoir modifié n'importe quoi : on réinvalide largement plutôt que
      // d'entretenir une carte des dépendances qui finirait fausse.
      void client.invalidateQueries()
    },
  })
}

export const useBackups = () =>
  useQuery({ queryKey: keys.backups, queryFn: () => api.get<BackupArchive[]>('/api/backups') })

export const useAnomalies = (includeResolved = false) =>
  useQuery({
    queryKey: [...keys.anomalies, includeResolved],
    queryFn: () => api.get<Anomaly[]>(`/api/anomalies${includeResolved ? '?all=true' : ''}`),
    refetchInterval: 15_000,
  })

// La mise en sommeil est la capacité noyau `hub.anomaly.snooze` (pas de module propriétaire :
// c'est AnomalyEngine qui tient la table), exécutée par le même chemin que toute autre mutation
// plutôt que par un endpoint dédié — même autorisation, même journal d'audit, et la réinvalidation
// large de useRunCapability couvre déjà la liste des anomalies.
export function useSnoozeAnomaly() {
  const run = useRunCapability()
  return {
    ...run,
    mutate: (
      { key, hours }: { key: string; hours?: number },
      options?: Parameters<typeof run.mutate>[1],
    ) => run.mutate({ key: 'hub.anomaly.snooze', args: { key, hours: hours ?? null } }, options),
  }
}

export const useJournal = () =>
  useQuery({
    queryKey: keys.journal,
    queryFn: () => api.get<HubEvent[]>('/api/journal?count=100'),
    refetchInterval: 15_000,
  })
