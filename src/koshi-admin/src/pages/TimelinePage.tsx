import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { useState } from 'react'
import { FilterBar, PageHeader, Pagination, defaultFilters, filtersToQuery, tableClass, tbodyClass, td, th, theadClass, type Filters } from '@/components/forms'
import { Badge, Card, CardHeader, EmptyState, ErrorState, LoadingState, formatDateTime, formatDuration } from '@/components/ui'
import { api } from '@/lib/api'
import type { PagedResult, TimelineItem } from '@/lib/types'

const kindTone = { Application: 'blue', Idle: 'gray', Paused: 'amber', PolicyViolation: 'red' } as const
const kindLabel = { Application: 'Dastur', Idle: 'Idle', Paused: 'To‘xtatilgan', PolicyViolation: 'Cheklov buzilishi' } as const

export function TimelinePage() {
  const [filters, setFilters] = useState<Filters>(defaultFilters(1))
  const [page, setPage] = useState(1)
  const q = useQuery({
    queryKey: ['timeline', filters, page],
    queryFn: ({ signal }) => api<PagedResult<TimelineItem>>('/api/monitoring/timeline', { query: { ...filtersToQuery(filters), page, pageSize: 50 }, signal }),
    placeholderData: keepPreviousData,
  })

  return (
    <>
      <PageHeader title="Faoliyat tarixi" subtitle="Kun davomida dasturlar, bo‘sh va to‘xtatilgan holatlar tarixi" />
      <FilterBar value={filters} onChange={(f) => (setFilters(f), setPage(1))} />
      <Card>
        <CardHeader title="Hodisalar" subtitle={q.data ? `${q.data.total} ta yozuv` : undefined} />
        {q.isPending ? (
          <LoadingState />
        ) : q.isError ? (
          <ErrorState error={q.error} onRetry={() => q.refetch()} />
        ) : q.data.items.length === 0 ? (
          <EmptyState title="Bu davr uchun hodisalar yo‘q" hint="Sana oralig‘ini kengaytiring yoki boshqa qurilmani tanlang." />
        ) : (
          <div className="overflow-x-auto">
            <table className={tableClass}>
              <thead className={theadClass}>
                <tr>
                  <th className={th}>Boshlanish</th>
                  <th className={th}>Tugash</th>
                  <th className={th}>Davomiylik</th>
                  <th className={th}>Qurilma</th>
                  <th className={th}>Turi</th>
                  <th className={th}>Dastur</th>
                  <th className={th}>Oyna</th>
                </tr>
              </thead>
              <tbody className={tbodyClass}>
                {q.data.items.map((i) => (
                  <tr key={i.id}>
                    <td className={`${td} text-xs`}>{formatDateTime(i.startedAt)}</td>
                    <td className={`${td} text-xs`}>{formatDateTime(i.endedAt)}</td>
                    <td className={`${td} text-xs`}>{formatDuration(i.durationSeconds)}</td>
                    <td className={td}>{i.deviceName}</td>
                    <td className={td}>
                      <Badge tone={kindTone[i.kind]}>{kindLabel[i.kind]}</Badge>
                    </td>
                    <td className={`${td} font-mono text-xs`}>{i.processName ?? '—'}</td>
                    <td className={`${td} max-w-xs truncate text-xs text-slate-500`} title={i.windowTitle ?? undefined}>
                      {i.windowTitle ?? '—'}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
        {q.data && <Pagination page={page} pageSize={q.data.pageSize} total={q.data.total} onChange={setPage} />}
      </Card>
    </>
  )
}
