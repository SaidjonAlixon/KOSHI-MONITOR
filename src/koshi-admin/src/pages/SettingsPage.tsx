import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { CalendarClock, Plus, Save, Upload } from 'lucide-react'
import { useEffect, useState, type FormEvent } from 'react'
import { Checkbox, Field, Input, Modal, PageHeader, Textarea } from '@/components/forms'
import { Badge, Button, Card, CardHeader, ErrorState, LoadingState } from '@/components/ui'
import { ApiError, api, getAccessToken } from '@/lib/api'
import { Permissions, useAuth } from '@/lib/auth'
import { WEEKDAYS } from '@/lib/labels'
import type { AgentRelease, SystemSettings, Weekday, WorkSchedule } from '@/lib/types'

function ScheduleForm({ schedule, onDone }: { schedule: WorkSchedule | null; onDone: () => void }) {
  const qc = useQueryClient()
  const [days, setDays] = useState<Weekday[]>(schedule?.workDays ?? ['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday'])
  const save = useMutation({
    mutationFn: (f: FormData) => {
      const t = (k: string) => (String(f.get(k) ?? '') ? `${String(f.get(k))}:00` : null)
      const body = { name: String(f.get('name')), timeZoneId: String(f.get('timeZoneId')), workDays: days, workStart: t('workStart'), workEnd: t('workEnd'), breakStart: t('breakStart'), breakEnd: t('breakEnd') }
      return schedule ? api(`/api/schedules/${schedule.id}`, { method: 'PUT', body }) : api('/api/schedules', { method: 'POST', body })
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['schedules'] })
      onDone()
    },
  })
  const hhmm = (v: string | null | undefined) => (v ? v.slice(0, 5) : '')
  return (
    <form
      className="grid gap-4 sm:grid-cols-2"
      onSubmit={(e: FormEvent<HTMLFormElement>) => {
        e.preventDefault()
        save.mutate(new FormData(e.currentTarget))
      }}
    >
      <Field label="Nomi" className="sm:col-span-2">
        <Input name="name" required maxLength={120} defaultValue={schedule?.name} />
      </Field>
      <Field label="Vaqt zonasi" hint="IANA, masalan Asia/Tashkent">
        <Input name="timeZoneId" required defaultValue={schedule?.timeZoneId ?? 'Asia/Tashkent'} />
      </Field>
      <div>
        <p className="text-sm font-medium">Ish kunlari</p>
        <div className="mt-2 flex flex-wrap gap-2">
          {WEEKDAYS.map(([k, v]) => (
            <Checkbox key={k} label={v} checked={days.includes(k)} onChange={() => setDays((d) => (d.includes(k) ? d.filter((x) => x !== k) : [...d, k]))} />
          ))}
        </div>
      </div>
      <Field label="Ish boshlanishi">
        <Input name="workStart" type="time" required defaultValue={hhmm(schedule?.workStart) || '09:00'} />
      </Field>
      <Field label="Ish tugashi">
        <Input name="workEnd" type="time" required defaultValue={hhmm(schedule?.workEnd) || '18:00'} />
      </Field>
      <Field label="Tanaffus boshlanishi">
        <Input name="breakStart" type="time" defaultValue={schedule ? hhmm(schedule.breakStart) : '13:00'} />
      </Field>
      <Field label="Tanaffus tugashi">
        <Input name="breakEnd" type="time" defaultValue={schedule ? hhmm(schedule.breakEnd) : '14:00'} />
      </Field>
      {save.isError && (
        <div className="sm:col-span-2">
          <ErrorState error={save.error} />
        </div>
      )}
      <div className="flex justify-end gap-2 sm:col-span-2">
        <Button type="button" variant="secondary" onClick={onDone}>
          Bekor qilish
        </Button>
        <Button type="submit" loading={save.isPending} disabled={days.length === 0}>
          Saqlash
        </Button>
      </div>
    </form>
  )
}

// Protection password: exactly 7 chars = 3 letters + 2 digits + 2 symbols
function randomInt(maxExclusive: number): number {
  const r = crypto.getRandomValues(new Uint32Array(1))[0] ?? 0
  return Math.floor((r / 2 ** 32) * maxExclusive)
}

