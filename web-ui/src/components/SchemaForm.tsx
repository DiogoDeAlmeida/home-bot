import { useMemo, useState, type FormEvent } from 'react'
import {
  Alert,
  Badge,
  Button,
  Group,
  MultiSelect,
  NumberInput,
  PasswordInput,
  Select,
  Stack,
  Switch,
  TagsInput,
  Text,
  TextInput,
} from '@mantine/core'
import type { ConfigField, ConfigSurface } from '@/api/types'

/**
 * Génère un formulaire à partir d'un schéma servi par le serveur.
 *
 * **C'est la pièce qui rend le système extensible.** Ajouter un module ne doit demander aucune
 * ligne de TypeScript : le serveur décrit ses champs, ce composant les rend. Il sert
 * indifféremment la configuration d'un module et les réglages du hub — même contrat, même code
 * (ADR-0013).
 *
 * **C'est aussi le seul fichier qui connaisse Mantine en profondeur.** Chaque `ConfigFieldKind`
 * s'adosse à un composant accessible et éprouvé — c'est ce qui a motivé l'abandon des primitives
 * écrites à la main : `Select`, `MultiSelect` et `TagsInput` demandent une navigation clavier et
 * une gestion du focus qu'on n'a aucune raison de réimplémenter.
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
    <form onSubmit={submit}>
      <Stack gap="md">
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
          <Alert color="yellow" variant="light">
            Champs obligatoires à renseigner : {missingRequired.map((f) => f.label).join(', ')}.
          </Alert>
        )}

        {error && (
          <Alert color="red" variant="light">
            {error}
          </Alert>
        )}
        {saved && dirty.size === 0 && !error && (
          <Alert color="green" variant="light">
            Configuration enregistrée.
          </Alert>
        )}

        <Group>
          <Button
            type="submit"
            loading={saving}
            disabled={dirty.size === 0 || missingRequired.length > 0}
          >
            Enregistrer
          </Button>
          {dirty.size > 0 && (
            <Text size="xs" c="dimmed">
              {dirty.size} champ{dirty.size > 1 ? 's' : ''} modifié{dirty.size > 1 ? 's' : ''}
            </Text>
          )}
        </Group>
      </Stack>
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
  // ADR-0011 : le contrat porte OptionsFrom, le front ne le résout pas encore. Le dire
  // explicitement vaut mieux qu'un champ texte inexpliqué là où on attend une liste.
  const unresolvedOptions = Boolean(field.optionsFrom) && !field.options
  const description = [
    field.help,
    unresolvedOptions
      ? `Saisie manuelle : la liste alimentée par ${field.optionsFrom} n'est pas encore implémentée.` +
        (blockedBy.length > 0 ? ` Dépend de : ${blockedBy.join(', ')}.` : '')
      : null,
  ]
    .filter(Boolean)
    .join(' ')

  const label = (
    <Group gap="xs">
      <span>{field.label}</span>
      {field.secret && (
        <Badge size="xs" variant="light" color="gray">
          secret
        </Badge>
      )}
    </Group>
  )

  const common = {
    label,
    description: description || undefined,
    required: field.required,
  }

  switch (field.kind) {
    case 'Boolean':
      return (
        <Switch
          {...common}
          checked={value === 'true'}
          onChange={(event) => onChange(String(event.currentTarget.checked))}
        />
      )

    case 'Secret':
      return (
        <PasswordInput
          {...common}
          autoComplete="new-password"
          value={value}
          placeholder={field.value ?? 'Non renseigné'}
          description={
            field.value
              ? `${description} Valeur enregistrée : ${field.value}. Laisser vide pour la conserver.`.trim()
              : common.description
          }
          onChange={(event) => onChange(event.currentTarget.value)}
        />
      )

    case 'Integer':
      return (
        <NumberInput
          {...common}
          value={value === '' ? '' : Number(value)}
          onChange={(next) => onChange(String(next))}
        />
      )

    case 'Duration':
      return (
        <NumberInput
          {...common}
          min={1}
          suffix=" s"
          value={value === '' ? '' : Number(value)}
          onChange={(next) => onChange(String(next))}
        />
      )

    case 'Url':
      return (
        <TextInput
          {...common}
          type="url"
          inputMode="url"
          placeholder="http://192.168.1.233:7878"
          value={value}
          onChange={(event) => onChange(event.currentTarget.value)}
        />
      )

    case 'Select':
      // Options figées : vraie liste déroulante. Options dynamiques non résolues : saisie libre.
      return field.options ? (
        <Select
          {...common}
          data={field.options.map((option) => ({ value: option.value, label: option.label }))}
          value={value || null}
          clearable={!field.required}
          onChange={(next) => onChange(next ?? '')}
        />
      ) : (
        <TextInput
          {...common}
          value={value}
          disabled={blockedBy.length > 0}
          onChange={(event) => onChange(event.currentTarget.value)}
        />
      )

    case 'MultiSelect': {
      const selected = value ? value.split(',').filter(Boolean) : []
      return field.options ? (
        <MultiSelect
          {...common}
          data={field.options.map((option) => ({ value: option.value, label: option.label }))}
          value={selected}
          disabled={blockedBy.length > 0}
          searchable
          onChange={(next) => onChange(next.join(','))}
        />
      ) : (
        // Sans liste à proposer, TagsInput reste le bon composant : saisie libre, mais les
        // valeurs restent des jetons manipulables au clavier plutôt qu'une chaîne à virgules.
        <TagsInput
          {...common}
          value={selected}
          disabled={blockedBy.length > 0}
          onChange={(next) => onChange(next.join(','))}
        />
      )
    }

    default:
      return (
        <TextInput
          {...common}
          value={value}
          onChange={(event) => onChange(event.currentTarget.value)}
        />
      )
  }
}
