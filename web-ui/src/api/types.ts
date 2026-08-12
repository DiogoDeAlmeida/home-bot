/**
 * Miroir TypeScript des contrats servis par le Host.
 *
 * `ConfigField` est le type central : c'est lui que le générateur de formulaire consomme, et
 * il est identique qu'il décrive un module ou les réglages du hub (ADR-0013).
 */

export type ConfigFieldKind =
  | 'Text'
  | 'Url'
  | 'Secret'
  | 'Boolean'
  | 'Integer'
  | 'Duration'
  | 'Select'
  | 'MultiSelect'

export interface ConfigOption {
  value: string
  label: string
}

export interface ConfigField {
  key: string
  label: string
  kind: ConfigFieldKind
  required: boolean
  secret: boolean
  help: string | null
  defaultValue: string | null
  options: ConfigOption[] | null
  /**
   * Options résolues à l'exécution. Présent dans le contrat, **non résolu en v1**
   * (ADR-0011) : le formulaire rend une saisie libre et le signale.
   */
  optionsFrom: string | null
  dependsOn: string[] | null
  /** Valeur courante. Un secret arrive masqué (`••••••1234`), jamais en clair. */
  value: string | null
}

export interface ConfigSurface {
  key: string
  fields: ConfigField[]
}

export interface ModuleSummary {
  key: string
  displayName: string
  description: string
  enabled: boolean
  configurationComplete: boolean
  isActive: boolean
  blockedReason: string | null
  capabilities: number
  pollers: number
  webhooks: string[]
}

export type HealthState = 0 | 1 | 2 | 3 | 4

export interface ServiceHealth {
  name: string
  state: HealthState
  message: string | null
  latency: string | null
}

export interface ModuleHealth {
  state: HealthState
  message: string | null
  services: ServiceHealth[]
  checkedAt: string
}

export interface CapabilitySummary {
  moduleKey: string
  key: string
  displayName: string
  description: string
  kind: 'Query' | 'Mutation'
  exposure: string
  requireConfirmation: boolean
  command: string | null
  parameters: {
    name: string
    description: string
    type: string
    required: boolean
    defaultValue: unknown
    choices: string[] | null
  }[]
}

export interface CapabilityResult {
  outcome: 0 | 1 | 2
  message: string | null
  payload: unknown
}

export interface BackupArchive {
  fileName: string
  sizeBytes: number
  createdAt: string
  entryCount: number
}

export type HubEventSeverity = 0 | 1 | 2

export interface HubEvent {
  moduleKey: string
  type: string
  severity: HubEventSeverity
  title: string
  body: string | null
  dedupeKey: string | null
  data: Record<string, string> | null
  occurredAt: string
}

export type AnomalyStateName = 'Open' | 'Snoozed' | 'Resolved'

/**
 * Une condition qui s'ouvre, dure et se résout. Le noyau n'en notifie que les transitions :
 * un import bloqué depuis dix heures est une ligne, pas six cents.
 */
export interface Anomaly {
  dedupeKey: string
  moduleKey: string
  type: string
  severity: HubEventSeverity
  title: string
  body: string | null
  state: AnomalyStateName
  openedAt: string
  lastSeenAt: string
  resolvedAt: string | null
  snoozedUntil: string | null
  occurrences: number
  durationSeconds: number
  data: Record<string, string> | null
}

export interface VolumeUsage {
  label: string
  path: string
  totalBytes: number
  freeBytes: number
  freePercent: number
  usedBytes: number
}

export type JourneyState = 0 | 1 | 2 | 3 | 4 | 5

export interface JourneySummary {
  key: string
  title: string | null
  mediaType: 0 | 1
  state: JourneyState
  needsAttention: boolean
  progress: number
  downloadSpeed: number
  bytesRemaining: number
  /** TimeSpan .NET sérialisé, ou null quand qBittorrent ne sait pas. */
  estimatedTimeLeft: string | null
  downloadCount: number
  episodeCount: number
  requestedAt: string | null
}

/**
 * Déjà trié et borné par le module : le palmarès et le résumé sont décidés côté serveur, pas
 * ici. C'est ce qui garantit que cette page et le message d'un salon montrent la même chose.
 */
export interface MediaOverview {
  top: JourneySummary[]
  totalJourneys: number
  downloading: number
  importing: number
  needsAttention: number
  downloadSpeed: number
  bytesRemaining: number
  bytesTotal: number
  observedAt: string | null
  unavailableSources: string[]
}

export interface SystemSnapshot {
  version: string
  startedAt: string
  uptime: string
  volumes: VolumeUsage[]
  observedAt: string | null
  /** Seuils configurés, portés par le snapshot pour que l'affichage suive le réglage réel. */
  warnBelowPercent: number
  criticalBelowPercent: number
}
