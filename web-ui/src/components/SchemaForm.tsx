import { useMemo, useState, type FormEvent } from 'react'
import type { ConfigField, ConfigSurface } from '@/api/types'
import { Alert, Badge, Button, Input, Label, Select, Toggle } from '@/components/ui/primitives'
import { cn } from '@/lib/utils'

/**
 * Génère un formulaire à partir d'un schéma servi par le serveur.
 *
 * **C'est la pièce qui rend le système extensible.** Ajouter un module ne doit demander aucune
 * ligne de TypeScript : le serveur décrit ses champs, ce composant les rend. Il sert
 * indifféremment la configuration d'un module et les réglages du hub — même contrat, même code
 * (ADR-0013).
 *
 * Deux règles gouvernent la soumission :
 *
 * 1. **Seuls les champs modifiés partent.** Un secret est renvoyé masqué par l'API ; le
 *    réémettre tel quel écraserait la vraie valeur par des points de suspension. Le serveur s'en
 *    protège aussi, mais un client qui envoie ce qu'il ne devrait pas est un bug à part entière.
 * 2. **Un champ vidé part explicitement à `null`**, ce qui supprime la clé côté serveur.
 */
export function SchemaForm({
  surface,
  onSave,
  saving,
  error,
  saved,
}: {
  surface: ConfigSurface
  onSave: (values: Record<string, string | null>) => void
  saving: boolean
  error?: string
  saved?: boolean
}) {
  const initial = useMemo(() => {
    const map: Record<string, string> = {}
    for (const field of surface.fields) {
      // Un secret n'est jamais préchargé : le champ reste vide, le masque sert d'indice.
      map[field.key] = field.secret ? '' : (field.value ?? field.defaultValue ?? '')
    }
    return map
  }, [surface])

  const [values, setValues] = useState<Record<string, string>>(initial)
  const [dirty, setDirty] = useState<Set<string>>(new Set())

  const update = (key: string, value: string) => {
    setValues((current) => ({ ...current, [key]: value }))
    setDirty((current) => new Set(current).add(key))
  }

  const missingRequired = surface.fields.filter(
    (field) =>
      field.required &&
      !values[field.key]?.trim() &&
      // Un secret déjà enregistré et non modifié reste satisfait.
      !(field.secret && field.value),
  )

  const submit = (event: FormEvent) => {
    event.preventDefault()
    if (missingRequired.length > 0) return

    const payload: Record<string, string | null> = {}
    for (const key of dirty) {
      const value = values[key]?.trim() ?? ''
      payload[key] = value === '' ? null : value
    }
    onSave(payload)
    setDirty(new Set())
  }

  return (
    <form onSubmit={submit} className="space-y-5">
      {surface.fields.map((field) => (
        <Field
          key={field.key}
          field={field}
          value={values[field.key] ?? ''}
          blockedBy={unmetDependencies(field, values)}
          onChange={(value) => update(field.key, value)}
        />
      ))}

      {missingRequired.length > 0 && (
        <Alert tone="warn">
          Champs obligatoires à renseigner : {missingRequired.map((f) => f.label).join(', ')}.
        </Alert>
      )}

      {error && <Alert tone="bad">{error}</Alert>}
      {saved && dirty.size === 0 && !error && <Alert tone="ok">Configuration enregistrée.</Alert>}

      <div className="flex items-center gap-3">
        <Button type="submit" disabled={saving || dirty.size === 0 || missingRequired.length > 0}>
          {saving ? 'Enregistrement…' : 'Enregistrer'}
        </Button>
        {dirty.size > 0 && (
          <span className="text-xs text-ink-muted">
            {dirty.size} champ{dirty.size > 1 ? 's' : ''} modifié{dirty.size > 1 ? 's' : ''}
          </span>
        )}
      </div>
    </form>
  )
}

/** Champs dont la valeur conditionne la résolution de celui-ci (`dependsOn`). */
function unmetDependencies(field: ConfigField, values: Record<string, string>): string[] {
  if (!field.dependsOn) return []
  return field.dependsOn.filter((key) => !values[key]?.trim())
}

