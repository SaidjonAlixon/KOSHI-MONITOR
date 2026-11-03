import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { History, Pencil, Plus, Send, ShieldAlert, Undo2 } from 'lucide-react'
import { useEffect, useState, type FormEvent } from 'react'
import { Checkbox, Field, Input, Modal, PageHeader, Select, Textarea, useDepartments, useDevicesLookup } from '@/components/forms'
import { Badge, Button, Card, CardHeader, EmptyState, ErrorState, LoadingState, formatDateTime } from '@/components/ui'
import { ApiError, api } from '@/lib/api'
import { Permissions, useAuth } from '@/lib/auth'
import { CAPTURE_LABELS, MODE_LABELS } from '@/lib/labels'
import type { Policy, PolicyAssignment, PolicyMode, TitleCapture, WorkSchedule } from '@/lib/types'

interface PolicyDraft {
  name: string
  description: string
  mode: PolicyMode
  priority: number
  workScheduleId: string
  idleThresholdSeconds: number
  samplingIntervalSeconds: number
  heartbeatIntervalSeconds: number
  windowTitleCapture: TitleCapture
  trackBackgroundProcesses: boolean
  allDevices: boolean
  departmentIds: string[]
  deviceIds: string[]
}

function toDraft(p: Policy | null): PolicyDraft {
  return {
    name: p?.name ?? '',
    description: p?.description ?? '',
    mode: p?.mode ?? 'AuditOnly',
    priority: p?.priority ?? 100,
    workScheduleId: p?.workScheduleId ?? '',
    idleThresholdSeconds: p?.idleThresholdSeconds ?? 300,
    samplingIntervalSeconds: p?.samplingIntervalSeconds ?? 3,
    heartbeatIntervalSeconds: p?.heartbeatIntervalSeconds ?? 30,
    windowTitleCapture: p?.windowTitleCapture ?? 'Masked',
    trackBackgroundProcesses: p?.trackBackgroundProcesses ?? true,
    allDevices: p ? p.assignments.some((a) => a.scope === 'AllDevices') : true,
    departmentIds: p?.assignments.filter((a) => a.scope === 'Department').map((a) => a.departmentId!) ?? [],
    deviceIds: p?.assignments.filter((a) => a.scope === 'Device').map((a) => a.deviceId!) ?? [],
  }
}

