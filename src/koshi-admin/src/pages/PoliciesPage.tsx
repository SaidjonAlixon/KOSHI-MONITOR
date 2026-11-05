import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { History, Pencil, Plus, Send, Trash2, Undo2 } from 'lucide-react'
import { useEffect, useState, type FormEvent } from 'react'
import { Link } from 'react-router'
import { Checkbox, Field, Input, Modal, PageHeader, Select, Textarea, useDepartments, useDevicesLookup } from '@/components/forms'
import { Badge, Button, Card, CardHeader, EmptyState, ErrorState, LoadingState, formatDateTime } from '@/components/ui'
import { ApiError, api } from '@/lib/api'
import { Permissions, useAuth } from '@/lib/auth'
import { CAPTURE_LABELS, CLASSIFICATION_LABELS, MODE_LABELS } from '@/lib/labels'
import type { ApplicationRule, Policy, PolicyAssignment, PolicyMode, TitleCapture, WorkSchedule } from '@/lib/types'

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

function assignmentLabel(a: PolicyAssignment) {
  if (a.scope === 'AllDevices') return 'Barcha kompyuterlar'
  if (a.scope === 'Department') return a.departmentName ? `Bo‘lim: ${a.departmentName}` : 'Bo‘lim'
  const who = [a.employeeName, a.deviceName].filter(Boolean).join(' — ')
  return who || 'Bitta kompyuter'
}

function isPersonal(p: Policy) {
  return p.assignments.length > 0 && p.assignments.every((a) => a.scope === 'Device')
}

function personalDeviceId(p: Policy) {
  return p.assignments.find((a) => a.scope === 'Device')?.deviceId ?? ''
}

function restrictionLabel(r: ApplicationRule) {
  if (r.restriction === 'Block') return 'Bloklangan'
  if (r.requirePassword) return 'Parol bilan ochiladi'
  if (r.dailyLimitMinutes) return `${r.dailyLimitMinutes} daq/kun`
  return 'Cheklov yo‘q'
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
        <Field label="Rejim" hint="Kuzatish — faqat yozib boradi. Cheklash — taqiqlangan dasturlarni yopadi.">
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
        <Field label="Oyna nomi">
          <Select value={d.windowTitleCapture} onChange={(e) => set({ windowTitleCapture: e.target.value as TitleCapture })}>
            {Object.entries(CAPTURE_LABELS).map(([k, v]) => (
              <option key={k} value={k}>
                {v}
              </option>
            ))}
          </Select>
        </Field>
        <Field label="Bekor vaqt (soniya)" hint="Shu vaqtdan keyin kompyuter “bo‘sh” hisoblanadi">
          <Input type="number" min={60} max={7200} value={d.idleThresholdSeconds} onChange={(e) => set({ idleThresholdSeconds: Number(e.target.value) })} />
        </Field>
        <Field label="Tekshirish oralig‘i (soniya)">
          <Input type="number" min={2} max={5} value={d.samplingIntervalSeconds} onChange={(e) => set({ samplingIntervalSeconds: Number(e.target.value) })} />
        </Field>
      </div>
      <Checkbox label="Fondagi dasturlar vaqtini ham hisoblash" checked={d.trackBackgroundProcesses} onChange={(e) => set({ trackBackgroundProcesses: e.target.checked })} />

      <div className="rounded-xl border border-slate-200 p-4 dark:border-white/10">
        <p className="text-sm font-semibold">Qayerga qo‘llanadi</p>
        <div className="mt-3 space-y-3">
          <Checkbox label="Barcha kompyuterlar" checked={d.allDevices} onChange={(e) => set({ allDevices: e.target.checked })} />
          <div>
            <p className="mb-1 text-xs font-medium text-slate-500">Bo‘limlar</p>
            <div className="flex flex-wrap gap-3">
              {(deps.data ?? []).map((dep) => (
                <Checkbox key={dep.id} label={dep.name} checked={d.departmentIds.includes(dep.id)} onChange={() => set({ departmentIds: toggle(d.departmentIds, dep.id) })} />
              ))}
            </div>
          </div>
          <div>
            <p className="mb-1 text-xs font-medium text-slate-500">Alohida kompyuterlar</p>
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
          Saqlash
        </Button>
      </div>
    </form>
  )
}

