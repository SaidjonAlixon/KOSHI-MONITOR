import { useQuery } from '@tanstack/react-query'
import { useState } from 'react'
import { Bar, BarChart, CartesianGrid, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts'
import { ActivityDetailView } from '@/components/ActivityDetail'
import { FilterBar, PageHeader, defaultFilters, filtersToQuery, type Filters } from '@/components/forms'
import { FormulaCard, ReportTable } from '@/components/ReportTable'
import { Card, CardHeader, EmptyState, ErrorState, LoadingState } from '@/components/ui'
import { api } from '@/lib/api'
import { Permissions, useAuth } from '@/lib/auth'
import type { Report } from '@/lib/types'

export function ProductivityPage() {
  const { can } = useAuth()
  const individual = can(Permissions.ReportsIndividual)
  const [filters, setFilters] = useState<Filters>(defaultFilters(0))
  const [showKpi, setShowKpi] = useState(false)

  const groupBy = individual ? 'employee' : 'department'
  const kpi = useQuery({
    queryKey: ['productivity', filters, groupBy],
    queryFn: ({ signal }) => api<Report>('/api/reports/productivity', { query: { ...filtersToQuery(filters), groupBy }, signal }),
    enabled: showKpi,
  })
  const chart = (kpi.data?.rows ?? []).filter((r) => r.productivityScore !== null).map((r) => ({ name: r.groupName, KPI: r.productivityScore, Coverage: Math.round(r.coverage * 100) }))

  const activityQuery = {
    from: filtersToQuery(filters).from,
    to: filtersToQuery(filters).to,
    deviceId: filters.deviceId || undefined,
    employeeId: filters.employeeId || undefined,
    departmentId: filters.departmentId || undefined,
    branch: filters.branch || undefined,
  }

  return (
    <div className="space-y-4">
      <PageHeader
        title="Kompyuter faoliyati"
        subtitle="Tanlangan kun uchun: yoqilish, dasturlar, oynalar va brauzer"
        actions={
          <button
            onClick={() => setShowKpi((v) => !v)}
            className="rounded-xl border border-slate-200 bg-white px-3.5 py-2 text-sm font-medium text-slate-700 hover:bg-slate-50 dark:border-white/10 dark:bg-white/5 dark:text-slate-200"
          >
            {showKpi ? 'KPI jadvalini yashirish' : 'KPI (unumdorlik) baholarini ko‘rsatish'}
          </button>
        }
      />
      <FilterBar value={filters} onChange={setFilters} />

      <ActivityDetailView query={activityQuery} />

      {showKpi && (
        <div className="space-y-4">
          {kpi.isPending ? (
            <LoadingState />
          ) : kpi.isError ? (
            <ErrorState error={kpi.error} onRetry={() => kpi.refetch()} />
          ) : (
            <>
              <div className="grid gap-6 xl:grid-cols-[1fr_380px]">
                <Card>
                  <CardHeader title="KPI va coverage" subtitle="Ishonchsiz (coverage past) natijalar kulrang belgilangan" />
                  <div className="h-72 p-4">
                    {chart.length === 0 ? (
                      <EmptyState title="KPI hisoblash uchun ma’lumot yetarli emas" />
                    ) : (
                      <ResponsiveContainer>
                        <BarChart data={chart}>
                          <CartesianGrid strokeDasharray="3 3" stroke="#94a3b833" />
                          <XAxis dataKey="name" fontSize={11} />
                          <YAxis domain={[0, 100]} fontSize={11} />
                          <Tooltip />
                          <Bar dataKey="KPI" fill="#2f80ff" radius={[6, 6, 0, 0]} />
                          <Bar dataKey="Coverage" fill="#a78bfa" radius={[6, 6, 0, 0]} />
                        </BarChart>
                      </ResponsiveContainer>
                    )}
                  </div>
                </Card>
                <FormulaCard report={kpi.data} />
              </div>
              <Card>
                <CardHeader title="Batafsil KPI" subtitle="Qatorni bosib metrikalarni ko‘ring" />
                {kpi.data.rows.length === 0 ? <EmptyState title="Ma’lumot yo‘q" /> : <ReportTable report={kpi.data} />}
              </Card>
            </>
          )}
        </div>
      )}
    </div>
  )
}