function PolicyForm({ policy, onDone }: { policy: Policy | null; onDone: (p: Policy) => void }) {
  const qc = useQueryClient()
  const deps = useDepartments()
  const devs = useDevicesLookup()
  const schedules = useQuery({ queryKey: ['schedules'], queryFn: () => api<WorkSchedule[]>('/api/schedules') })
  const [d, setD] = useState(() => toDraft(policy))
  const set = (patch: Partial<PolicyDraft>) => setD((x) => ({ ...x, ...patch }))
  const toggle = (list: string[], id: string) => (list.includes(id) ? list.filter((x) => x !== id) : [...list, id])

  const save = useMutation({
    mutationFn: () => {
      const assignments: PolicyAssignment[] = [
        ...(d.allDevices ? [{ scope: 'AllDevices' as const, departmentId: null, deviceId: null }] : []),
        ...d.departmentIds.map((id) => ({ scope: 'Department' as const, departmentId: id, deviceId: null })),
        ...d.deviceIds.map((id) => ({ scope: 'Device' as const, departmentId: null, deviceId: id })),
      ]
      const body = { ...d, description: d.description || null, workScheduleId: d.workScheduleId || null, assignments }
      return policy ? api<Policy>(`/api/policies/${policy.id}`, { method: 'PUT', body }) : api<Policy>('/api/policies', { method: 'POST', body })
    },
    onSuccess: (p) => {
      qc.invalidateQueries({ queryKey: ['policies'] })
      onDone(p)
    },
  })
  const submit = (e: FormEvent) => {
    e.preventDefault()
    save.mutate()
  }

  return (
    <form onSubmit={submit} className="space-y-5">
      <div className="grid gap-4 sm:grid-cols-2">
        <Field label="Nomi">
          <Input required maxLength={120} value={d.name} onChange={(e) => set({ name: e.target.value })} />
        </Field>
        <Field label="Rejim" hint="Avval audit-only’da sinang, keyin enforcement’ga o‘ting">
          <Select value={d.mode} onChange={(e) => set({ mode: e.target.value as PolicyMode })}>
            {Object.entries(MODE_LABELS).map(([k, v]) => (
              <option key={k} value={k}>
                {v}
              </option>
            ))}
          </Select>
        </Field>
        <Field label="Tavsif" className="sm:col-span-2">
          <Textarea rows={2} maxLength={1000} value={d.description} onChange={(e) => set({ description: e.target.value })} />
        </Field>
        <Field label="Ish jadvali">
          <Select value={d.workScheduleId} onChange={(e) => set({ workScheduleId: e.target.value })}>
            <option value="">Standart</option>
            {(schedules.data ?? []).map((s) => (
              <option key={s.id} value={s.id}>
                {s.name}
              </option>
            ))}
          </Select>
        </Field>
        <Field label="Oyna sarlavhasi">
          <Select value={d.windowTitleCapture} onChange={(e) => set({ windowTitleCapture: e.target.value as TitleCapture })}>
            {Object.entries(CAPTURE_LABELS).map(([k, v]) => (
              <option key={k} value={k}>
                {v}
              </option>
            ))}
          </Select>
        </Field>
        <Field label="Idle chegarasi (soniya)">
          <Input type="number" min={60} max={7200} value={d.idleThresholdSeconds} onChange={(e) => set({ idleThresholdSeconds: Number(e.target.value) })} />
        </Field>
        <Field label="Tekshirish intervali (2–5 s)">
          <Input type="number" min={2} max={5} value={d.samplingIntervalSeconds} onChange={(e) => set({ samplingIntervalSeconds: Number(e.target.value) })} />
        </Field>
        <Field label="Heartbeat intervali (s)">
          <Input type="number" min={15} max={600} value={d.heartbeatIntervalSeconds} onChange={(e) => set({ heartbeatIntervalSeconds: Number(e.target.value) })} />
        </Field>
        <Field label="Ustuvorlik" hint="Kichik raqam ustun">
          <Input type="number" min={1} max={10000} value={d.priority} onChange={(e) => set({ priority: Number(e.target.value) })} />
        </Field>
      </div>
      <Checkbox label="Fondagi jarayonlar vaqtini ham hisoblash" checked={d.trackBackgroundProcesses} onChange={(e) => set({ trackBackgroundProcesses: e.target.checked })} />

      <div className="rounded-xl border border-slate-200 p-4 dark:border-white/10">
        <p className="text-sm font-semibold">Qo‘llaniladigan qurilmalar</p>
        <div className="mt-3 space-y-3">
          <Checkbox label="Barcha qurilmalar" checked={d.allDevices} onChange={(e) => set({ allDevices: e.target.checked })} />
          <div>
            <p className="mb-1 text-xs font-medium text-slate-500">Bo‘limlar</p>
            <div className="flex flex-wrap gap-3">
              {(deps.data ?? []).map((dep) => (
                <Checkbox key={dep.id} label={dep.name} checked={d.departmentIds.includes(dep.id)} onChange={() => set({ departmentIds: toggle(d.departmentIds, dep.id) })} />
              ))}
            </div>
          </div>
          <div>
            <p className="mb-1 text-xs font-medium text-slate-500">Alohida qurilmalar</p>
            <div className="flex max-h-32 flex-wrap gap-3 overflow-y-auto">
              {(devs.data?.items ?? []).map((dev) => (
                <Checkbox key={dev.id} label={dev.displayName} checked={d.deviceIds.includes(dev.id)} onChange={() => set({ deviceIds: toggle(d.deviceIds, dev.id) })} />
              ))}
            </div>
          </div>
        </div>
      </div>
      {save.isError && <ErrorState error={save.error} />}
      <div className="flex justify-end gap-2">
        <Button type="submit" loading={save.isPending}>
          Qoralamani saqlash
        </Button>
      </div>
    </form>
  )
}

