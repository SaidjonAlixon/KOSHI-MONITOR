import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { CheckCheck } from 'lucide-react'
import { useState } from 'react'
import { PageHeader, Pagination, Select, tableClass, tbodyClass, td, th, theadClass } from '@/components/forms'
import { Badge, Button, Card, CardHeader, EmptyState, ErrorState, LoadingState, formatDateTime } from '@/components/ui'
import { api } from '@/lib/api'
import { Permissions, useAuth } from '@/lib/auth'
import { ALERT_LABELS } from '@/lib/labels'
import type { Alert, PagedResult } from '@/lib/types'

const sevTone = { Info: 'blue', Warning: 'amber', Critical: 'red' } as const

export function AlertsPage() {
  const { can } = useAuth()
  const qc = useQueryClient()
  const [page, setPage] = useState(1)
  const [open, setOpen] = useState('true')
  const [type, setType] = useState('')
  const q = useQuery({
    queryKey: ['alerts', page, open, type],
    queryFn: ({ signal }) => api<PagedResult<Alert>>('/api/alerts', { query: { page, pageSize: 25, open, type }, signal }),
    placeholderData: keepPreviousData,
    refetchInterval: 30_000,
  })
  const ack = useMutation({
    mutationFn: (id: string) => api(`/api/alerts/${id}/acknowledge`, { method: 'POST' }),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['alerts'] }),
  })

  return (
    <>
      <PageHeader title="Alerts" subtitle="Aloqa uzilishi, siyosat xatolari, cheklov buzilishlari va xavfsizlik hodisalari" />
      <Card>
        <CardHeader
          title="Ogohlantirishlar"
          subtitle={q.data ? `${q.data.total} ta` : undefined}
          actions={
            <div className="flex gap-2">
              <Select value={open} onChange={(e) => (setOpen(e.target.value), setPage(1))} className="w-40" aria-label="Holat">
                <option value="true">Ochiq</option>
                <option value="false">Yopilgan</option>
                <option value="">Barchasi</option>
              </Select>
              <Select value={type} onChange={(e) => (setType(e.target.value), setPage(1))} className="w-48" aria-label="Turi">
                <option value="">Barcha turlar</option>
                {Object.entries(ALERT_LABELS).map(([k, v]) => (
                  <option key={k} value={k}>
                    {v}
                  </option>
                ))}
              </Select>
            </div>
          }
        />
        {q.isPending ? (
          <LoadingState />
        ) : q.isError ? (
          <ErrorState error={q.error} onRetry={() => q.refetch()} />
        ) : q.data.items.length === 0 ? (
          <EmptyState title="Ogohlantirishlar yo‘q" />
        ) : (
          <table className={tableClass}>
            <thead className={theadClass}>
              <tr>
                <th className={th}>Vaqt</th>
                <th className={th}>Turi</th>
                <th className={th}>Daraja</th>
                <th className={th}>Qurilma</th>
                <th className={th}>Xabar</th>
                <th className={th}>Holat</th>
                {can(Permissions.AlertsManage) && <th className={th} />}
              </tr>
            </thead>
            <tbody className={tbodyClass}>
              {q.data.items.map((a) => (
                <tr key={a.id}>
                  <td className={`${td} text-xs`}>{formatDateTime(a.createdAt)}</td>
                  <td className={td}>{ALERT_LABELS[a.type]}</td>
                  <td className={td}>
                    <Badge tone={sevTone[a.severity]}>{a.severity}</Badge>
                  </td>
                  <td className={td}>{a.deviceName ?? '—'}</td>
                  <td className={`${td} text-xs`}>{a.message}</td>
                  <td className={`${td} text-xs`}>{a.resolvedAt ? `Hal bo‘ldi ${formatDateTime(a.resolvedAt)}` : a.acknowledgedAt ? `Tasdiqlandi ${formatDateTime(a.acknowledgedAt)}` : <Badge tone="amber">Ochiq</Badge>}</td>
                  {can(Permissions.AlertsManage) && (
                    <td className={td}>
                      {!a.acknowledgedAt && !a.resolvedAt && (
                        <Button variant="ghost" title="Tasdiqlash" onClick={() => ack.mutate(a.id)}>
                          <CheckCheck className="size-4" />
                        </Button>
                      )}
                    </td>
                  )}
                </tr>
              ))}
            </tbody>
          </table>
        )}
        {q.data && <Pagination page={page} pageSize={q.data.pageSize} total={q.data.total} onChange={setPage} />}
      </Card>
    </>
  )
}
