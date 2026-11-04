import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Monitor, Pencil, Plus, Trash2, Undo2, UserRound } from 'lucide-react'
import { useEffect, useMemo, useState, type FormEvent } from 'react'
import { InstallControlCard, SiteBlockCard, VpnControlCard } from '@/components/DeviceControls'
import { Checkbox, Field, FilterBar, Input, Modal, PageHeader, Select, defaultFilters, filtersToQuery, tableClass, tbodyClass, td, th, theadClass, useDevicesLookup, useEmployeesLookup, type Filters } from '@/components/forms'
import { Badge, Button, Card, CardHeader, EmptyState, ErrorState, LoadingState, formatDuration } from '@/components/ui'
import { api } from '@/lib/api'
import { Permissions, useAuth } from '@/lib/auth'
import { CLASSIFICATION_LABELS } from '@/lib/labels'
import type { ApplicationRule, ApplicationScope, ApplicationSummary, Classification, Restriction } from '@/lib/types'

const clsTone = { Approved: 'green', NotApproved: 'red', Neutral: 'gray', Mixed: 'amber', Unclassified: 'gray' } as const

interface Draft {
  id?: string
  name: string
  processName: string
  classification: Classification
  restriction: Restriction
  dailyLimitMinutes: string
  requirePassword: boolean
  password: string
  pathPattern: string
  publisher: string
  description: string
}

const emptyDraft = (processName = ''): Draft => ({ name: processName, processName, classification: 'Approved', restriction: 'None', dailyLimitMinutes: '', requirePassword: false, password: '', pathPattern: '', publisher: '', description: '' })

function toDraft(r: ApplicationRule): Draft {
  return {
    id: r.id, name: r.name, processName: r.processName, classification: r.classification, restriction: r.restriction,
    dailyLimitMinutes: r.dailyLimitMinutes?.toString() ?? '', requirePassword: r.requirePassword, password: '',
    pathPattern: r.pathPattern ?? '', publisher: r.publisher ?? '', description: r.description ?? '',
  }
}

