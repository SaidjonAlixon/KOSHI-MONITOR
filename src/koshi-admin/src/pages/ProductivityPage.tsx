import { useQuery } from '@tanstack/react-query'
import { useState } from 'react'
import { Bar, BarChart, CartesianGrid, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts'
import { Field, FilterBar, PageHeader, Select, defaultFilters, filtersToQuery, type Filters } from '@/components/forms'
import { FormulaCard, ReportTable } from '@/components/ReportTable'
import { Card, CardHeader, EmptyState, ErrorState, LoadingState } from '@/components/ui'
import { api } from '@/lib/api'
import { Permissions, useAuth } from '@/lib/auth'
import type { Report } from '@/lib/types'

export function ProductivityPage() {
  const { can } = useAuth()
  const individual = can(Permissions.ReportsIndividual)
  const [filters, setFilters] = useState<Filters>(defaultFilters(7))
  const [groupBy, setGroupBy] = useState(individual ? 'employee' : 'department')
  const q = useQuery({
    queryKey: ['productivity', filters, groupBy],
    queryFn: ({ signal }) => api<Report>('/api/reports/productivity', { query: { ...filtersToQuery(filters), groupBy }, signal }),
  })
  const chart = (q.data?.rows ?? []).filter((r) => r.productivityScore !== null).map((r) => ({ name: r.groupName, KPI: r.productivityScore, Coverage: Math.round(r.coverage * 100) }))

  return (
    <>
      <PageHeader
        title="Productivity"
        subtitle="KPI, faol vaqt, tanaffuslar va tendensiyalar — ishonchlilik darajasi bilan"
        actions={
          <Field label="Guruhlash">
            <Select value={groupBy} onChange={(e) => setGroupBy(e.target.value)} className="w-44">
              {individual && <option value="employee">Xodimlar</option>}
              <option value="department">Bo‘limlar</option>
              {individual && <option value="device">Qurilmalar</option>}
            </Select>
          </Field>
        }
      />
      <FilterBar value={filters} onChange={setFilters} />
      {q.isPending ? (
        <LoadingState />
      ) : q.isError ? (
        <ErrorState error={q.error} onRetry={() => q.refetch()} />
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
            <FormulaCard report={q.data} />
          </div>
          <Card>
            <CardHeader title="Batafsil" subtitle="Qatorni bosib metrikalarni ko‘ring" />
            {q.data.rows.length === 0 ? <EmptyState title="Ma’lumot yo‘q" /> : <ReportTable report={q.data} />}
          </Card>
        </>
      )}
    </>
  )
}
