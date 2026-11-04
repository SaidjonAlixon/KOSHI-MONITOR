import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Ban, Clock, Globe, PackageX, Plus, Power, Search, ShieldOff, Trash2 } from 'lucide-react'
import { useEffect, useMemo, useState, type FormEvent, type ReactNode } from 'react'
import { Link } from 'react-router'
import { Field, Input, Modal, Select, tableClass, tbodyClass, td, th, theadClass, useDevicesLookup } from '@/components/forms'
import { Badge, Button, Card, CardHeader, EmptyState, ErrorState, LoadingState, formatDateTime } from '@/components/ui'
import { ApiError, api } from '@/lib/api'
import type { BlockedSite, DeviceControls } from '@/lib/types'

const CATEGORIES = ['Ijtimoiy tarmoqlar', 'Video va kino', 'O‘yinlar', 'Onlayn do‘konlar', 'Tikish (bukmeker)', 'Yangiliklar', 'Boshqa'] as const

const PRESETS: { label: string; category: (typeof CATEGORIES)[number]; domains: string[] }[] = [
  { label: 'Ijtimoiy tarmoqlar', category: 'Ijtimoiy tarmoqlar', domains: ['facebook.com', 'instagram.com', 'tiktok.com', 'vk.com', 'ok.ru', 'x.com', 'twitter.com', 'threads.net', 'snapchat.com', 'pinterest.com'] },
  { label: 'Video va kino', category: 'Video va kino', domains: ['youtube.com', 'youtu.be', 'netflix.com', 'twitch.tv', 'kinopoisk.ru', 'ivi.ru', 'rutube.ru', 'mover.uz'] },
  { label: 'O‘yinlar', category: 'O‘yinlar', domains: ['steampowered.com', 'steamcommunity.com', 'epicgames.com', 'roblox.com', 'miniclip.com', 'poki.com', 'y8.com', 'friv.com'] },
  { label: 'Onlayn do‘konlar', category: 'Onlayn do‘konlar', domains: ['uzum.uz', 'olx.uz', 'aliexpress.com', 'wildberries.ru', 'ozon.ru', 'temu.com', 'amazon.com'] },
  { label: 'Tikish saytlari', category: 'Tikish (bukmeker)', domains: ['1xbet.com', 'melbet.com', 'mostbet.com', 'betwinner.com', 'parimatch.com'] },
]

const DURATIONS = [
  { minutes: 15, label: '15 daq' },
  { minutes: 30, label: '30 daq' },
  { minutes: 60, label: '1 soat' },
  { minutes: 120, label: '2 soat' },
  { minutes: 240, label: '4 soat' },
  { minutes: 480, label: '8 soat' },
  { minutes: 1440, label: '1 kun' },
]

function errorText(e: unknown) {
  return e instanceof ApiError ? [e.message, ...Object.values(e.errors).flat()].join(' ') : 'Xatolik yuz berdi'
}

function useNow(intervalMs = 1000) {
  const [now, setNow] = useState(() => Date.now())
  useEffect(() => {
    const t = window.setInterval(() => setNow(Date.now()), intervalMs)
    return () => window.clearInterval(t)
  }, [intervalMs])
  return now
}

function formatRemaining(ms: number) {
  if (ms <= 0) return '0:00'
  const total = Math.floor(ms / 1000)
  const h = Math.floor(total / 3600)
  const m = Math.floor((total % 3600) / 60)
  const s = total % 60
  const pad = (n: number) => String(n).padStart(2, '0')
  return h > 0 ? `${h}:${pad(m)}:${pad(s)}` : `${m}:${pad(s)}`
}

function useInvalidateSites() {
  const qc = useQueryClient()
  return () => qc.invalidateQueries({ queryKey: ['blocked-sites'] })
}

// ---------- Allow (timer) modal ----------