function PublishDialog({ policy, onClose }: { policy: Policy; onClose: () => void }) {
  const qc = useQueryClient()
  const [note, setNote] = useState('')
  const [approve, setApprove] = useState(false)
  const publish = useMutation({
    mutationFn: () => api<Policy>(`/api/policies/${policy.id}/publish`, { method: 'POST', body: { note: note || null, approveEnforcement: approve } }),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['policies'] })
      onClose()
    },
  })
  const errors = publish.error instanceof ApiError ? Object.values(publish.error.errors).flat() : []
  return (
    <div className="space-y-4">
      <p className="text-sm text-slate-600 dark:text-slate-300">
        Siyosat server tomonida tekshiriladi, yangi versiya (v{policy.version + 1}) yaratiladi va mos qurilmalarga yetkaziladi. Har bir qurilmaning natijasi “Pending → Applied / Failed / Unsupported / Requires administrator approval” sifatida qayd qilinadi.
      </p>
      <Field label="Izoh (audit uchun)">
        <Input maxLength={500} value={note} onChange={(e) => setNote(e.target.value)} />
      </Field>
      {policy.mode === 'Enforce' && (
        <div className="rounded-xl border border-amber-300 bg-amber-50 p-3 dark:border-amber-500/30 dark:bg-amber-500/10">
          <p className="flex items-center gap-2 text-sm font-semibold text-amber-800 dark:text-amber-200">
            <ShieldAlert className="size-4" /> Enforcement rejimi
          </p>
          <p className="mt-1 text-xs text-amber-800 dark:text-amber-200">Taqiqlangan dasturlar Windows mexanizmlari (AppLocker) orqali bloklanadi. Avval audit-only rejimda sinovdan o‘tkazganingizga ishonch hosil qiling.</p>
          <div className="mt-2">
            <Checkbox label="Enforcement’ni administrator sifatida tasdiqlayman" checked={approve} onChange={(e) => setApprove(e.target.checked)} />
          </div>
        </div>
      )}
      {publish.isError && (
        <div className="rounded-xl bg-rose-50 p-3 text-sm text-rose-700 dark:bg-rose-500/10 dark:text-rose-300">
          <p className="font-medium">{(publish.error as Error).message}</p>
          <ul className="mt-1 list-disc pl-5 text-xs">
            {errors.map((e) => (
              <li key={e}>{e}</li>
            ))}
          </ul>
        </div>
      )}
      <div className="flex justify-end gap-2">
        <Button variant="secondary" onClick={onClose}>
          Bekor qilish
        </Button>
        <Button loading={publish.isPending} onClick={() => publish.mutate()} disabled={policy.mode === 'Enforce' && !approve}>
          <Send className="size-4" /> Publish
        </Button>
      </div>
    </div>
  )
}