function generateProtectionPassword(): string {
  const letters = 'abcdefghijkmnpqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ'
  const digits = '23456789'
  const symbols = '!@#$%&*?'
  const pick = (set: string) => set.charAt(randomInt(set.length))
  const chars = [pick(letters), pick(letters), pick(letters), pick(digits), pick(digits), pick(symbols), pick(symbols)]
  for (let i = chars.length - 1; i > 0; i--) {
    const j = randomInt(i + 1)
    const tmp = chars[i]!
    chars[i] = chars[j]!
    chars[j] = tmp
  }
  return chars.join('')
}

function isValidProtectionPassword(p: string): boolean {
  if (p.length !== 7) return false
  const letters = (p.match(/[A-Za-z]/g) ?? []).length
  const digits = (p.match(/[0-9]/g) ?? []).length
  const symbols = (p.match(/[^A-Za-z0-9\s]/g) ?? []).length
  return letters === 3 && digits === 2 && symbols === 2
}

function bumpPatch(version: string): string {
  const m = /^(\d+)\.(\d+)\.(\d+)$/.exec(version.trim())
  if (!m) return version
  return `${m[1]}.${m[2]}.${Number(m[3]) + 1}`
}

function AgentReleaseManager({ editable }: { editable: boolean }) {
  const qc = useQueryClient()
  const current = useQuery({ queryKey: ['agent-release'], queryFn: () => api<AgentRelease | null>('/api/agent/release') })
  const [version, setVersion] = useState('')
  const [file, setFile] = useState<File | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [ok, setOk] = useState(false)

  const publish = useMutation({
    mutationFn: async () => {
      if (!file) throw new ApiError(400, 'validation_failed', 'EXE faylni tanlang.')
      const fd = new FormData()
      fd.set('version', version.trim())
      fd.set('file', file)
      const token = getAccessToken()
      const res = await fetch('/api/agent/release', {
        method: 'POST',
        headers: token ? { Authorization: `Bearer ${token}`, 'X-KOSHI-Client': 'admin' } : {},
        body: fd,
        credentials: 'same-origin',
      })
      if (!res.ok) {
        const body = await res.json().catch(() => ({}))
        throw new ApiError(res.status, body.code ?? 'error', body.title ?? 'Yuklab bo‘lmadi', body.errors ?? {})
      }
      return (await res.json()) as AgentRelease
    },
    onSuccess: () => {
      setOk(true)
      setFile(null)
      setVersion('')
      qc.invalidateQueries({ queryKey: ['agent-release'] })
    },
    onError: (e) => setError(e instanceof ApiError ? [e.message, ...Object.values(e.errors).flat()].join(' ') : 'Xatolik'),
  })

  const rel = current.data
  const nextVersion = rel ? bumpPatch(rel.version) : '1.0.0'
  return (
    <div className="rounded-xl border border-slate-200 p-4 dark:border-white/10">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <p className="text-sm font-medium">Agentning joriy (serverdagi) versiyasi</p>
          <p className="mt-1 text-xs text-slate-500">Barcha kompyuterlardagi agentlar shu versiyaga yangilanadi</p>
        </div>
        {current.isPending ? (
          <span className="text-xs text-slate-400">Yuklanmoqda…</span>
        ) : rel ? (
          <div className="flex items-center gap-2 rounded-xl border border-emerald-200 bg-emerald-50 px-3.5 py-2 dark:border-emerald-500/20 dark:bg-emerald-500/10">
            <span className="size-2 rounded-full bg-emerald-500" />
            <div className="text-right">
              <p className="text-sm font-semibold text-emerald-700 dark:text-emerald-300">v{rel.version}</p>
              <p className="text-[11px] text-emerald-600/80 dark:text-emerald-400/80">
                {rel.buildDate} · {(rel.size / 1_048_576).toFixed(0)} MB · {rel.source === 'uploaded' ? 'admin yuklagan' : 'tizim'}
              </p>
            </div>
          </div>
        ) : (
          <span className="rounded-xl border border-amber-200 bg-amber-50 px-3.5 py-2 text-xs font-medium text-amber-700 dark:border-amber-500/20 dark:bg-amber-500/10 dark:text-amber-300">
            Hali chiqarilmagan
          </span>
        )}
      </div>

      {editable && (
        <form
          className="mt-4 flex flex-wrap items-end gap-3 border-t border-slate-100 pt-4 dark:border-white/5"
          onSubmit={(e) => {
            e.preventDefault()
            setError(null)
            setOk(false)
            publish.mutate()
          }}
        >
          <Field label="Yangi versiya (X.Y.Z)" hint={rel ? `Joriy: ${rel.version} — keyingisi ${nextVersion}` : undefined}>
            <Input value={version} onChange={(e) => setVersion(e.target.value)} placeholder={nextVersion} pattern="^\d+\.\d+\.\d+$" required className="w-40" />
          </Field>
          <Field label="Installer (EXE)">
            <Input type="file" accept=".exe,application/octet-stream" onChange={(e) => setFile(e.target.files?.[0] ?? null)} required className="w-72" />
          </Field>
          <Button type="button" variant="secondary" onClick={() => setVersion(nextVersion)}>
            {nextVersion} ni qo‘yish
          </Button>
          <Button type="submit" loading={publish.isPending} disabled={!file || !version}>
            <Upload className="size-4" /> Chiqarish
          </Button>
        </form>
      )}
      {error && <p className="mt-2 text-xs text-rose-600">{error}</p>}
      {ok && <p className="mt-2 text-xs text-emerald-600">Yangi versiya chiqarildi. Agentlarga “Yangilash” bildirishnomasi boradi (ruxsat yoqilgan bo‘lsa).</p>}
    </div>
  )
}