function PublishDialog({ policy, onClose }: { policy: Policy; onClose: () => void }) {
  const qc = useQueryClient()
  const [note, setNote] = useState('')
  const publish = useMutation({
    mutationFn: () => api<Policy>(`/api/policies/${policy.id}/publish`, { method: 'POST', body: { note: note || null, approveEnforcement: true } }),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['policies'] })
      onClose()
    },
  })
  const errors = publish.error instanceof ApiError ? Object.values(publish.error.errors).flat() : []
  return (
    <div className="space-y-4">
      <p className="text-sm text-slate-600 dark:text-slate-300">
        Siyosat {policy.mode === 'Enforce' ? 'dastur cheklovlari bilan' : 'kuzatish rejimida'} kompyuterlarga yuboriladi. O‘zgarish darhol kuchga kiradi.
      </p>
      <Field label="Izoh (ixtiyoriy)">
        <Input maxLength={500} value={note} onChange={(e) => setNote(e.target.value)} placeholder="masalan, Telegram taqiqlandi" />
      </Field>
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
        <Button loading={publish.isPending} onClick={() => publish.mutate()}>
          <Send className="size-4" /> Yuborish
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
  const remove = useMutation({
    mutationFn: (id: string) => api(`/api/policies/${id}`, { method: 'DELETE' }),
    onSuccess: (_, id) => {
      qc.invalidateQueries({ queryKey: ['policies'] })
      if (selectedId === id) setSelectedId(null)
    },
  })

  const rulesHref = selected && isPersonal(selected) && personalDeviceId(selected)
    ? `/app/applications?deviceId=${personalDeviceId(selected)}`
    : '/app/applications'

  return (
    <>
      <PageHeader
        title="Siyosatlar"
        subtitle="Qaysi kompyuterda nima kuzatiladi va qaysi dasturlar cheklanadi — hammasini shu yerda ochib ko‘rish va boshqarish mumkin"
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
        <EmptyState title="Siyosatlar yo‘q" hint="Yangi siyosat qo‘shing yoki Dasturlar bo‘limidan qoida yarating." />
      ) : (
        <div className="grid gap-6 xl:grid-cols-[320px_1fr]">
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
                <p className="mt-1 truncate text-xs text-slate-500">{p.assignments.map(assignmentLabel).join(', ') || 'Qamrov belgilanmagan'}</p>
                <div className="mt-2 flex flex-wrap gap-1">
                  <Badge tone={p.mode === 'Enforce' ? 'red' : 'blue'}>{MODE_LABELS[p.mode]}</Badge>
                  {isPersonal(p) && <Badge tone="amber">Bitta kompyuter</Badge>}
                  {p.hasUnpublishedChanges && <Badge tone="amber">Yuborilmagan o‘zgarish</Badge>}
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
                    <div className="flex flex-wrap gap-2">
                      <Button variant="secondary" onClick={() => setEditing(selected)}>
                        <Pencil className="size-4" /> Tahrirlash
                      </Button>
                      <Button onClick={() => setPublishing(true)}>
                        <Send className="size-4" /> Yuborish
                      </Button>
                      {isPersonal(selected) && (
                        <Button
                          variant="danger"
                          loading={remove.isPending}
                          onClick={() => confirm('Bu kompyuterning shaxsiy qoidalari o‘chirilsinmi? U yana umumiy siyosatga qaytadi.') && remove.mutate(selected.id)}
                        >
                          <Trash2 className="size-4" /> O‘chirish
                        </Button>
                      )}
                    </div>
                  )}
                />
                <dl className="grid gap-4 p-5 text-sm sm:grid-cols-3">
                  {[
                    ['Rejim', MODE_LABELS[selected.mode]],
                    ['Qamrov', selected.assignments.map(assignmentLabel).join(', ') || '—'],
                    ['Dastur qoidalari', `${selected.rules.length} ta`],
                    ['Oyna nomi', CAPTURE_LABELS[selected.windowTitleCapture]],
                    ['Bekor vaqt', `${Math.round(selected.idleThresholdSeconds / 60)} daqiqa`],
                    ['Oxirgi yuborilgan', formatDateTime(selected.publishedAt)],
                  ].map(([k, v]) => (
                    <div key={k}>
                      <dt className="text-xs text-slate-500">{k}</dt>
                      <dd className="mt-0.5 font-medium">{v}</dd>
                    </div>
                  ))}
                </dl>
                {rollback.isError && <ErrorState error={rollback.error} />}
                {remove.isError && <ErrorState error={remove.error} />}
              </Card>

              <Card>
                <CardHeader
                  title="Dastur qoidalari"
                  subtitle="Har bir qoidani ochib ko‘rish va Dasturlar bo‘limida tahrirlash mumkin"
                  actions={
                    <Link to={rulesHref} className="inline-flex items-center gap-1 text-sm font-medium text-brand-600 hover:underline">
                      Qoidalarni ochish
                    </Link>
                  }
                />
                {selected.rules.length === 0 ? (
                  <EmptyState title="Qoida yo‘q" hint="Dasturlar bo‘limidan qo‘shing — keyin shu yerda ochiladi." />
                ) : (
                  <ul className="divide-y divide-slate-100 dark:divide-white/5">
                    {selected.rules.map((r) => (
                      <li key={r.id} className="flex flex-wrap items-center justify-between gap-2 px-5 py-3 text-sm">
                        <div>
                          <p className="font-medium">{r.name}</p>
                          <p className="font-mono text-xs text-slate-500">{r.processName}</p>
                        </div>
                        <div className="flex flex-wrap items-center gap-1.5">
                          <Badge tone={r.classification === 'Approved' ? 'green' : r.classification === 'NotApproved' ? 'red' : 'gray'}>
                            {CLASSIFICATION_LABELS[r.classification]}
                          </Badge>
                          <Badge tone={r.restriction === 'Block' ? 'red' : r.requirePassword ? 'amber' : 'gray'}>{restrictionLabel(r)}</Badge>
                        </div>
                      </li>
                    ))}
                  </ul>
                )}
              </Card>

              <div className="grid gap-6 lg:grid-cols-2">
                <Card>
                  <CardHeader title="Kompyuterlardagi holat" subtitle={selected.publishedVersion ? `v${selected.publishedVersion} yuborilganidan keyin` : 'Hali yuborilmagan'} />
                  <div className="grid grid-cols-2 gap-3 p-5 sm:grid-cols-3">
                    {[
                      ['Kutilmoqda', selected.results.pending, 'blue'],
                      ['Qo‘llangan', selected.results.applied, 'green'],
                      ['Xato', selected.results.failed, 'red'],
                    ].map(([k, v, t]) => (
                      <div key={k as string} className="rounded-xl border border-slate-100 p-3 dark:border-white/5">
                        <Badge tone={t as 'blue'}>{k}</Badge>
                        <p className="mt-2 text-2xl font-semibold tabular-nums">{v}</p>
                      </div>
                    ))}
                  </div>
                </Card>
                <Card>
                  <CardHeader title="Tarix" subtitle="Har bir yuborish yozib qolinadi" />
                  {selected.versions.length === 0 ? (
                    <EmptyState title="Hali yuborilmagan" />
                  ) : (
                    <ul className="divide-y divide-slate-100 dark:divide-white/5">
                      {selected.versions.map((v) => (
                        <li key={v.version} className="flex items-center justify-between px-5 py-3 text-sm">
                          <span className="flex min-w-0 items-center gap-2">
                            <History className="size-4 shrink-0 text-slate-400" />
                            <span>
                              v{v.version}
                              {v.version === selected.publishedVersion && (
                                <Badge tone="green">joriy</Badge>
                              )}
                              {v.note && <span className="ml-2 text-xs text-slate-500">{v.note}</span>}
                            </span>
                          </span>
                          <span className="flex shrink-0 items-center gap-2 text-xs text-slate-500">
                            {formatDateTime(v.createdAt)}
                            {manage && v.version !== selected.publishedVersion && (
                              <Button variant="ghost" onClick={() => confirm(`v${v.version} holatiga qaytarilsinmi?`) && rollback.mutate({ id: selected.id, target: v.version })}>
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
      <Modal open={publishing && !!selected} title={`Yuborish: ${selected?.name ?? ''}`} onClose={() => setPublishing(false)}>
        {selected && <PublishDialog policy={selected} onClose={() => setPublishing(false)} />}
      </Modal>
    </>
  )
}
