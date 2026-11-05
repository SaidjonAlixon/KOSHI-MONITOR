import { HubConnectionBuilder, HubConnectionState, LogLevel, type HubConnection } from '@microsoft/signalr'
import { useQuery } from '@tanstack/react-query'
import { Activity, AppWindow, ArrowLeft, Clock, Eye, Lock, LogOut, Maximize, MessageSquare, MousePointerClick, PackageX, Power, RotateCcw, Square, XCircle, XOctagon } from 'lucide-react'
import { useCallback, useEffect, useRef, useState, type KeyboardEvent, type MouseEvent, type ReactNode, type WheelEvent } from 'react'
import { Link, useParams } from 'react-router'
import { DeviceEventsFeed } from '@/components/DeviceEvents'
import { PageHeader, Select } from '@/components/forms'
import { Badge, Button, Card, formatDateTime, formatDuration } from '@/components/ui'
import { api, getAccessToken } from '@/lib/api'
import { Permissions, useAuth } from '@/lib/auth'
import type { ActivityDetail, Device, LiveDevice } from '@/lib/types'

type Status = 'connecting' | 'ready' | 'starting' | 'live' | 'ended' | 'error'

const QUALITY = {
  low: { quality: 35, maxWidth: 1024, fps: 8 },
  medium: { quality: 55, maxWidth: 1366, fps: 10 },
  high: { quality: 75, maxWidth: 1920, fps: 12 },
} as const

function decodeBase64(data: string | Uint8Array): Uint8Array {
  if (typeof data !== 'string') return data
  const bin = atob(data)
  const bytes = new Uint8Array(bin.length)
  for (let i = 0; i < bin.length; i++) bytes[i] = bin.charCodeAt(i)
  return bytes
}

function ToolGroup({ title, hint, children }: { title: string; hint?: string; children: ReactNode }) {
  return (
    <div className="rounded-xl border border-slate-200 p-3 dark:border-white/10">
      <p className="mb-0.5 text-[11px] font-semibold uppercase tracking-wide text-slate-500">{title}</p>
      {hint && <p className="mb-2 text-[11px] text-slate-400">{hint}</p>}
      <div className={`flex flex-wrap gap-2 ${hint ? '' : 'mt-2'}`}>{children}</div>
    </div>
  )
}

function todayRange() {
  const start = new Date()
  start.setHours(0, 0, 0, 0)
  return { from: start.toISOString(), to: new Date(start.getTime() + 86_400_000).toISOString() }
}

function Stat({ icon, label, value, hint }: { icon: ReactNode; label: string; value: ReactNode; hint?: ReactNode }) {
  return (
    <div className="flex min-w-0 items-start gap-2.5 rounded-xl bg-slate-50 px-3 py-2.5 dark:bg-white/[0.04]">
      <span className="mt-0.5 flex size-8 shrink-0 items-center justify-center rounded-lg bg-white text-brand-600 shadow-sm dark:bg-ink-900 dark:text-brand-300">{icon}</span>
      <div className="min-w-0">
        <p className="text-[11px] text-slate-500">{label}</p>
        <div className="truncate text-sm font-semibold tabular-nums">{value}</div>
        {hint && <div className="truncate text-[11px] text-slate-400">{hint}</div>}
      </div>
    </div>
  )
}