function RuleForm({ policyId, draft, onDone }: { policyId: string; draft: Draft; onDone: () => void }) {
  const qc = useQueryClient()
  const [d, setD] = useState(draft)
  const save = useMutation({
    mutationFn: () => {
      const body = {
        policyId, name: d.name, processName: d.processName, classification: d.classification, restriction: d.restriction,
        dailyLimitMinutes: d.restriction === 'Block' || !d.dailyLimitMinutes ? null : Number(d.dailyLimitMinutes),
        requirePassword: d.restriction !== 'Block' && d.requirePassword,
        password: d.password || null,
        pathPattern: d.pathPattern || null, publisher: d.publisher || null, description: d.description || null,
      }
      return d.id ? api(`/api/applications/rules/${d.id}`, { method: 'PUT', body }) : api('/api/applications/rules', { method: 'POST', body })
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['rules'] })
      qc.invalidateQueries({ queryKey: ['app-scope'] })
      qc.invalidateQueries({ queryKey: ['policies'] })
      onDone()
    },
  })
  const set = (patch: Partial<Draft>) => setD((x) => ({ ...x, ...patch }))
  const submit = (e: FormEvent) => {
    e.preventDefault()
    save.mutate()
  }
  return (
    <form onSubmit={submit} className="grid gap-4 sm:grid-cols-2">
      <Field label="Qoida nomi">
        <Input required maxLength={120} value={d.name} onChange={(e) => set({ name: e.target.value })} />
      </Field>
      <Field label="Jarayon (EXE) nomi" hint="masalan, chrome.exe">
        <Input required pattern="^[^\\/:*?<>|]+\.[eE][xX][eE]$" value={d.processName} onChange={(e) => set({ processName: e.target.value })} />
      </Field>
      <Field label="Tasnif (unumdorlik uchun)">
        <Select value={d.classification} onChange={(e) => set({ classification: e.target.value as Classification })}>
          <option value="Approved">Tasdiqlangan (ish uchun)</option>
          <option value="Neutral">Neytral</option>
          <option value="NotApproved">Tasdiqlanmagan</option>
        </Select>
      </Field>
      <Field label="Cheklov">
        <Select value={d.restriction} onChange={(e) => set({ restriction: e.target.value as Restriction })}>
          <option value="None">Cheklanmagan</option>
          <option value="Block">Bloklash (taqiqlangan)</option>
        </Select>
      </Field>
      {d.restriction === 'None' && (
        <Field label="Kunlik limit (daqiqa)" hint="Bo‘sh — limitsiz. Oshsa, ogohlantirish yoziladi">
          <Input type="number" min={1} max={1440} value={d.dailyLimitMinutes} onChange={(e) => set({ dailyLimitMinutes: e.target.value })} />
        </Field>
      )}
      {d.restriction === 'None' && (
        <div className="sm:col-span-2">
          <Checkbox
            label="Parol bilan himoyalash — bu dastur ochilganda xodimdan parol so‘raladi"
            checked={d.requirePassword}
            onChange={(e) => set({ requirePassword: e.target.checked })}
          />
          {d.requirePassword && (
            <div className="mt-2">
              <Field label={d.id ? 'Yangi parol (bo‘sh — eskisi qoladi)' : 'Parol'} hint="Faqat admin biladigan parol. Agent uni mahalliy tekshiradi.">
                <Input type="text" minLength={4} maxLength={128} value={d.password} onChange={(e) => set({ password: e.target.value })} placeholder="kamida 4 belgi" />
              </Field>
            </div>
          )}
        </div>
      )}
      <Field label="AppLocker yo‘l sharti (ixtiyoriy)" hint="%PROGRAMFILES%\Game\*">
        <Input maxLength={400} value={d.pathPattern} onChange={(e) => set({ pathPattern: e.target.value })} />
      </Field>
      <Field label="Publisher sharti (ixtiyoriy)" hint="O=VENDOR LTD">
        <Input maxLength={400} value={d.publisher} onChange={(e) => set({ publisher: e.target.value })} />
      </Field>
      <Field label="Izoh" className="sm:col-span-2">
        <Input maxLength={500} value={d.description} onChange={(e) => set({ description: e.target.value })} />
      </Field>
      {save.isError && (
        <div className="sm:col-span-2">
          <ErrorState error={save.error} />
        </div>
      )}
      <p className="text-xs text-slate-500 sm:col-span-2">O‘zgarish saqlanishi bilan siyosat avtomatik nashr qilinadi va ulangan kompyuterlarga darhol qo‘llaniladi.</p>
      <div className="flex justify-end gap-2 sm:col-span-2">
        <Button type="button" variant="secondary" onClick={onDone}>
          Bekor qilish
        </Button>
        <Button type="submit" loading={save.isPending}>
          Saqlash
        </Button>
      </div>
    </form>
  )
}