function Field({
  field,
  value,
  blockedBy,
  onChange,
}: {
  field: ConfigField
  value: string
  blockedBy: string[]
  onChange: (value: string) => void
}) {
  const id = `field-${field.key}`

  return (
    <div className="space-y-1.5">
      <div className="flex items-center gap-2">
        <Label htmlFor={id}>{field.label}</Label>
        {field.required && <Badge tone="warn">obligatoire</Badge>}
        {field.secret && <Badge tone="neutral">secret</Badge>}
      </div>

      <Control field={field} id={id} value={value} blockedBy={blockedBy} onChange={onChange} />

      {field.help && <p className="text-xs text-ink-muted">{field.help}</p>}

      {field.secret && field.value && (
        <p className="text-xs text-ink-muted">
          Valeur enregistrée : <code className="font-mono">{field.value}</code>. Laisser vide pour
          la conserver.
        </p>
      )}

      {/* ADR-0011 : le contrat porte OptionsFrom, le front ne le résout pas encore. Le dire
          explicitement vaut mieux qu'un champ texte inexpliqué là où on attend une liste. */}
      {field.optionsFrom && !field.options && (
        <p className="text-xs text-ink-muted">
          Saisie manuelle : la liste déroulante alimentée par{' '}
          <code className="font-mono">{field.optionsFrom}</code> n'est pas encore implémentée.
          {blockedBy.length > 0 && ` Dépend de : ${blockedBy.join(', ')}.`}
        </p>
      )}
    </div>
  )
}

function Control({
  field,
  id,
  value,
  blockedBy,
  onChange,
}: {
  field: ConfigField
  id: string
  value: string
  blockedBy: string[]
  onChange: (value: string) => void
}) {
  switch (field.kind) {
    case 'Boolean':
      return (
        <div className="flex h-9 items-center">
          <Toggle
            label={field.label}
            checked={value === 'true'}
            onCheckedChange={(next) => onChange(String(next))}
          />
        </div>
      )

    case 'Secret':
      return (
        <Input
          id={id}
          type="password"
          autoComplete="new-password"
          value={value}
          placeholder={field.value ?? 'Non renseigné'}
          onChange={(event) => onChange(event.target.value)}
        />
      )

    case 'Integer':
      return (
        <Input
          id={id}
          type="number"
          value={value}
          onChange={(event) => onChange(event.target.value)}
        />
      )

    case 'Duration':
      return (
        <div className="flex items-center gap-2">
          <Input
            id={id}
            type="number"
            min={1}
            value={value}
            onChange={(event) => onChange(event.target.value)}
            className="w-32"
          />
          <span className="text-sm text-ink-muted">secondes</span>
        </div>
      )

    case 'Url':
      return (
        <Input
          id={id}
          type="url"
          inputMode="url"
          placeholder="http://192.168.1.10:7878"
          value={value}
          onChange={(event) => onChange(event.target.value)}
        />
      )

    case 'Select':
      // Options figées : vraie liste. Options dynamiques non résolues : saisie libre (ADR-0011).
      return field.options ? (
        <Select id={id} value={value} onChange={(event) => onChange(event.target.value)}>
          {!field.required && <option value="">—</option>}
          {field.options.map((option) => (
            <option key={option.value} value={option.value}>
              {option.label}
            </option>
          ))}
        </Select>
      ) : (
        <Input
          id={id}
          value={value}
          onChange={(event) => onChange(event.target.value)}
          disabled={blockedBy.length > 0}
        />
      )

    case 'MultiSelect':
      return (
        <Input
          id={id}
          value={value}
          placeholder="valeurs séparées par des virgules"
          onChange={(event) => onChange(event.target.value)}
          disabled={blockedBy.length > 0}
          className={cn(blockedBy.length > 0 && 'opacity-60')}
        />
      )

    default:
      return <Input id={id} value={value} onChange={(event) => onChange(event.target.value)} />
  }
}
