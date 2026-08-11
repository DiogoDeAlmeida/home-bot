import { useState, type FormEvent } from 'react'
import { PasswordInput } from '@mantine/core'
import { useLogin } from '@/api/hooks'
import { Alert, Button, Card, Center, Container, Stack, Text, Title } from '@/components/ui'

export function LoginPage() {
  const login = useLogin()
  const [password, setPassword] = useState('')

  const submit = (event: FormEvent) => {
    event.preventDefault()
    if (password) login.mutate(password)
  }

  return (
    <Container size={380} h="100%">
      <Center h="100%">
        <Card w="100%">
          <Stack gap="md">
            <Stack gap={4}>
              <Title order={2} size="h4">
                Homelab Hub
              </Title>
              <Text size="sm" c="dimmed">
                Accès administrateur.
              </Text>
            </Stack>

            <form onSubmit={submit}>
              <Stack gap="md">
                <PasswordInput
                  label="Mot de passe"
                  autoComplete="current-password"
                  data-autofocus
                  value={password}
                  onChange={(event) => setPassword(event.currentTarget.value)}
                />

                {login.isError && (
                  <Alert color="red" variant="light">
                    Mot de passe incorrect.
                  </Alert>
                )}

                <Button type="submit" disabled={!password} loading={login.isPending} fullWidth>
                  Se connecter
                </Button>
              </Stack>
            </form>
          </Stack>
        </Card>
      </Center>
    </Container>
  )
}