function AllowForm({ site, onDone, defaultDeviceId }: { site: BlockedSite; onDone: () => void; defaultDeviceId?: string }) {
  const invalidate = useInvalidateSites()
  const devices = useDevicesLookup()
  const now = useNow()
  const [minutes, setMinutes] = useState(30)
  const [custom, setCustom] = useState('')
  const [scope, setScope] = useState<'all' | 'device'>(defaultDeviceId ? 'device' : 'all')
  const [deviceId, setDeviceId] = useState(defaultDeviceId ?? '')
  const [reason, setReason] = useState('')
  const [error, setError] = useState<string | null>(null)

  const grant = useMutation({
    mutationFn: () =>
      api<BlockedSite>(`/api/web-filter/sites/${site.id}/allow`, {
        method: 'POST',
        body: { minutes: custom ? Number(custom) : minutes, deviceId: scope === 'device' ? deviceId : null, reason: reason || null },
      }),
    onSuccess: () => {
      invalidate()
      onDone()
    },
    onError: (e) => setError(errorText(e)),
  })
  const revoke = useMutation({
    mutationFn: (id: string) => api(`/api/web-filter/allowances/${id}`, { method: 'DELETE' }),
    onSuccess: () => invalidate(),
  })

  const submit = (e: FormEvent) => {
    e.preventDefault()
    setError(null)
    grant.mutate()
  }

  return (
    <form onSubmit={submit} className="space-y-5">
      <p className="text-sm text-slate-600 dark:text-slate-300">
        <span className="font-mono font-semibold">{site.domain}</span> saytiga vaqtincha kirishga ruxsat. Vaqt tugashi bilan sayt avtomatik yana bloklanadi (kompyuter internetsiz bo‘lsa ham).
      </p>

      <div>
        <p className="text-sm font-medium">Ruxsat muddati</p>
        <div className="mt-2 flex flex-wrap gap-2">
          {DURATIONS.map((d) => (
            <button
              key={d.minutes}
              type="button"
              onClick={() => {
                setMinutes(d.minutes)
                setCustom('')
              }}
              className={`rounded-xl border px-3 py-1.5 text-sm transition ${!custom && minutes === d.minutes ? 'border-brand-500 bg-brand-50 text-brand-700 dark:bg-brand-500/10 dark:text-brand-300' : 'border-slate-200 hover:bg-slate-50 dark:border-white/10 dark:hover:bg-white/5'}`}
            >
              {d.label}
            </button>
          ))}
          <Input type="number" min={1} max={10080} value={custom} onChange={(e) => setCustom(e.target.value)} placeholder="boshqa (daq)" className="w-36" />
        </div>
      </div>

      <div className="grid gap-4 sm:grid-cols-2">
        <Field label="Kimga">
          <Select value={scope} onChange={(e) => setScope(e.target.value as 'all' | 'device')}>
            <option value="all">Barcha kompyuterlarga</option>
            <option value="device">Bitta kompyuterga</option>
          </Select>
        </Field>
        {scope === 'device' && (
          <Field label="Kompyuter">
            <Select required value={deviceId} onChange={(e) => setDeviceId(e.target.value)}>
              <option value="">Tanlang…</option>
              {(devices.data?.items ?? []).map((d) => (
                <option key={d.id} value={d.id}>
                  {d.displayName}{d.employeeName ? ` — ${d.employeeName}` : ''}
                </option>
              ))}
            </Select>
          </Field>
        )}
        <Field label="Sabab (ixtiyoriy)" className="sm:col-span-2">
          <Input maxLength={300} value={reason} onChange={(e) => setReason(e.target.value)} placeholder="masalan: marketing bo‘limi uchun reklama tekshiruvi" />
        </Field>
      </div>

      {site.activeAllowances.length > 0 && (
        <div className="rounded-xl border border-slate-200 dark:border-white/10">
          <p className="border-b border-slate-100 px-4 py-2 text-xs font-semibold uppercase text-slate-500 dark:border-white/5">Amaldagi ruxsatlar</p>
          <ul className="divide-y divide-slate-100 dark:divide-white/5">
            {site.activeAllowances.map((a) => (
              <li key={a.id} className="flex items-center justify-between gap-3 px-4 py-2 text-sm">
                <span>
                  <span className="font-medium">{a.deviceName ?? 'Barcha kompyuterlar'}</span>
                  <span className="ml-2 font-mono text-emerald-600">{formatRemaining(new Date(a.expiresAt).getTime() - now)} qoldi</span>
                  <span className="block text-xs text-slate-500">{formatDateTime(a.expiresAt)} gacha{a.createdBy ? ` · ${a.createdBy}` : ''}{a.reason ? ` · ${a.reason}` : ''}</span>
                </span>
                <Button type="button" variant="ghost" onClick={() => revoke.mutate(a.id)} loading={revoke.isPending && revoke.variables === a.id}>
                  Bekor qilish
                </Button>
              </li>
            ))}
          </ul>
        </div>
      )}

      {error && <p className="text-sm text-rose-600">{error}</p>}
      <div className="flex justify-end gap-2">
        <Button type="button" variant="secondary" onClick={onDone}>
          Yopish
        </Button>
        <Button type="submit" loading={grant.isPending} disabled={scope === 'device' && !deviceId}>
          <Clock className="size-4" /> Ruxsat berish
        </Button>
      </div>
    </form>
  )
}

