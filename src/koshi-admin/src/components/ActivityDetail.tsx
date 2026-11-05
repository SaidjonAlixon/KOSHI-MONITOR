import { useQuery } from '@tanstack/react-query'
import { AppWindow, Clock, Globe, Power, Search } from 'lucide-react'
import { useState, type ReactNode } from 'react'
import { DeviceEventsFeed } from '@/components/DeviceEvents'
import { toLocalInput } from '@/components/forms'
import { Badge, Card, EmptyState, ErrorState, LoadingState, formatDateTime, formatDuration } from '@/components/ui'
import { api } from '@/lib/api'
import type { ActivityDetail, BrowsingItem, PowerSession, WindowUsage } from '@/lib/types'

/** Query sent to /api/monitoring/activity-detail. A single computer must be resolvable (deviceId or an employee with one device). */
export interface ActivityQuery {
  from?: string
  to?: string
  deviceId?: string
  employeeId?: string
  departmentId?: string
  branch?: string
  [key: string]: string | undefined
}

const thC = 'sticky top-0 z-10 bg-slate-50 px-3 py-1.5 text-left text-[10px] font-semibold uppercase tracking-wide text-slate-500 dark:bg-ink-900'
const tdC = 'px-3 py-1.5 text-xs'
const tableC = 'w-full text-xs'
const tbodyC = 'divide-y divide-slate-100 dark:divide-white/5'

const kindTone: Record<string, 'blue' | 'amber' | 'green' | 'gray' | 'red'> = {
  qidiruv: 'amber',
  video: 'red',
  sahifa: 'blue',
}

function clock(value: string | null | undefined): string {
  if (!value) return '—'
  return new Date(value).toLocaleTimeString('uz-UZ', { hour: '2-digit', minute: '2-digit', second: '2-digit' })
}

