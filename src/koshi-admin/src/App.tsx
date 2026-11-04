import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { lazy, Suspense, type ComponentType, type ReactNode } from 'react'
import { createBrowserRouter, Navigate, RouterProvider, type RouteObject } from 'react-router'
import { AppLayout } from '@/components/AppLayout'
import { LoadingState } from '@/components/ui'
import { ApiError } from '@/lib/api'
import { AuthProvider, Permissions, RequireAuth, type Permission } from '@/lib/auth'
import { LandingPage } from '@/pages/LandingPage'
import { LoginPage } from '@/pages/LoginPage'

function page<T extends Record<string, ComponentType>>(loader: () => Promise<T>, name: keyof T) {
  return lazy(async () => ({ default: (await loader())[name] as ComponentType }))
}

const DashboardPage = page(() => import('@/pages/DashboardPage'), 'DashboardPage')
const LivePage = page(() => import('@/pages/LivePage'), 'LivePage')
const TimelinePage = page(() => import('@/pages/TimelinePage'), 'TimelinePage')
const EmployeesPage = page(() => import('@/pages/EmployeesPage'), 'EmployeesPage')
const DevicesPage = page(() => import('@/pages/DevicesPage'), 'DevicesPage')
const ApplicationsPage = page(() => import('@/pages/ApplicationsPage'), 'ApplicationsPage')
const PoliciesPage = page(() => import('@/pages/PoliciesPage'), 'PoliciesPage')
const ProductivityPage = page(() => import('@/pages/ProductivityPage'), 'ProductivityPage')
const ReportsPage = page(() => import('@/pages/ReportsPage'), 'ReportsPage')
const AlertsPage = page(() => import('@/pages/AlertsPage'), 'AlertsPage')
const AuditPage = page(() => import('@/pages/AuditPage'), 'AuditPage')
const UsersPage = page(() => import('@/pages/UsersPage'), 'UsersPage')
const SettingsPage = page(() => import('@/pages/SettingsPage'), 'SettingsPage')
const SupportPage = page(() => import('@/pages/SupportPage'), 'SupportPage')
const RemotePage = page(() => import('@/pages/RemotePage'), 'RemotePage')
const LocationPage = page(() => import('@/pages/LocationPage'), 'LocationPage')

function guarded(permission: Permission, element: ReactNode) {
  return (
    <RequireAuth permission={permission}>
      <Suspense fallback={<LoadingState />}>{element}</Suspense>
    </RequireAuth>
  )
}

export const routes: RouteObject[] = [
  { path: '/', element: <LandingPage /> },
  { path: '/login', element: <LoginPage /> },
  {
    path: '/app',
    element: (
      <RequireAuth>
        <AppLayout />
      </RequireAuth>
    ),
    children: [
      { index: true, element: guarded(Permissions.DashboardRead, <DashboardPage />) },
      { path: 'live', element: guarded(Permissions.MonitoringLive, <LivePage />) },
      { path: 'timeline', element: guarded(Permissions.MonitoringRead, <TimelinePage />) },
      { path: 'location', element: guarded(Permissions.LocationRead, <LocationPage />) },
      { path: 'employees', element: guarded(Permissions.EmployeesRead, <EmployeesPage />) },
      { path: 'devices', element: guarded(Permissions.DevicesRead, <DevicesPage />) },
      { path: 'applications', element: guarded(Permissions.PoliciesRead, <ApplicationsPage />) },
      { path: 'policies', element: guarded(Permissions.PoliciesRead, <PoliciesPage />) },
      { path: 'productivity', element: guarded(Permissions.ReportsRead, <ProductivityPage />) },
      { path: 'reports', element: guarded(Permissions.ReportsRead, <ReportsPage />) },
      { path: 'alerts', element: guarded(Permissions.AlertsRead, <AlertsPage />) },
      { path: 'audit', element: guarded(Permissions.AuditRead, <AuditPage />) },
      { path: 'users', element: guarded(Permissions.UsersManage, <UsersPage />) },
      { path: 'settings', element: guarded(Permissions.SettingsRead, <SettingsPage />) },
      { path: 'support', element: guarded(Permissions.SupportChat, <SupportPage />) },
      { path: 'remote/:deviceId', element: guarded(Permissions.RemoteControl, <RemotePage />) },
    ],
  },
  { path: '*', element: <Navigate to="/" replace /> },
]

export function createQueryClient(): QueryClient {
  return new QueryClient({
    defaultOptions: {
      queries: {
        staleTime: 10_000,
        refetchOnWindowFocus: false,
        retry: (count, error) => !(error instanceof ApiError && error.status < 500) && count < 2,
      },
    },
  })
}

const router = createBrowserRouter(routes)
const queryClient = createQueryClient()

export default function App() {
  return (
    <QueryClientProvider client={queryClient}>
      <AuthProvider>
        <RouterProvider router={router} />
      </AuthProvider>
    </QueryClientProvider>
  )
}
