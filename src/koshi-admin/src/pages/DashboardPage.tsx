import { useQuery } from '@tanstack/react-query'
import { AlertTriangle, AppWindow, Clock, MonitorCheck, MonitorOff, MonitorSmartphone, ShieldAlert, WifiOff } from 'lucide-react'
import type { ComponentType } from 'react'
import { Bar, BarChart, CartesianGrid, Cell, Legend, Pie, PieChart, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts'
import { Card, CardHeader, EmptyState, ErrorState, LoadingState, formatDuration } from '@/components/ui'
import { api } from '@/lib/api'
import type { DashboardSummary } from '@/lib/types'

const PIE_COLORS = ['#2f80ff', '#38bdf8', '#8b5cf6', '#22c55e', '#f59e0b', '#ec4899', '#14b8a6', '#64748b']

function Stat({ label, value, icon: Icon, tone }: { label: string; value: string | number; icon: ComponentType<{ className?: string }>; tone: string }) {
  return (
    <Card className="p-5">
      <div className="flex items-center justify-between">
        <p className="text-xs font-medium uppercase tracking-wide text-slate-500">{label}</p>
        <span className={`rounded-xl p-2 ${tone}`}>
          <Icon className="size-4" />
        </span>
      </div>
      <p className="mt-3 text-2xl font-semibold tabular-nums">{value}</p>
    </Card>
  )
}

const hours = (s: number) => Math.round((s / 3600) * 10) / 10

export function DashboardPage() {
  const q = useQuery({ queryKey: ['dashboard'], queryFn: ({ signal }) => api<DashboardSummary>('/api/dashboard/summary', { signal }), refetchInterval: 60_000 })

  if (q.isPending) return <LoadingState />
  if (q.isError) return <ErrorState error={q.error} onRetry={() => q.refetch()} />
  const d = q.data

  const daily = d.activityByDay.map((p) => ({ day: p.date.slice(5), Faol: hours(p.activeSeconds), Idle: hours(p.idleSeconds), Nomaʼlum: hours(p.unknownSeconds) }))
  const apps = d.topApplications.map((a) => ({ name: a.processName, value: hours(a.foregroundSeconds) }))
  const status = [
    { name: 'Onlayn', value: d.onlineDevices },
    { name: 'Offlayn', value: d.offlineDevices },
    { name: 'Kutilmoqda', value: d.pendingDevices },
    { name: 'Bloklangan', value: d.blockedDevices },
  ]

  return (
    <div className="space-y-6">
      <div className="relative overflow-hidden rounded-2xl bg-gradient-to-r from-ink-900 via-ink-800 to-ink-900 p-6 text-white">
        <div className="pointer-events-none absolute -right-10 -top-16 size-56 rounded-full bg-brand-500/30 blur-3xl" />
        <div className="pointer-events-none absolute -bottom-20 right-40 size-56 rounded-full bg-accent-500/30 blur-3xl" />
        <p className="relative text-sm text-slate-300">KOSHI Platform</p>
        <h1 className="relative mt-1 text-2xl font-semibold">Umumiy holat</h1>
        <p className="relative mt-1 text-sm text-slate-400">Oxirgi yangilanish {new Date(d.generatedAt).toLocaleTimeString('uz-UZ')} · ma’lumotlar har daqiqada yangilanadi</p>
      </div>

      <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
        <Stat label="Jami qurilmalar" value={d.totalDevices} icon={MonitorSmartphone} tone="bg-brand-50 text-brand-600 dark:bg-brand-500/10" />
        <Stat label="Onlayn" value={d.onlineDevices} icon={MonitorCheck} tone="bg-emerald-50 text-emerald-600 dark:bg-emerald-500/10" />
        <Stat label="Offlayn" value={d.offlineDevices} icon={MonitorOff} tone="bg-slate-100 text-slate-600 dark:bg-white/5" />
        <Stat label="Bugungi monitoring vaqti" value={formatDuration(d.monitoredSecondsToday)} icon={Clock} tone="bg-violet-50 text-violet-600 dark:bg-violet-500/10" />
        <Stat label="Bugun faol dasturlar" value={d.activeApplicationsToday} icon={AppWindow} tone="bg-sky-50 text-sky-600 dark:bg-sky-500/10" />
        <Stat label="Policy xatolari" value={d.policyFailures} icon={ShieldAlert} tone="bg-rose-50 text-rose-600 dark:bg-rose-500/10" />
        <Stat label="Ma’lumot yetishmayotgan" value={d.devicesMissingData} icon={WifiOff} tone="bg-amber-50 text-amber-600 dark:bg-amber-500/10" />
        <Stat label="Ochiq ogohlantirishlar" value={d.openAlerts} icon={AlertTriangle} tone="bg-orange-50 text-orange-600 dark:bg-orange-500/10" />
      </div>

      <div className="grid gap-4 xl:grid-cols-3">
        <Card className="xl:col-span-2">
          <CardHeader title="Kunlar bo‘yicha faol va idle vaqt (soat)" subtitle="Oxirgi 7 kun; “Nomaʼlum” — jadvaldagi telemetriyasiz vaqt" />
          <div className="h-72 p-4">
            {daily.every((x) => x.Faol + x.Idle === 0) ? (
              <EmptyState title="Hali telemetriya yo‘q" hint="Agent aktivatsiya qilinib, ma’lumot yuborgach grafik paydo bo‘ladi." />
            ) : (
              <ResponsiveContainer>
                <BarChart data={daily}>
                  <CartesianGrid strokeDasharray="3 3" stroke="#94a3b833" />
                  <XAxis dataKey="day" fontSize={12} />
                  <YAxis fontSize={12} />
                  <Tooltip />
                  <Legend />
                  <Bar dataKey="Faol" stackId="a" fill="#2f80ff" radius={[0, 0, 0, 0]} />
                  <Bar dataKey="Idle" stackId="a" fill="#a78bfa" />
                  <Bar dataKey="Nomaʼlum" stackId="a" fill="#cbd5e1" radius={[6, 6, 0, 0]} />
                </BarChart>
              </ResponsiveContainer>
            )}
          </div>
        </Card>
        <Card>
          <CardHeader title="Qurilmalar holati" />
          <div className="h-72 p-4">
            {d.totalDevices === 0 ? (
              <EmptyState title="Qurilmalar yo‘q" hint="“Qurilmalar” bo‘limida birinchi qurilmani ro‘yxatdan o‘tkazing." />
            ) : (
              <ResponsiveContainer>
                <PieChart>
                  <Pie data={status} dataKey="value" nameKey="name" innerRadius={55} outerRadius={90} paddingAngle={2}>
                    {status.map((_, i) => (
                      <Cell key={i} fill={['#22c55e', '#94a3b8', '#38bdf8', '#f43f5e'][i]} />
                    ))}
                  </Pie>
                  <Tooltip />
                  <Legend />
                </PieChart>
              </ResponsiveContainer>
            )}
          </div>
        </Card>
      </div>

      <div className="grid gap-4 xl:grid-cols-2">
        <Card>
          <CardHeader title="Dasturlar bo‘yicha vaqt taqsimoti (soat)" subtitle="Oxirgi 7 kun, faol oynadagi vaqt" />
          <div className="h-72 p-4">
            {apps.length === 0 ? (
              <EmptyState title="Dastur ma’lumoti yo‘q" />
            ) : (
              <ResponsiveContainer>
                <PieChart>
                  <Pie data={apps} dataKey="value" nameKey="name" outerRadius={95}>
                    {apps.map((_, i) => (
                      <Cell key={i} fill={PIE_COLORS[i % PIE_COLORS.length]} />
                    ))}
                  </Pie>
                  <Tooltip />
                  <Legend />
                </PieChart>
              </ResponsiveContainer>
            )}
          </div>
        </Card>
        <Card>
          <CardHeader title="Bo‘limlar bo‘yicha KPI" subtitle="Ishonchsiz (coverage past) natijalar ko‘rsatilmaydi" />
          {d.departmentKpis.length === 0 ? (
            <EmptyState title="Bo‘lim ma’lumoti yo‘q" />
          ) : (
            <table className="w-full text-sm">
              <thead className="text-left text-xs uppercase text-slate-500">
                <tr>
                  <th className="px-5 py-3">Bo‘lim</th>
                  <th className="px-5 py-3">KPI</th>
                  <th className="px-5 py-3">Coverage</th>
                  <th className="px-5 py-3">Faol</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100 dark:divide-white/5">
                {d.departmentKpis.map((k) => (
                  <tr key={k.departmentId ?? 'none'}>
                    <td className="px-5 py-3 font-medium">{k.departmentName}</td>
                    <td className="px-5 py-3 tabular-nums">{k.productivityScore ?? <span className="text-slate-400">ishonchsiz</span>}</td>
                    <td className="px-5 py-3 tabular-nums">{Math.round(k.coverage * 100)}%</td>
                    <td className="px-5 py-3">{formatDuration(k.activeSeconds)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </Card>
      </div>
    </div>
  )
}