/** Live state of the computer and today's summary — shown under the screen, never drawn onto it. */
function DeviceInsights({ deviceId, device }: { deviceId: string; device: Device | undefined }) {
  const { can } = useAuth()
  const live = useQuery({
    queryKey: ['remote-live', deviceId],
    queryFn: ({ signal }) => api<LiveDevice[]>('/api/monitoring/live', { query: { deviceId }, signal }),
    enabled: can(Permissions.MonitoringLive),
    refetchInterval: 5000,
  })
  const range = todayRange()
  const today = useQuery({
    queryKey: ['remote-today', deviceId, range.from],
    queryFn: ({ signal }) => api<ActivityDetail>('/api/monitoring/activity-detail', { query: { deviceId, ...range }, signal }),
    enabled: can(Permissions.MonitoringRead),
    refetchInterval: 60_000,
  })

  const l = live.data?.[0]
  const t = today.data?.totals
  const online = l ? l.agentStatus === 'Online' || l.agentStatus === 'Paused' : !!device?.isOnline
  const idle = l?.currentState === 'Idle'
  const topApps = (today.data?.apps ?? []).slice(0, 5)
  const maxSeconds = Math.max(1, ...topApps.map((a) => a.foregroundSeconds))

  return (
    <Card className="p-4">
      <div className="mb-3 flex flex-wrap items-center justify-between gap-2">
        <div>
          <p className="text-sm font-semibold">Kompyuter holati</p>
          <p className="text-[11px] text-slate-400">Hozirgi faoliyat va bugungi qisqa hisobot — har 5 soniyada yangilanadi</p>
        </div>
        <Badge tone={online ? 'green' : 'gray'}>{online ? 'Onlayn' : 'Oflayn'}</Badge>
      </div>

      <div className="grid gap-2 sm:grid-cols-2 xl:grid-cols-4">
        <Stat
          icon={<AppWindow className="size-4" />}
          label="Hozir ochiq dastur"
          value={l?.currentProcessName ?? '—'}
          hint={<span title={l?.currentWindowTitle ?? undefined}>{l?.currentWindowTitle ?? 'Oyna nomi yo‘q'}</span>}
        />
        <Stat
          icon={<Activity className="size-4" />}
          label="Xodim holati"
          value={!online ? 'Kompyuter oflayn' : idle ? 'Bo‘sh turibdi' : l?.currentState ? 'Ishlayapti' : '—'}
          hint={l?.activityDurationSeconds != null ? `${formatDuration(l.activityDurationSeconds)} davomida` : undefined}
        />
        <Stat
          icon={<Power className="size-4" />}
          label="Bugun yoniq"
          value={formatDuration(t?.onlineSeconds)}
          hint={l?.connectedAt ? `Oxirgi yoqilish: ${formatDateTime(l.connectedAt)}` : t ? `${t.powerSessionCount} marta yoqilgan` : undefined}
        />
        <Stat
          icon={<Clock className="size-4" />}
          label="Bugun faol ish"
          value={formatDuration(t?.activeSeconds)}
          hint={t ? `Bo‘sh: ${formatDuration(t.idleSeconds)} · ${t.appCount} ta dastur` : undefined}
        />
      </div>

      <div className="mt-4 grid gap-4 lg:grid-cols-[1fr_280px]">
        <div>
          <p className="mb-2 text-[11px] font-semibold uppercase tracking-wide text-slate-500">Bugun eng ko‘p ishlatilgan</p>
          {topApps.length === 0 ? (
            <p className="py-4 text-xs text-slate-400">{today.isPending ? 'Yuklanmoqda…' : 'Bugun hali dastur ma’lumoti yo‘q'}</p>
          ) : (
            <ul className="space-y-2">
              {topApps.map((a) => (
                <li key={a.processName} className="text-xs">
                  <div className="mb-1 flex items-center justify-between gap-2">
                    <span className="truncate font-mono text-slate-700 dark:text-slate-200">{a.processName}</span>
                    <span className="shrink-0 tabular-nums text-slate-500">
                      {formatDuration(a.foregroundSeconds)} · {Math.round(a.share * 100)}%
                    </span>
                  </div>
                  <div className="h-1.5 overflow-hidden rounded-full bg-slate-100 dark:bg-white/10">
                    <div className="h-full rounded-full bg-brand-500" style={{ width: `${Math.max(4, (a.foregroundSeconds / maxSeconds) * 100)}%` }} />
                  </div>
                </li>
              ))}
            </ul>
          )}
        </div>
        <div>
          <p className="mb-2 text-[11px] font-semibold uppercase tracking-wide text-slate-500">Qurilma</p>
          <dl className="space-y-1.5 text-xs">
            {[
              ['Kompyuter nomi', device?.hostname ?? '—'],
              ['Xodim', device?.employeeName ?? 'biriktirilmagan'],
              ['Bo‘lim', device?.departmentName ?? '—'],
              ['Windows', device?.osVersion ?? '—'],
              ['Agent', device?.agentVersion ? `v${device.agentVersion}` : '—'],
              ['Oxirgi aloqa', formatDateTime(l?.lastHeartbeatAt ?? device?.lastHeartbeatAt)],
            ].map(([k, v]) => (
              <div key={k} className="flex justify-between gap-3">
                <dt className="text-slate-500">{k}</dt>
                <dd className="truncate text-right font-medium text-slate-700 dark:text-slate-200" title={v}>{v}</dd>
              </div>
            ))}
          </dl>
        </div>
      </div>
    </Card>
  )
}

