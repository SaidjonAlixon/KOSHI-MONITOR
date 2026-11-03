import { HubConnectionBuilder, HubConnectionState, LogLevel, type HubConnection } from '@microsoft/signalr'
import { useQuery } from '@tanstack/react-query'
import { ArrowLeft, Eye, Lock, LogOut, Maximize, MessageSquare, MousePointerClick, Power, RotateCcw, Square, XCircle, XOctagon } from 'lucide-react'
import { useCallback, useEffect, useRef, useState, type KeyboardEvent, type MouseEvent, type WheelEvent } from 'react'
import { Link, useParams } from 'react-router'
import { PageHeader, Select } from '@/components/forms'
import { Badge, Button, Card } from '@/components/ui'
import { api, getAccessToken } from '@/lib/api'
import type { Device } from '@/lib/types'

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

export function RemotePage() {
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
      addLog(control ? 'Boshqaruv sessiyasi boshlandi — xodim ekranida ogohlantirish ko‘rsatildi' : 'Jonli kuzatuv boshlandi — xodim ekranida ogohlantirish ko‘rsatildi')
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
        subtitle="Xodim ekranida sessiya davomida ogohlantirish banneri ko‘rinadi va u sessiyani tugatishi mumkin. Har bir sessiya va buyruq audit jurnaliga yoziladi."
        actions={
          <Link to="/app/devices" className="inline-flex items-center gap-2 rounded-xl px-3 py-2 text-sm text-slate-600 hover:bg-slate-100 dark:text-slate-300 dark:hover:bg-white/10">
            <ArrowLeft className="size-4" /> Qurilmalar
          </Link>
        }
      />

      <Card className="p-3">
        <div className="flex flex-wrap items-center gap-2">
          <Badge tone={tone}>{statusLabel}</Badge>
          {device.data && <span className="text-xs text-slate-500">{device.data.employeeName ?? 'biriktirilmagan'} · {device.data.hostname}</span>}
          <div className="ml-auto flex flex-wrap items-center gap-2">
            <Select value={quality} onChange={(e) => setQuality(e.target.value as keyof typeof QUALITY)} disabled={live} className="w-36" aria-label="Sifat">
              <option value="low">Past (tez)</option>
              <option value="medium">O‘rta</option>
              <option value="high">Yuqori</option>
            </Select>
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
        <div className="mt-3 flex flex-wrap gap-2 border-t border-slate-100 pt-3 dark:border-white/5">
          <Button variant="secondary" onClick={() => command('lock')} disabled={status === 'error'}>
            <Lock className="size-4" /> Bloklash
          </Button>
          <Button variant="secondary" onClick={() => { const m = prompt('Xodimga xabar:'); if (m) void command('message', m.slice(0, 500)) }}>
            <MessageSquare className="size-4" /> Xabar
          </Button>
          <Button variant="secondary" onClick={() => { const p = prompt('Yopiladigan jarayon (masalan, chrome.exe):'); if (p) void command('kill-process', p) }}>
            <XOctagon className="size-4" /> Jarayonni yopish
          </Button>
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
        </div>
      </Card>

      <div className="grid gap-4 xl:grid-cols-[1fr_300px]">
        <div className="relative overflow-hidden rounded-2xl border border-slate-200 bg-ink-950 dark:border-white/10">
          <canvas
            ref={canvasRef}
            tabIndex={0}
            className={`block aspect-video w-full bg-ink-950 object-contain outline-none focus:ring-2 focus:ring-brand-500 ${controlEnabled ? 'cursor-crosshair' : 'cursor-default'}`}
            onMouseMove={onMove}
            onMouseDown={(e) => send('down', e, { button: e.button })}
            onMouseUp={(e) => send('up', e, { button: e.button })}
            onWheel={(e) => send('wheel', e, { delta: e.deltaY })}
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
        <Card className="p-4">
          <p className="text-sm font-semibold">Hodisalar</p>
          <ul className="mt-2 space-y-1.5 text-xs text-slate-600 dark:text-slate-300">
            {log.length === 0 ? <li className="text-slate-400">Hali hodisa yo‘q</li> : log.map((l, i) => <li key={i}>{l}</li>)}
          </ul>
        </Card>
      </div>
    </>
  )
}
