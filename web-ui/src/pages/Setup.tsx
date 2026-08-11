import { useState, type FormEvent } from 'react'
import { useCompleteSetup, useSetupState } from '@/api/hooks'
import { Alert, Button, Card, Input, Label } from '@/components/ui/primitives'

/**
 * Assistant de premier démarrage.
 *
 * Tant qu'il n'a pas abouti, le hub est verrouillé : seules cette page et la sonde de
 * disponibilité répondent, webhooks compris. « Refuser de démarrer sans mot de passe » était
 * irréalisable puisque le mot de passe se définit ici même.
 */
export function SetupPage() {
  const state = useSetupState()
  const complete = useCompleteSetup()
  const [password, setPassword] = useState('')
  const [confirmation, setConfirmation] = useState('')

  const minimum = state.data?.minimumPasswordLength ?? 10
  const tooShort = password.length > 0 && password.length < minimum
  const mismatch = confirmation.length > 0 && password !== confirmation
  const ready = password.length >= minimum && password === confirmation

  const submit = (event: FormEvent) => {
    event.preventDefault()
    if (ready) complete.mutate(password)
  }

  return (
    <div className="mx-auto flex min-h-full max-w-md items-center px-4">
      <Card className="w-full">
        <h1 className="text-lg font-semibold tracking-tight">Premier démarrage</h1>
        <p className="mt-1 mb-5 text-sm text-ink-muted">
          Cette interface concentre toutes les clés d'API du homelab. Définis un mot de passe
          administrateur pour la déverrouiller.
        </p>

        <form onSubmit={submit} className="space-y-4">
          <div className="space-y-1.5">
            <Label htmlFor="password">Mot de passe</Label>
            <Input
              id="password"
              type="password"
              autoComplete="new-password"
              autoFocus
              value={password}
              onChange={(event) => setPassword(event.target.value)}
            />
            <p className="text-xs text-ink-muted">{minimum} caractères minimum.</p>
          </div>

          <div className="space-y-1.5">
            <Label htmlFor="confirmation">Confirmation</Label>
            <Input
              id="confirmation"
              type="password"
              autoComplete="new-password"
              value={confirmation}
              onChange={(event) => setConfirmation(event.target.value)}
            />
          </div>

          {tooShort && <Alert tone="warn">Trop court : {minimum} caractères minimum.</Alert>}
          {mismatch && <Alert tone="warn">Les deux saisies diffèrent.</Alert>}
          {complete.isError && <Alert tone="bad">{(complete.error as Error).message}</Alert>}

          <Button type="submit" disabled={!ready || complete.isPending} className="w-full">
            {complete.isPending ? 'Initialisation…' : 'Initialiser le hub'}
          </Button>
        </form>
      </Card>
    </div>
  )
}
