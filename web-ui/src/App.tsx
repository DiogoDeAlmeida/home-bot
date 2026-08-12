import { BrowserRouter, Route, Routes } from 'react-router'
import { useSession, useSetupState } from '@/api/hooks'
import { Layout } from '@/components/Layout'
import { Center, Loader } from '@/components/ui'
import { AnomaliesPage } from '@/pages/Anomalies'
import { DashboardPage } from '@/pages/Dashboard'
import { JournalPage } from '@/pages/Journal'
import { LoginPage } from '@/pages/Login'
import { ModulesPage } from '@/pages/Modules'
import { SettingsPage } from '@/pages/Settings'
import { SetupPage } from '@/pages/Setup'

/**
 * Trois états mutuellement exclusifs, dans cet ordre :
 *
 * 1. **Installation** — le hub n'a pas de mot de passe administrateur. L'API renvoie 503 sur
 *    tout le reste, y compris les webhooks : rien ne doit pouvoir écrire dans le hub avant
 *    qu'il ait un propriétaire.
 * 2. **Connexion** — configuré mais sans session.
 * 3. **Application**.
 */
export function App() {
  const setup = useSetupState()
  const session = useSession()

  if (setup.isLoading || session.isLoading) {
    return (
      <Center h="100%">
        <Loader />
      </Center>
    )
  }

  if (setup.data && !setup.data.configured) return <SetupPage />

  if (!session.data?.authenticated) return <LoginPage />

  return (
    <BrowserRouter>
      <Routes>
        <Route element={<Layout />}>
          <Route index element={<DashboardPage />} />
          <Route path="modules" element={<ModulesPage />} />
          <Route path="parametres" element={<SettingsPage />} />
          <Route path="anomalies" element={<AnomaliesPage />} />
          <Route path="journal" element={<JournalPage />} />
          <Route path="*" element={<DashboardPage />} />
        </Route>
      </Routes>
    </BrowserRouter>
  )
}
