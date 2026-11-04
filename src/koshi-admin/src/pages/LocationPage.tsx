import 'leaflet/dist/leaflet.css'
import { useMutation, useQuery } from '@tanstack/react-query'
import * as L from 'leaflet'
import { Crosshair, ExternalLink, MapPin, Radio, Search } from 'lucide-react'
import { useEffect, useMemo, useRef, useState } from 'react'
import { Input, PageHeader, Select, tableClass, tbodyClass, td, th, theadClass } from '@/components/forms'
import { Badge, Button, Card, CardHeader, EmptyState, ErrorState, LoadingState, formatDateTime, formatDuration } from '@/components/ui'
import { ApiError, api } from '@/lib/api'
import { LOCATION_SOURCE_LABELS } from '@/lib/labels'
import { useMonitoringRealtime } from '@/lib/realtime'
import type { DeviceLocation, LocationPoint } from '@/lib/types'

const DEFAULT_CENTER: L.LatLngTuple = [41.311, 69.279]
const isApprox = (source: string) => source === 'ip'

function markerStyle(d: DeviceLocation, selected: boolean): L.CircleMarkerOptions {
  const color = !d.isOnline ? '#94a3b8' : isApprox(d.source) ? '#f59e0b' : '#10b981'
  return {
    radius: selected ? 11 : 8,
    color: selected ? '#1d4ed8' : '#ffffff',
    weight: selected ? 3 : 2,
    fillColor: color,
    fillOpacity: 0.95,
    dashArray: isApprox(d.source) ? '3 3' : undefined,
  }
}

function placeLabel(p: { city: string | null; country: string | null }) {
  return [p.city, p.country].filter(Boolean).join(', ') || '—'
}

function LocationMap({ devices, selected, history, onSelect }: {
  devices: DeviceLocation[]
  selected: DeviceLocation | null
  history: LocationPoint[]
  onSelect: (id: string) => void
}) {
  const el = useRef<HTMLDivElement>(null)
  const map = useRef<L.Map | null>(null)
  const markers = useRef<L.LayerGroup | null>(null)
  const overlay = useRef<L.LayerGroup | null>(null)
  const fitted = useRef(false)
  const flownTo = useRef<string | null>(null)

  useEffect(() => {
    if (!el.current || map.current) return
    const m = L.map(el.current, { zoomControl: true, attributionControl: true }).setView(DEFAULT_CENTER, 6)
    L.tileLayer('https://tile.openstreetmap.org/{z}/{x}/{y}.png', {
      maxZoom: 19,
      attribution: '&copy; OpenStreetMap',
      // The site sends "Referrer-Policy: no-referrer"; OpenStreetMap tile servers require a referrer.
      referrerPolicy: 'strict-origin-when-cross-origin',
    }).addTo(m)
    markers.current = L.layerGroup().addTo(m)
    overlay.current = L.layerGroup().addTo(m)
    map.current = m
    return () => {
      m.remove()
      map.current = null
    }
  }, [])

  useEffect(() => {
    const m = map.current
    const group = markers.current
    if (!m || !group) return
    group.clearLayers()
    devices.forEach((d) => {
      L.circleMarker([d.latitude, d.longitude], markerStyle(d, d.deviceId === selected?.deviceId))
        .bindTooltip(`${d.deviceName}${d.employeeName ? ` — ${d.employeeName}` : ''}`, { direction: 'top', offset: [0, -8] })
        .on('click', () => onSelect(d.deviceId))
        .addTo(group)
    })
    if (!fitted.current && devices.length > 0 && !selected) {
      fitted.current = true
      if (devices.length === 1) m.setView([devices[0]!.latitude, devices[0]!.longitude], isApprox(devices[0]!.source) ? 11 : 15)
      else m.fitBounds(L.latLngBounds(devices.map((d) => [d.latitude, d.longitude] as L.LatLngTuple)), { padding: [40, 40], maxZoom: 15 })
    }
  }, [devices, selected, onSelect])

  useEffect(() => {
    const m = map.current
    const group = overlay.current
    if (!m || !group) return
    group.clearLayers()
    if (!selected) {
      flownTo.current = null
      return
    }
    const path = history.map((p) => [p.latitude, p.longitude] as L.LatLngTuple)
    if (path.length > 1) {
      L.polyline(path, { color: '#2563eb', weight: 3, opacity: 0.7 }).addTo(group)
      history.forEach((p) =>
        L.circleMarker([p.latitude, p.longitude], { radius: 4, color: '#2563eb', weight: 1, fillColor: '#93c5fd', fillOpacity: 1 })
          .bindTooltip(`${formatDateTime(p.firstSeenAt)} — ${formatDateTime(p.lastSeenAt)}`)
          .addTo(group),
      )
    }
    if (selected.accuracyMeters) {
      L.circle([selected.latitude, selected.longitude], {
        radius: selected.accuracyMeters,
        color: isApprox(selected.source) ? '#f59e0b' : '#2563eb',
        weight: 1,
        fillOpacity: 0.08,
      }).addTo(group)
    }
    // Realtime refetches must not yank the map away while the admin is panning.
    if (flownTo.current !== selected.deviceId) {
      flownTo.current = selected.deviceId
      m.flyTo([selected.latitude, selected.longitude], isApprox(selected.source) ? 11 : 16, { duration: 0.6 })
    }
  }, [selected, history])

  return <div ref={el} className="h-[560px] w-full rounded-b-2xl" />
}

