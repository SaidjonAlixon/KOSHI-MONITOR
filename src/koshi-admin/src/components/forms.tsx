import { useQuery } from '@tanstack/react-query'
import { ArrowDown, ArrowUp, ArrowUpDown, X } from 'lucide-react'
import { forwardRef, useEffect, type InputHTMLAttributes, type ReactNode, type SelectHTMLAttributes, type TextareaHTMLAttributes } from 'react'
import { Button } from '@/components/ui'
import { api } from '@/lib/api'
import type { Department, Device, Employee, PagedResult } from '@/lib/types'

export function PageHeader({ title, subtitle, actions }: { title: string; subtitle?: string; actions?: ReactNode }) {
  return (
    <div className="flex flex-wrap items-end justify-between gap-4">
      <div>
        <h1 className="text-xl font-semibold tracking-tight">{title}</h1>
        {subtitle && <p className="mt-1 text-sm text-slate-500 dark:text-slate-400">{subtitle}</p>}
      </div>
      {actions && <div className="flex flex-wrap gap-2">{actions}</div>}
    </div>
  )
}

export function Modal({ open, title, onClose, children, footer, wide }: { open: boolean; title: string; onClose: () => void; children: ReactNode; footer?: ReactNode; wide?: boolean }) {
  useEffect(() => {
    if (!open) return
    const onKey = (e: KeyboardEvent) => e.key === 'Escape' && onClose()
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [open, onClose])
  if (!open) return null
  return (
    <div className="fixed inset-0 z-50 flex items-start justify-center overflow-y-auto bg-ink-950/60 p-4 backdrop-blur-sm sm:items-center" onMouseDown={(e) => e.target === e.currentTarget && onClose()}>
      <div role="dialog" aria-modal="true" aria-label={title} className={`w-full ${wide ? 'max-w-3xl' : 'max-w-lg'} rounded-2xl border border-slate-200 bg-white shadow-2xl dark:border-white/10 dark:bg-ink-900`}>
        <header className="flex items-center justify-between border-b border-slate-100 px-5 py-4 dark:border-white/5">
          <h2 className="text-base font-semibold">{title}</h2>
          <button onClick={onClose} className="rounded-lg p-1 text-slate-400 hover:bg-slate-100 dark:hover:bg-white/10" aria-label="Yopish">
            <X className="size-5" />
          </button>
        </header>
        <div className="max-h-[70vh] overflow-y-auto px-5 py-4">{children}</div>
        {footer && <footer className="flex justify-end gap-2 border-t border-slate-100 px-5 py-3 dark:border-white/5">{footer}</footer>}
      </div>
    </div>
  )
}

const control =
  'w-full rounded-xl border border-slate-200 bg-white px-3 py-2 text-sm outline-none transition focus:border-brand-500 focus:ring-2 focus:ring-brand-500/25 disabled:bg-slate-50 dark:border-white/10 dark:bg-white/5 dark:disabled:bg-white/5'

export function Field({ label, error, hint, children, className = '' }: { label: string; error?: string; hint?: string; children: ReactNode; className?: string }) {
  return (
    <label className={`block text-sm ${className}`}>
      <span className="font-medium text-slate-700 dark:text-slate-200">{label}</span>
      <div className="mt-1">{children}</div>
      {hint && !error && <span className="mt-1 block text-xs text-slate-500">{hint}</span>}
      {error && <span className="mt-1 block text-xs text-rose-600">{error}</span>}
    </label>
  )
}

export const Input = forwardRef<HTMLInputElement, InputHTMLAttributes<HTMLInputElement>>(function Input({ className = '', ...props }, ref) {
  return <input ref={ref} {...props} className={`${control} ${className}`} />
})

export const Select = forwardRef<HTMLSelectElement, SelectHTMLAttributes<HTMLSelectElement>>(function Select({ className = '', ...props }, ref) {
  return <select ref={ref} {...props} className={`${control} dark:bg-ink-900 ${className}`} />
})

export const Textarea = forwardRef<HTMLTextAreaElement, TextareaHTMLAttributes<HTMLTextAreaElement>>(function Textarea({ className = '', ...props }, ref) {
  return <textarea ref={ref} {...props} className={`${control} ${className}`} />
})

export function Checkbox({ label, ...props }: InputHTMLAttributes<HTMLInputElement> & { label: ReactNode }) {
  return (
    <label className="flex cursor-pointer items-start gap-2 text-sm">
      <input type="checkbox" {...props} className="mt-0.5 size-4 rounded border-slate-300 accent-brand-600" />
      <span>{label}</span>
    </label>
  )
}

export function Pagination({ page, pageSize, total, onChange }: { page: number; pageSize: number; total: number; onChange: (page: number) => void }) {
  const pages = Math.max(1, Math.ceil(total / pageSize))
  if (total <= pageSize) return null
  return (
    <div className="flex items-center justify-between border-t border-slate-100 px-5 py-3 text-sm dark:border-white/5">
      <span className="text-slate-500">
        Jami {total} · {page}/{pages}-sahifa
      </span>
      <div className="flex gap-2">
        <Button variant="secondary" disabled={page <= 1} onClick={() => onChange(page - 1)}>
          Oldingi
        </Button>
        <Button variant="secondary" disabled={page >= pages} onClick={() => onChange(page + 1)}>
          Keyingi
        </Button>
      </div>
    </div>
  )
}

export interface SortState {
  by: string
  dir: 'asc' | 'desc'
}

export function SortHeader({ label, field, sort, onSort }: { label: string; field: string; sort: SortState; onSort: (s: SortState) => void }) {
  const active = sort.by === field
  const Icon = !active ? ArrowUpDown : sort.dir === 'asc' ? ArrowUp : ArrowDown
  return (
    <th className="px-5 py-3" aria-sort={active ? (sort.dir === 'asc' ? 'ascending' : 'descending') : 'none'}>
      <button className="inline-flex items-center gap-1 uppercase hover:text-slate-800 dark:hover:text-slate-200" onClick={() => onSort({ by: field, dir: active && sort.dir === 'asc' ? 'desc' : 'asc' })}>
        {label} <Icon className="size-3" />
      </button>
    </th>
  )
}

export const th = 'px-5 py-3'
export const td = 'px-5 py-3'
export const tableClass = 'w-full text-sm'
export const theadClass = 'text-left text-xs uppercase text-slate-500 bg-slate-50/60 dark:bg-white/[0.02]'
export const tbodyClass = 'divide-y divide-slate-100 dark:divide-white/5'

// ---------- shared lookups ----------

export function useDepartments(enabled = true) {
  return useQuery({ queryKey: ['departments'], queryFn: () => api<Department[]>('/api/departments'), enabled, staleTime: 60_000 })
}

export function useEmployeesLookup(enabled = true) {
  return useQuery({
    queryKey: ['employees-lookup'],
    queryFn: () => api<PagedResult<Employee>>('/api/employees', { query: { pageSize: 200 } }),
    enabled,
    staleTime: 60_000,
  })
}

export function useDevicesLookup(enabled = true) {
  return useQuery({
    queryKey: ['devices-lookup'],
    queryFn: () => api<PagedResult<Device>>('/api/devices', { query: { pageSize: 200 } }),
    enabled,
    staleTime: 60_000,
  })
}

// ---------- filters ----------

export interface Filters {
  from: string
  to: string
  departmentId: string
  branch: string
  employeeId: string
  deviceId: string
}

export function toLocalInput(d: Date): string {
  const pad = (n: number) => String(n).padStart(2, '0')
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`
}

export function defaultFilters(days = 7): Filters {
  const to = new Date()
  const from = new Date(to.getTime() - days * 86_400_000)
  return { from: toLocalInput(from), to: toLocalInput(to), departmentId: '', branch: '', employeeId: '', deviceId: '' }
}

/** Converts the date-only filter to the API query (from = start of day, to = end of the selected day). */
export function filtersToQuery(f: Filters) {
  const from = f.from ? new Date(`${f.from}T00:00:00`).toISOString() : undefined
  const to = f.to ? new Date(new Date(`${f.to}T00:00:00`).getTime() + 86_400_000).toISOString() : undefined
  return { from, to, departmentId: f.departmentId, branch: f.branch, employeeId: f.employeeId, deviceId: f.deviceId }
}

export function FilterBar({ value, onChange, show = ['date', 'department', 'branch', 'employee', 'device'] }: { value: Filters; onChange: (f: Filters) => void; show?: ('date' | 'department' | 'branch' | 'employee' | 'device')[] }) {
  const deps = useDepartments(show.includes('department') || show.includes('branch'))
  const emps = useEmployeesLookup(show.includes('employee'))
  const devs = useDevicesLookup(show.includes('device'))
  const branches = [...new Set((deps.data ?? []).map((d) => d.branch).filter((b): b is string => !!b))]
  const set = (patch: Partial<Filters>) => onChange({ ...value, ...patch })

  return (
    <div className="grid gap-3 rounded-2xl border border-slate-200/80 bg-white p-4 sm:grid-cols-2 lg:grid-cols-6 dark:border-white/10 dark:bg-ink-900">
      {show.includes('date') && (
        <>
          <Field label="Dan">
            <Input type="date" value={value.from} max={value.to} onChange={(e) => set({ from: e.target.value })} />
          </Field>
          <Field label="Gacha">
            <Input type="date" value={value.to} min={value.from} onChange={(e) => set({ to: e.target.value })} />
          </Field>
        </>
      )}
      {show.includes('branch') && (
        <Field label="Filial">
          <Select value={value.branch} onChange={(e) => set({ branch: e.target.value })}>
            <option value="">Barchasi</option>
            {branches.map((b) => (
              <option key={b}>{b}</option>
            ))}
          </Select>
        </Field>
      )}
      {show.includes('department') && (
        <Field label="Bo‘lim">
          <Select value={value.departmentId} onChange={(e) => set({ departmentId: e.target.value })}>
            <option value="">Barchasi</option>
            {(deps.data ?? []).map((d) => (
              <option key={d.id} value={d.id}>
                {d.name}
              </option>
            ))}
          </Select>
        </Field>
      )}
      {show.includes('employee') && (
        <Field label="Xodim">
          <Select value={value.employeeId} onChange={(e) => set({ employeeId: e.target.value })}>
            <option value="">Barchasi</option>
            {(emps.data?.items ?? []).map((e) => (
              <option key={e.id} value={e.id}>
                {e.fullName}
              </option>
            ))}
          </Select>
        </Field>
      )}
      {show.includes('device') && (
        <Field label="Qurilma">
          <Select value={value.deviceId} onChange={(e) => set({ deviceId: e.target.value })}>
            <option value="">Barchasi</option>
            {(devs.data?.items ?? []).map((d) => (
              <option key={d.id} value={d.id}>
                {d.displayName}
              </option>
            ))}
          </Select>
        </Field>
      )}
    </div>
  )
}