export function ApplicationsPage() {
  const { can } = useAuth()
  const qc = useQueryClient()
  const manage = can(Permissions.PoliciesManage)
  const [filters, setFilters] = useState<Filters>(defaultFilters(30))
  const [draft, setDraft] = useState<Draft | null>(null)
  const [editPolicyId, setEditPolicyId] = useState('')

  const emps = useEmployeesLookup()
  const devs = useDevicesLookup()
  const employeeId = filters.employeeId
  const deviceId = filters.deviceId

  const devicesForEmployee = useMemo(() => {
    const all = devs.data?.items ?? []
    return employeeId ? all.filter((d) => d.employeeId === employeeId) : all
  }, [devs.data, employeeId])

  useEffect(() => {
    if (!employeeId) return
    if (deviceId && devicesForEmployee.some((d) => d.id === deviceId)) return
    const only = devicesForEmployee.length === 1 ? devicesForEmployee[0] : undefined
    const next = only?.id ?? ''
    if (next === deviceId) return
    setFilters((f) => ({ ...f, deviceId: next }))
  }, [employeeId, deviceId, devicesForEmployee])

  const setTarget = (patch: { employeeId?: string; deviceId?: string }) => setFilters((f) => ({ ...f, ...patch }))

  const scope = useQuery({
    queryKey: ['app-scope', deviceId],
    queryFn: () => api<ApplicationScope>('/api/applications/scope', { query: { deviceId } }),
  })
  const policyId = scope.data?.policyId ?? ''
  const needsDeviceChoice = !!employeeId && !deviceId && devicesForEmployee.length !== 1
  const canEditRules = !!policyId && !needsDeviceChoice

  const usage = useQuery({
    queryKey: ['applications', filters],
    queryFn: ({ signal }) => api<ApplicationSummary[]>('/api/applications', { query: filtersToQuery(filters), signal }),
    enabled: can(Permissions.MonitoringRead),
  })
  const rules = useQuery({
    queryKey: ['rules', policyId],
    queryFn: () => api<ApplicationRule[]>('/api/applications/rules', { query: { policyId } }),
    enabled: !!policyId,
  })

  const ensure = useMutation({
    mutationFn: (id: string) => api<ApplicationScope>('/api/applications/scope', { method: 'POST', body: { deviceId: id } }),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['app-scope'] })
      qc.invalidateQueries({ queryKey: ['rules'] })
      qc.invalidateQueries({ queryKey: ['policies'] })
    },
  })
  const revert = useMutation({
    mutationFn: (id: string) => api(`/api/applications/scope/${id}`, { method: 'DELETE' }),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['app-scope'] })
      qc.invalidateQueries({ queryKey: ['rules'] })
      qc.invalidateQueries({ queryKey: ['policies'] })
    },
  })
  const remove = useMutation({
    mutationFn: (id: string) => api(`/api/applications/rules/${id}`, { method: 'DELETE' }),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['rules'] })
      qc.invalidateQueries({ queryKey: ['policies'] })
    },
  })

  const resolvePolicy = async (): Promise<string> => {
    if (!scope.data) return policyId
    if (!deviceId || scope.data.isDevicePolicy || !scope.data.inheritsFromGlobal) return scope.data.policyId
    const next = await ensure.mutateAsync(deviceId)
    return next.policyId
  }

  const openDraft = async (seed: Draft) => {
    if (!canEditRules) return
    const pid = await resolvePolicy()
    let next = seed
    if (seed.id && pid !== policyId) {
      const copied = await qc.fetchQuery({
        queryKey: ['rules', pid],
        queryFn: () => api<ApplicationRule[]>('/api/applications/rules', { query: { policyId: pid } }),
      })
      const match = copied.find((r) => r.processName.toLowerCase() === seed.processName.toLowerCase())
      next = match ? toDraft(match) : { ...seed, id: undefined }
    }
    setEditPolicyId(pid)
    setDraft(next)
  }

  const deleteRule = async (r: ApplicationRule) => {
    if (!confirm(`${r.processName} qoidasi o‘chirilsinmi?`)) return
    const pid = await resolvePolicy()
    if (pid === r.policyId) {
      remove.mutate(r.id)
      return
    }
    const copied = await qc.fetchQuery({
      queryKey: ['rules', pid],
      queryFn: () => api<ApplicationRule[]>('/api/applications/rules', { query: { policyId: pid } }),
    })
    const match = copied.find((x) => x.processName.toLowerCase() === r.processName.toLowerCase())
    if (match) remove.mutate(match.id)
  }

  const total = (usage.data ?? []).reduce((s, a) => s + a.foregroundSeconds, 0)
  const selectedDevice = (devs.data?.items ?? []).find((d) => d.id === deviceId)
  const selectedEmployee = (emps.data?.items ?? []).find((e) => e.id === employeeId)
  const inheritHint = !!deviceId && (scope.data?.inheritsFromGlobal ?? false)

  return (
    <>
      <PageHeader title="Dasturlar boshqaruvi" subtitle="Avval xodim yoki kompyuterni tanlang — qoidalar faqat shu tanlovga qo‘llaniladi" />

      <Card>
        <CardHeader title="Kim uchun?" subtitle="1-qadam: xodim yoki kompyuterni tanlang. Keyin dastur qoidalari shu odam/kompyuterga yoziladi." />
        <div className="grid gap-4 p-5 sm:grid-cols-2">
          <Field label="Xodim">
            <div className="relative">
              <UserRound className="pointer-events-none absolute left-3 top-1/2 size-4 -translate-y-1/2 text-slate-400" />
              <Select value={employeeId} onChange={(e) => setTarget({ employeeId: e.target.value, deviceId: '' })} className="pl-9">
                <option value="">Barcha xodimlar</option>
                {(emps.data?.items ?? []).map((e) => (
                  <option key={e.id} value={e.id}>
                    {e.fullName}
                  </option>
                ))}
              </Select>
            </div>
          </Field>
          <Field label="Kompyuter" hint={employeeId && devicesForEmployee.length === 0 ? 'Bu xodimga biriktirilgan kompyuter yo‘q' : undefined}>
            <div className="relative">
              <Monitor className="pointer-events-none absolute left-3 top-1/2 size-4 -translate-y-1/2 text-slate-400" />
              <Select value={deviceId} onChange={(e) => setTarget({ deviceId: e.target.value })} className="pl-9">
                <option value="">{employeeId ? 'Xodimning kompyuterini tanlang…' : 'Barcha kompyuterlar (standart)'}</option>
                {devicesForEmployee.map((d) => (
                  <option key={d.id} value={d.id}>
                    {d.displayName}{d.employeeName ? ` — ${d.employeeName}` : ''}{d.hostname ? ` (${d.hostname})` : ''}
                  </option>
                ))}
              </Select>
            </div>
          </Field>
        </div>
        <div className="flex flex-wrap items-center justify-between gap-3 border-t border-slate-100 px-5 py-3 dark:border-white/5">
          <div className="flex flex-wrap items-center gap-2 text-sm">
            {scope.isPending ? (
              <span className="text-slate-500">Siyosat aniqlanmoqda…</span>
            ) : scope.isError ? (
              <span className="text-rose-600">Siyosatni o‘qib bo‘lmadi</span>
            ) : needsDeviceChoice ? (
              <Badge tone="amber">Kompyuterni tanlang</Badge>
            ) : scope.data?.isDevicePolicy ? (
              <>
                <Badge tone="blue">Shaxsiy qoidalar</Badge>
                <span className="text-slate-600 dark:text-slate-300">{scope.data.label}</span>
              </>
            ) : (
              <>
                <Badge>{deviceId ? 'Standart qoidalar' : 'Barcha kompyuterlar'}</Badge>
                <span className="text-slate-600 dark:text-slate-300">
                  {deviceId
                    ? `${scope.data?.label ?? selectedDevice?.displayName}: hozir umumiy qoidalar. O‘zgartirish faqat shu kompyuterga yoziladi.`
                    : selectedEmployee
                      ? `${selectedEmployee.fullName} — avval kompyuterini tanlang`
                      : 'Qoidalar barcha kompyuterlarga tatbiq etiladi'}
                </span>
              </>
            )}
          </div>
          {manage && scope.data?.isDevicePolicy && deviceId && (
            <Button
              variant="secondary"
              loading={revert.isPending}
              onClick={() => confirm('Shaxsiy qoidalar o‘chirilsinmi? Bu kompyuter yana umumiy (standart) qoidalarga qaytadi.') && revert.mutate(deviceId)}
            >
              <Undo2 className="size-4" /> Standartga qaytarish
            </Button>
          )}
        </div>
      </Card>

      <div className="grid gap-6 xl:grid-cols-2">
        <Card>
          <CardHeader
            title="Siyosat qoidalari"
            subtitle={inheritHint ? 'Hozir standart qoidalar ko‘rsatilgan — tahrir shu kompyuter uchun alohida nusxa yaratadi' : 'Tanlangan xodim/kompyuter uchun ruxsat va taqiqlar'}
            actions={
              manage && canEditRules ? (
                <Button onClick={() => void openDraft(emptyDraft())} loading={ensure.isPending}>
                  <Plus className="size-4" />
                </Button>
              ) : undefined
            }
          />
          {needsDeviceChoice ? (
            <EmptyState title="Kompyuterni tanlang" hint="Bu xodimda bir nechta kompyuter bor. Qoidalar qaysi biriga yozilishini yuqoridan tanlang." />
          ) : rules.isPending && policyId ? (
            <LoadingState />
          ) : rules.isError ? (
            <ErrorState error={rules.error} />
          ) : (rules.data ?? []).length === 0 ? (
            <EmptyState title="Qoidalar yo‘q" hint="O‘ngdagi ro‘yxatdan dasturni tanlab tez qoida qo‘shishingiz mumkin." />
          ) : (
            <table className={tableClass}>
              <thead className={theadClass}>
                <tr>
                  <th className={th}>Dastur</th>
                  <th className={th}>Tasnif</th>
                  <th className={th}>Cheklov</th>
                  {manage && <th className={th} />}
                </tr>
              </thead>
              <tbody className={tbodyClass}>
                {rules.data?.map((r) => (
                  <tr key={r.id}>
                    <td className={td}>
                      <p className="font-medium">{r.name}</p>
                      <p className="font-mono text-xs text-slate-500">{r.processName}</p>
                    </td>
                    <td className={td}>
                      <Badge tone={clsTone[r.classification]}>{CLASSIFICATION_LABELS[r.classification]}</Badge>
                    </td>
                    <td className={`${td} text-xs`}>
                      <div className="flex flex-wrap items-center gap-1.5">
                        {r.restriction === 'Block' ? <Badge tone="red">Bloklangan</Badge> : r.dailyLimitMinutes ? `${r.dailyLimitMinutes} daq/kun` : null}
                        {r.requirePassword && <Badge tone="amber">Parolli qulf</Badge>}
                        {r.restriction !== 'Block' && !r.dailyLimitMinutes && !r.requirePassword && '—'}
                      </div>
                    </td>
                    {manage && (
                      <td className={td}>
                        <div className="flex justify-end gap-1">
                          <Button variant="ghost" disabled={!canEditRules} onClick={() => void openDraft(toDraft(r))}>
                            <Pencil className="size-4" />
                          </Button>
                          <Button variant="ghost" disabled={!canEditRules} onClick={() => void deleteRule(r)}>
                            <Trash2 className="size-4 text-rose-500" />
                          </Button>
                        </div>
                      </td>
                    )}
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </Card>

        <Card>
          <CardHeader title="Kuzatilgan dasturlar" subtitle={deviceId || employeeId ? 'Tanlangan xodim/kompyuterdagi faol va fondagi vaqt' : 'Barcha kompyuterlardagi faol va fondagi vaqt'} />
          {!can(Permissions.MonitoringRead) ? (
            <EmptyState title="Ruxsat yo‘q" hint="monitoring.read ruxsati kerak." />
          ) : usage.isPending ? (
            <LoadingState />
          ) : usage.isError ? (
            <ErrorState error={usage.error} />
          ) : usage.data.length === 0 ? (
            <EmptyState title="Dastur ma’lumoti yo‘q" />
          ) : (
            <div className="max-h-[520px] overflow-y-auto">
              <table className={tableClass}>
                <thead className={theadClass}>
                  <tr>
                    <th className={th}>Dastur</th>
                    <th className={th}>Faol</th>
                    <th className={th}>Fonda</th>
                    <th className={th}>Ulush</th>
                    <th className={th}>Tasnif</th>
                    {manage && <th className={th} />}
                  </tr>
                </thead>
                <tbody className={tbodyClass}>
                  {usage.data.map((a) => (
                    <tr key={a.processName}>
                      <td className={`${td} font-mono text-xs`}>{a.processName}</td>
                      <td className={`${td} text-xs`}>{formatDuration(a.foregroundSeconds)}</td>
                      <td className={`${td} text-xs`}>{formatDuration(a.backgroundSeconds)}</td>
                      <td className={td}>
                        <div className="h-1.5 w-20 rounded-full bg-slate-100 dark:bg-white/10">
                          <div className="brand-gradient h-1.5 rounded-full" style={{ width: `${total ? Math.max(2, (a.foregroundSeconds / total) * 100) : 0}%` }} />
                        </div>
                      </td>
                      <td className={td}>
                        <Badge tone={clsTone[a.classification]}>{CLASSIFICATION_LABELS[a.classification]}</Badge>
                      </td>
                      {manage && (
                        <td className={td}>
                          <Button variant="ghost" title="Qoida qo‘shish" disabled={!canEditRules} onClick={() => void openDraft(emptyDraft(a.processName))}>
                            <Plus className="size-4" />
                          </Button>
                        </td>
                      )}
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </Card>
      </div>
      <FilterBar value={filters} onChange={setFilters} show={['date']} />
      <div className="grid gap-6 xl:grid-cols-2">
        <SiteBlockCard manage={manage} deviceId={deviceId || undefined} />
        <VpnControlCard manage={manage} />
        <InstallControlCard manage={manage} />
      </div>
      <Modal open={draft !== null} title={draft?.id ? 'Qoidani tahrirlash' : 'Yangi qoida'} onClose={() => setDraft(null)} wide>
        {draft && <RuleForm policyId={editPolicyId || policyId} draft={draft} onDone={() => setDraft(null)} />}
      </Modal>
    </>
  )
}