// ---------- Blocked sites ----------

export function SiteBlockCard({ manage, deviceId }: { manage: boolean; deviceId?: string }) {
  const invalidate = useInvalidateSites()
  const now = useNow()
  const q = useQuery({
    queryKey: ['blocked-sites'],
    queryFn: () => api<BlockedSite[]>('/api/web-filter/sites'),
    refetchInterval: (query) => ((query.state.data ?? []).some((s) => s.activeAllowances.length > 0) ? 4_000 : 30_000),
  })
  const [input, setInput] = useState('')
  const [category, setCategory] = useState<string>('Boshqa')
  const [search, setSearch] = useState('')
  const [allowFor, setAllowFor] = useState<BlockedSite | null>(null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    const justExpired = (q.data ?? []).some((s) =>
      s.activeAllowances.some((a) => {
        const left = new Date(a.expiresAt).getTime() - now
        return left <= 0 && left > -4000
      }),
    )
    if (justExpired) void q.refetch()
  }, [now, q.data, q.refetch])

  const add = useMutation({
    mutationFn: (body: { domains: string[]; category: string }) => api<BlockedSite[]>('/api/web-filter/sites', { method: 'POST', body }),
    onSuccess: () => {
      setInput('')
      setError(null)
      invalidate()
    },
    onError: (e) => setError(errorText(e)),
  })
  const toggle = useMutation({
    mutationFn: (s: BlockedSite) => api(`/api/web-filter/sites/${s.id}`, { method: 'PUT', body: { category: s.category, note: s.note, isEnabled: !s.isEnabled } }),
    onSuccess: () => invalidate(),
  })
  const remove = useMutation({
    mutationFn: (id: string) => api(`/api/web-filter/sites/${id}`, { method: 'DELETE' }),
    onSuccess: () => invalidate(),
  })

  const sites = useMemo(() => {
    const term = search.trim().toLowerCase()
    const all = q.data ?? []
    return term ? all.filter((s) => s.domain.includes(term) || s.category?.toLowerCase().includes(term)) : all
  }, [q.data, search])
  const openAllow = allowFor ? (q.data ?? []).find((s) => s.id === allowFor.id) ?? allowFor : null

  const submit = (e: FormEvent) => {
    e.preventDefault()
    const domains = input.split(/[\s,;]+/).map((d) => d.trim()).filter(Boolean)
    if (domains.length > 0) add.mutate({ domains, category })
  }

  return (
    <Card className="xl:col-span-2">
      <CardHeader
        title="Saytlar cheklovi"
        subtitle="Taqiqlangan saytlar barcha brauzerlarda (Chrome, Edge, Firefox, Yandex, Brave, Opera; inkognito rejimida ham) ochilmaydi. Vaqtli ruxsat berish mumkin."
        actions={
          <span className="inline-flex items-center gap-1.5 rounded-xl bg-rose-50 px-3 py-1.5 text-xs font-medium text-rose-700 dark:bg-rose-500/10 dark:text-rose-300">
            <Ban className="size-3.5" /> {(q.data ?? []).filter((s) => s.isEnabled).length} ta bloklangan
          </span>
        }
      />
      {manage && (
        <form onSubmit={submit} className="space-y-3 border-b border-slate-100 p-5 dark:border-white/5">
          <div className="flex flex-wrap items-end gap-3">
            <Field label="Sayt manzili" hint="Bir nechtasini vergul yoki probel bilan kiriting. Sayt va uning barcha subdomenlari bloklanadi." className="min-w-[280px] flex-1">
              <Input value={input} onChange={(e) => setInput(e.target.value)} placeholder="masalan: youtube.com, https://instagram.com" />
            </Field>
            <Field label="Toifa" className="w-52">
              <Select value={category} onChange={(e) => setCategory(e.target.value)}>
                {CATEGORIES.map((c) => (
                  <option key={c} value={c}>
                    {c}
                  </option>
                ))}
              </Select>
            </Field>
            <Button type="submit" loading={add.isPending} disabled={!input.trim()}>
              <Plus className="size-4" /> Bloklash
            </Button>
          </div>
          <div className="flex flex-wrap items-center gap-2">
            <span className="text-xs text-slate-500">Tayyor to‘plamlar:</span>
            {PRESETS.map((p) => (
              <button
                key={p.label}
                type="button"
                onClick={() => add.mutate({ domains: p.domains, category: p.category })}
                className="rounded-full border border-slate-200 px-3 py-1 text-xs text-slate-600 hover:border-rose-300 hover:bg-rose-50 hover:text-rose-700 dark:border-white/10 dark:text-slate-300 dark:hover:bg-rose-500/10"
              >
                + {p.label}
              </button>
            ))}
          </div>
          {error && <p className="text-sm text-rose-600">{error}</p>}
        </form>
      )}

      <div className="border-b border-slate-100 px-5 py-3 dark:border-white/5">
        <label className="relative block max-w-sm">
          <Search className="pointer-events-none absolute left-3 top-1/2 size-4 -translate-y-1/2 text-slate-400" />
          <Input value={search} onChange={(e) => setSearch(e.target.value)} placeholder="Sayt yoki toifa bo‘yicha qidirish…" className="pl-9" />
        </label>
      </div>

      {q.isPending ? (
        <LoadingState />
      ) : q.isError ? (
        <ErrorState error={q.error} onRetry={() => q.refetch()} />
      ) : sites.length === 0 ? (
        <EmptyState title="Taqiqlangan saytlar yo‘q" hint="Yuqorida sayt manzilini kiriting yoki tayyor to‘plamni tanlang." />
      ) : (
        <div className="max-h-[480px] overflow-y-auto">
          <table className={tableClass}>
            <thead className={theadClass}>
              <tr>
                <th className={th}>Sayt</th>
                <th className={th}>Toifa</th>
                <th className={th}>Holat</th>
                {manage && <th className={th} />}
              </tr>
            </thead>
            <tbody className={tbodyClass}>
              {sites.map((s) => {
                const active = s.activeAllowances.filter((a) => new Date(a.expiresAt).getTime() > now)
                const global = active.find((a) => a.deviceId === null)
                return (
                  <tr key={s.id} className={s.isEnabled ? '' : 'opacity-60'}>
                    <td className={td}>
                      <span className="flex items-center gap-2 font-mono text-sm">
                        <Globe className="size-4 text-slate-400" /> {s.domain}
                      </span>
                      <span className="text-xs text-slate-500">va barcha subdomenlari · {formatDateTime(s.createdAt)}</span>
                    </td>
                    <td className={td}>{s.category ? <Badge tone="gray">{s.category}</Badge> : '—'}</td>
                    <td className={`${td} text-xs`}>
                      {!s.isEnabled ? (
                        <Badge tone="gray">Cheklov o‘chirilgan</Badge>
                      ) : global ? (
                        <span className="inline-flex items-center gap-1.5 text-emerald-700 dark:text-emerald-300">
                          <Clock className="size-3.5" /> Hammaga ruxsat · <span className="font-mono">{formatRemaining(new Date(global.expiresAt).getTime() - now)}</span> qoldi
                        </span>
                      ) : active.length > 0 ? (
                        <span className="flex flex-col gap-0.5">
                          <Badge tone="red">Bloklangan</Badge>
                          <span className="text-emerald-700 dark:text-emerald-300">
                            {active.length} ta kompyuterga ruxsat · {formatRemaining(Math.max(...active.map((a) => new Date(a.expiresAt).getTime())) - now)}
                          </span>
                        </span>
                      ) : (
                        <Badge tone="red">Bloklangan</Badge>
                      )}
                    </td>
                    {manage && (
                      <td className={td}>
                        <div className="flex justify-end gap-1">
                          <Button variant="ghost" title="Vaqtli ruxsat berish" disabled={!s.isEnabled} onClick={() => setAllowFor(s)}>
                            <Clock className="size-4 text-emerald-600" />
                          </Button>
                          <Button variant="ghost" title={s.isEnabled ? 'Cheklovni vaqtincha o‘chirish' : 'Cheklovni yoqish'} onClick={() => toggle.mutate(s)}>
                            <Power className={`size-4 ${s.isEnabled ? 'text-amber-500' : 'text-slate-400'}`} />
                          </Button>
                          <Button variant="ghost" title="Ro‘yxatdan o‘chirish" onClick={() => confirm(`${s.domain} taqiqlangan saytlar ro‘yxatidan o‘chirilsinmi?`) && remove.mutate(s.id)}>
                            <Trash2 className="size-4 text-rose-500" />
                          </Button>
                        </div>
                      </td>
                    )}
                  </tr>
                )
              })}
            </tbody>
          </table>
        </div>
      )}
      <p className="border-t border-slate-100 px-5 py-3 text-xs text-slate-500 dark:border-white/5">
        O‘zgarishlar ulangan kompyuterlarga darhol yuboriladi. Vaqtli ruxsat tugashi bilan sayt avtomatik yana bloklanadi (YouTube video oqimi ham). Brauzerda “administrator tomonidan bloklangan” deb ko‘rsatiladi.
      </p>

      <Modal open={openAllow !== null} title="Vaqtli ruxsat berish" onClose={() => setAllowFor(null)} wide>
        {openAllow && <AllowForm site={openAllow} onDone={() => setAllowFor(null)} defaultDeviceId={deviceId} />}
      </Modal>
    </Card>
  )
}