export function LocationPage() {
  const realtime = useMonitoringRealtime([['locations'], ['location-history']])
  const [selectedId, setSelectedId] = useState<string | null>(null)
  const [days, setDays] = useState(7)
  const [search, setSearch] = useState('')
  const [notice, setNotice] = useState<string | null>(null)

  const q = useQuery({
    queryKey: ['locations'],
    queryFn: ({ signal }) => api<DeviceLocation[]>('/api/locations', { signal }),
    refetchInterval: realtime === 'connected' ? 60_000 : 15_000,
  })
  const history = useQuery({
    queryKey: ['location-history', selectedId, days],
    queryFn: ({ signal }) => api<LocationPoint[]>(`/api/locations/${selectedId}/history`, { query: { days }, signal }),
    enabled: selectedId !== null,
  })
  const locate = useMutation({
    mutationFn: (id: string) => api(`/api/locations/${id}/locate`, { method: 'POST' }),
    onSuccess: () => setNotice('So‘rov yuborildi — agent joylashuvni aniqlab, bir necha soniyada yuboradi.'),
    onError: (e) => setNotice(e instanceof ApiError ? e.message : 'So‘rovni yuborib bo‘lmadi'),
  })

  const devices = useMemo(() => q.data ?? [], [q.data])
  const filtered = useMemo(() => {
    const term = search.trim().toLowerCase()
    if (!term) return devices
    return devices.filter((d) =>
      [d.deviceName, d.hostname, d.employeeName, d.departmentName, d.city].some((v) => v?.toLowerCase().includes(term)),
    )
  }, [devices, search])
  const selected = devices.find((d) => d.deviceId === selectedId) ?? null
  const counts = useMemo(() => ({
    total: devices.length,
    online: devices.filter((d) => d.isOnline).length,
    precise: devices.filter((d) => !isApprox(d.source)).length,
  }), [devices])

  return (
    <>
      <PageHeader
        title="Joylashuv"
        subtitle="Kompyuterlar qayerda ekanini xaritada kuzatish: GPS / Wi-Fi bo‘yicha aniq, aks holda IP bo‘yicha taxminiy"
        actions={
          <span className={`inline-flex items-center gap-2 rounded-xl border px-3 py-2 text-xs font-medium ${realtime === 'connected' ? 'border-emerald-200 text-emerald-700 dark:border-emerald-500/30 dark:text-emerald-300' : 'border-amber-200 text-amber-700 dark:border-amber-500/30 dark:text-amber-300'}`}>
            <Radio className={`size-4 ${realtime === 'connected' ? 'animate-pulse' : ''}`} />
            {realtime === 'connected' ? 'Real-time ulangan' : 'Har 15 soniyada yangilanadi'}
          </span>
        }
      />

      <div className="grid gap-4 sm:grid-cols-3">
        <Card className="flex items-center justify-between p-4">
          <span className="text-sm text-slate-500">Joylashuvi ma’lum</span>
          <span className="text-2xl font-semibold tabular-nums">{counts.total}</span>
        </Card>
        <Card className="flex items-center justify-between p-4">
          <span className="text-sm text-slate-500">Hozir onlayn</span>
          <span className="text-2xl font-semibold tabular-nums text-emerald-600">{counts.online}</span>
        </Card>
        <Card className="flex items-center justify-between p-4">
          <span className="text-sm text-slate-500">Aniq (GPS / Wi-Fi)</span>
          <span className="text-2xl font-semibold tabular-nums text-brand-600">{counts.precise}</span>
        </Card>
      </div>

      {q.isPending ? (
        <LoadingState />
      ) : q.isError ? (
        <ErrorState error={q.error} onRetry={() => q.refetch()} />
      ) : (
        <div className="grid gap-6 xl:grid-cols-[360px_1fr]">
          <Card className="flex max-h-[640px] flex-col">
            <CardHeader title="Kompyuterlar" subtitle="Xaritada ko‘rish uchun tanlang" />
            <div className="border-b border-slate-100 p-3 dark:border-white/5">
              <label className="relative block">
                <Search className="pointer-events-none absolute left-3 top-1/2 size-4 -translate-y-1/2 text-slate-400" />
                <Input value={search} onChange={(e) => setSearch(e.target.value)} placeholder="Qurilma, xodim yoki shahar…" className="w-full pl-9" />
              </label>
            </div>
            {filtered.length === 0 ? (
              <EmptyState title="Joylashuv ma’lumoti yo‘q" hint="Yangi agent versiyasi o‘rnatilgan kompyuterlar joylashuvini har 5 daqiqada yuboradi." />
            ) : (
              <ul className="flex-1 divide-y divide-slate-100 overflow-y-auto dark:divide-white/5">
                {filtered.map((d) => (
                  <li key={d.deviceId}>
                    <button
                      onClick={() => setSelectedId(d.deviceId)}
                      className={`w-full px-4 py-3 text-left transition hover:bg-slate-50 dark:hover:bg-white/5 ${d.deviceId === selectedId ? 'bg-brand-50 dark:bg-brand-500/10' : ''}`}
                    >
                      <div className="flex items-center justify-between gap-2">
                        <span className="flex items-center gap-2 truncate text-sm font-medium">
                          <span className={`size-2 shrink-0 rounded-full ${d.isOnline ? 'bg-emerald-500' : 'bg-slate-300'}`} />
                          {d.deviceName}
                        </span>
                        <Badge tone={isApprox(d.source) ? 'amber' : 'green'}>{isApprox(d.source) ? 'Taxminiy' : 'Aniq'}</Badge>
                      </div>
                      <p className="mt-0.5 truncate text-xs text-slate-500">{d.employeeName ?? 'Xodim biriktirilmagan'}{d.departmentName ? ` · ${d.departmentName}` : ''}</p>
                      <p className="mt-1 flex items-center gap-1 truncate text-xs text-slate-500">
                        <MapPin className="size-3 shrink-0" /> {placeLabel(d)} · {formatDateTime(d.lastSeenAt)}
                      </p>
                    </button>
                  </li>
                ))}
              </ul>
            )}
          </Card>

          <div className="space-y-6">
            <Card>
              <CardHeader
                title={selected ? selected.deviceName : 'Xarita'}
                subtitle={selected ? `${selected.employeeName ?? 'Xodim biriktirilmagan'} · ${LOCATION_SOURCE_LABELS[selected.source]}` : 'Yashil — aniq, sariq — taxminiy (IP), kulrang — offlayn'}
                actions={selected && (
                  <div className="flex flex-wrap items-center gap-2">
                    <Button variant="secondary" onClick={() => locate.mutate(selected.deviceId)} loading={locate.isPending} disabled={!selected.isOnline} title={selected.isOnline ? undefined : 'Kompyuter offlayn'}>
                      <Crosshair className="size-4" /> Hozir aniqlash
                    </Button>
                    <a
                      href={`https://www.google.com/maps?q=${selected.latitude},${selected.longitude}`}
                      target="_blank"
                      rel="noreferrer"
                      className="inline-flex items-center gap-1.5 rounded-xl border border-slate-200 px-3 py-2 text-sm text-slate-700 hover:bg-slate-50 dark:border-white/10 dark:text-slate-200 dark:hover:bg-white/5"
                    >
                      <ExternalLink className="size-4" /> Google Maps
                    </a>
                  </div>
                )}
              />
              <LocationMap devices={devices} selected={selected} history={history.data ?? []} onSelect={setSelectedId} />
            </Card>
            {notice && <p className="rounded-xl bg-brand-50 px-4 py-2 text-sm text-brand-700 dark:bg-brand-500/10 dark:text-brand-300">{notice}</p>}

            {selected && (
              <Card>
                <CardHeader
                  title="Joylashuv tafsilotlari va tarixi"
                  subtitle="Bir joyda turgan vaqt bitta yozuv sifatida ko‘rsatiladi"
                  actions={
                    <Select value={days} onChange={(e) => setDays(Number(e.target.value))} className="w-36">
                      <option value={1}>Oxirgi 24 soat</option>
                      <option value={7}>Oxirgi 7 kun</option>
                      <option value={30}>Oxirgi 30 kun</option>
                      <option value={90}>Oxirgi 90 kun</option>
                    </Select>
                  }
                />
                <dl className="grid gap-4 border-b border-slate-100 p-5 text-sm sm:grid-cols-2 lg:grid-cols-4 dark:border-white/5">
                  <div>
                    <dt className="text-xs text-slate-500">Koordinata</dt>
                    <dd className="font-mono">{selected.latitude.toFixed(6)}, {selected.longitude.toFixed(6)}</dd>
                  </div>
                  <div>
                    <dt className="text-xs text-slate-500">Aniqlik</dt>
                    <dd>{selected.accuracyMeters ? `±${Math.round(selected.accuracyMeters)} m` : '—'} · {LOCATION_SOURCE_LABELS[selected.source]}</dd>
                  </div>
                  <div>
                    <dt className="text-xs text-slate-500">Hudud (internet bo‘yicha)</dt>
                    <dd>{[selected.city, selected.region, selected.country].filter(Boolean).join(', ') || '—'}</dd>
                  </div>
                  <div>
                    <dt className="text-xs text-slate-500">Tarmoq</dt>
                    <dd>{selected.publicIp ?? '—'}{selected.isp ? ` · ${selected.isp}` : ''}</dd>
                  </div>
                  <div>
                    <dt className="text-xs text-slate-500">Shu joyda</dt>
                    <dd>{formatDateTime(selected.firstSeenAt)} dan · {formatDuration((new Date(selected.lastSeenAt).getTime() - new Date(selected.firstSeenAt).getTime()) / 1000)}</dd>
                  </div>
                  <div>
                    <dt className="text-xs text-slate-500">Oxirgi aniqlangan</dt>
                    <dd>{formatDateTime(selected.lastSeenAt)}</dd>
                  </div>
                </dl>
                {isApprox(selected.source) && (
                  <p className="border-b border-slate-100 bg-amber-50 px-5 py-2 text-xs text-amber-700 dark:border-white/5 dark:bg-amber-500/10 dark:text-amber-300">
                    Bu kompyuterda Windows joylashuv xizmati o‘chiq yoki ruxsat berilmagan, shuning uchun joylashuv IP manzil bo‘yicha taxminiy (shahar darajasida). Aniq joylashuv uchun Windows Sozlamalari → Maxfiylik → Joylashuv bo‘limida joylashuvni yoqing.
                  </p>
                )}
                {history.isPending ? (
                  <LoadingState />
                ) : history.isError ? (
                  <ErrorState error={history.error} onRetry={() => history.refetch()} />
                ) : history.data.length === 0 ? (
                  <EmptyState title="Bu davrda yozuv yo‘q" />
                ) : (
                  <div className="max-h-80 overflow-auto">
                    <table className={tableClass}>
                      <thead className={theadClass}>
                        <tr>
                          <th className={th}>Dan</th>
                          <th className={th}>Gacha</th>
                          <th className={th}>Davomiylik</th>
                          <th className={th}>Joy</th>
                          <th className={th}>Manba</th>
                        </tr>
                      </thead>
                      <tbody className={tbodyClass}>
                        {[...history.data].reverse().map((p) => (
                          <tr key={`${p.firstSeenAt}-${p.latitude}`}>
                            <td className={`${td} text-xs`}>{formatDateTime(p.firstSeenAt)}</td>
                            <td className={`${td} text-xs`}>{formatDateTime(p.lastSeenAt)}</td>
                            <td className={`${td} text-xs`}>{formatDuration((new Date(p.lastSeenAt).getTime() - new Date(p.firstSeenAt).getTime()) / 1000)}</td>
                            <td className={`${td} text-xs`}>
                              <a href={`https://www.google.com/maps?q=${p.latitude},${p.longitude}`} target="_blank" rel="noreferrer" className="text-brand-600 hover:underline">
                                {placeLabel(p)} ({p.latitude.toFixed(4)}, {p.longitude.toFixed(4)})
                              </a>
                            </td>
                            <td className={`${td} text-xs`}>{LOCATION_SOURCE_LABELS[p.source]}{p.accuracyMeters ? ` ±${Math.round(p.accuracyMeters)} m` : ''}</td>
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  </div>
                )}
              </Card>
            )}
          </div>
        </div>
      )}
    </>
  )
}
