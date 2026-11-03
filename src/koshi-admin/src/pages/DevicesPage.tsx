import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Ban, Copy, Download, KeyRound, MonitorPlay, Pencil, Plus, RotateCcw, ShieldOff } from 'lucide-react'
import { useState, type FormEvent } from 'react'
import { Link, useSearchParams } from 'react-router'
import { Field, Input, Modal, PageHeader, Pagination, Select, SortHeader, tableClass, tbodyClass, td, th, theadClass, useEmployeesLookup, type SortState } from '@/components/forms'
import { Badge, Button, Card, CardHeader, EmptyState, ErrorState, LoadingState, formatDateTime } from '@/components/ui'
import { api } from '@/lib/api'
import { Permissions, useAuth } from '@/lib/auth'
import { AGENT_INSTALLER } from '@/lib/labels'
import type { ActivationCode, Device, PagedResult } from '@/lib/types'

const statusTone = { Active: 'green', Pending: 'blue', Blocked: 'red', Revoked: 'gray' } as const
const statusLabel = { Active: 'Faol', Pending: 'Aktivatsiya kutilmoqda', Blocked: 'Bloklangan', Revoked: 'Bekor qilingan' } as const

function DeviceForm({ device, onDone, onCode }: { device: Device | null; onDone: () => void; onCode: (c: ActivationCode) => void }) {
  const qc = useQueryClient()
  const emps = useEmployeesLookup()
  const save = useMutation({
    mutationFn: (body: { displayName: string; employeeId: string | null }): Promise<Device | ActivationCode> =>
      device
        ? api<Device>(`/api/devices/${device.id}`, { method: 'PUT', body })
        : api<ActivationCode>('/api/devices', { method: 'POST', body: { ...body, activationCodeTtlMinutes: 60 } }),
    onSuccess: (r) => {
      qc.invalidateQueries({ queryKey: ['devices'] })
      if (!device) onCode(r as ActivationCode)
      onDone()
    },
  })
  const submit = (e: FormEvent<HTMLFormElement>) => {
    e.preventDefault()
    const f = new FormData(e.currentTarget)
    save.mutate({ displayName: String(f.get('displayName')).trim(), employeeId: String(f.get('employeeId')) || null })
  }
  return (
    <form onSubmit={submit} className="space-y-4">
      <Field label="Qurilma nomi">
        <Input name="displayName" required maxLength={120} defaultValue={device?.displayName} placeholder="masalan, BUX-PC-07" />
      </Field>
      <Field label="Biriktirilgan xodim" hint={device ? 'Boshqa xodimga o‘tkazish audit jurnaliga yoziladi' : undefined}>
        <Select name="employeeId" defaultValue={device?.employeeId ?? ''}>
          <option value="">Biriktirilmagan</option>
          {(emps.data?.items ?? []).map((e) => (
            <option key={e.id} value={e.id}>
              {e.fullName} — {e.position ?? 'lavozim yo‘q'}
            </option>
          ))}
        </Select>
      </Field>
      {save.isError && <ErrorState error={save.error} />}
      <div className="flex justify-end gap-2">
        <Button type="button" variant="secondary" onClick={onDone}>
          Bekor qilish
        </Button>
        <Button type="submit" loading={save.isPending}>
          {device ? 'Saqlash' : 'Yaratish va kod olish'}
        </Button>
      </div>
    </form>
  )
}