// ---------- VPN and installation controls ----------

function useControls() {
  const qc = useQueryClient()
  const q = useQuery({ queryKey: ['device-controls'], queryFn: () => api<DeviceControls>('/api/web-filter/controls') })
  const save = useMutation({
    mutationFn: (body: { vpnBlocked: boolean; installBlocked: boolean }) => api<DeviceControls>('/api/web-filter/controls', { method: 'PUT', body }),
    onSuccess: (data) => qc.setQueryData(['device-controls'], data),
  })
  return { q, save }
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
      className={`relative inline-flex h-7 w-12 shrink-0 items-center rounded-full transition disabled:cursor-not-allowed disabled:opacity-50 ${checked ? 'bg-rose-500' : 'bg-slate-300 dark:bg-white/20'}`}
    >
      <span className={`inline-block size-5 rounded-full bg-white shadow transition ${checked ? 'translate-x-6' : 'translate-x-1'}`} />
    </button>
  )
}

function ControlCard({ icon: Icon, title, subtitle, enabled, onToggle, saving, disabled, children, error }: {
  icon: typeof ShieldOff
  title: string
  subtitle: string
  enabled: boolean
  onToggle: (v: boolean) => void
  saving: boolean
  disabled?: boolean
  children: ReactNode
  error?: string | null
}) {
  return (
    <Card>
      <div className="flex items-start justify-between gap-4 border-b border-slate-100 px-5 py-4 dark:border-white/5">
        <div className="flex items-start gap-3">
          <span className={`flex size-10 shrink-0 items-center justify-center rounded-xl ${enabled ? 'bg-rose-50 text-rose-600 dark:bg-rose-500/10' : 'bg-slate-100 text-slate-500 dark:bg-white/5'}`}>
            <Icon className="size-5" />
          </span>
          <div>
            <h2 className="text-sm font-semibold">{title}</h2>
            <p className="mt-0.5 text-xs text-slate-500">{subtitle}</p>
          </div>
        </div>
        <div className="flex items-center gap-2">
          <span className={`text-xs font-medium ${enabled ? 'text-rose-600' : 'text-slate-500'}`}>{saving ? 'Saqlanmoqda…' : enabled ? 'Taqiqlangan' : 'Ruxsat'}</span>
          <Switch checked={enabled} disabled={disabled || saving} onChange={onToggle} label={title} />
        </div>
      </div>
      <div className="space-y-2 p-5 text-xs text-slate-600 dark:text-slate-300">{children}</div>
      {error && <p className="px-5 pb-4 text-sm text-rose-600">{error}</p>}
    </Card>
  )
}

