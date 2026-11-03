import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Pencil, Plus, Trash2 } from 'lucide-react'
import { useEffect, useState, type FormEvent } from 'react'
import { Checkbox, Field, FilterBar, Input, Modal, PageHeader, Select, defaultFilters, filtersToQuery, tableClass, tbodyClass, td, th, theadClass, type Filters } from '@/components/forms'
import { Badge, Button, Card, CardHeader, EmptyState, ErrorState, LoadingState, formatDuration } from '@/components/ui'
import { api } from '@/lib/api'
import { Permissions, useAuth } from '@/lib/auth'
import { CLASSIFICATION_LABELS } from '@/lib/labels'
import type { ApplicationRule, ApplicationSummary, Classification, Policy, Restriction } from '@/lib/types'

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
      <p className="text-xs text-slate-500 sm:col-span-2">O‘zgarish siyosat qoralamasiga yoziladi va siyosat publish qilinganda agentlarga yetkaziladi. Tizim jarayonlarini bloklash server tomonida rad etiladi.</p>
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
  const [policyId, setPolicyId] = useState('')
  const [draft, setDraft] = useState<Draft | null>(null)

  const policies = useQuery({ queryKey: ['policies'], queryFn: () => api<Policy[]>('/api/policies') })
  useEffect(() => {
    if (!policyId && policies.data?.[0]) setPolicyId(policies.data[0].id)
  }, [policies.data, policyId])

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
  const remove = useMutation({
    mutationFn: (id: string) => api(`/api/applications/rules/${id}`, { method: 'DELETE' }),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['rules'] })
      qc.invalidateQueries({ queryKey: ['policies'] })
    },
  })
  const total = (usage.data ?? []).reduce((s, a) => s + a.foregroundSeconds, 0)

  return (
    <>
      <PageHeader title="Dasturlar boshqaruvi" subtitle="Ruxsat berilgan va taqiqlangan dasturlar, foydalanish limitlari va tasnif" />
      <div className="grid gap-6 xl:grid-cols-2">
        <Card>
          <CardHeader
            title="Siyosat qoidalari"
            subtitle="Qoidalar siyosat qoralamasiga tegishli"
            actions={
              <div className="flex gap-2">
                <Select value={policyId} onChange={(e) => setPolicyId(e.target.value)} className="w-56" aria-label="Siyosat">
                  {(policies.data ?? []).map((p) => (
                    <option key={p.id} value={p.id}>
                      {p.name} {p.hasUnpublishedChanges ? '•' : ''}
                    </option>
                  ))}
                </Select>
                {manage && policyId && (
                  <Button onClick={() => setDraft(emptyDraft())}>
                    <Plus className="size-4" />
                  </Button>
                )}
              </div>
            }
          />
          {rules.isPending && policyId ? (
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
                          <Button
                            variant="ghost"
                            onClick={() =>
                              setDraft({ id: r.id, name: r.name, processName: r.processName, classification: r.classification, restriction: r.restriction, dailyLimitMinutes: r.dailyLimitMinutes?.toString() ?? '', requirePassword: r.requirePassword, password: '', pathPattern: r.pathPattern ?? '', publisher: r.publisher ?? '', description: r.description ?? '' })
                            }
                          >
                            <Pencil className="size-4" />
                          </Button>
                          <Button variant="ghost" onClick={() => confirm(`${r.processName} qoidasi o‘chirilsinmi?`) && remove.mutate(r.id)}>
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
          <CardHeader title="Kuzatilgan dasturlar" subtitle="Faol oynadagi va fondagi vaqt" />
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
                          <Button variant="ghost" title="Qoida qo‘shish" disabled={!policyId} onClick={() => setDraft(emptyDraft(a.processName))}>
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
      <FilterBar value={filters} onChange={setFilters} />
      <Modal open={draft !== null} title={draft?.id ? 'Qoidani tahrirlash' : 'Yangi qoida'} onClose={() => setDraft(null)} wide>
        {draft && <RuleForm policyId={policyId} draft={draft} onDone={() => setDraft(null)} />}
      </Modal>
    </>
  )
}