export function RemotePage() {
  const { can } = useAuth()
  const { deviceId = '' } = useParams()
  const device = useQuery({ queryKey: ['device', deviceId], queryFn: () => api<Device>(`/api/devices/${deviceId}`) })
  const canvasRef = useRef<HTMLCanvasElement>(null)
  const connRef = useRef<HubConnection | null>(null)
  const sessionRef = useRef<string | null>(null)
  const drawingRef = useRef(false)
  const lastMoveRef = useRef(0)
  const [status, setStatus] = useState<Status>('connecting')
  const [message, setMessage] = useState<string>('')
  const [quality, setQuality] = useState<keyof typeof QUALITY>('medium')
  const [fps, setFps] = useState(0)
  const [log, setLog] = useState<string[]>([])
  const [controlEnabled, setControlEnabled] = useState(false)
  const [frameSize, setFrameSize] = useState<{ w: number; h: number } | null>(null)
  const frameCount = useRef(0)

  const addLog = useCallback((s: string) => setLog((l) => [`${new Date().toLocaleTimeString('uz-UZ')} — ${s}`, ...l].slice(0, 8)), [])

  useEffect(() => {
    const conn = new HubConnectionBuilder()
      .withUrl('/hubs/remote', { accessTokenFactory: () => getAccessToken() ?? '' })
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build()
    connRef.current = conn

    conn.on('frame', (id: string, data: string, w: number, h: number) => {
      if (id !== sessionRef.current || drawingRef.current) return
      drawingRef.current = true
      frameCount.current += 1
      createImageBitmap(new Blob([decodeBase64(data) as BlobPart], { type: 'image/jpeg' }))
        .then((bmp) => {
          const c = canvasRef.current
          if (c) {
            if (c.width !== w || c.height !== h) {
              c.width = w
              c.height = h
              setFrameSize({ w, h })
            }
            c.getContext('2d')?.drawImage(bmp, 0, 0)
          }
          bmp.close()
        })
        .catch(() => undefined)
        .finally(() => {
          drawingRef.current = false
        })
      setStatus('live')
    })
    conn.on('sessionEnded', (id: string, reason: string) => {
      if (id !== sessionRef.current) return
      sessionRef.current = null
      setStatus('ended')
      setMessage(reason)
      addLog(`Sessiya tugadi: ${reason}`)
    })
    conn.on('commandResult', (_id: string, ok: boolean, msg: string) => {
      addLog(`${ok ? '✔' : '✖'} ${msg}`)
    })
    conn.onreconnecting(() => setStatus('connecting'))
    conn.onreconnected(() => setStatus(sessionRef.current ? 'live' : 'ready'))

    conn
      .start()
      .then(() => setStatus('ready'))
      .catch((e: Error) => {
        setStatus('error')
        setMessage(e.message.includes('401') || e.message.includes('403') ? 'remote.control ruxsati yo‘q' : 'Real-time serverga ulanib bo‘lmadi')
      })

    const timer = window.setInterval(() => {
      setFps(frameCount.current)
      frameCount.current = 0
    }, 1000)

    return () => {
      window.clearInterval(timer)
      if (sessionRef.current && conn.state === HubConnectionState.Connected) void conn.invoke('Stop', sessionRef.current)
      void conn.stop()
    }
  }, [addLog])

  const start = async (control: boolean) => {
    const conn = connRef.current
    if (!conn) return
    setControlEnabled(control)
    setStatus('starting')
    setMessage('')
    try {
      const q = QUALITY[quality]
      sessionRef.current = await conn.invoke<string>('Start', deviceId, q.quality, q.maxWidth, q.fps)
      addLog(control ? 'Boshqaruv sessiyasi boshlandi' : 'Jonli kuzatuv boshlandi — xodim ekranida hech narsa ko‘rinmaydi')
      if (control) canvasRef.current?.focus()
    } catch (e) {
      setStatus('error')
      setMessage((e as Error).message.replace(/^.*HubException: /, ''))
    }
  }

  const stop = async () => {
    if (sessionRef.current) await connRef.current?.invoke('Stop', sessionRef.current)
    sessionRef.current = null
    setStatus('ready')
    addLog('Sessiya to‘xtatildi')
  }

  const command = async (cmd: string, argument: string | null = null) => {
    try {
      await connRef.current?.invoke('Command', deviceId, cmd, argument)
      addLog(`Buyruq yuborildi: ${cmd}`)
    } catch (e) {
      addLog(`✖ ${(e as Error).message.replace(/^.*HubException: /, '')}`)
    }
  }

  const send = (type: string, e: MouseEvent<HTMLCanvasElement> | WheelEvent<HTMLCanvasElement> | null, extra: Partial<{ button: number; delta: number; keyCode: number }> = {}) => {
    const sid = sessionRef.current
    const c = canvasRef.current
    if (!controlEnabled || !sid || !c || connRef.current?.state !== HubConnectionState.Connected) return
    let x = 0
    let y = 0
    if (e) {
      const r = c.getBoundingClientRect()
      x = Math.min(1, Math.max(0, (e.clientX - r.left) / r.width))
      y = Math.min(1, Math.max(0, (e.clientY - r.top) / r.height))
    }
    void connRef.current.send('Input', sid, { type, x, y, button: extra.button ?? 0, delta: extra.delta ?? 0, keyCode: extra.keyCode ?? 0 })
  }

  const sendRef = useRef(send)
  sendRef.current = send
  const wheelRef = useRef({ dx: 0, dy: 0, timer: 0 as number | 0, last: null as WheelEvent | null })

  // React's onWheel is passive (can't stop the page from scrolling), so attach a native listener.
  useEffect(() => {
    const c = canvasRef.current
    if (!c) return
    const onWheel = (ev: globalThis.WheelEvent) => {
      if (!controlEnabled || !sessionRef.current) return
      ev.preventDefault()
      const unit = ev.deltaMode === 1 ? 40 : ev.deltaMode === 2 ? 800 : 1
      const w = wheelRef.current
      w.dx += (ev.shiftKey && !ev.deltaX ? ev.deltaY : ev.deltaX) * unit
      w.dy += (ev.shiftKey && !ev.deltaX ? 0 : ev.deltaY) * unit
      w.last = ev as unknown as WheelEvent
      if (w.timer) return
      w.timer = window.setTimeout(() => {
        w.timer = 0
        const e = w.last as unknown as WheelEvent<HTMLCanvasElement>
        if (Math.abs(w.dy) >= 1) sendRef.current('wheel', e, { delta: Math.round(w.dy) })
        if (Math.abs(w.dx) >= 1) sendRef.current('hwheel', e, { delta: Math.round(w.dx) })
        w.dx = 0
        w.dy = 0
      }, 30)
    }
    c.addEventListener('wheel', onWheel, { passive: false })
    return () => c.removeEventListener('wheel', onWheel)
  }, [controlEnabled])

  const onMove = (e: MouseEvent<HTMLCanvasElement>) => {
    const now = performance.now()
    if (now - lastMoveRef.current < 40) return
    lastMoveRef.current = now
    send('move', e)
  }
  const onKey = (type: 'keydown' | 'keyup') => (e: KeyboardEvent<HTMLCanvasElement>) => {
    if (!sessionRef.current) return
    e.preventDefault()
    send(type, null, { keyCode: e.keyCode })
  }

  const live = status === 'live' || status === 'starting'
  const tone = status === 'live' ? 'green' : status === 'error' ? 'red' : status === 'ended' ? 'gray' : 'amber'
  const statusLabel = { connecting: 'Ulanmoqda…', ready: 'Tayyor', starting: 'Boshlanmoqda…', live: `Jonli · ${fps} kadr/s`, ended: 'Tugagan', error: 'Xato' }[status]

  return (
    <>
      <PageHeader
        title={`Masofaviy boshqaruv — ${device.data?.displayName ?? '…'}`}
        subtitle="Jonli ko‘rish xodim ekranida ko‘rinmaydi va u ✕ bilan yopa olmaydi. Sessiyani faqat administrator to‘xtatadi. Har bir sessiya va buyruq audit jurnaliga yoziladi."
        actions={
          <Link to="/app/devices" className="inline-flex items-center gap-2 rounded-xl px-3 py-2 text-sm text-slate-600 hover:bg-slate-100 dark:text-slate-300 dark:hover:bg-white/10">
            <ArrowLeft className="size-4" /> Qurilmalar
          </Link>
        }
      />

      <Card className="p-4">
        {/* Sessiya: kuzatuv / boshqaruv */}
        <div className="flex flex-wrap items-center gap-3">
          <Badge tone={tone}>{statusLabel}</Badge>
          {live && <Badge tone={controlEnabled ? 'blue' : 'green'}>{controlEnabled ? 'Boshqaruv rejimi' : 'Kuzatuv rejimi'}</Badge>}
          {device.data && <span className="text-xs text-slate-500">{device.data.employeeName ?? 'biriktirilmagan'} · {device.data.hostname}</span>}
          <div className="ml-auto flex flex-wrap items-center gap-2">
            <label className="flex items-center gap-1.5 text-xs text-slate-500" title="Jonli ko‘rish tasvirining sifati va tezligi. Past — tezroq, kam internet; Yuqori — aniqroq, ko‘proq internet.">
              Tasvir sifati
              <Select value={quality} onChange={(e) => setQuality(e.target.value as keyof typeof QUALITY)} disabled={live} className="w-32" aria-label="Jonli ko‘rish tasviri sifati">
                <option value="low">Past (tez)</option>
                <option value="medium">O‘rta</option>
                <option value="high">Yuqori (aniq)</option>
              </Select>
            </label>
            {live ? (
              <>
                <Button variant={controlEnabled ? 'primary' : 'secondary'} onClick={() => setControlEnabled((v) => !v)} title="Sichqoncha va klaviatura boshqaruvini yoqish/o‘chirish">
                  {controlEnabled ? <MousePointerClick className="size-4" /> : <Eye className="size-4" />}
                  {controlEnabled ? 'Boshqaruv yoqilgan' : 'Faqat kuzatuv'}
                </Button>
                <Button variant="danger" onClick={stop}>
                  <Square className="size-4" /> To‘xtatish
                </Button>
              </>
            ) : (
              <>
                <Button onClick={() => start(false)} disabled={status === 'connecting' || status === 'error'}>
                  <Eye className="size-4" /> Jonli ko‘rish
                </Button>
                <Button variant="secondary" onClick={() => start(true)} disabled={status === 'connecting' || status === 'error'}>
                  <MousePointerClick className="size-4" /> Boshqaruvni boshlash
                </Button>
              </>
            )}
            <Button variant="secondary" title="To‘liq ekran" onClick={() => canvasRef.current?.requestFullscreen()}>
              <Maximize className="size-4" />
            </Button>
          </div>
        </div>

        {/* Rejim banneri — boshqaruv va kuzatuv aniq farqlanadi */}
        {live && (
          <div
            className={`mt-3 rounded-xl px-4 py-2 text-xs ring-1 ${
              controlEnabled
                ? 'bg-brand-50 text-brand-700 ring-brand-500/20 dark:bg-brand-500/10 dark:text-brand-200'
                : 'bg-emerald-50 text-emerald-700 ring-emerald-500/20 dark:bg-emerald-500/10 dark:text-emerald-200'
            }`}
          >
            {controlEnabled
              ? 'Boshqaruv rejimi yoniq: sichqoncha va klaviatura shu kompyuterni boshqaradi. Klaviatura uchun ekran ustiga bosing.'
              : 'Kuzatuv rejimi: ekran faqat ko‘rinmoqda, hech narsa boshqarilmaydi. Xodim ekranida hech nima ko‘rinmaydi.'}
          </div>
        )}

        {/* Guruhlangan amallar */}
        <div className="mt-4 grid gap-3 lg:grid-cols-3">
          <ToolGroup title="Xodim bilan ishlash" hint="Ekranni bloklash, xabar yuborish, dasturni yopish">
            <Button variant="secondary" onClick={() => command('lock')} disabled={status === 'error'}>
              <Lock className="size-4" /> Bloklash
            </Button>
            <Button variant="secondary" onClick={() => { const m = prompt('Xodimga xabar:'); if (m) void command('message', m.slice(0, 500)) }}>
              <MessageSquare className="size-4" /> Xabar
            </Button>
            <Button variant="secondary" onClick={() => { const p = prompt('Yopiladigan jarayon (masalan, chrome.exe):'); if (p) void command('kill-process', p) }}>
              <XOctagon className="size-4" /> Jarayonni yopish
            </Button>
          </ToolGroup>

          <ToolGroup title="Quvvat boshqaruvi" hint="Sessiyadan chiqish, qayta ishga tushirish yoki o‘chirish">
            <Button variant="secondary" onClick={() => confirm('Foydalanuvchi sessiyadan chiqarilsinmi?') && command('logoff')}>
              <LogOut className="size-4" /> Chiqish
            </Button>
            <Button variant="secondary" onClick={() => confirm('Kompyuter 60 soniyadan keyin qayta ishga tushsinmi?') && command('restart')}>
              <RotateCcw className="size-4" /> Qayta ishga tushirish
            </Button>
            <Button variant="danger" onClick={() => confirm('Kompyuter 60 soniyadan keyin o‘chirilsinmi?') && command('shutdown')}>
              <Power className="size-4" /> O‘chirish
            </Button>
            <Button variant="ghost" onClick={() => command('cancel-shutdown')}>
              <XCircle className="size-4" /> Bekor qilish
            </Button>
          </ToolGroup>

          {can(Permissions.DevicesManage) && (
            <div className="rounded-xl border-2 border-rose-300 bg-rose-50/60 p-3 dark:border-rose-500/40 dark:bg-rose-500/10">
              <p className="mb-0.5 text-[11px] font-semibold uppercase tracking-wide text-rose-600 dark:text-rose-300">Xavfli amal</p>
              <p className="mb-2 text-[11px] text-rose-500/90 dark:text-rose-300/80">KOSHI dasturini shu kompyuterdan butunlay olib tashlaydi. Qaytarib bo‘lmaydi.</p>
              <Button
                className="w-full !bg-rose-800 !text-white hover:!bg-rose-900"
                onClick={async () => {
                  if (!confirm(`1/2 — ${device.data?.displayName ?? 'Bu kompyuter'} dan KOSHI butunlay o‘chirilsinmi?\n\nDastur, xizmat va fayllar o‘chadi.`)) return
                  if (!confirm('2/2 — Rostan ham o‘chirasizmi? Bu amalni qaytarib bo‘lmaydi.')) return
                  try {
                    await api(`/api/devices/${deviceId}/uninstall`, { method: 'POST' })
                    addLog('KOSHI bu kompyuterdan o‘chirilmoqda')
                    setStatus('ended')
                    setMessage('Agent o‘chirilmoqda')
                  } catch (e) {
                    addLog(`✖ ${(e as Error).message}`)
                  }
                }}
              >
                <PackageX className="size-4" /> Kompyuterdan o‘chirish
              </Button>
            </div>
          )}
        </div>
      </Card>

      <div className="grid items-start gap-4 xl:grid-cols-[1fr_300px]">
        <div className="min-w-0 space-y-4">
        <div
          className={`relative overflow-hidden rounded-2xl border bg-ink-950 transition ${
            controlEnabled && live ? 'border-brand-500 ring-2 ring-brand-500/40' : 'border-slate-200 dark:border-white/10'
          }`}
        >
          <canvas
            ref={canvasRef}
            tabIndex={0}
            style={{ aspectRatio: frameSize ? `${frameSize.w} / ${frameSize.h}` : '16 / 9' }}
            className={`block w-full bg-ink-950 outline-none focus:ring-2 focus:ring-brand-500 ${controlEnabled ? 'cursor-crosshair' : 'cursor-default'}`}
            onMouseMove={onMove}
            onMouseDown={(e) => send('down', e, { button: e.button })}
            onMouseUp={(e) => send('up', e, { button: e.button })}
            onContextMenu={(e) => e.preventDefault()}
            onKeyDown={onKey('keydown')}
            onKeyUp={onKey('keyup')}
          />
          {!live && (
            <div className="absolute inset-0 flex flex-col items-center justify-center gap-2 text-center text-sm text-slate-400">
              <p>{status === 'error' ? message : status === 'ended' ? `Sessiya tugadi: ${message}` : '“Jonli ko‘rish” tugmasini bosing — bu kompyuterda nima qilinayotgani shu yerda ko‘rinadi'}</p>
              <p className="text-xs text-slate-500">Boshqaruv uchun “Boshqaruvni boshlash”ni tanlang. Klaviatura uchun ekran ustiga bosing. Ctrl+Alt+Del va UAC oynalari Windows himoyasi tufayli uzatilmaydi.</p>
            </div>
          )}
        </div>
        <DeviceInsights deviceId={deviceId} device={device.data} />
        </div>
        <div className="space-y-4">
          <Card className="p-4">
            <p className="text-sm font-semibold">Sessiya jurnali</p>
            <p className="mb-2 text-[11px] text-slate-400">Shu masofaviy sessiyada bajarilgan amallar</p>
            <ul className="space-y-1.5 text-xs text-slate-600 dark:text-slate-300">
              {log.length === 0 ? <li className="text-slate-400">Hali amal yo‘q</li> : log.map((l, i) => <li key={i}>{l}</li>)}
            </ul>
          </Card>
          <Card className="p-4">
            <p className="text-sm font-semibold">Kompyuter hodisalari</p>
            <p className="mb-2 text-[11px] text-slate-400">Har kun alohida saqlanadi. Dastur ochilishi/yopilishi, kompyuter yoqilishi/o‘chishi — aniq vaqt bilan</p>
            <DeviceEventsFeed deviceId={deviceId} compact />
          </Card>
        </div>
      </div>
    </>
  )
}