export function PoliciesPage() {
  const { can } = useAuth()
  const qc = useQueryClient()
  const manage = can(Permissions.PoliciesManage)
  const q = useQuery({ queryKey: ['policies'], queryFn: () => api<Policy[]>('/api/policies') })
  const [selectedId, setSelectedId] = useState<string | null>(null)
  const [editing, setEditing] = useState<Policy | 'new' | null>(null)
  const [publishing, setPublishing] = useState(false)
  const selected = q.data?.find((p) => p.id === selectedId) ?? q.data?.[0] ?? null

  useEffect(() => {
    if (!selectedId && q.data?.[0]) setSelectedId(q.data[0].id)
  }, [q.data, selectedId])

  const rollback = useMutation({
    mutationFn: ({ id, target }: { id: string; target?: number }) => api<Policy>(`/api/policies/${id}/rollback`, { method: 'POST', body: { targetVersion: target ?? null } }),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['policies'] }),
  })

  return (
    <>
      <PageHeader
        title="Policies"
        subtitle="Monitoring jadvali, sarlavha siyosati, cheklovlar, versiyalash va favqulodda rollback"
        actions={manage && (
          <Button onClick={() => setEditing('new')}>
            <Plus className="size-4" /> Yangi siyosat
          </Button>
        )}
      />
      {q.isPending ? (
        <LoadingState />
      ) : q.isError ? (
        <ErrorState error={q.error} onRetry={() => q.refetch()} />
      ) : q.data.length === 0 ? (
        <EmptyState title="Siyosatlar yo‘q" />
      ) : (
        <div className="grid gap-6 xl:grid-cols-[340px_1fr]">
          <div className="space-y-3">
            {q.data.map((p) => (
              <button
                key={p.id}
                onClick={() => setSelectedId(p.id)}
                className={`w-full rounded-2xl border p-4 text-left transition ${selected?.id === p.id ? 'border-brand-400 bg-brand-50/60 dark:bg-brand-500/10' : 'border-slate-200 bg-white hover:border-slate-300 dark:border-white/10 dark:bg-ink-900'}`}
              >
                <div className="flex items-center justify-between gap-2">
                  <p className="font-semibold">{p.name}</p>
                  <Badge tone={p.status === 'Published' ? 'green' : 'gray'}>{p.status === 'Published' ? `v${p.publishedVersion}` : 'Qoralama'}</Badge>
                </div>
                <div className="mt-2 flex flex-wrap gap-1">
                  <Badge tone={p.mode === 'Enforce' ? 'red' : 'blue'}>{MODE_LABELS[p.mode]}</Badge>
                  {p.hasUnpublishedChanges && <Badge tone="amber">Publish qilinmagan o‘zgarish</Badge>}
                </div>
              </button>
            ))}
          </div>

          {selected && (
            <div className="space-y-6">
              <Card>
                <CardHeader
                  title={selected.name}
                  subtitle={selected.description ?? undefined}
                  actions={manage && (
                    <div className="flex gap-2">
                      <Button variant="secondary" onClick={() => setEditing(selected)}>
                        <Pencil className="size-4" /> Tahrirlash
                      </Button>
                      <Button onClick={() => setPublishing(true)}>
                        <Send className="size-4" /> Publish
                      </Button>
                      {selected.publishedVersion && selected.versions.length > 1 && (
                        <Button variant="danger" loading={rollback.isPending} onClick={() => confirm('Oldingi versiyaga favqulodda rollback qilinsinmi?') && rollback.mutate({ id: selected.id })}>
                          <Undo2 className="size-4" /> Rollback
                        </Button>
                      )}
                    </div>
                  )}
                />
                <dl className="grid gap-4 p-5 text-sm sm:grid-cols-3">
                  {[
                    ['Rejim', MODE_LABELS[selected.mode]],
                    ['Oyna sarlavhasi', CAPTURE_LABELS[selected.windowTitleCapture]],
                    ['Idle chegarasi', `${selected.idleThresholdSeconds} s`],
                    ['Tekshirish intervali', `${selected.samplingIntervalSeconds} s`],
                    ['Heartbeat', `${selected.heartbeatIntervalSeconds} s`],
                    ['Qoidalar', `${selected.rules.length} ta`],
                    ['Qamrov', selected.assignments.map((a) => (a.scope === 'AllDevices' ? 'Barcha qurilmalar' : a.scope === 'Department' ? 'Bo‘lim' : 'Qurilma')).join(', ') || '—'],
                    ['Screenshot', 'O‘chirilgan (MVP)'],
                    ['Oxirgi publish', formatDateTime(selected.publishedAt)],
                  ].map(([k, v]) => (
                    <div key={k}>
                      <dt className="text-xs text-slate-500">{k}</dt>
                      <dd className="mt-0.5 font-medium">{v}</dd>
                    </div>
                  ))}
                </dl>
                {rollback.isError && <ErrorState error={rollback.error} />}
              </Card>

              <div className="grid gap-6 lg:grid-cols-2">
                <Card>
                  <CardHeader title="Qo‘llash natijalari" subtitle={selected.publishedVersion ? `v${selected.publishedVersion} bo‘yicha` : 'Hali publish qilinmagan'} />
                  <div className="grid grid-cols-2 gap-3 p-5 sm:grid-cols-3">
                    {[
                      ['Pending', selected.results.pending, 'blue'],
                      ['Applied', selected.results.applied, 'green'],
                      ['Failed', selected.results.failed, 'red'],
                      ['Unsupported', selected.results.unsupported, 'gray'],
                      ['Admin tasdig‘i kerak', selected.results.requiresAdministratorApproval, 'amber'],
                    ].map(([k, v, t]) => (
                      <div key={k as string} className="rounded-xl border border-slate-100 p-3 dark:border-white/5">
                        <Badge tone={t as 'blue'}>{k}</Badge>
                        <p className="mt-2 text-2xl font-semibold tabular-nums">{v}</p>
                      </div>
                    ))}
                  </div>
                </Card>
                <Card>
                  <CardHeader title="Versiyalar" subtitle="Har bir publish va rollback auditga yoziladi" />
                  {selected.versions.length === 0 ? (
                    <EmptyState title="Versiyalar yo‘q" />
                  ) : (
                    <ul className="divide-y divide-slate-100 dark:divide-white/5">
                      {selected.versions.map((v) => (
                        <li key={v.version} className="flex items-center justify-between px-5 py-3 text-sm">
                          <span className="flex items-center gap-2">
                            <History className="size-4 text-slate-400" /> v{v.version}
                            {v.version === selected.publishedVersion && <Badge tone="green">faol</Badge>}
                            <span className="text-xs text-slate-500">{v.note}</span>
                          </span>
                          <span className="flex items-center gap-2 text-xs text-slate-500">
                            {formatDateTime(v.createdAt)}
                            {manage && v.version !== selected.publishedVersion && (
                              <Button variant="ghost" onClick={() => confirm(`v${v.version} ga qaytarilsinmi?`) && rollback.mutate({ id: selected.id, target: v.version })}>
                                <Undo2 className="size-3.5" />
                              </Button>
                            )}
                          </span>
                        </li>
                      ))}
                    </ul>
                  )}
                </Card>
              </div>
            </div>
          )}
        </div>
      )}

      <Modal open={editing !== null} title={editing === 'new' ? 'Yangi siyosat' : 'Siyosatni tahrirlash'} onClose={() => setEditing(null)} wide>
        {editing !== null && (
          <PolicyForm
            policy={editing === 'new' ? null : editing}
            onDone={(p) => {
              setEditing(null)
              setSelectedId(p.id)
            }}
          />
        )}
      </Modal>
      <Modal open={publishing && !!selected} title={`Publish: ${selected?.name ?? ''}`} onClose={() => setPublishing(false)}>
        {selected && <PublishDialog policy={selected} onClose={() => setPublishing(false)} />}
      </Modal>
    </>
  )
}