export function VpnControlCard({ manage }: { manage: boolean }) {
  const { q, save } = useControls()
  if (q.isPending) return <Card><LoadingState /></Card>
  if (q.isError) return <Card><ErrorState error={q.error} /></Card>
  const c = q.data
  return (
    <ControlCard
      icon={ShieldOff}
      title="VPN ishlatishni cheklash"
      subtitle="Monitoring va sayt cheklovlarini VPN orqali aylanib o‘tishning oldini oladi"
      enabled={c.vpnBlocked}
      saving={save.isPending}
      disabled={!manage}
      onToggle={(v) => save.mutate({ vpnBlocked: v, installBlocked: c.installBlocked })}
      error={save.isError ? errorText(save.error) : null}
    >
      <p>Yoqilganda har bir kompyuterda:</p>
      <ul className="list-disc space-y-1 pl-5">
        <li>VPN dasturlari (NordVPN, Proton, Surfshark, OpenVPN, WireGuard, Psiphon, Hotspot Shield, v2rayN, Hiddify, Tor va boshqalar) darhol yopiladi, ularning xizmatlari o‘chiriladi;</li>
        <li>VPN tarmoq adapterlari (TAP, Wintun, WireGuard) o‘chiriladi, Windows’dagi VPN ulanishlari uziladi;</li>
        <li>brauzerlardagi VPN/proxy kengaytmalari ishlamaydi (to‘g‘ridan-to‘g‘ri ulanish majburiy);</li>
        <li>VPN provayderlari saytlari bloklanadi — VPN yuklab olinmaydi.</li>
      </ul>
      <p className="text-slate-500">Korporativ VPN mijozlari (FortiClient, Cisco AnyConnect, GlobalProtect) cheklanmaydi. O‘chirilganda hamma narsa avvalgi holatiga qaytariladi.</p>
    </ControlCard>
  )
}

