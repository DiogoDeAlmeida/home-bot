import { NavLink, Outlet } from 'react-router'
import { useLogout } from '@/api/hooks'
import { Button } from '@/components/ui/primitives'
import { cn } from '@/lib/utils'

const NAV = [
  { to: '/', label: 'Tableau de bord', end: true },
  { to: '/modules', label: 'Modules', end: false },
  { to: '/parametres', label: 'Paramètres', end: false },
  { to: '/journal', label: 'Journal', end: false },
]

export function Layout() {
  const logout = useLogout()

  return (
    <div className="mx-auto flex min-h-full max-w-5xl flex-col px-4">
      <header className="flex flex-wrap items-center gap-4 border-b border-border-subtle py-4">
        <span className="text-lg font-semibold tracking-tight">Homelab Hub</span>

        <nav className="flex flex-1 flex-wrap gap-1">
          {NAV.map((item) => (
            <NavLink
              key={item.to}
              to={item.to}
              end={item.end}
              className={({ isActive }) =>
                cn(
                  'rounded-md px-3 py-1.5 text-sm transition-colors',
                  isActive ? 'bg-surface text-ink shadow-xs' : 'text-ink-muted hover:text-ink',
                )
              }
            >
              {item.label}
            </NavLink>
          ))}
        </nav>

        <Button variant="ghost" size="sm" onClick={() => logout.mutate()}>
          Déconnexion
        </Button>
      </header>

      <main className="flex-1 py-6">
        <Outlet />
      </main>
    </div>
  )
}

export function PageTitle({ title, subtitle }: { title: string; subtitle?: string }) {
  return (
    <div className="mb-5">
      <h1 className="text-xl font-semibold tracking-tight">{title}</h1>
      {subtitle && <p className="mt-1 text-sm text-ink-muted">{subtitle}</p>}
    </div>
  )
}
