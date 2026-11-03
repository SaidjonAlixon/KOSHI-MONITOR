import {
  Activity, AppWindow, BarChart3, Bell, FileSpreadsheet, FileText, History, LayoutDashboard, LogOut, Menu, Monitor,
  Moon, PanelLeftClose, Search, Settings, ShieldCheck, Sun, UserCog, Users, X,
} from 'lucide-react'
import { useEffect, useState, type ComponentType } from 'react'
import { NavLink, Outlet, useLocation, useNavigate } from 'react-router'
import { KoshiIcon, KoshiLogo } from '@/components/Brand'
import { Permissions, useAuth, type Permission } from '@/lib/auth'

interface NavItem {
  to: string
  label: string
  icon: ComponentType<{ className?: string }>
  permission: Permission
}

export const NAV_GROUPS: { title: string; items: NavItem[] }[] = [
  {
    title: 'Umumiy',
    items: [
      { to: '/app', label: 'Dashboard', icon: LayoutDashboard, permission: Permissions.DashboardRead },
      { to: '/app/live', label: 'Live Monitoring', icon: Activity, permission: Permissions.MonitoringLive },
      { to: '/app/timeline', label: 'Activity Timeline', icon: History, permission: Permissions.MonitoringRead },
    ],
  },
  {
    title: 'Boshqaruv',
    items: [
      { to: '/app/employees', label: 'Xodimlar', icon: Users, permission: Permissions.EmployeesRead },
      { to: '/app/devices', label: 'Qurilmalar', icon: Monitor, permission: Permissions.DevicesRead },
      { to: '/app/applications', label: 'App Management', icon: AppWindow, permission: Permissions.PoliciesRead },
      { to: '/app/policies', label: 'Policies', icon: ShieldCheck, permission: Permissions.PoliciesRead },
    ],
  },
  {
    title: 'Tahlil',
    items: [
      { to: '/app/productivity', label: 'Productivity', icon: BarChart3, permission: Permissions.ReportsRead },
      { to: '/app/reports', label: 'Reports', icon: FileSpreadsheet, permission: Permissions.ReportsRead },
    ],
  },
  {
    title: 'Xavfsizlik',
    items: [
      { to: '/app/alerts', label: 'Alerts', icon: Bell, permission: Permissions.AlertsRead },
      { to: '/app/audit', label: 'Audit Logs', icon: FileText, permission: Permissions.AuditRead },
      { to: '/app/users', label: 'Roles & Permissions', icon: UserCog, permission: Permissions.UsersManage },
      { to: '/app/settings', label: 'Settings', icon: Settings, permission: Permissions.SettingsRead },
    ],
  },
]

export const NAV_ITEMS = NAV_GROUPS.flatMap((g) => g.items)

function useTheme(): [boolean, () => void] {
  const [dark, setDark] = useState(() => localStorage.getItem('koshi-theme') === 'dark')
  useEffect(() => {
    document.documentElement.classList.toggle('dark', dark)
    localStorage.setItem('koshi-theme', dark ? 'dark' : 'light')
  }, [dark])
  return [dark, () => setDark((d) => !d)]
}

function SidebarNav({ collapsed, onNavigate }: { collapsed: boolean; onNavigate?: () => void }) {
  const { can } = useAuth()
  return (
    <nav className="flex-1 space-y-6 overflow-y-auto px-3 py-4" aria-label="Asosiy menyu">
      {NAV_GROUPS.map((group) => {
        const items = group.items.filter((i) => can(i.permission))
        if (items.length === 0) return null
        return (
          <div key={group.title}>
            {!collapsed && <p className="mb-2 px-3 text-[11px] font-semibold uppercase tracking-wider text-slate-500">{group.title}</p>}
            <div className="space-y-1">
              {items.map(({ to, label, icon: Icon }) => (
                <NavLink
                  key={to}
                  to={to}
                  end={to === '/app'}
                  title={label}
                  onClick={onNavigate}
                  className={({ isActive }) =>
                    `group relative flex items-center gap-3 rounded-xl px-3 py-2 text-sm font-medium transition ${
                      isActive ? 'bg-white/10 text-white' : 'text-slate-400 hover:bg-white/5 hover:text-white'
                    }`
                  }
                >
                  {({ isActive }) => (
                    <>
                      {isActive && <span className="brand-gradient absolute inset-y-1.5 left-0 w-1 rounded-full" />}
                      <Icon className={`size-[18px] shrink-0 ${isActive ? 'text-brand-400' : 'text-slate-500 group-hover:text-slate-300'}`} />
                      {!collapsed && <span className="truncate">{label}</span>}
                    </>
                  )}
                </NavLink>
              ))}
            </div>
          </div>
        )
      })}
    </nav>
  )
}

