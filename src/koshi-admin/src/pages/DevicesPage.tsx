import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Ban, Check, Copy, Download, History, KeyRound, Monitor, MonitorPlay, PackageX, Pencil, Plus, RotateCcw, ShieldCheck, ShieldOff, Timer, Trash2, Wifi, WifiOff } from 'lucide-react'
import { useEffect, useMemo, useState, type FormEvent, type ReactNode } from 'react'
import { Link, useSearchParams } from 'react-router'
import { Field, Input, Modal, PageHeader, Pagination, Select, tableClass, tbodyClass, td, th, theadClass, useDevicesLookup, useEmployeesLookup } from '@/components/forms'
import { Badge, Button, Card, EmptyState, ErrorState, LoadingState, formatDateTime, formatDuration } from '@/components/ui'
import { api } from '@/lib/api'
import { copyText } from '@/lib/copy'
import { Permissions, useAuth } from '@/lib/auth'
import { AGENT_INSTALLER } from '@/lib/labels'
import type { ActivationCode, Device, DeviceConnection, PagedResult } from '@/lib/types'

function ConnectionsModal({ device, onClose }: { device: Device; onClose: () => void }) {
  const q = useQuery({ queryKey: ['connections', device.id], queryFn: () => api<DeviceConnection[]>(`/api/devices/${device.id}/connections`, { query: { days: 14 } }) })
  return (
    <Modal open title={`Onlayn tarix — ${device.displayName}`} onClose={onClose} wide>
      {q.isPending ? (
        <LoadingState />
      ) : q.isError ? (
        <ErrorState error={q.error} />
      ) : q.data.length === 0 ? (
        <EmptyState title="Ulanishlar yo‘q" hint="Agent hali serverga real-time ulanmagan." />
      ) : (
        <table className={tableClass}>
          <thead className={theadClass}>
            <tr>
              <th className={th}>Ulandi (onlayn)</th>
              <th className={th}>Uzildi (offlayn)</th>
              <th className={th}>Davomiylik</th>
            </tr>
          </thead>
          <tbody className={tbodyClass}>
            {q.data.map((c, i) => (
              <tr key={i}>
                <td className={`${td} text-xs`}>{formatDateTime(c.connectedAt)}</td>
                <td className={`${td} text-xs`}>{c.disconnectedAt ? formatDateTime(c.disconnectedAt) : <Badge tone="green">Hozir onlayn</Badge>}</td>
                <td className={`${td} text-xs`}>{formatDuration(c.durationSeconds)}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </Modal>
  )
}

const statusTone = { Active: 'green', Pending: 'blue', Blocked: 'red', Revoked: 'gray' } as const
const statusLabel = { Active: 'Faol', Pending: 'Aktivatsiya kutilmoqda', Blocked: 'Bloklangan', Revoked: 'Bekor qilingan' } as const

const LICENSE_PRESETS = [
  { value: '0', label: 'Doimiy' },
  { value: '7', label: '7 kun' },
  { value: '30', label: '30 kun' },
  { value: '90', label: '90 kun' },
  { value: '365', label: '1 yil' },
  { value: 'custom', label: 'Boshqa muddat…' },
] as const

function readLicenseDays(f: FormData): number {
  const preset = String(f.get('licensePreset') ?? '0')
  if (preset === 'custom') {
    const n = Number(f.get('licenseDaysCustom') || 0)
    return Number.isFinite(n) ? Math.min(3650, Math.max(1, Math.round(n))) : 1
  }
  const n = Number(preset)
  return Number.isFinite(n) && n > 0 ? n : 0
}

function licenseDaysLabel(days: number | null | undefined): string {
  if (!days) return 'Doimiy'
  return `${days} kun`
}

function pad2(n: number) {
  return String(n).padStart(2, '0')
}

function remainingParts(until: string | null | undefined) {
  if (!until) return { days: 0, hours: 0, minutes: 0, seconds: 0, expired: false, permanent: true }
  const ms = new Date(until).getTime() - Date.now()
  if (!Number.isFinite(ms) || ms <= 0) return { days: 0, hours: 0, minutes: 0, seconds: 0, expired: true, permanent: false }
  const total = Math.floor(ms / 1000)
  return {
    days: Math.floor(total / 86_400),
    hours: Math.floor((total % 86_400) / 3600),
    minutes: Math.floor((total % 3600) / 60),
    seconds: total % 60,
    expired: false,
    permanent: false,
  }
}

function useNowTick(enabled: boolean) {
  const [, setTick] = useState(0)
  useEffect(() => {
    if (!enabled) return
    const id = window.setInterval(() => setTick((n) => n + 1), 1000)
    return () => window.clearInterval(id)
  }, [enabled])
}

/** Compact live timer: “1 kun 23:37:36” in a soft indigo chip. */
function LicenseTimer({ until, onEdit }: { until: string | null | undefined; onEdit?: () => void }) {
  const p = remainingParts(until)
  useNowTick(!p.permanent && !p.expired)
  const tone = p.expired ? 'rose' : p.permanent ? 'emerald' : 'brand'
  const shell =
    tone === 'rose'
      ? 'bg-rose-50 text-rose-700 ring-rose-200 dark:bg-rose-500/10 dark:text-rose-300 dark:ring-rose-500/20'
      : tone === 'emerald'
        ? 'bg-emerald-50 text-emerald-700 ring-emerald-200 dark:bg-emerald-500/10 dark:text-emerald-300 dark:ring-emerald-500/20'
        : 'bg-brand-50 text-brand-700 ring-brand-200 dark:bg-brand-500/15 dark:text-brand-200 dark:ring-brand-500/25'
  return (
    <div className="inline-flex items-center gap-2">
      <span className={`inline-flex items-center gap-1.5 rounded-xl px-2.5 py-1 text-xs font-semibold tabular-nums ring-1 ${shell}`}>
        {p.permanent ? (
          'Doimiy'
        ) : p.expired ? (
          'Muddati tugagan'
        ) : (
          <>
            {p.days > 0 && (
              <>
                <span>{p.days}</span>
                <span className="font-medium opacity-55">kun</span>
                <span className="opacity-25">·</span>
              </>
            )}
            <span className="font-mono tracking-wide">
              {pad2(p.hours)}:{pad2(p.minutes)}:{pad2(p.seconds)}
            </span>
          </>
        )}
      </span>
      {onEdit && (
        <button type="button" onClick={onEdit} className="rounded-lg px-2 py-1 text-xs font-medium text-brand-600 hover:bg-brand-50 dark:hover:bg-brand-500/10">
          Cho‘zish
        </button>
      )}
    </div>
  )
}

function LicenseDurationField({ hint }: { hint?: string }) {
  const [mode, setMode] = useState('0')
  return (
    <Field label="Aktivatsiya muddati" hint={hint ?? 'Kod ishlatilgandan keyin dastur shu kungacha ishlaydi. Tugagach kompyuter yangi kod so‘raydi.'}>
      <Select name="licensePreset" value={mode} onChange={(e) => setMode(e.target.value)}>
        {LICENSE_PRESETS.map((p) => (
          <option key={p.value} value={p.value}>
            {p.label}
          </option>
        ))}
      </Select>
      {mode === 'custom' && (
        <Input name="licenseDaysCustom" type="number" min={1} max={3650} defaultValue={30} className="mt-2" placeholder="Kun soni" />
      )}
    </Field>
  )
}

function Switch({ checked, disabled, onChange, label }: { checked: boolean; disabled?: boolean; onChange: (v: boolean) => void; label: string }) {
  return (
    <button
      type="button"
      role="switch"
      aria-checked={checked}
      aria-label={label}
      disabled={disabled}
      onClick={() => onChange(!checked)}
      className={`relative inline-flex h-5 w-9 shrink-0 items-center rounded-full transition disabled:cursor-not-allowed disabled:opacity-50 ${checked ? 'bg-emerald-500' : 'bg-slate-300 dark:bg-white/20'}`}
    >
      <span className={`inline-block size-4 rounded-full bg-white shadow transition ${checked ? 'translate-x-4' : 'translate-x-0.5'}`} />
    </button>
  )
}

function ActionBtn({ title, onClick, children, danger, muted }: { title: string; onClick?: () => void; children: ReactNode; danger?: boolean; muted?: boolean }) {
  return (
    <button
      type="button"
      title={title}
      disabled={!onClick || muted}
      onClick={onClick}
      className={`inline-flex items-center gap-1.5 rounded-xl border px-2.5 py-1.5 text-xs font-medium transition disabled:cursor-not-allowed disabled:opacity-40 ${
        danger
          ? 'border-rose-200 bg-white text-rose-600 hover:bg-rose-50 dark:border-rose-500/20 dark:bg-transparent dark:hover:bg-rose-500/10'
          : 'border-slate-200 bg-white text-slate-600 hover:border-brand-200 hover:bg-brand-50 hover:text-brand-700 dark:border-white/10 dark:bg-white/5 dark:text-slate-300 dark:hover:bg-white/10'
      }`}
    >
      {children}
    </button>
  )
}

function Presence({ online }: { online: boolean }) {
  return (
    <span
      className={`inline-flex items-center gap-2 rounded-full px-3.5 py-1.5 text-sm font-semibold shadow-sm ${
        online
          ? 'bg-emerald-500 text-white'
          : 'bg-slate-200 text-slate-600 dark:bg-white/10 dark:text-slate-300'
      }`}
    >
      {online ? <Wifi className="size-4" /> : <WifiOff className="size-4" />}
      {online ? 'Onlayn' : 'Oflayn'}
    </span>
  )
}

function DeviceForm({ device, onDone, onCode }: { device: Device | null; onDone: () => void; onCode: (c: ActivationCode) => void }) {
  const qc = useQueryClient()
  const emps = useEmployeesLookup()
  const save = useMutation({
    mutationFn: (body: { displayName: string; employeeId: string | null; licenseDays?: number }): Promise<Device | ActivationCode> =>
      device
        ? api<Device>(`/api/devices/${device.id}`, { method: 'PUT', body: { displayName: body.displayName, employeeId: body.employeeId } })
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
    save.mutate({
      displayName: String(f.get('displayName')).trim(),
      employeeId: String(f.get('employeeId')) || null,
      ...(device ? {} : { licenseDays: readLicenseDays(f) }),
    })
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
      {!device && <LicenseDurationField />}
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

function IssueCodeForm({ device, onDone, onCode }: { device: Device; onDone: () => void; onCode: (c: ActivationCode) => void }) {
  const qc = useQueryClient()
  const issue = useMutation({
    mutationFn: (licenseDays: number) =>
      api<ActivationCode>(`/api/devices/${device.id}/activation-code`, { method: 'POST', body: { ttlMinutes: 60, licenseDays } }),
    onSuccess: (r) => {
      qc.invalidateQueries({ queryKey: ['devices'] })
      onCode(r)
      onDone()
    },
  })
  const submit = (e: FormEvent<HTMLFormElement>) => {
    e.preventDefault()
    issue.mutate(readLicenseDays(new FormData(e.currentTarget)))
  }
  return (
    <form onSubmit={submit} className="space-y-4">
      <p className="text-sm text-slate-600 dark:text-slate-300">
        {device.displayName} uchun yangi aktivatsiya kodi. Kompyuterda eski kirish o‘chadi, xodim yangi kodni kiritadi.
      </p>
      <LicenseDurationField />
      {issue.isError && <ErrorState error={issue.error} />}
      <div className="flex justify-end gap-2">
        <Button type="button" variant="secondary" onClick={onDone}>
          Bekor qilish
        </Button>
        <Button type="submit" loading={issue.isPending}>
          Kod yaratish
        </Button>
      </div>
    </form>
  )
}

function ExtendLicenseForm({ device, onDone }: { device: Device; onDone: () => void }) {
  const qc = useQueryClient()
  const save = useMutation({
    mutationFn: (licenseDays: number) => api<Device>(`/api/devices/${device.id}/license`, { method: 'PUT', body: { licenseDays } }),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['devices'] })
      qc.invalidateQueries({ queryKey: ['devices-lookup'] })
      onDone()
    },
  })
  const submit = (e: FormEvent<HTMLFormElement>) => {
    e.preventDefault()
    save.mutate(readLicenseDays(new FormData(e.currentTarget)))
  }
  return (
    <form onSubmit={submit} className="space-y-4">
      <p className="text-sm text-slate-600 dark:text-slate-300">
        {device.displayName} aktivatsiyasini cho‘zing yoki doimiy qiling. Taymer hozirdan boshlab yangi muddatni ko‘rsatadi.
      </p>
      <LicenseDurationField hint="Doimiy — muddatsiz. Boshqa variant — shu kundan boshlab yangi muddat." />
      {save.isError && <ErrorState error={save.error} />}
      <div className="flex justify-end gap-2">
        <Button type="button" variant="secondary" onClick={onDone}>
          Bekor qilish
        </Button>
        <Button type="submit" loading={save.isPending}>
          Muddatni saqlash
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
  const [sort, setSort] = useState({ by: 'displayName', dir: 'asc' })
  const [code, setCode] = useState<ActivationCode | null>(null)
  const [copied, setCopied] = useState(false)
  const [copyError, setCopyError] = useState(false)
  const [protectionQuery, setProtectionQuery] = useState('')
  const [editing, setEditing] = useState<Device | 'new' | null>(null)
  const [issuing, setIssuing] = useState<Device | null>(null)
  const [extending, setExtending] = useState<Device | null>(null)
  const [connections, setConnections] = useState<Device | null>(null)
  const manage = can(Permissions.DevicesManage)
  const allDevices = useDevicesLookup()

  useEffect(() => {
    setCopied(false)
    setCopyError(false)
  }, [code?.activationCode])

  const copyCode = async () => {
    if (!code?.activationCode) return
    const ok = await copyText(code.activationCode)
    setCopied(ok)
    setCopyError(!ok)
    if (ok) window.setTimeout(() => setCopied(false), 2500)
  }

  const protectionDevices = useMemo(() => {
    const q = protectionQuery.trim().toLowerCase()
    return (allDevices.data?.items ?? [])
      .filter((d) => d.status !== 'Revoked')
      .filter((d) => {
        if (!q) return true
        return [d.displayName, d.employeeName, d.hostname, d.departmentName]
          .filter(Boolean)
          .some((s) => s!.toLowerCase().includes(q))
      })
  }, [allDevices.data?.items, protectionQuery])

  const q = useQuery({
    queryKey: ['devices', page, search, status, online, sort],
    queryFn: ({ signal }) => api<PagedResult<Device>>('/api/devices', { query: { page, pageSize: 20, search, status, online, sortBy: sort.by, sortDir: sort.dir }, signal }),
    placeholderData: keepPreviousData,
  })

  const action = useMutation({
    mutationFn: ({ id, op }: { id: string; op: 'block' | 'unblock' | 'revoke' }): Promise<Device> =>
      api<Device>(`/api/devices/${id}/${op}`, { method: 'POST', body: op === 'block' ? { reason: 'Administrator tomonidan bloklandi' } : undefined }),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['devices'] })
    },
  })

  const remove = useMutation({
    mutationFn: (id: string) => api(`/api/devices/${id}`, { method: 'DELETE' }),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['devices'] }),
  })

  const uninstall = useMutation({
    mutationFn: (id: string) => api(`/api/devices/${id}/uninstall`, { method: 'POST' }),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['devices'] }),
  })

  const protection = useMutation({
    mutationFn: ({ id, enabled }: { id: string; enabled: boolean }) => api(`/api/devices/${id}/protection`, { method: 'PUT', body: { enabled } }),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['devices'] })
      qc.invalidateQueries({ queryKey: ['devices-lookup'] })
    },
    onError: (e) => alert((e as Error).message),
  })

  const setFilter = (key: string, value: string) => {
    const next = new URLSearchParams(params)
    if (value) next.set(key, value)
    else next.delete(key)
    if (key !== 'page') next.delete('page')
    setParams(next)
  }

  const toggleProtection = (d: Device, next: boolean) => {
    if (!d.protectionPasswordSet) {
      alert('Avval Sozlamalar → Tizim himoyasi parolini o‘rnating. Keyin shu kompyuterda himoyani yoqasiz.')
      return
    }
    protection.mutate({ id: d.id, enabled: next })
  }

  const live = allDevices.data?.items ?? []
  const activeLive = live.filter((d) => d.status !== 'Revoked')
  const onlineCount = activeLive.filter((d) => d.isOnline).length
  const protectedCount = activeLive.filter((d) => d.protectionEnabled).length

  return (
    <>
      <PageHeader
        title="Qurilmalar"
        subtitle="Kompyuterlar, aktivatsiya muddati va tizim himoyasi — hammasi bir joyda"
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

      <div className="flex flex-wrap gap-2">
        <SummaryChip label="Jami" value={String(q.data?.total ?? activeLive.length)} />
        <SummaryChip label="Onlayn" value={String(onlineCount)} tone="green" />
        <SummaryChip label="Himoya yoniq" value={`${protectedCount}/${activeLive.length || 0}`} />
      </div>

      {code && (
        <Card className="overflow-hidden">
          <div className="flex flex-wrap items-center justify-between gap-4 border-b border-brand-100 bg-brand-50/70 px-5 py-4 dark:border-brand-500/20 dark:bg-brand-500/10">
            <div className="min-w-0">
              <p className="text-sm font-semibold text-ink-900 dark:text-white">Bir martalik aktivatsiya kodi</p>
              <p className="mt-0.5 text-xs text-slate-500">Faqat hozir ko‘rsatiladi · ishlatish: {licenseDaysLabel(code.licenseDays)}</p>
            </div>
            <LicenseTimer until={code.expiresAt} />
          </div>
          <div className="flex flex-wrap items-center gap-3 px-5 py-4">
            <button
              type="button"
              onClick={() => void copyCode()}
              title="Nusxa olish"
              className="select-all rounded-xl border border-slate-200 bg-slate-50 px-4 py-2 font-mono text-lg tracking-[0.2em] dark:border-white/10 dark:bg-white/5"
            >
              {code.activationCode}
            </button>
            <Button type="button" variant="secondary" onClick={() => void copyCode()}>
              {copied ? <Check className="size-4 text-emerald-600" /> : <Copy className="size-4" />}
              {copied ? 'Nusxa olindi' : 'Nusxa olish'}
            </Button>
            <Button variant="ghost" onClick={() => setCode(null)}>
              Yopish
            </Button>
            {copyError && <p className="text-xs text-rose-600">Nusxa olinmadi — kodni bosing va ushlab turing.</p>}
            <p className="w-full text-xs text-slate-500">
              Kompyuterda <a href={AGENT_INSTALLER} className="font-medium text-brand-600 underline">KOSHI Agent</a> ni administrator sifatida ishga tushiring va shu kodni kiriting.
            </p>
          </div>
        </Card>
      )}

      <Card>
          <div className="flex flex-wrap items-center justify-between gap-3 border-b border-slate-100 px-5 py-4 dark:border-white/5">
            <div>
              <h2 className="text-base font-semibold">Qurilmalar ro‘yxati</h2>
              <p className="mt-0.5 text-xs text-slate-500">{q.data ? `${q.data.total} ta qurilma` : 'Yuklanmoqda…'}</p>
            </div>
            <div className="flex flex-wrap items-center gap-2">
              <div className="inline-flex rounded-xl border border-slate-200 bg-slate-50 p-1 dark:border-white/10 dark:bg-white/5">
                <PresenceFilter active={online === ''} onClick={() => setFilter('online', '')}>Barchasi</PresenceFilter>
                <PresenceFilter active={online === 'true'} onClick={() => setFilter('online', 'true')} on>
                  Onlayn
                </PresenceFilter>
                <PresenceFilter active={online === 'false'} onClick={() => setFilter('online', 'false')}>
                  Oflayn
                </PresenceFilter>
              </div>
              <Input type="search" defaultValue={search} placeholder="Nom, host, xodim…" onKeyDown={(e) => e.key === 'Enter' && setFilter('search', e.currentTarget.value.trim())} className="w-52" />
              <Select value={status} onChange={(e) => setFilter('status', e.target.value)} aria-label="Holat" className="w-44">
                <option value="">Barcha holatlar</option>
                {Object.entries(statusLabel).map(([k, v]) => (
                  <option key={k} value={k}>
                    {v}
                  </option>
                ))}
              </Select>
              <Select
                value={`${sort.by}:${sort.dir}`}
                onChange={(e) => {
                  const [by, dir] = e.target.value.split(':')
                  setSort({ by: by || 'displayName', dir: dir === 'desc' ? 'desc' : 'asc' })
                }}
                aria-label="Saralash"
                className="w-40"
              >
                <option value="displayName:asc">Nomi (A–Z)</option>
                <option value="displayName:desc">Nomi (Z–A)</option>
                <option value="status:asc">Holat</option>
                <option value="lastHeartbeatAt:desc">Oxirgi aloqa</option>
              </Select>
            </div>
          </div>

          {q.isPending ? (
            <LoadingState />
          ) : q.isError ? (
            <ErrorState error={q.error} onRetry={() => q.refetch()} />
          ) : q.data.items.length === 0 ? (
            <EmptyState title="Qurilma topilmadi" hint={search || status ? 'Filtrlarni o‘zgartirib ko‘ring.' : 'Birinchi qurilmani qo‘shing va agentga aktivatsiya kodini kiriting.'} />
          ) : (
            <ul className="divide-y divide-slate-100 dark:divide-white/5">
              {q.data.items.map((d) => {
                const licenseUntil = d.licenseExpiresAt ?? (d.status === 'Pending' ? d.activationExpiresAt : null)
                const showTimer = d.status === 'Pending' || d.status === 'Active' || d.status === 'Blocked'
                const missingKey = d.status === 'Pending' && !d.licenseExpiresAt && !d.activationExpiresAt
                return (
                  <li key={d.id} className="px-5 py-5 hover:bg-slate-50/60 dark:hover:bg-white/[0.02]">
                    <div className="flex flex-wrap items-center justify-between gap-x-6 gap-y-4">
                      <div className="flex min-w-[18rem] flex-1 items-center gap-4">
                        <div className={`flex size-12 shrink-0 items-center justify-center rounded-2xl ${d.isOnline ? 'bg-emerald-50 text-emerald-600 dark:bg-emerald-500/10 dark:text-emerald-300' : 'bg-slate-100 text-slate-400 dark:bg-white/5'}`}>
                          <Monitor className="size-5" />
                        </div>
                        <div className="min-w-0">
                          <div className="flex flex-wrap items-center gap-2.5">
                            <p className="text-base font-semibold leading-tight">{d.displayName}</p>
                            <Badge tone={statusTone[d.status]}>{statusLabel[d.status]}</Badge>
                          </div>
                          <p className="mt-1 truncate text-sm text-slate-500">
                            {[d.hostname, d.employeeName ?? 'biriktirilmagan', d.departmentName].filter(Boolean).join(' · ')}
                          </p>
                          <p className="mt-0.5 text-xs text-slate-400">
                            {d.osVersion ?? 'Windows'}
                            {d.agentVersion ? ` · Agent v${d.agentVersion}` : ''}
                            {d.appliedPolicyVersion ? ` · Siyosat v${d.appliedPolicyVersion}` : ''}
                          </p>
                        </div>
                      </div>

                      {d.status === 'Active' && <Presence online={d.isOnline} />}
                    </div>

                    <div className="mt-4 flex flex-wrap items-center justify-between gap-3 border-t border-slate-100 pt-4 dark:border-white/5">
                      <div className="flex flex-wrap items-center gap-6">
                        <div>
                          <p className="mb-1.5 text-[10px] font-semibold uppercase tracking-wider text-slate-400">Aktivatsiya</p>
                          {missingKey ? (
                            <p className="text-xs font-medium text-rose-600">Kalit yo‘q — yangi kod yarating</p>
                          ) : showTimer ? (
                            <LicenseTimer until={licenseUntil} onEdit={manage ? () => setExtending(d) : undefined} />
                          ) : (
                            <span className="text-xs text-slate-400">—</span>
                          )}
                          <button type="button" className="mt-1.5 inline-flex items-center gap-1 text-xs text-slate-500 hover:text-brand-600" onClick={() => setConnections(d)}>
                            <History className="size-3.5" /> {formatDateTime(d.lastHeartbeatAt)}
                          </button>
                        </div>
                        <div>
                          <p className="mb-1.5 text-[10px] font-semibold uppercase tracking-wider text-slate-400">Tizim himoyasi</p>
                          <div className="flex items-center gap-2">
                            <Switch
                              checked={!!d.protectionEnabled}
                              disabled={!manage || d.status === 'Revoked' || d.status === 'Pending' || protection.isPending}
                              label={`${d.displayName}: tizim himoyasi`}
                              onChange={(next) => toggleProtection(d, next)}
                            />
                            <span className={`text-xs font-medium ${d.protectionEnabled ? 'text-emerald-600 dark:text-emerald-300' : 'text-slate-400'}`}>
                              {d.protectionEnabled ? 'Yoniq' : 'O‘chiq'}
                            </span>
                          </div>
                        </div>
                      </div>

                      {manage && (
                        <div className="flex flex-wrap items-center gap-2">
                          {can(Permissions.RemoteControl) && d.status === 'Active' && (
                            d.isOnline ? (
                              <Link to={`/app/remote/${d.id}`} className="inline-flex items-center gap-1.5 rounded-xl brand-gradient px-3 py-1.5 text-xs font-semibold text-white shadow-sm shadow-brand-600/20">
                                <MonitorPlay className="size-3.5" /> Masofaviy
                              </Link>
                            ) : (
                              <ActionBtn title="Oflayn — masofaviy ochilmaydi" muted>
                                <MonitorPlay className="size-3.5" /> Masofaviy
                              </ActionBtn>
                            )
                          )}
                          <ActionBtn title="Tahrirlash / xodimga biriktirish" onClick={() => setEditing(d)}>
                            <Pencil className="size-3.5" /> Tahrir
                          </ActionBtn>
                          {(d.status === 'Pending' || d.status === 'Revoked' || d.status === 'Active') && (
                            <ActionBtn title="Yangi aktivatsiya kodi" onClick={() => setIssuing(d)}>
                              <KeyRound className="size-3.5" /> Kod
                            </ActionBtn>
                          )}
                          {(d.status === 'Pending' || d.status === 'Active' || d.status === 'Blocked') && (
                            <ActionBtn title="Aktivatsiya muddatini cho‘zish" onClick={() => setExtending(d)}>
                              <Timer className="size-3.5" /> Muddat
                            </ActionBtn>
                          )}
                          {d.status === 'Active' && d.isOnline && (
                            <ActionBtn
                              danger
                              title="KOSHI dasturini shu kompyuterdan o‘chirish"
                              onClick={() =>
                                confirm(
                                  `${d.displayName} (${d.hostname ?? 'kompyuter'}) dan KOSHI butunlay o‘chirilsinmi?\n\nDastur, xizmat va fayllar o‘sha kompyuterdan o‘chadi. Buni qaytarib bo‘lmaydi. Qurilma onlayn bo‘lishi shart.`,
                                ) && uninstall.mutate(d.id)
                              }
                            >
                              <PackageX className="size-3.5" /> Dasturni o‘chirish
                            </ActionBtn>
                          )}
                          {d.status === 'Active' && (
                            <ActionBtn title="Bloklash" onClick={() => confirm(`${d.displayName} bloklansinmi? Telemetriya qabul qilinmaydi.`) && action.mutate({ id: d.id, op: 'block' })}>
                              <Ban className="size-3.5" /> Blok
                            </ActionBtn>
                          )}
                          {d.status === 'Blocked' && (
                            <ActionBtn title="Blokdan chiqarish" onClick={() => action.mutate({ id: d.id, op: 'unblock' })}>
                              <RotateCcw className="size-3.5" /> Ochish
                            </ActionBtn>
                          )}
                          {d.status !== 'Revoked' && (
                            <ActionBtn danger title="Kirishni bekor qilish" onClick={() => confirm(`${d.displayName} credential’i bekor qilinsinmi?`) && action.mutate({ id: d.id, op: 'revoke' })}>
                              <ShieldOff className="size-3.5" /> Bekor
                            </ActionBtn>
                          )}
                          <ActionBtn
                            danger
                            title="Qurilmani o‘chirish"
                            onClick={() =>
                              confirm(`${d.displayName} ro‘yxatdan o‘chirilsinmi?\n\nKompyuterdagi aktivatsiya ham o‘chadi va dastur yangi kod so‘raydi. Tarix saqlanadi.`) && remove.mutate(d.id)
                            }
                          >
                            <Trash2 className="size-3.5" /> O‘chirish
                          </ActionBtn>
                        </div>
                      )}
                    </div>
                  </li>
                )
              })}
            </ul>
          )}
          {action.isError && <ErrorState error={action.error} />}
          {uninstall.isError && <ErrorState error={uninstall.error} />}
          {q.data && <Pagination page={page} pageSize={q.data.pageSize} total={q.data.total} onChange={(p) => setFilter('page', String(p))} />}
        </Card>

        <Card>
          <div className="flex items-start justify-between gap-3 border-b border-slate-100 px-5 py-4 dark:border-white/5">
            <div>
              <h2 className="flex items-center gap-2 text-sm font-semibold">
                <ShieldCheck className="size-4 text-brand-600" /> Tizim himoyasi
              </h2>
              <p className="mt-0.5 text-xs text-slate-500">Har bir kompyuterda alohida yoqiladi</p>
            </div>
          </div>
          <div className="px-5 py-3">
            <Input
              type="search"
              value={protectionQuery}
              onChange={(e) => setProtectionQuery(e.target.value)}
              placeholder="Qidirish…"
              aria-label="Tizim himoyasida qidirish"
            />
          </div>
          <div className="max-h-[28rem] overflow-y-auto">
            {allDevices.isPending ? (
              <LoadingState />
            ) : allDevices.isError ? (
              <ErrorState error={allDevices.error} onRetry={() => allDevices.refetch()} />
            ) : activeLive.length === 0 ? (
              <EmptyState title="Qurilma yo‘q" hint="Avval kompyuter qo‘shing." />
            ) : protectionDevices.length === 0 ? (
              <EmptyState title="Topilmadi" hint="Qidiruvni o‘zgartiring." />
            ) : (
              <ul className="grid gap-px sm:grid-cols-2 xl:grid-cols-3">
                {protectionDevices.map((d) => (
                  <li key={d.id} className="flex items-center justify-between gap-3 px-5 py-3">
                    <div className="min-w-0">
                      <p className="truncate text-sm font-medium">{d.displayName}</p>
                      <p className="truncate text-[11px] text-slate-400">{d.hostname ?? d.employeeName ?? '—'}</p>
                    </div>
                    <div className="flex shrink-0 items-center gap-2">
                      <span className={`text-xs font-medium ${d.protectionEnabled ? 'text-emerald-600' : 'text-slate-400'}`}>{d.protectionEnabled ? 'Yoniq' : 'O‘chiq'}</span>
                      <Switch
                        checked={!!d.protectionEnabled}
                        disabled={!manage || d.status === 'Pending' || protection.isPending}
                        label={`${d.displayName}: tizim himoyasi`}
                        onChange={(next) => toggleProtection(d, next)}
                      />
                    </div>
                  </li>
                ))}
              </ul>
            )}
          </div>
          {protection.isError && <ErrorState error={protection.error} />}
          <p className="border-t border-slate-100 px-5 py-3 text-[11px] leading-relaxed text-slate-500 dark:border-white/5">
            Parol <Link to="/app/settings" className="font-medium text-brand-600 underline">Sozlamalar</Link> da bir marta qo‘yiladi. Yoqish faqat shu kompyuterga ta’sir qiladi.
          </p>
        </Card>

      <Modal open={editing !== null} title={editing === 'new' ? 'Yangi qurilma' : 'Qurilmani tahrirlash'} onClose={() => setEditing(null)}>
        {editing !== null && <DeviceForm device={editing === 'new' ? null : editing} onDone={() => setEditing(null)} onCode={setCode} />}
      </Modal>
      <Modal open={issuing !== null} title="Aktivatsiya kodi" onClose={() => setIssuing(null)}>
        {issuing && <IssueCodeForm device={issuing} onDone={() => setIssuing(null)} onCode={setCode} />}
      </Modal>
      <Modal open={extending !== null} title="Aktivatsiya muddati" onClose={() => setExtending(null)}>
        {extending && <ExtendLicenseForm device={extending} onDone={() => setExtending(null)} />}
      </Modal>
      {connections && <ConnectionsModal device={connections} onClose={() => setConnections(null)} />}
    </>
  )
}

function SummaryChip({ label, value, tone }: { label: string; value: string; tone?: 'green' }) {
  return (
    <div className="inline-flex items-baseline gap-2 rounded-2xl border border-slate-200/80 bg-white px-4 py-2.5 shadow-sm dark:border-white/10 dark:bg-ink-900">
      <span className="text-xs text-slate-500">{label}</span>
      <span className={`text-base font-semibold tabular-nums ${tone === 'green' ? 'text-emerald-600' : ''}`}>{value}</span>
    </div>
  )
}

function PresenceFilter({ active, onClick, children, on }: { active: boolean; onClick: () => void; children: ReactNode; on?: boolean }) {
  return (
    <button
      type="button"
      onClick={onClick}
      className={`rounded-lg px-3.5 py-1.5 text-sm font-semibold transition ${
        active
          ? on
            ? 'bg-emerald-500 text-white shadow-sm'
            : 'bg-white text-slate-800 shadow-sm dark:bg-ink-800 dark:text-white'
          : 'text-slate-500 hover:text-slate-800 dark:hover:text-slate-200'
      }`}
    >
      {children}
    </button>
  )
}
