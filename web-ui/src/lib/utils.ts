import { clsx, type ClassValue } from 'clsx'
import { twMerge } from 'tailwind-merge'

/** Fusionne des classes Tailwind en résolvant les conflits. Convention shadcn/ui. */
export function cn(...inputs: ClassValue[]) {
  return twMerge(clsx(inputs))
}

const UNITS = ['o', 'Ko', 'Mo', 'Go', 'To']

export function formatBytes(bytes: number): string {
  let value = bytes
  let unit = 0
  while (value >= 1024 && unit < UNITS.length - 1) {
    value /= 1024
    unit++
  }
  return `${value.toLocaleString('fr-FR', { maximumFractionDigits: 1 })} ${UNITS[unit]}`
}

/** « 3 j 04 h 12 min » à partir d'un TimeSpan .NET sérialisé (« 1.04:12:33.4 »). */
export function formatUptime(timespan: string): string {
  const match = /^(?:(\d+)\.)?(\d{2}):(\d{2}):(\d{2})/.exec(timespan)
  if (!match) return timespan

  const [, days, hours, minutes] = match
  const parts: string[] = []
  if (days) parts.push(`${Number(days)} j`)
  if (days || Number(hours) > 0) parts.push(`${Number(hours)} h`)
  parts.push(`${Number(minutes)} min`)
  return parts.join(' ')
}

export function formatDateTime(iso: string): string {
  return new Date(iso).toLocaleString('fr-FR', { dateStyle: 'short', timeStyle: 'medium' })
}