export function DevicesPage() {
  const { can } = useAuth()
  const qc = useQueryClient()
  const [params, setParams] = useSearchParams()
  const page = Number(params.get('page') ?? 1)
  const search = params.get('search') ?? ''
  const status = params.get('status') ?? ''
  const online = params.get('online') ?? ''
  const [sort, setSort] = useState<SortState>({ by: 'displayName', dir: 'asc' })
  const [code, setCode] = useState<ActivationCode | null>(null)
  const [editing, setEditing] = useState<Device | 'new' | null>(null)
  const manage = can(Permissions.DevicesManage)

  const q = useQuery({
    queryKey: ['devices', page, search, status, online, sort],
    queryFn: ({ signal }) => api<PagedResult<Device>>('/api/devices', { query: { page, pageSize: 20, search, status, online, sortBy: sort.by, sortDir: sort.dir }, signal }),
    placeholderData: keepPreviousData,
  })

  const action = useMutation({
    mutationFn: ({ id, op }: { id: string; op: 'block' | 'unblock' | 'revoke' | 'activation-code' }): Promise<ActivationCode | Device> =>
      op === 'activation-code'
        ? api<ActivationCode>(`/api/devices/${id}/activation-code`, { method: 'POST', body: { ttlMinutes: 60 } })
        : api<Device>(`/api/devices/${id}/${op}`, { method: 'POST', body: op === 'block' ? { reason: 'Administrator tomonidan bloklandi' } : undefined }),
    onSuccess: (result, vars) => {
      if (vars.op === 'activation-code') setCode(result as ActivationCode)
      qc.invalidateQueries({ queryKey: ['devices'] })
    },
  })

  const setFilter = (key: string, value: string) => {
    const next = new URLSearchParams(params)
    if (value) next.set(key, value)
    else next.delete(key)
    if (key !== 'page') next.delete('page')
    setParams(next)
  }

  return (
    <>
      <PageHeader
        title="Qurilmalar"
        subtitle="Ro‘yxatdan o‘tkazish, agent holati, oxirgi aloqa, bloklash va kirishni bekor qilish"
        actions={manage && (
          <>
            <a href={AGENT_INSTALLER} download className="inline-flex items-center gap-2 rounded-xl border border-slate-200 bg-white px-3.5 py-2 text-sm font-medium text-slate-700 hover:bg-slate-50 dark:border-white/10 dark:bg-white/5 dark:text-slate-200">
              <Download className="size-4" /> Agent installer (EXE)
            </a>
            <Button onClick={() => setEditing('new')}>
              <Plus className="size-4" /> Qurilma qo‘shish
            </Button>
          </>
        )}
      />

      {code && (
        <Card className="border-brand-300 bg-brand-50/60 p-5 dark:bg-brand-500/10">
          <div className="flex flex-wrap items-center justify-between gap-4">
            <div>
              <p className="text-sm font-semibold">Bir martalik aktivatsiya kodi</p>
              <p className="text-xs text-slate-500">Faqat hozir ko‘rsatiladi va bir marta ishlatiladi. Amal qilish muddati: {formatDateTime(code.expiresAt)}</p>
              <p className="mt-1 text-xs text-slate-500">
                Xodim kompyuterida <a href={AGENT_INSTALLER} className="text-brand-600 underline">KOSHI Agent installer</a>ni administrator sifatida ishga tushiring va shu kodni kiriting.
              </p>
            </div>
            <div className="flex items-center gap-2">
              <code className="rounded-xl bg-white px-4 py-2 font-mono text-lg tracking-widest dark:bg-ink-950">{code.activationCode}</code>
              <Button variant="secondary" onClick={() => navigator.clipboard.writeText(code.activationCode)} aria-label="Nusxa olish">
                <Copy className="size-4" />
              </Button>
              <Button variant="ghost" onClick={() => setCode(null)}>
                Yopish
              </Button>
            </div>
          </div>
        </Card>
      )}

      <Card>
        <CardHeader
          title="Qurilmalar ro‘yxati"
          subtitle={q.data ? `${q.data.total} ta qurilma` : undefined}
          actions={
            <div className="flex flex-wrap gap-2">
              <Input type="search" defaultValue={search} placeholder="Nom, host, xodim…" onKeyDown={(e) => e.key === 'Enter' && setFilter('search', e.currentTarget.value.trim())} className="w-52" />
              <Select value={status} onChange={(e) => setFilter('status', e.target.value)} aria-label="Holat" className="w-44">
                <option value="">Barcha holatlar</option>
                {Object.entries(statusLabel).map(([k, v]) => (
                  <option key={k} value={k}>
                    {v}
                  </option>
                ))}
              </Select>
              <Select value={online} onChange={(e) => setFilter('online', e.target.value)} aria-label="Aloqa" className="w-36">
                <option value="">Onlayn/offlayn</option>
                <option value="true">Onlayn</option>
                <option value="false">Offlayn</option>
              </Select>
            </div>
          }
        />
        {q.isPending ? (
          <LoadingState />
        ) : q.isError ? (
          <ErrorState error={q.error} onRetry={() => q.refetch()} />
        ) : q.data.items.length === 0 ? (
          <EmptyState title="Qurilma topilmadi" hint={search || status ? 'Filtrlarni o‘zgartirib ko‘ring.' : 'Birinchi qurilmani qo‘shing va agentga aktivatsiya kodini kiriting.'} />
        ) : (
          <div className="overflow-x-auto">
            <table className={tableClass}>
              <thead className={theadClass}>
                <tr>
                  <SortHeader label="Qurilma" field="displayName" sort={sort} onSort={setSort} />
                  <th className={th}>Xodim / bo‘lim</th>
                  <SortHeader label="Holat" field="status" sort={sort} onSort={setSort} />
                  <th className={th}>Windows / Agent</th>
                  <SortHeader label="Oxirgi aloqa" field="lastHeartbeatAt" sort={sort} onSort={setSort} />
                  <th className={th}>Policy</th>
                  {manage && <th className={`${th} text-right`}>Amallar</th>}
                </tr>
              </thead>
              <tbody className={tbodyClass}>
                {q.data.items.map((d) => (
                  <tr key={d.id} className="hover:bg-slate-50/60 dark:hover:bg-white/[0.02]">
                    <td className={td}>
                      <div className="flex items-center gap-2">
                        <div>
                          <p className="font-medium">{d.displayName}</p>
                          <p className="text-xs text-slate-500">{d.hostname ?? '—'}</p>
                        </div>
                        {can(Permissions.RemoteControl) && d.status === 'Active' && (
                          <Link to={`/app/remote/${d.id}`} title="Masofaviy boshqaruv" className={`ml-auto rounded-lg p-1.5 ${d.isOnline ? 'text-brand-600 hover:bg-brand-50 dark:hover:bg-brand-500/10' : 'pointer-events-none text-slate-300'}`}>
                            <MonitorPlay className="size-4" />
                          </Link>
                        )}
                      </div>
                    </td>
                    <td className={td}>
                      {d.employeeName ?? <span className="text-slate-400">biriktirilmagan</span>}
                      <p className="text-xs text-slate-500">{d.departmentName ?? ''}</p>
                    </td>
                    <td className={td}>
                      <div className="flex flex-wrap gap-1">
                        <Badge tone={statusTone[d.status]}>{statusLabel[d.status]}</Badge>
                        {d.status === 'Active' && <Badge tone={d.isOnline ? 'green' : 'gray'}>{d.isOnline ? 'Onlayn' : 'Offlayn'}</Badge>}
                      </div>
                    </td>
                    <td className={`${td} text-xs text-slate-500`}>
                      {d.osVersion ?? '—'}
                      <br />
                      {d.agentVersion ? `Agent v${d.agentVersion}` : '—'}
                    </td>
                    <td className={`${td} text-xs`}>{formatDateTime(d.lastHeartbeatAt)}</td>
                    <td className={`${td} text-xs`}>{d.appliedPolicyVersion ? `v${d.appliedPolicyVersion}` : '—'}</td>
                    {manage && (
                      <td className={td}>
                        <div className="flex justify-end gap-1">
                          <Button variant="ghost" title="Tahrirlash / xodimga biriktirish" onClick={() => setEditing(d)}>
                            <Pencil className="size-4" />
                          </Button>
                          {(d.status === 'Pending' || d.status === 'Revoked' || d.status === 'Active') && (
                            <Button variant="ghost" title="Yangi aktivatsiya kodi (qayta o‘rnatish)" onClick={() => action.mutate({ id: d.id, op: 'activation-code' })}>
                              <KeyRound className="size-4" />
                            </Button>
                          )}
                          {d.status === 'Active' && (
                            <Button variant="ghost" title="Bloklash" onClick={() => confirm(`${d.displayName} bloklansinmi? Telemetriya qabul qilinmaydi.`) && action.mutate({ id: d.id, op: 'block' })}>
                              <Ban className="size-4" />
                            </Button>
                          )}
                          {d.status === 'Blocked' && (
                            <Button variant="ghost" title="Blokdan chiqarish" onClick={() => action.mutate({ id: d.id, op: 'unblock' })}>
                              <RotateCcw className="size-4" />
                            </Button>
                          )}
                          {d.status !== 'Revoked' && (
                            <Button variant="ghost" title="Kirishni bekor qilish (yo‘qolgan qurilma / xodim ketgan)" onClick={() => confirm(`${d.displayName} credential’i bekor qilinsinmi?`) && action.mutate({ id: d.id, op: 'revoke' })}>
                              <ShieldOff className="size-4 text-rose-500" />
                            </Button>
                          )}
                        </div>
                      </td>
                    )}
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
        {action.isError && <ErrorState error={action.error} />}
        {q.data && <Pagination page={page} pageSize={q.data.pageSize} total={q.data.total} onChange={(p) => setFilter('page', String(p))} />}
      </Card>

      <Modal open={editing !== null} title={editing === 'new' ? 'Yangi qurilma' : 'Qurilmani tahrirlash'} onClose={() => setEditing(null)}>
        {editing !== null && <DeviceForm device={editing === 'new' ? null : editing} onDone={() => setEditing(null)} onCode={setCode} />}
      </Modal>
    </>
  )
}
