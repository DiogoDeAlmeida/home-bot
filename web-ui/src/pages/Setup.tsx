import { useState, type FormEvent } from 'react'
import { PasswordInput } from '@mantine/core'
import { useCompleteSetup, useSetupState } from '@/api/hooks'
import { Alert, Button, Card, Center, Container, Stack, Text, Title } from '@/components/ui'

/**
 * Assistant de premier démarrage.
 *
 * Tant qu'il n'a pas abouti, l'API est verrouillée : seules cette page et la sonde de
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
    <Container size={420} h="100%">
      <Center h="100%">
        <Card w="100%">
          <Stack gap="md">
            <Stack gap={4}>
              <Title order={2} size="h4">
                Premier démarrage
              </Title>
              <Text size="sm" c="dimmed">
                Cette interface concentre toutes les clés d'API du homelab. Définis un mot de
                passe administrateur pour la déverrouiller.
              </Text>
            </Stack>

            <form onSubmit={submit}>
              <Stack gap="md">
                <PasswordInput
                  label="Mot de passe"
                  description={`${minimum} caractères minimum.`}
                  autoComplete="new-password"
                  data-autofocus
                  value={password}
                  onChange={(event) => setPassword(event.currentTarget.value)}
                />
                <PasswordInput
                  label="Confirmation"
                  autoComplete="new-password"
                  value={confirmation}
                  onChange={(event) => setConfirmation(event.currentTarget.value)}
                />

                {tooShort && (
                  <Alert color="yellow" variant="light">
                    Trop court : {minimum} caractères minimum.
                  </Alert>
                )}
                {mismatch && (
                  <Alert color="yellow" variant="light">
                    Les deux saisies diffèrent.
                  </Alert>
                )}
                {complete.isError && (
                  <Alert color="red" variant="light">
                    {(complete.error as Error).message}
                  </Alert>
                )}

                <Button type="submit" disabled={!ready} loading={complete.isPending} fullWidth>
                  Initialiser le hub
                </Button>
              </Stack>
            </form>
          </Stack>
        </Card>
      </Center>
    </Container>
  )
}
