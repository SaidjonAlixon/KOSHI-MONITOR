import { useQuery } from '@tanstack/react-query'
import { MonitorPlay, Radio } from 'lucide-react'
import { useMemo, useState } from 'react'
import { Link } from 'react-router'
import { Permissions, useAuth } from '@/lib/auth'
import { FilterBar, PageHeader, defaultFilters, tableClass, tbodyClass, td, th, theadClass, type Filters } from '@/components/forms'
import { Badge, Card, CardHeader, EmptyState, ErrorState, LoadingState, formatDateTime, formatDuration } from '@/components/ui'
import { api } from '@/lib/api'
import { useMonitoringRealtime } from '@/lib/realtime'
import type { LiveDevice } from '@/lib/types'

const tone = { Online: 'green', Offline: 'gray', Paused: 'amber', Blocked: 'red' } as const
const label = { Online: 'Onlayn', Offline: 'Offlayn', Paused: 'To‘xtatilgan', Blocked: 'Bloklangan' } as const

export function LivePage() {
  const { can } = useAuth()
  const [filters, setFilters] = useState<Filters>(defaultFilters())
  const realtime = useMonitoringRealtime([['live']])
  const q = useQuery({
    queryKey: ['live', filters.departmentId, filters.branch, filters.employeeId, filters.deviceId],
    queryFn: ({ signal }) =>
      api<LiveDevice[]>('/api/monitoring/live', { query: { departmentId: filters.departmentId, branch: filters.branch, employeeId: filters.employeeId, deviceId: filters.deviceId }, signal }),
    refetchInterval: realtime === 'connected' ? 60_000 : 10_000,
  })
  const counts = useMemo(() => {
    const c = { Online: 0, Offline: 0, Paused: 0, Blocked: 0 }
    q.data?.forEach((d) => (c[d.agentStatus] += 1))
    return c
  }, [q.data])

  return (
    <>
      <PageHeader
        title="Jonli kuzatuv"
        subtitle="Faol dastur, faol oyna (policy ruxsat bergan holda, maskalangan), davomiylik va agent aloqasi"
        actions={
          <span className={`inline-flex items-center gap-2 rounded-xl border px-3 py-2 text-xs font-medium ${realtime === 'connected' ? 'border-emerald-200 text-emerald-700 dark:border-emerald-500/30 dark:text-emerald-300' : 'border-amber-200 text-amber-700 dark:border-amber-500/30 dark:text-amber-300'}`}>
            <Radio className={`size-4 ${realtime === 'connected' ? 'animate-pulse' : ''}`} />
            {realtime === 'connected' ? 'Real-time ulangan' : realtime === 'reconnecting' || realtime === 'connecting' ? 'Ulanmoqda…' : 'Real-time uzildi — har 10 s yangilanadi'}
          </span>
        }
      />
      <FilterBar value={filters} onChange={setFilters} show={['branch', 'department', 'employee', 'device']} />
      <div className="grid gap-4 sm:grid-cols-4">
        {(Object.keys(counts) as (keyof typeof counts)[]).map((k) => (
          <Card key={k} className="flex items-center justify-between p-4">
            <span className="text-sm text-slate-500">{label[k]}</span>
            <span className="text-2xl font-semibold tabular-nums">{counts[k]}</span>
          </Card>
        ))}
      </div>
      <Card>
        <CardHeader title="Qurilmalar faoliyati" subtitle={q.dataUpdatedAt ? `Yangilandi: ${new Date(q.dataUpdatedAt).toLocaleTimeString('uz-UZ')}` : undefined} />
        {q.isPending ? (
          <LoadingState />
        ) : q.isError ? (
          <ErrorState error={q.error} onRetry={() => q.refetch()} />
        ) : q.data.length === 0 ? (
          <EmptyState title="Faol qurilmalar yo‘q" hint="Aktivatsiya qilingan agentlar heartbeat yuborganda shu yerda ko‘rinadi." />
        ) : (
          <div className="overflow-x-auto">
            <table className={tableClass}>
              <thead className={theadClass}>
                <tr>
                  <th className={th}>Xodim</th>
                  <th className={th}>Qurilma</th>
                  <th className={th}>Faol dastur</th>
                  <th className={th}>Faol oyna</th>
                  <th className={th}>Faoliyat boshlandi</th>
                  <th className={th}>Davomiylik</th>
                  <th className={th}>Onlayn holati</th>
                  <th className={th}>Oxirgi heartbeat</th>
                  <th className={th}>Agent</th>
                </tr>
              </thead>
              <tbody className={tbodyClass}>
                {q.data.map((d) => (
                  <tr key={d.deviceId}>
                    <td className={td}>
                      {d.employeeName ?? <span className="text-slate-400">—</span>}
                      <p className="text-xs text-slate-500">{d.departmentName ?? ''}</p>
                    </td>
                    <td className={`${td} font-medium`}>
                      <span className="flex items-center gap-2">
                        {d.deviceName}
                        {can(Permissions.RemoteControl) && d.agentStatus === 'Online' && (
                          <Link to={`/app/remote/${d.deviceId}`} title="Masofaviy boshqaruv" className="rounded-lg p-1 text-brand-600 hover:bg-brand-50 dark:hover:bg-brand-500/10">
                            <MonitorPlay className="size-4" />
                          </Link>
                        )}
                      </span>
                    </td>
                    <td className={`${td} font-mono text-xs`}>{d.currentProcessName ?? '—'}</td>
                    <td className={`${td} max-w-xs truncate text-xs text-slate-500`} title={d.currentWindowTitle ?? undefined}>
                      {d.currentWindowTitle ?? '—'}
                    </td>
                    <td className={`${td} text-xs`}>{formatDateTime(d.activityStartedAt)}</td>
                    <td className={`${td} text-xs`}>
                      {formatDuration(d.activityDurationSeconds)}
                      {d.currentState === 'Idle' && <span className="ml-1 text-violet-500">(idle)</span>}
                    </td>
                    <td className={`${td} text-xs`}>
                      {d.agentStatus === 'Offline' ? (
                        d.disconnectedAt ? <span className="text-slate-500">{formatDateTime(d.disconnectedAt)} dan offlayn</span> : <span className="text-slate-400">offlayn</span>
                      ) : (
                        <span className="text-emerald-600 dark:text-emerald-400">
                          {formatDateTime(d.connectedAt)} dan · {formatDuration(d.onlineSeconds)}
                        </span>
                      )}
                    </td>
                    <td className={`${td} text-xs`}>{formatDateTime(d.lastHeartbeatAt)}</td>
                    <td className={td}>
                      <Badge tone={tone[d.agentStatus]}>{label[d.agentStatus]}</Badge>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </Card>
    </>
  )
}