function browsingLink(b: BrowsingItem): string | null {
  if (b.url) return b.url
  const q = b.query?.trim()
  if (b.kind === 'qidiruv' && q) {
    const enc = encodeURIComponent(q)
    if (b.site === 'Google') return `https://www.google.com/search?q=${enc}`
    if (b.site === 'Yandex') return `https://yandex.ru/search/?text=${enc}`
    if (b.site === 'Bing') return `https://www.bing.com/search?q=${enc}`
  }
  if (b.kind === 'video' && b.site === 'YouTube') {
    return q ? `https://www.youtube.com/results?search_query=${encodeURIComponent(q)}` : 'https://www.youtube.com'
  }
  const fromTitle = b.title.match(/https?:\/\/[^\s<>"']+/i)
  if (fromTitle) return fromTitle[0]
  if (b.site && !['Google', 'YouTube', 'Bing', 'Yandex'].includes(b.site)) {
    const host = b.site.includes('.') ? b.site : `${b.site.toLowerCase()}.com`
    return `https://${host.replace(/^https?:\/\//, '')}`
  }
  return null
}

function prettyUrl(url: string): string {
  try {
    const u = new URL(url)
    const host = u.hostname.replace(/^www\./, '')
    const path = `${u.pathname}${u.search}`.replace(/\/$/, '')
    const s = path && path !== '/' ? `${host}${path}` : host
    return s.length > 64 ? `${s.slice(0, 61)}…` : s
  } catch {
    return url
  }
}

function Panel({
  title,
  subtitle,
  count,
  actions,
  children,
  height = 'h-[300px]',
}: {
  title: string
  subtitle?: string
  count?: number
  actions?: ReactNode
  children: ReactNode
  height?: string
}) {
  return (
    <section className={`flex min-h-0 flex-col overflow-hidden rounded-2xl border border-slate-200/80 bg-white shadow-sm dark:border-white/10 dark:bg-ink-900 ${height}`}>
      <header className="flex shrink-0 items-start justify-between gap-3 border-b border-slate-100 px-4 py-2.5 dark:border-white/5">
        <div className="min-w-0">
          <h2 className="truncate text-sm font-semibold">{title}</h2>
          {subtitle && <p className="mt-0.5 truncate text-[11px] text-slate-500 dark:text-slate-400">{subtitle}</p>}
        </div>
        <div className="flex shrink-0 items-center gap-2">
          {actions}
          {count != null && (
            <span className="rounded-full bg-slate-100 px-2 py-0.5 text-[11px] font-medium tabular-nums text-slate-600 dark:bg-white/10 dark:text-slate-300">
              {count}
            </span>
          )}
        </div>
      </header>
      <div className="min-h-0 flex-1 overflow-auto">{children}</div>
    </section>
  )
}

function Metric({ icon, label, value, hint }: { icon: ReactNode; label: string; value: string; hint?: string }) {
  return (
    <div className="flex min-w-0 items-center gap-2.5 rounded-xl bg-slate-50 px-3 py-2 dark:bg-white/[0.04]">
      <div className="flex size-8 shrink-0 items-center justify-center rounded-lg bg-white text-brand-600 shadow-sm dark:bg-ink-900 dark:text-brand-300">
        {icon}
      </div>
      <div className="min-w-0">
        <p className="truncate text-[11px] text-slate-500">{label}</p>
        <p className="truncate text-sm font-semibold tabular-nums">{value}</p>
        {hint && <p className="truncate text-[10px] text-slate-400">{hint}</p>}
      </div>
    </div>
  )
}

function MiniEmpty({ title }: { title: string }) {
  return <p className="px-4 py-8 text-center text-xs text-slate-400">{title}</p>
}

export function ActivityDetailView({ query }: { query: ActivityQuery }) {
  const ready = !!(query.deviceId || query.employeeId)
  const q = useQuery({
    queryKey: ['activity-detail', query],
    queryFn: ({ signal }) => api<ActivityDetail>('/api/monitoring/activity-detail', { query, signal }),
    enabled: ready,
  })
  const [windowTab, setWindowTab] = useState<'fg' | 'bg'>('fg')

  if (!ready) {
    return (
      <Card>
        <EmptyState
          title="Kompyuterni tanlang"
          hint="To‘liq faoliyat bitta kompyuter uchun ko‘rsatiladi. Yuqoridagi “Qurilma” yoki “Xodim” filtridan birini tanlang."
        />
      </Card>
    )
  }
  if (q.isPending) return <LoadingState />
  if (q.isError) return <ErrorState error={q.error} onRetry={() => q.refetch()} />

  const a = q.data
  const t = a.totals
  const windows = windowTab === 'fg' ? a.foregroundWindows : a.backgroundWindows

  return (
    <div className="space-y-4">
      <Card>
        <div className="flex flex-wrap items-start justify-between gap-3 border-b border-slate-100 px-4 py-3 dark:border-white/5">
          <div className="min-w-0">
            <h2 className="truncate text-sm font-semibold">
              {a.deviceName}
              {a.employeeName ? <span className="font-medium text-slate-500"> · {a.employeeName}</span> : null}
            </h2>
            <p className="mt-0.5 truncate text-[11px] text-slate-500">
              {a.hostname ?? '—'}
              {a.departmentName ? ` · ${a.departmentName}` : ''} · {formatDateTime(a.from)} — {formatDateTime(a.to)}
            </p>
          </div>
          <p className="text-[11px] text-slate-500">
            Birinchi: <span className="font-medium text-slate-700 dark:text-slate-200">{formatDateTime(t.firstActivityAt)}</span>
            <span className="mx-2 text-slate-300">·</span>
            Oxirgi: <span className="font-medium text-slate-700 dark:text-slate-200">{formatDateTime(t.lastActivityAt)}</span>
          </p>
        </div>
        <div className="grid grid-cols-2 gap-2 p-3 lg:grid-cols-4">
          <Metric icon={<Power className="size-4" />} label="Yoniq vaqt" value={formatDuration(t.onlineSeconds)} hint={`${t.powerSessionCount} marta yoqilgan`} />
          <Metric icon={<Clock className="size-4" />} label="Faol ish" value={formatDuration(t.activeSeconds)} hint={`Bo‘sh: ${formatDuration(t.idleSeconds)}`} />
          <Metric icon={<AppWindow className="size-4" />} label="Dasturlar" value={String(t.appCount)} hint={`${a.foregroundWindows.length} ta oyna`} />
          <Metric icon={<Globe className="size-4" />} label="Brauzer" value={String(t.browsingCount)} hint={a.browsing[0] ? a.browsing[0].title : 'Sahifa / qidiruv'} />
        </div>
      </Card>

      <div className="grid gap-4 xl:grid-cols-2">
        <Panel title="Kunlik hodisalar jurnali" subtitle="Dastur ochilishi/yopilishi, yoqilish/o‘chish — ichida scroll" height="h-[560px]">
          <div className="h-full p-3">
            <DeviceEventsFeed deviceId={a.deviceId} initialDate={toLocalInput(new Date(a.from))} fill />
          </div>
        </Panel>
        <Panel title="Yoqilgan / o‘chgan" subtitle="Har bir ishlash seansi" count={a.powerSessions.length} height="h-[560px]">
          <PowerTable rows={a.powerSessions} />
        </Panel>
      </div>

      <div className="grid gap-4 xl:grid-cols-2">
        <Panel title="Ishlatilgan dasturlar" subtitle="Ekranda va fonda" count={a.apps.length} height="h-[520px]">
          {a.apps.length === 0 ? (
            <MiniEmpty title="Dastur ma’lumoti yo‘q" />
          ) : (
            <table className={tableC}>
              <thead>
                <tr>
                  <th className={thC}>Dastur</th>
                  <th className={thC}>Ekranda</th>
                  <th className={thC}>Fonda</th>
                  <th className={thC}>Ulush</th>
                </tr>
              </thead>
              <tbody className={tbodyC}>
                {a.apps.map((app) => (
                  <tr key={app.processName} className="hover:bg-slate-50/80 dark:hover:bg-white/[0.03]">
                    <td className={`${tdC} font-mono text-slate-700 dark:text-slate-200`}>{app.processName}</td>
                    <td className={`${tdC} tabular-nums`}>{formatDuration(app.foregroundSeconds)}</td>
                    <td className={`${tdC} tabular-nums text-slate-500`}>{formatDuration(app.backgroundSeconds)}</td>
                    <td className={`${tdC} tabular-nums`}>{Math.round(app.share * 100)}%</td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </Panel>
        <Panel title="Brauzer: qidiruv va havolalar" subtitle="Kirilgan sahifa / qidiruv havolasi" count={a.browsing.length} height="h-[520px]">
          <BrowsingTable rows={a.browsing} />
        </Panel>
      </div>

      <Panel
        title="Oynalar"
        subtitle="Ekranda yoki fonda ochiq turgan sarlavhalar"
        count={windows.length}
        height="h-[520px]"
        actions={
          <div className="inline-flex rounded-lg border border-slate-200 p-0.5 text-[11px] dark:border-white/10">
            <button
              type="button"
              className={`rounded-md px-2 py-0.5 font-medium ${windowTab === 'fg' ? 'bg-brand-50 text-brand-700 dark:bg-brand-500/15 dark:text-brand-300' : 'text-slate-500 hover:text-slate-700'}`}
              onClick={() => setWindowTab('fg')}
            >
              Ekran
            </button>
            <button
              type="button"
              className={`rounded-md px-2 py-0.5 font-medium ${windowTab === 'bg' ? 'bg-brand-50 text-brand-700 dark:bg-brand-500/15 dark:text-brand-300' : 'text-slate-500 hover:text-slate-700'}`}
              onClick={() => setWindowTab('bg')}
            >
              Fon
            </button>
          </div>
        }
      >
        <WindowsTable rows={windows} background={windowTab === 'bg'} />
      </Panel>
    </div>
  )
}

function PowerTable({ rows }: { rows: PowerSession[] }) {
  if (rows.length === 0) return <MiniEmpty title="Bu davrda yoqilish yo‘q" />
  return (
    <table className={tableC}>
      <thead>
        <tr>
          <th className={thC}>Yoqilgan</th>
          <th className={thC}>O‘chgan</th>
          <th className={thC}>Davomiyligi</th>
        </tr>
      </thead>
      <tbody className={tbodyC}>
        {rows.map((p, i) => (
          <tr key={i} className="hover:bg-slate-50/80 dark:hover:bg-white/[0.03]">
            <td className={`${tdC} tabular-nums`}>{clock(p.onAt)}</td>
            <td className={tdC}>{p.ongoing ? <Badge tone="green">Hozir yoniq</Badge> : <span className="tabular-nums">{clock(p.offAt)}</span>}</td>
            <td className={`${tdC} tabular-nums`}>{formatDuration(p.durationSeconds)}</td>
          </tr>
        ))}
      </tbody>
    </table>
  )
}

function BrowsingTable({ rows }: { rows: BrowsingItem[] }) {
  if (rows.length === 0) {
    return <MiniEmpty title="Brauzer faoliyati topilmadi" />
  }
  return (
    <table className={tableC}>
      <thead>
        <tr>
          <th className={thC}>Turi</th>
          <th className={thC}>Nima qilgan</th>
          <th className={thC}>Havola</th>
          <th className={thC}>Vaqt</th>
        </tr>
      </thead>
      <tbody className={tbodyC}>
        {rows.map((b, i) => {
          const href = browsingLink(b)
          return (
            <tr key={i} className="hover:bg-slate-50/80 dark:hover:bg-white/[0.03]">
              <td className={tdC}>
                <Badge tone={kindTone[b.kind] ?? 'gray'}>
                  {b.kind === 'qidiruv' ? <Search className="size-3" /> : <Globe className="size-3" />}
                  {b.kind}
                </Badge>
              </td>
              <td className={`${tdC} max-w-[12rem]`}>
                <p className="truncate font-medium" title={b.query ? `“${b.query}”` : b.title}>
                  {b.query ? `“${b.query}”` : b.title}
                </p>
                <p className="truncate text-[10px] text-slate-400">
                  {b.site ?? '—'} · {b.browser}
                </p>
              </td>
              <td className={`${tdC} max-w-[16rem]`}>
                {href ? (
                  <a
                    href={href}
                    target="_blank"
                    rel="noreferrer"
                    className="block truncate font-mono text-[11px] text-brand-600 hover:underline dark:text-brand-300"
                    title={href}
                  >
                    {prettyUrl(href)}
                  </a>
                ) : (
                  <span className="text-slate-300">—</span>
                )}
              </td>
              <td className={`${tdC} whitespace-nowrap tabular-nums text-slate-500`}>{formatDuration(b.seconds)}</td>
            </tr>
          )
        })}
      </tbody>
    </table>
  )
}

function WindowsTable({ rows, background }: { rows: WindowUsage[]; background?: boolean }) {
  if (rows.length === 0) return <MiniEmpty title={background ? 'Fon oynasi yo‘q' : 'Oyna ma’lumoti yo‘q'} />
  return (
    <table className={tableC}>
      <thead>
        <tr>
          <th className={thC}>Oyna sarlavhasi</th>
          <th className={thC}>Dastur</th>
          <th className={thC}>Vaqt</th>
          <th className={thC}>Marta</th>
        </tr>
      </thead>
      <tbody className={tbodyC}>
        {rows.map((w, i) => (
          <tr key={i} className="hover:bg-slate-50/80 dark:hover:bg-white/[0.03]">
            <td className={`${tdC} max-w-md`}>
              <p className="truncate" title={w.windowTitle ?? ''}>
                {w.windowTitle ?? '—'}
              </p>
            </td>
            <td className={`${tdC} font-mono text-slate-500`}>{w.processName}</td>
            <td className={`${tdC} tabular-nums`}>{formatDuration(w.seconds)}</td>
            <td className={`${tdC} tabular-nums`}>{w.count}</td>
          </tr>
        ))}
      </tbody>
    </table>
  )
}