export function SettingsPage() {
  const { can } = useAuth()
  const qc = useQueryClient()
  const editable = can(Permissions.SettingsManage)
  const q = useQuery({ queryKey: ['settings'], queryFn: () => api<SystemSettings>('/api/settings') })
  const schedules = useQuery({ queryKey: ['schedules'], queryFn: () => api<WorkSchedule[]>('/api/schedules') })
  const [form, setForm] = useState<SystemSettings | null>(null)
  const [editingSchedule, setEditingSchedule] = useState<WorkSchedule | 'new' | null>(null)
  useEffect(() => {
    if (q.data) setForm(q.data)
  }, [q.data])

  const save = useMutation({
    mutationFn: (s: SystemSettings) => api<SystemSettings>('/api/settings', { method: 'PUT', body: s }),
    onSuccess: (s) => qc.setQueryData(['settings'], s),
  })

  if (q.isPending || !form) return <LoadingState />
  if (q.isError) return <ErrorState error={q.error} onRetry={() => q.refetch()} />
  const set = (patch: Partial<SystemSettings>) => setForm((f) => (f ? { ...f, ...patch } : f))
  const num = (k: keyof SystemSettings, label: string, hint: string, min: number, max: number, step = 1) => (
    <Field label={label} hint={hint}>
      <Input type="number" min={min} max={max} step={step} disabled={!editable} value={form[k] as number} onChange={(e) => set({ [k]: Number(e.target.value) } as Partial<SystemSettings>)} />
    </Field>
  )

  return (
    <>
      <PageHeader
        title="Sozlamalar"
        subtitle="Saqlash muddatlari, bildirishnomalar, monitoring xabari va unumdorlik formulasi"
        actions={editable && (
          <Button onClick={() => save.mutate(form)} loading={save.isPending}>
            <Save className="size-4" /> Saqlash
          </Button>
        )}
      />
      {save.isError && <ErrorState error={save.error} />}
      {save.isSuccess && <p className="rounded-xl bg-emerald-50 px-4 py-2 text-sm text-emerald-700 dark:bg-emerald-500/10 dark:text-emerald-300">Sozlamalar saqlandi va audit jurnaliga yozildi.</p>}
      <div className="grid gap-6 xl:grid-cols-2">
        <Card>
          <CardHeader title="Ma’lumotlarni saqlash va o‘chirish" subtitle="Muddati o‘tgan yozuvlar avtomatik o‘chiriladi" />
          <div className="grid gap-4 p-5 sm:grid-cols-2">
            {num('telemetryRetentionDays', 'Telemetriya (kun)', 'Dastur hodisalari va sessiyalar', 7, 3650)}
            {num('heartbeatRetentionDays', 'Heartbeat (kun)', 'Aloqa tarixi', 1, 365)}
            {num('auditRetentionDays', 'Audit jurnali (kun)', 'Kamida 30 kun', 30, 3650)}
            {num('offlineThresholdSeconds', 'Offlayn chegarasi (s)', 'Heartbeat kelmasa offlayn deb hisoblanadi', 60, 3600)}
            <Field label="Tashkilot vaqt zonasi" hint="Kunlik hisobotlar shu zonada">
              <Input disabled={!editable} value={form.organizationTimeZone} onChange={(e) => set({ organizationTimeZone: e.target.value })} />
            </Field>
          </div>
        </Card>
        <Card>
          <CardHeader title="Unumdorlik formulasi" subtitle="Vaznlar 0–1; mavjud metrikalar bo‘yicha normallashtiriladi" />
          <div className="grid gap-4 p-5 sm:grid-cols-2">
            {num('approvedUsageWeight', 'Tasdiqlangan dasturlar vazni', 'approved / classified', 0, 1, 0.05)}
            {num('scheduleAdherenceWeight', 'Jadvalga rioya vazni', 'active in schedule / active', 0, 1, 0.05)}
            {num('reportedActivityWeight', 'Faollik vazni', 'active / (active + idle)', 0, 1, 0.05)}
            {num('minimumCoverage', 'Minimal coverage', 'Bundan past bo‘lsa “ishonchsiz”', 0, 1, 0.05)}
          </div>
        </Card>
        <Card className="xl:col-span-2">
          <CardHeader title="Agent yangilanishi" subtitle="Yangi versiya chiqarsangiz, ishlab turgan agentlar uni o‘zi yuklab o‘rnatadi" />
          <div className="space-y-5 p-5">
            <Checkbox
              label={
                <span>
                  <span className="font-medium">Avtomatik yangilanishga ruxsat berish</span>
                  <span className="block text-xs text-slate-500">Yoqilsa, agent yangi versiyani yuklab, SHA-256 ni tekshirib, jim o‘rnatadi. Aktivatsiya saqlanadi. O‘chirilsa, yangilanish faqat qo‘lda o‘rnatiladi.</span>
                </span>
              }
              disabled={!editable}
              checked={form.agentAutoUpdate}
              onChange={(e) => set({ agentAutoUpdate: e.target.checked })}
            />
            <AgentReleaseManager editable={editable} />
          </div>
        </Card>
        <Card className="xl:col-span-2">
          <CardHeader title="Tizim himoyasi" subtitle="Kompyuter sozlamalariga (nastroyka) kirishni paroldan himoyalash — agentni o‘chirish yo‘li bloklanadi" />
          <div className="space-y-4 p-5">
            <Checkbox
              label={
                <span>
                  <span className="font-medium">Kompyuter sozlamalariga kirish uchun parol so‘ralsin</span>
                  <span className="block text-xs text-slate-500">
                    Yoqilsa, xodim Windows “Sozlamalar” (Параметры) ilovasini ochganda: “Kompyuter sozlamalariga kirish uchun parol kiriting” degan oyna chiqadi. Parolsiz ochilmaydi — shu bois “Ilovalar → O‘chirish” orqali KOSHI’ni o‘chirib bo‘lmaydi. (KOSHI o‘zi allaqachon o‘chirish ro‘yxatida ko‘rinmaydi.)
                  </span>
                </span>
              }
              disabled={!editable}
              checked={form.agentProtectionEnabled}
              onChange={(e) => set({ agentProtectionEnabled: e.target.checked, ...(e.target.checked ? {} : { agentProtectionPassword: '' }) })}
            />
            {form.agentProtectionEnabled && (
              <div className="rounded-xl border border-slate-200 p-4 dark:border-white/10">
                <Field
                  label="Himoya paroli (bo‘sh qoldirilsa — eskisi saqlanadi)"
                  hint="Qoida: 7 xonali — 3 ta harf, 2 ta raqam, 2 ta belgi. Tizim o‘zi ham yaratib berishi mumkin."
                  error={form.agentProtectionPassword && !isValidProtectionPassword(form.agentProtectionPassword) ? 'Parol 7 xonali bo‘lishi va 3 harf, 2 raqam, 2 belgidan iborat bo‘lishi kerak.' : undefined}
                >
                  <div className="flex flex-wrap items-center gap-2">
                    <Input
                      type="text"
                      maxLength={7}
                      disabled={!editable}
                      value={form.agentProtectionPassword ?? ''}
                      onChange={(e) => set({ agentProtectionPassword: e.target.value })}
                      placeholder="masalan: aB3!k9#"
                      className="w-48 font-mono tracking-widest"
                    />
                    {editable && (
                      <>
                        <Button type="button" variant="secondary" onClick={() => set({ agentProtectionPassword: generateProtectionPassword() })}>
                          Avtomatik yaratish
                        </Button>
                        {form.agentProtectionPassword && (
                          <Button type="button" variant="ghost" onClick={() => navigator.clipboard.writeText(form.agentProtectionPassword ?? '')}>
                            Nusxa olish
                          </Button>
                        )}
                      </>
                    )}
                  </div>
                </Field>
                <p className="mt-2 text-xs text-amber-600">Parolni eslab qoling yoki nusxa olib saqlang — xodimlar Windows Sozlamalarini ochish uchun aynan shu parolni so‘raydi.</p>
              </div>
            )}
          </div>
        </Card>
        <Card className="xl:col-span-2">
          <CardHeader title="Agentdagi xabarlar" subtitle="Xodim tray oynasida ko‘radi" />
          <div className="p-5">
            <Field label="Monitoring siyosati xabari">
              <Textarea rows={3} disabled={!editable} value={form.monitoringNotice} onChange={(e) => set({ monitoringNotice: e.target.value })} />
            </Field>
          </div>
        </Card>
        <Card className="xl:col-span-2">
          <CardHeader
            title="Ish jadvallari"
            subtitle="Ish vaqti, tanaffus va ish kunlari"
            actions={can(Permissions.PoliciesManage) && (
              <Button variant="secondary" onClick={() => setEditingSchedule('new')}>
                <Plus className="size-4" /> Jadval qo‘shish
              </Button>
            )}
          />
          <ul className="divide-y divide-slate-100 dark:divide-white/5">
            {(schedules.data ?? []).map((s) => (
              <li key={s.id} className="flex items-center justify-between px-5 py-3 text-sm">
                <span className="flex items-center gap-2">
                  <CalendarClock className="size-4 text-slate-400" /> {s.name} {s.isDefault && <Badge tone="blue">standart</Badge>}
                </span>
                <span className="flex items-center gap-3 text-xs text-slate-500">
                  {s.workDays.map((d) => WEEKDAYS.find((w) => w[0] === d)?.[1]).join(', ')} · {s.workStart.slice(0, 5)}–{s.workEnd.slice(0, 5)}
                  {s.breakStart && ` · tanaffus ${s.breakStart.slice(0, 5)}–${s.breakEnd?.slice(0, 5)}`} · {s.timeZoneId}
                  {can(Permissions.PoliciesManage) && (
                    <Button variant="ghost" onClick={() => setEditingSchedule(s)}>
                      Tahrirlash
                    </Button>
                  )}
                </span>
              </li>
            ))}
          </ul>
        </Card>
      </div>
      <Modal open={editingSchedule !== null} title={editingSchedule === 'new' ? 'Yangi ish jadvali' : 'Ish jadvalini tahrirlash'} onClose={() => setEditingSchedule(null)} wide>
        {editingSchedule !== null && <ScheduleForm schedule={editingSchedule === 'new' ? null : editingSchedule} onDone={() => setEditingSchedule(null)} />}
      </Modal>
    </>
  )
}
