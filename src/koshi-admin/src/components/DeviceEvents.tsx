import { useQuery } from '@tanstack/react-query'
import { AppWindow, ChevronLeft, ChevronRight, Moon, PauseCircle, Power, ShieldAlert } from 'lucide-react'
import { useEffect, useState, type ReactNode } from 'react'
import { EmptyState, ErrorState, LoadingState } from '@/components/ui'
import { toLocalInput } from '@/components/forms'
import { api } from '@/lib/api'
import type { DeviceConnection, PagedResult, TimelineItem } from '@/lib/types'

function clock(value: string): string {
  return new Date(value).toLocaleTimeString('uz-UZ', { hour: '2-digit', minute: '2-digit', second: '2-digit' })
}

function duration(seconds: number | null | undefined): string {
  if (seconds == null || !Number.isFinite(seconds) || seconds <= 0) return ''
  const h = Math.floor(seconds / 3600)
  const m = Math.floor((seconds % 3600) / 60)
  const s = Math.round(seconds % 60)
  if (h > 0) return `${h} soat ${m} daq`
  if (m > 0) return `${m} daq ${s} s`
  return `${s} s`
}

function dayBounds(date: string) {
  const from = new Date(`${date}T00:00:00`)
  const to = new Date(from.getTime() + 86_400_000)
  return { from: from.toISOString(), to: to.toISOString() }
}

function shiftDate(date: string, days: number): string {
  const d = new Date(`${date}T12:00:00`)
  d.setDate(d.getDate() + days)
  return toLocalInput(d)
}

function prettyDay(date: string): string {
  const today = toLocalInput(new Date())
  const yesterday = shiftDate(today, -1)
  if (date === today) return 'Bugun'
  if (date === yesterday) return 'Kecha'
  return new Date(`${date}T12:00:00`).toLocaleDateString('uz-UZ', { weekday: 'long', day: 'numeric', month: 'long', year: 'numeric' })
}

interface FeedItem {
  at: number
  icon: ReactNode
  title: string
  detail?: string
  timing: string
  tone: string
}

const tones: Record<string, string> = {
  app: 'text-brand-600 dark:text-brand-300',
  power: 'text-emerald-600 dark:text-emerald-300',
  off: 'text-slate-400',
  idle: 'text-amber-600 dark:text-amber-300',
  violation: 'text-rose-600 dark:text-rose-300',
}

