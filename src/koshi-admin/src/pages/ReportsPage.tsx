import { useMutation, useQuery } from '@tanstack/react-query'
import { Download, Printer } from 'lucide-react'
import { useState } from 'react'
import { Bar, BarChart, CartesianGrid, Legend, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts'
import { Field, FilterBar, Input, PageHeader, Select, defaultFilters, toLocalInput, type Filters } from '@/components/forms'
import { FormulaCard, ReportTable } from '@/components/ReportTable'
import { Button, Card, CardHeader, EmptyState, ErrorState, LoadingState } from '@/components/ui'
import { api, downloadFile } from '@/lib/api'
import { Permissions, useAuth } from '@/lib/auth'
import type { Report } from '@/lib/types'

type Period = 'daily' | 'weekly' | 'monthly'
const periods: [Period, string][] = [
  ['daily', 'Kunlik'],
  ['weekly', 'Haftalik'],
  ['monthly', 'Oylik'],
]

export function ReportsPage() {
  const { can } = useAuth()
  const [period, setPeriod] = useState<Period>('daily')
  const [date, setDate] = useState(toLocalInput(new Date()))
  const [groupBy, setGroupBy] = useState('employee')
  const [filters, setFilters] = useState<Filters>(defaultFilters())
  const query = { date, groupBy, departmentId: filters.departmentId, branch: filters.branch, employeeId: filters.employeeId, deviceId: filters.deviceId }

  const q = useQuery({ queryKey: ['report', period, query], queryFn: ({ signal }) => api<Report>(`/api/reports/${period}`, { query, signal }) })
  const exportCsv = useMutation({ mutationFn: () => downloadFile('/api/reports/export', { type: period, ...query }, `koshi-${period}.csv`) })
  const daily = (q.data?.daily ?? []).map((p) => ({ day: p.date.slice(5), Faol: +(p.activeSeconds / 3600).toFixed(1), Idle: +(p.idleSeconds / 3600).toFixed(1), Nomaʼlum: +(p.unknownSeconds / 3600).toFixed(1) }))

  return (
    <>
      <PageHeader
        title="Hisobotlar"
        subtitle="Kunlik, haftalik va oylik hisobotlar. CSV fayl Excel’da ochiladi; PDF uchun “Chop etish → PDF sifatida saqlash”."
        actions={
          <>
            {can(Permissions.ReportsExport) && (
              <Button variant="secondary" loading={exportCsv.isPending} onClick={() => exportCsv.mutate()}>
                <Download className="size-4" /> CSV / Excel
              </Button>
            )}
            <Button variant="secondary" onClick={() => window.print()}>
              <Printer className="size-4" /> PDF / Chop etish
            </Button>
          </>
        }
      />
      <div className="flex flex-wrap items-end gap-3 print:hidden">
        <div className="inline-flex rounded-xl border border-slate-200 bg-white p-1 dark:border-white/10 dark:bg-ink-900">
          {periods.map(([k, v]) => (
            <button key={k} onClick={() => setPeriod(k)} className={`rounded-lg px-4 py-1.5 text-sm font-medium ${period === k ? 'brand-gradient text-white' : 'text-slate-600 hover:bg-slate-100 dark:text-slate-300 dark:hover:bg-white/5'}`}>
              {v}
            </button>
          ))}
        </div>
        <Field label={period === 'daily' ? 'Sana' : period === 'weekly' ? 'Hafta ichidagi sana' : 'Oy ichidagi sana'}>
          <Input type="date" value={date} onChange={(e) => setDate(e.target.value)} />
        </Field>
        <Field label="Guruhlash">
          <Select value={groupBy} onChange={(e) => setGroupBy(e.target.value)} className="w-40">
            <option value="employee">Xodimlar</option>
            <option value="department">Bo‘limlar</option>
            <option value="device">Qurilmalar</option>
          </Select>
        </Field>
      </div>
      <div className="print:hidden">
        <FilterBar value={filters} onChange={setFilters} show={['branch', 'department', 'employee', 'device']} />
      </div>
      {exportCsv.isError && <ErrorState error={exportCsv.error} />}
      {q.isPending ? (
        <LoadingState />
      ) : q.isError ? (
        <ErrorState error={q.error} onRetry={() => q.refetch()} />
      ) : (
        <>
          {period !== 'daily' && (
            <Card>
              <CardHeader title="Kunlar bo‘yicha (soat)" />
              <div className="h-64 p-4">
                <ResponsiveContainer>
                  <BarChart data={daily}>
                    <CartesianGrid strokeDasharray="3 3" stroke="#94a3b833" />
                    <XAxis dataKey="day" fontSize={11} />
                    <YAxis fontSize={11} />
                    <Tooltip />
                    <Legend />
                    <Bar dataKey="Faol" stackId="a" fill="#2f80ff" />
                    <Bar dataKey="Idle" stackId="a" fill="#a78bfa" />
                    <Bar dataKey="Nomaʼlum" stackId="a" fill="#cbd5e1" />
                  </BarChart>
                </ResponsiveContainer>
              </div>
            </Card>
          )}
          <Card>
            <CardHeader title={`${periods.find((p) => p[0] === period)?.[1]} hisobot`} subtitle={`${q.data.rows.length} qator · guruhlash: ${q.data.groupBy}`} />
            {q.data.rows.length === 0 ? <EmptyState title="Bu davr uchun ma’lumot yo‘q" /> : <ReportTable report={q.data} />}
          </Card>
          <FormulaCard report={q.data} />
        </>
      )}
    </>
  )
}
