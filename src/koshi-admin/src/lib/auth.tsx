import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from 'react'
import { Navigate, useLocation } from 'react-router'
import * as client from './api'
import type { CurrentUser } from './api'

export const Permissions = {
  DashboardRead: 'dashboard.read',
  DevicesRead: 'devices.read',
  DevicesManage: 'devices.manage',
  MonitoringRead: 'monitoring.read',
  MonitoringLive: 'monitoring.live',
  PoliciesRead: 'policies.read',
  PoliciesManage: 'policies.manage',
  ReportsRead: 'reports.read',
  ReportsIndividual: 'reports.individual',
  ReportsExport: 'reports.export',
  EmployeesRead: 'employees.read',
  EmployeesManage: 'employees.manage',
  AuditRead: 'audit.read',
  AlertsRead: 'alerts.read',
  AlertsManage: 'alerts.manage',
  UsersManage: 'users.manage',
  SettingsRead: 'settings.read',
  SettingsManage: 'settings.manage',
  RemoteControl: 'remote.control',
  DataUnscoped: 'data.unscoped',
  RolesManage: 'roles.manage',
} as const

export type Permission = (typeof Permissions)[keyof typeof Permissions]

interface AuthState {
  user: CurrentUser | null
  loading: boolean
  login: (email: string, password: string) => Promise<void>
  logout: () => Promise<void>
  can: (permission: Permission) => boolean
}

const AuthContext = createContext<AuthState | null>(null)

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<CurrentUser | null>(null)
  const [loading, setLoading] = useState(true)

  useEffect(() => {
    let active = true
    client
      .refreshSession()
      .then((r) => active && setUser(r?.user ?? null))
      .catch(() => active && setUser(null))
      .finally(() => active && setLoading(false))
    const off = client.onAccessTokenChange((t) => {
      if (t === null) setUser(null)
    })
    return () => {
      active = false
      off()
    }
  }, [])

  const login = useCallback(async (email: string, password: string) => {
    const r = await client.login(email, password)
    setUser(r.user)
  }, [])

  const logout = useCallback(async () => {
    await client.logout()
    setUser(null)
  }, [])

  const can = useCallback((p: Permission) => user?.permissions.includes(p) ?? false, [user])

  const value = useMemo(() => ({ user, loading, login, logout, can }), [user, loading, login, logout, can])
  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
}

export function useAuth(): AuthState {
  const ctx = useContext(AuthContext)
  if (!ctx) throw new Error('useAuth must be used inside AuthProvider')
  return ctx
}

/** UI guard only; every API endpoint enforces the same permission on the server. */
export function RequireAuth({ permission, children }: { permission?: Permission; children: ReactNode }) {
  const { user, loading, can } = useAuth()
  const location = useLocation()
  if (loading) return <FullScreenLoader />
  if (!user) return <Navigate to="/login" replace state={{ from: location.pathname }} />
  if (permission && !can(permission)) return <Forbidden />
  return <>{children}</>
}

export function FullScreenLoader() {
  return (
    <div className="flex h-full items-center justify-center" role="status" aria-live="polite">
      <div className="size-10 animate-spin rounded-full border-4 border-brand-500/30 border-t-brand-500" />
      <span className="sr-only">Yuklanmoqda…</span>
    </div>
  )
}

function Forbidden() {
  return (
    <div className="mx-auto mt-24 max-w-md rounded-2xl border border-slate-200 bg-white p-8 text-center dark:border-white/10 dark:bg-ink-900">
      <h1 className="text-lg font-semibold">Ruxsat yo'q</h1>
      <p className="mt-2 text-sm text-slate-500">Bu bo'limni ko'rish uchun sizning rolingizda tegishli ruxsat mavjud emas.</p>
    </div>
  )
}