export function DeviceEventsFeed({
  deviceId,
  initialDate,
  compact,
  fill,
}: {
  deviceId: string
  initialDate?: string
  compact?: boolean
  fill?: boolean
}) {
  const [date, setDate] = useState(initialDate ?? toLocalInput(new Date()))
  useEffect(() => {
    if (initialDate) setDate(initialDate)
  }, [initialDate])

  const { from, to } = dayBounds(date)
  const today = toLocalInput(new Date())
  const timeline = useQuery({
    queryKey: ['device-events-timeline', deviceId, date],
    queryFn: ({ signal }) =>
      api<PagedResult<TimelineItem>>('/api/monitoring/timeline', { query: { deviceId, from, to, page: 1, pageSize: 500 }, signal }),
    refetchInterval: date === today ? 20_000 : false,
  })
  const connections = useQuery({
    queryKey: ['device-events-connections', deviceId, date],
    queryFn: ({ signal }) => api<DeviceConnection[]>(`/api/devices/${deviceId}/connections`, { query: { from, to }, signal }),
    refetchInterval: date === today ? 20_000 : false,
  })

  const items: FeedItem[] = []
  for (const t of timeline.data?.items ?? []) {
    const start = new Date(t.startedAt).getTime()
    const dur = duration(t.durationSeconds)
    if (t.kind === 'Application') {
      items.push({
        at: start,
        icon: <AppWindow className="size-3.5" />,
        title: t.windowTitle ? t.windowTitle : t.processName ?? 'Dastur',
        detail: t.windowTitle && t.processName ? t.processName : undefined,
        timing: `ochildi ${clock(t.startedAt)} → yopildi ${clock(t.endedAt)}${dur ? ` · ${dur} ochiq turdi` : ''}`,
        tone: 'app',
      })
    } else if (t.kind === 'PolicyViolation') {
      items.push({
        at: start,
        icon: <ShieldAlert className="size-3.5" />,
        title: `Taqiqlangan dastur: ${t.processName ?? ''}`,
        detail: t.windowTitle ?? undefined,
        timing: `aniqlandi ${clock(t.startedAt)}`,
        tone: 'violation',
      })
    } else if (t.kind === 'Idle') {
      items.push({
        at: start,
        icon: <Moon className="size-3.5" />,
        title: 'Bo‘sh turdi (harakatsiz)',
        timing: `${clock(t.startedAt)} → ${clock(t.endedAt)}${dur ? ` · ${dur}` : ''}`,
        tone: 'idle',
      })
    } else if (t.kind === 'Paused') {
      items.push({
        at: start,
        icon: <PauseCircle className="size-3.5" />,
        title: 'Monitoring pauza qilindi',
        timing: `${clock(t.startedAt)} → ${clock(t.endedAt)}${dur ? ` · ${dur}` : ''}`,
        tone: 'idle',
      })
    }
  }

  for (const c of connections.data ?? []) {
    if (toLocalInput(new Date(c.connectedAt)) === date) {
      items.push({
        at: new Date(c.connectedAt).getTime(),
        icon: <Power className="size-3.5" />,
        title: 'Kompyuter yoqildi / onlayn bo‘ldi',
        timing: clock(c.connectedAt),
        tone: 'power',
      })
    }
    if (c.disconnectedAt && toLocalInput(new Date(c.disconnectedAt)) === date) {
      items.push({
        at: new Date(c.disconnectedAt).getTime(),
        icon: <Power className="size-3.5" />,
        title: 'Kompyuter o‘chdi / oflayn bo‘ldi',
        timing: `${clock(c.disconnectedAt)}${duration(c.durationSeconds) ? ` · ${duration(c.durationSeconds)} yoniq turdi` : ''}`,
        tone: 'off',
      })
    }
  }

  items.sort((a, b) => b.at - a.at)

  return (
    <div className={fill ? 'flex h-full min-h-0 flex-col' : undefined}>
      <div className="mb-2 flex shrink-0 flex-wrap items-center gap-2">
        <button
          type="button"
          className="rounded-lg border border-slate-200 p-1 hover:bg-slate-50 dark:border-white/10 dark:hover:bg-white/5"
          onClick={() => setDate(shiftDate(date, -1))}
          aria-label="Oldingi kun"
        >
          <ChevronLeft className="size-4" />
        </button>
        <input
          type="date"
          value={date}
          max={today}
          onChange={(e) => e.target.value && setDate(e.target.value)}
          className="rounded-lg border border-slate-200 bg-white px-2 py-1 text-xs outline-none focus:border-brand-500 dark:border-white/10 dark:bg-white/5"
          aria-label="Kun"
        />
        <button
          type="button"
          className="rounded-lg border border-slate-200 p-1 hover:bg-slate-50 disabled:opacity-40 dark:border-white/10 dark:hover:bg-white/5"
          onClick={() => setDate(shiftDate(date, 1))}
          disabled={date >= today}
          aria-label="Keyingi kun"
        >
          <ChevronRight className="size-4" />
        </button>
        <p className="text-xs font-medium capitalize text-slate-600 dark:text-slate-300">{prettyDay(date)}</p>
        {date !== today && (
          <button type="button" className="text-[11px] font-medium text-brand-600 hover:underline" onClick={() => setDate(today)}>
            Bugunga
          </button>
        )}
        {!timeline.isPending && items.length > 0 && (
          <p className="ml-auto text-[11px] tabular-nums text-slate-400">{items.length} ta hodisa</p>
        )}
      </div>
      <div
        className={`overflow-y-auto overscroll-contain pr-1 ${
          fill ? 'min-h-0 flex-1' : compact ? 'max-h-52' : 'max-h-64'
        }`}
      >
        {timeline.isPending ? (
          <LoadingState />
        ) : timeline.isError ? (
          <ErrorState error={timeline.error} onRetry={() => timeline.refetch()} />
        ) : items.length === 0 ? (
          <EmptyState title="Bu kunda hodisa yo‘q" hint="Boshqa kunni tanlang — har bir kun alohida saqlanadi." />
        ) : (
          <ul className="space-y-1 text-xs">
            {items.map((it, i) => (
              <li key={i} className="flex items-start gap-2 rounded-lg px-1 py-0.5 hover:bg-slate-50 dark:hover:bg-white/5">
                <span className={`mt-0.5 ${tones[it.tone] ?? 'text-slate-400'}`}>{it.icon}</span>
                <div className="min-w-0 flex-1">
                  <p className="truncate font-medium text-slate-700 dark:text-slate-200" title={it.title}>
                    {it.title}
                  </p>
                  {it.detail && <p className="truncate font-mono text-[10px] text-slate-400">{it.detail}</p>}
                  <p className="text-[11px] text-slate-500">{it.timing}</p>
                </div>
              </li>
            ))}
          </ul>
        )}
      </div>
    </div>
  )
}
