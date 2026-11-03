import { HubConnectionBuilder, LogLevel } from '@microsoft/signalr'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { ExternalLink, MessageSquare, Monitor, Send } from 'lucide-react'
import { useEffect, useRef, useState, type FormEvent } from 'react'
import { useSearchParams } from 'react-router'
import { Input, PageHeader } from '@/components/forms'
import { Badge, Button, Card, EmptyState, ErrorState, LoadingState, formatDateTime } from '@/components/ui'
import { api, getAccessToken } from '@/lib/api'
import type { Device, PagedResult, SupportMessage, SupportThread } from '@/lib/types'

export function SupportPage() {
  const qc = useQueryClient()
  const [params] = useSearchParams()
  const initialDevice = params.get('device') ?? ''
  const isPopup = params.get('popup') === '1'
  const [selectedId, setSelectedId] = useState<string>(initialDevice)
  const [text, setText] = useState('')
  const [typingDevices, setTypingDevices] = useState<Record<string, number>>({})
  const endRef = useRef<HTMLDivElement>(null)
  const lastTypingSentRef = useRef<number>(0)

  const threads = useQuery({
    queryKey: ['support-threads'],
    queryFn: () => api<SupportThread[]>('/api/support/threads'),
    refetchInterval: 8000,
  })

  const devices = useQuery({
    queryKey: ['devices-all'],
    queryFn: () => api<PagedResult<Device>>('/api/devices', { query: { page: 1, pageSize: 100 } }),
  })

  useEffect(() => {
    if (!selectedId) {
      if (threads.data?.[0]) setSelectedId(threads.data[0].deviceId)
      else if (devices.data?.items?.[0]) setSelectedId(devices.data.items[0].id)
    }
  }, [threads.data, devices.data, selectedId])

  const messages = useQuery({
    queryKey: ['support-messages', selectedId],
    queryFn: () => api<SupportMessage[]>(`/api/support/${selectedId}`),
    enabled: !!selectedId,
    refetchInterval: 6000,
  })

  // Real-time SignalR subscription for instant messages and typing indicators
  useEffect(() => {
    const conn = new HubConnectionBuilder()
      .withUrl('/hubs/monitoring', { accessTokenFactory: () => getAccessToken() ?? '' })
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build()

    conn.on('supportMessage', (msg: SupportMessage) => {
      setTypingDevices((prev) => {
        if (!prev[msg.deviceId]) return prev
        const next = { ...prev }
        delete next[msg.deviceId]
        return next
      })
      qc.setQueryData<SupportMessage[]>(['support-messages', msg.deviceId], (old) => {
        if (!old) return [msg]
        if (old.some((x) => x.id === msg.id)) return old
        return [...old, msg]
      })
      void qc.invalidateQueries({ queryKey: ['support-threads'] })
    })

    conn.on('supportTyping', (deviceId: string) => {
      const id = String(deviceId)
      setTypingDevices((prev) => ({ ...prev, [id]: Date.now() + 3500 }))
    })

    void conn.start().catch(() => undefined)
    const cleanupTimer = window.setInterval(() => {
      const now = Date.now()
      setTypingDevices((prev) => {
        let changed = false
        const next: Record<string, number> = {}
        for (const [k, exp] of Object.entries(prev)) {
          if (exp > now) next[k] = exp
          else changed = true
        }
        return changed ? next : prev
      })
    }, 1000)

    return () => {
      window.clearInterval(cleanupTimer)
      void conn.stop()
    }
  }, [qc])

  useEffect(() => {
    endRef.current?.scrollIntoView({ behavior: 'smooth' })
  }, [messages.data?.length, selectedId, typingDevices[selectedId]])

  const send = useMutation({
    mutationFn: (body: string) => api<SupportMessage>(`/api/support/${selectedId}`, { method: 'POST', body: { text: body } }),
    onSuccess: (msg) => {
      qc.setQueryData<SupportMessage[]>(['support-messages', selectedId], (old) => {
        if (!old) return [msg]
        if (old.some((x) => x.id === msg.id)) return old
        return [...old, msg]
      })
      void qc.invalidateQueries({ queryKey: ['support-threads'] })
    },
  })

  const notifyAdminTyping = () => {
    if (!selectedId) return
    const now = Date.now()
    if (now - lastTypingSentRef.current < 1200) return
    lastTypingSentRef.current = now
    void api(`/api/support/${selectedId}/typing`, { method: 'POST' }).catch(() => undefined)
  }

  const onSubmit = (e: FormEvent) => {
    e.preventDefault()
    const trimmed = text.trim()
    if (!trimmed || !selectedId) return
    setText('')
    send.mutate(trimmed)
  }

  const openPopup = () => {
    if (!selectedId) return
    window.open(
      `/app/support?popup=1&device=${encodeURIComponent(selectedId)}`,
      `koshi_chat_${selectedId}`,
      'width=520,height=680,menubar=no,toolbar=no,location=no,status=no'
    )
  }

  const threadMap = new Map((threads.data ?? []).map((t) => [t.deviceId, t]))
  const allDevices = devices.data?.items ?? []
  const mergedList: SupportThread[] = [
    ...(threads.data ?? []),
    ...allDevices
      .filter((d) => !threadMap.has(d.id))
      .map((d) => ({
        deviceId: d.id,
        deviceName: d.displayName,
        employeeName: d.employeeName,
        unreadFromDevice: 0,
        lastMessageAt: null,
        lastMessagePreview: null,
      })),
  ]

  const activeThread = mergedList.find((t) => t.deviceId === selectedId)
  const isSelectedTyping = Boolean(selectedId && (typingDevices[selectedId] ?? 0) > Date.now())

  return (
    <>
      {!isPopup && (
        <PageHeader
          title="Yordam chati"
          subtitle="Xodimlar kompyuteridagi KOSHI Agent ilovasi bilan tezkor real-time muloqot"
        />
      )}

      <div className={`grid gap-6 ${isPopup ? 'grid-cols-1' : 'lg:grid-cols-[340px_1fr]'}`}>
        {!isPopup && (
          <Card className="flex h-[640px] flex-col overflow-hidden">
            <div className="border-b border-slate-100 px-4 py-3 dark:border-white/5">
              <p className="text-sm font-semibold">Qurilmalar va muloqotlar</p>
              <p className="text-xs text-slate-500">Tanlab yozishni boshlang</p>
            </div>
            <div className="flex-1 divide-y divide-slate-100 overflow-y-auto dark:divide-white/5">
              {threads.isPending && devices.isPending ? (
                <LoadingState />
              ) : mergedList.length === 0 ? (
                <EmptyState title="Qurilmalar yo‘q" hint="Agent o‘rnatilgan kompyuterlar shu yerda ko‘rinadi." />
              ) : (
                mergedList.map((t) => {
                  const active = t.deviceId === selectedId
                  const typing = (typingDevices[t.deviceId] ?? 0) > Date.now()
                  return (
                    <button
                      key={t.deviceId}
                      type="button"
                      onClick={() => setSelectedId(t.deviceId)}
                      className={`flex w-full items-start gap-3 px-4 py-3 text-left transition ${
                        active ? 'bg-brand-50/80 dark:bg-brand-500/10' : 'hover:bg-slate-50 dark:hover:bg-white/5'
                      }`}
                    >
                      <div className="mt-0.5 flex size-9 shrink-0 items-center justify-center rounded-xl bg-slate-100 text-slate-600 dark:bg-white/10 dark:text-slate-300">
                        <Monitor className="size-4" />
                      </div>
                      <div className="min-w-0 flex-1">
                        <div className="flex items-center justify-between gap-2">
                          <p className="truncate text-sm font-semibold">{t.deviceName}</p>
                          {t.unreadFromDevice > 0 && <Badge tone="red">{t.unreadFromDevice}</Badge>}
                        </div>
                        <p className="truncate text-xs text-slate-500">{t.employeeName ?? 'Biriktirilmagan'}</p>
                        {typing ? (
                          <p className="mt-1 truncate text-xs font-medium italic text-brand-600">yozmoqda…</p>
                        ) : (
                          t.lastMessagePreview && <p className="mt-1 truncate text-xs text-slate-600 dark:text-slate-400">{t.lastMessagePreview}</p>
                        )}
                      </div>
                    </button>
                  )
                })
              )}
            </div>
          </Card>
        )}

        <Card className="flex h-[640px] flex-col overflow-hidden">
          {!selectedId ? (
            <div className="flex flex-1 items-center justify-center">
              <EmptyState title="Suhbatni tanlang" hint="Chap ro‘yxatdan qurilmani tanlab xodim bilan yozishing." />
            </div>
          ) : (
            <>
              <div className="flex items-center justify-between border-b border-slate-100 px-5 py-3.5 dark:border-white/5">
                <div className="flex items-center gap-3">
                  <MessageSquare className="size-5 text-brand-600" />
                  <div>
                    <p className="text-sm font-semibold">{activeThread?.deviceName ?? 'Qurilma'}</p>
                    <p className="text-xs text-slate-500">
                      {isSelectedTyping ? (
                        <span className="font-medium italic text-brand-600">Xodim yozmoqda…</span>
                      ) : (
                        activeThread?.employeeName ?? 'Xodim biriktirilmagan'
                      )}
                    </p>
                  </div>
                </div>
                {!isPopup && (
                  <Button variant="secondary" onClick={openPopup} title="Chatni alohida oynada ochish">
                    <ExternalLink className="size-4" /> Alohida oynada ochish
                  </Button>
                )}
              </div>

              <div className="flex-1 space-y-3 overflow-y-auto bg-slate-50/60 p-5 dark:bg-ink-950/40">
                {messages.isPending ? (
                  <LoadingState />
                ) : messages.isError ? (
                  <ErrorState error={messages.error} />
                ) : (messages.data ?? []).length === 0 ? (
                  <EmptyState title="Xabarlar yo‘q" hint="Xodimga birinchi xabarni yuboring — agent oynasida darhol ko‘rinadi." />
                ) : (
                  messages.data?.map((m) => {
                    const isAdmin = m.sender === 'Admin'
                    return (
                      <div key={m.id} className={`flex ${isAdmin ? 'justify-end' : 'justify-start'}`}>
                        <div
                          className={`max-w-[75%] rounded-2xl px-4 py-2.5 text-sm shadow-xs ${
                            isAdmin
                              ? 'bg-brand-600 text-white'
                              : 'border border-slate-200 bg-white text-slate-900 dark:border-white/10 dark:bg-ink-900 dark:text-white'
                          }`}
                        >
                          {isAdmin && m.senderName && <p className="mb-0.5 text-[11px] font-semibold text-brand-100">{m.senderName}</p>}
                          <p className="whitespace-pre-wrap break-words">{m.text}</p>
                          <p className={`mt-1 text-right text-[10px] ${isAdmin ? 'text-brand-200' : 'text-slate-400'}`}>{formatDateTime(m.createdAt)}</p>
                        </div>
                      </div>
                    )
                  })
                )}
                {isSelectedTyping && (
                  <div className="flex justify-start">
                    <div className="rounded-2xl border border-brand-200 bg-brand-50 px-3.5 py-2 text-xs font-medium italic text-brand-700 dark:border-brand-500/20 dark:bg-brand-500/10 dark:text-brand-300">
                      Xodim yozmoqda…
                    </div>
                  </div>
                )}
                <div ref={endRef} />
              </div>

              <form onSubmit={onSubmit} className="flex items-center gap-2 border-t border-slate-100 p-3 dark:border-white/5">
                <Input
                  value={text}
                  onChange={(e) => {
                    setText(e.target.value)
                    if (e.target.value.trim()) notifyAdminTyping()
                  }}
                  placeholder="Xabaringizni yozing…"
                  maxLength={2000}
                  className="flex-1"
                />
                <Button type="submit" loading={send.isPending} disabled={!text.trim()}>
                  <Send className="size-4" /> Yuborish
                </Button>
              </form>
            </>
          )}
        </Card>
      </div>
    </>
  )
}
