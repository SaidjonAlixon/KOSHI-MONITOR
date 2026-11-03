import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { useState } from 'react'
import { Field, Input, Modal, PageHeader, Pagination, tableClass, tbodyClass, td, th, theadClass } from '@/components/forms'
import { Badge, Card, CardHeader, EmptyState, ErrorState, LoadingState, formatDateTime } from '@/components/ui'
import { api } from '@/lib/api'
import type { AuditLog, PagedResult } from '@/lib/types'

function prettyJson(s: string | null): string {
  if (!s) return '—'
  try {
    return JSON.stringify(JSON.parse(s), null, 2)
  } catch {
    return s
  }
}

export function AuditPage() {
  const [page, setPage] = useState(1)
  const [action, setAction] = useState('')
  const [search, setSearch] = useState('')
  const [from, setFrom] = useState('')
  const [to, setTo] = useState('')
  const [selected, setSelected] = useState<AuditLog | null>(null)
  const q = useQuery({
    queryKey: ['audit', page, action, search, from, to],
    queryFn: ({ signal }) =>
      api<PagedResult<AuditLog>>('/api/audit', {
        query: { page, pageSize: 50, action, search, from: from ? new Date(`${from}T00:00:00`).toISOString() : undefined, to: to ? new Date(new Date(`${to}T00:00:00`).getTime() + 86_400_000).toISOString() : undefined },
        signal,
      }),
    placeholderData: keepPreviousData,
  })

  return (
    <>
      <PageHeader title="Audit Logs" subtitle="Kim, qachon, qaysi qurilmada va qanday amalni bajargani. Jurnal faqat o‘qish uchun." />
      <div className="grid gap-3 rounded-2xl border border-slate-200/80 bg-white p-4 sm:grid-cols-4 dark:border-white/10 dark:bg-ink-900">
        <Field label="Amal (prefiks)">
          <Input placeholder="device., policy., auth.login" value={action} onChange={(e) => (setAction(e.target.value), setPage(1))} />
        </Field>
        <Field label="Qidiruv">
          <Input placeholder="e-mail yoki obyekt ID" value={search} onChange={(e) => (setSearch(e.target.value), setPage(1))} />
        </Field>
        <Field label="Dan">
          <Input type="date" value={from} onChange={(e) => (setFrom(e.target.value), setPage(1))} />
        </Field>
        <Field label="Gacha">
          <Input type="date" value={to} onChange={(e) => (setTo(e.target.value), setPage(1))} />
        </Field>
      </div>
      <Card>
        <CardHeader title="Jurnal" subtitle={q.data ? `${q.data.total} ta yozuv` : undefined} />
        {q.isPending ? (
          <LoadingState />
        ) : q.isError ? (
          <ErrorState error={q.error} onRetry={() => q.refetch()} />
        ) : q.data.items.length === 0 ? (
          <EmptyState title="Yozuvlar topilmadi" />
        ) : (
          <div className="overflow-x-auto">
            <table className={tableClass}>
              <thead className={theadClass}>
                <tr>
                  <th className={th}>Vaqt</th>
                  <th className={th}>Kim</th>
                  <th className={th}>Amal</th>
                  <th className={th}>Obyekt</th>
                  <th className={th}>Natija</th>
                  <th className={th}>IP</th>
                </tr>
              </thead>
              <tbody className={tbodyClass}>
                {q.data.items.map((a) => (
                  <tr key={a.id} className="cursor-pointer hover:bg-slate-50/60 dark:hover:bg-white/[0.02]" onClick={() => setSelected(a)}>
                    <td className={`${td} text-xs`}>{formatDateTime(a.occurredAt)}</td>
                    <td className={`${td} text-xs`}>
                      {a.actorEmail ?? (a.actorDeviceId ? `Qurilma ${a.actorDeviceId.slice(0, 8)}` : a.actorType)}
                    </td>
                    <td className={`${td} font-mono text-xs`}>{a.action}</td>
                    <td className={`${td} text-xs`}>
                      {a.entityType ?? '—'} {a.entityId ? <span className="text-slate-400">{a.entityId.slice(0, 8)}</span> : null}
                    </td>
                    <td className={td}>
                      <Badge tone={a.success ? 'green' : 'red'}>{a.success ? 'OK' : 'Rad etildi'}</Badge>
                    </td>
                    <td className={`${td} text-xs text-slate-500`}>{a.ipAddress ?? '—'}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
        {q.data && <Pagination page={page} pageSize={q.data.pageSize} total={q.data.total} onChange={setPage} />}
      </Card>
      <Modal open={selected !== null} title="Audit yozuvi" onClose={() => setSelected(null)} wide>
        {selected && (
          <dl className="grid gap-3 text-sm sm:grid-cols-2">
            {[
              ['Vaqt', formatDateTime(selected.occurredAt)],
              ['Aktor turi', selected.actorType],
              ['Foydalanuvchi', selected.actorEmail ?? '—'],
              ['Qurilma', selected.actorDeviceId ?? '—'],
              ['Amal', selected.action],
              ['Obyekt', `${selected.entityType ?? '—'} ${selected.entityId ?? ''}`],
              ['IP', selected.ipAddress ?? '—'],
              ['Natija', selected.success ? 'Muvaffaqiyatli' : 'Rad etildi'],
            ].map(([k, v]) => (
              <div key={k}>
                <dt className="text-xs text-slate-500">{k}</dt>
                <dd className="break-all font-medium">{v}</dd>
              </div>
            ))}
            <div className="sm:col-span-2">
              <dt className="text-xs text-slate-500">Tafsilotlar</dt>
              <dd>
                <pre className="mt-1 max-h-64 overflow-auto rounded-xl bg-slate-50 p-3 text-xs dark:bg-white/5">{prettyJson(selected.details)}</pre>
              </dd>
            </div>
          </dl>
        )}
      </Modal>
    </>
  )
}