export function InstallControlCard({ manage }: { manage: boolean }) {
  const { q, save } = useControls()
  if (q.isPending) return <Card><LoadingState /></Card>
  if (q.isError) return <Card><ErrorState error={q.error} /></Card>
  const c = q.data
  return (
    <ControlCard
      icon={PackageX}
      title="Yangi dastur o‘rnatishni cheklash"
      subtitle="Har qanday o‘rnatish faqat administrator paroli bilan davom etadi"
      enabled={c.installBlocked}
      saving={save.isPending}
      disabled={!manage || (!c.protectionPasswordSet && !c.installBlocked)}
      onToggle={(v) => save.mutate({ vpnBlocked: c.vpnBlocked, installBlocked: v })}
      error={save.isError ? errorText(save.error) : null}
    >
      <p>Yoqilganda xodim oddiy yoki “administrator sifatida” ishga tushirishidan qat’i nazar:</p>
      <ul className="list-disc space-y-1 pl-5">
        <li>har qanday o‘rnatuvchi (setup.exe, .msi, winget, App Installer, Microsoft Store) ishga tushishi bilan muzlatiladi;</li>
        <li>ekranda parol oynasi chiqadi — <b>Tizim himoyasi paroli</b> to‘g‘ri kiritilsagina o‘rnatish davom etadi (10 daqiqa ruxsat);</li>
        <li>parol 3 marta noto‘g‘ri kiritilsa, bekor qilinsa yoki 2,5 daqiqada kiritilmasa — o‘rnatish yopiladi.</li>
      </ul>
      <p className="text-slate-500">O‘rnatilgan dasturlarning yangilanishi va o‘chirish (uninstall) cheklanmaydi.</p>
      {!c.protectionPasswordSet && (
        <p className="rounded-lg bg-amber-50 px-3 py-2 text-amber-700 dark:bg-amber-500/10 dark:text-amber-300">
          Avval <Link to="/app/settings" className="font-semibold underline">Sozlamalar → Tizim himoyasi</Link> bo‘limida himoya parolini o‘rnating — o‘rnatish shu parol bilan ochiladi.
        </p>
      )}
    </ControlCard>
  )
}