export function AppLayout() {
  const { user, logout } = useAuth()
  const navigate = useNavigate()
  const location = useLocation()
  const [dark, toggleTheme] = useTheme()
  const [collapsed, setCollapsed] = useState(false)
  const [mobileOpen, setMobileOpen] = useState(false)
  const current = NAV_ITEMS.find((i) => (i.to === '/app' ? location.pathname === '/app' : location.pathname.startsWith(i.to)))

  useEffect(() => setMobileOpen(false), [location.pathname])

  const sidebarBody = (isCollapsed: boolean) => (
    <>
      <div className="flex h-20 shrink-0 items-center px-3">
        <div className={`flex items-center justify-center rounded-xl bg-white shadow-sm ${isCollapsed ? 'px-2 py-2' : 'w-full px-4 py-2.5'}`}>
          {isCollapsed ? <KoshiIcon className="size-11" /> : <KoshiLogo className="h-9 max-w-full" />}
        </div>
      </div>
      <SidebarNav collapsed={isCollapsed} onNavigate={() => setMobileOpen(false)} />
    </>
  )

  return (
    <div className="flex h-full">
      <aside className={`hidden shrink-0 flex-col border-r border-white/5 bg-gradient-to-b from-ink-900 to-ink-950 transition-[width] md:flex ${collapsed ? 'w-20' : 'w-64'}`}>
        {sidebarBody(collapsed)}
        <div className="border-t border-white/5 p-3">
          <button onClick={() => setCollapsed((c) => !c)} className="flex w-full items-center gap-3 rounded-xl px-3 py-2 text-xs text-slate-500 hover:bg-white/5 hover:text-slate-300">
            <PanelLeftClose className={`size-4 transition ${collapsed ? 'rotate-180' : ''}`} />
            {!collapsed && 'Menyuni yig‘ish'}
          </button>
        </div>
      </aside>

      {mobileOpen && (
        <div className="fixed inset-0 z-40 md:hidden">
          <div className="absolute inset-0 bg-ink-950/70" onClick={() => setMobileOpen(false)} />
          <aside className="relative flex h-full w-72 flex-col bg-gradient-to-b from-ink-900 to-ink-950">
            <button onClick={() => setMobileOpen(false)} className="absolute right-3 top-4 rounded-lg p-1 text-slate-400" aria-label="Menyuni yopish">
              <X className="size-5" />
            </button>
            {sidebarBody(false)}
          </aside>
        </div>
      )}

      <div className="flex min-w-0 flex-1 flex-col">
        <header className="flex h-16 shrink-0 items-center gap-4 border-b border-slate-200/80 bg-white/80 px-4 backdrop-blur md:px-6 dark:border-white/10 dark:bg-ink-900/80">
          <button className="rounded-lg p-1.5 text-slate-500 md:hidden" onClick={() => setMobileOpen(true)} aria-label="Menyuni ochish">
            <Menu className="size-5" />
          </button>
          <div className="hidden lg:block">
            <p className="text-xs text-slate-500">KOSHI Platform</p>
            <p className="text-sm font-semibold">{current?.label ?? 'Admin panel'}</p>
          </div>
          <label className="relative max-w-md flex-1 lg:ml-6">
            <span className="sr-only">Qidiruv</span>
            <Search className="pointer-events-none absolute left-3 top-1/2 size-4 -translate-y-1/2 text-slate-400" />
            <input
              type="search"
              placeholder="Qurilma yoki xodimni qidirish… (Enter)"
              onKeyDown={(e) => {
                const v = e.currentTarget.value.trim()
                if (e.key === 'Enter' && v) navigate(`/app/devices?search=${encodeURIComponent(v)}`)
              }}
              className="w-full rounded-xl border border-slate-200 bg-slate-50 py-2 pl-9 pr-3 text-sm outline-none focus:border-brand-400 focus:ring-2 focus:ring-brand-400/30 dark:border-white/10 dark:bg-white/5"
            />
          </label>
          <div className="ml-auto flex items-center gap-1 sm:gap-2">
            <button onClick={toggleTheme} className="rounded-xl p-2 text-slate-500 hover:bg-slate-100 dark:hover:bg-white/10" aria-label="Rejimni almashtirish">
              {dark ? <Sun className="size-5" /> : <Moon className="size-5" />}
            </button>
            <div className="hidden items-center gap-3 rounded-xl border border-slate-200 px-3 py-1.5 sm:flex dark:border-white/10">
              <span className="brand-gradient flex size-8 items-center justify-center rounded-lg text-sm font-semibold text-white">{user?.displayName.slice(0, 1).toUpperCase()}</span>
              <div className="text-left">
                <p className="text-sm font-medium leading-tight">{user?.displayName}</p>
                <p className="text-[11px] text-slate-500">{user?.roles.join(', ')}</p>
              </div>
            </div>
            <button
              onClick={async () => {
                await logout()
                navigate('/login', { replace: true })
              }}
              className="rounded-xl p-2 text-slate-500 hover:bg-slate-100 hover:text-rose-600 dark:hover:bg-white/10"
              aria-label="Chiqish"
              title="Chiqish"
            >
              <LogOut className="size-5" />
            </button>
          </div>
        </header>
        <main className="flex-1 overflow-y-auto">
          <div className="mx-auto max-w-[1600px] space-y-6 p-4 md:p-6">
            <Outlet />
          </div>
        </main>
      </div>
    </div>
  )
}
