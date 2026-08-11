import { useState, type FormEvent } from 'react'
import { useLogin } from '@/api/hooks'
import { Alert, Button, Card, Input, Label } from '@/components/ui/primitives'

export function LoginPage() {
  const login = useLogin()
  const [password, setPassword] = useState('')

  const submit = (event: FormEvent) => {
    event.preventDefault()
    if (password) login.mutate(password)
  }

  return (
    <div className="mx-auto flex min-h-full max-w-sm items-center px-4">
      <Card className="w-full">
        <h1 className="text-lg font-semibold tracking-tight">Homelab Hub</h1>
        <p className="mt-1 mb-5 text-sm text-ink-muted">Accès administrateur.</p>

        <form onSubmit={submit} className="space-y-4">
          <div className="space-y-1.5">
            <Label htmlFor="password">Mot de passe</Label>
            <Input
              id="password"
              type="password"
              autoComplete="current-password"
              autoFocus
              value={password}
              onChange={(event) => setPassword(event.target.value)}
            />
          </div>

          {login.isError && <Alert tone="bad">Mot de passe incorrect.</Alert>}

          <Button type="submit" disabled={!password || login.isPending} className="w-full">
            {login.isPending ? 'Connexion…' : 'Se connecter'}
          </Button>
        </form>
      </Card>
    </div>
  )
}
