import { zodResolver } from '@hookform/resolvers/zod'
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Building2, Pencil, Plus, Trash2 } from 'lucide-react'
import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { z } from 'zod'
import { Field, Input, Modal, PageHeader, Pagination, Select, SortHeader, tableClass, tbodyClass, td, th, theadClass, useDepartments, type SortState } from '@/components/forms'
import { Badge, Button, Card, CardHeader, EmptyState, ErrorState, LoadingState } from '@/components/ui'
import { api } from '@/lib/api'
import { Permissions, useAuth } from '@/lib/auth'
import type { Employee, PagedResult, WorkSchedule } from '@/lib/types'

const schema = z.object({
  fullName: z.string().trim().min(1, 'Majburiy').max(200),
  employeeNumber: z.string().trim().regex(/^[A-Za-z0-9_-]{1,50}$/, 'Faqat harf, raqam, - va _'),
  email: z.union([z.email('Noto‘g‘ri e-mail'), z.literal('')]),
  position: z.string().max(120),
  departmentId: z.string(),
  workScheduleId: z.string(),
  isActive: z.boolean(),
})
type FormValues = z.infer<typeof schema>

function EmployeeForm({ employee, onDone }: { employee: Employee | null; onDone: () => void }) {
  const qc = useQueryClient()
  const deps = useDepartments()
  const schedules = useQuery({ queryKey: ['schedules'], queryFn: () => api<WorkSchedule[]>('/api/schedules') })
  const { register, handleSubmit, formState } = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: {
      fullName: employee?.fullName ?? '',
      employeeNumber: employee?.employeeNumber ?? '',
      email: employee?.email ?? '',
      position: employee?.position ?? '',
      departmentId: employee?.departmentId ?? '',
      workScheduleId: employee?.workScheduleId ?? '',
      isActive: employee?.isActive ?? true,
    },
  })
  const save = useMutation({
    mutationFn: (v: FormValues) => {
      const body = { ...v, email: v.email || null, position: v.position || null, departmentId: v.departmentId || null, workScheduleId: v.workScheduleId || null }
      return employee ? api<Employee>(`/api/employees/${employee.id}`, { method: 'PUT', body }) : api<Employee>('/api/employees', { method: 'POST', body })
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['employees'] })
      qc.invalidateQueries({ queryKey: ['employees-lookup'] })
      onDone()
    },
  })

  return (
    <form id="employee-form" onSubmit={handleSubmit((v) => save.mutate(v))} className="grid gap-4 sm:grid-cols-2">
      <Field label="F.I.Sh." error={formState.errors.fullName?.message} className="sm:col-span-2">
        <Input {...register('fullName')} />
      </Field>
      <Field label="Tabel raqami" error={formState.errors.employeeNumber?.message}>
        <Input {...register('employeeNumber')} />
      </Field>
      <Field label="E-mail" error={formState.errors.email?.message}>
        <Input type="email" {...register('email')} />
      </Field>
      <Field label="Lavozim" hint="KPI faqat o‘xshash lavozimlar orasida solishtiriladi">
        <Input {...register('position')} />
      </Field>
      <Field label="Bo‘lim">
        <Select {...register('departmentId')}>
          <option value="">—</option>
          {(deps.data ?? []).map((d) => (
            <option key={d.id} value={d.id}>
              {d.name} {d.branch ? `(${d.branch})` : ''}
            </option>
          ))}
        </Select>
      </Field>
      <Field label="Ish jadvali" hint="Bo‘sh bo‘lsa — siyosat yoki standart jadval">
        <Select {...register('workScheduleId')}>
          <option value="">Standart</option>
          {(schedules.data ?? []).map((s) => (
            <option key={s.id} value={s.id}>
              {s.name}
            </option>
          ))}
        </Select>
      </Field>
      <Field label="Holat">
        <Select {...register('isActive', { setValueAs: (v) => v === true || v === 'true' })}>
          <option value="true">Faol</option>
          <option value="false">Bloklangan</option>
        </Select>
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
        <Button type="submit" loading={save.isPending}>
          Saqlash
        </Button>
      </div>
    </form>
  )
}

function Departments({ manage }: { manage: boolean }) {
  const qc = useQueryClient()
  const deps = useDepartments()
  const [name, setName] = useState('')
  const [branch, setBranch] = useState('')
  const add = useMutation({
    mutationFn: () => api('/api/departments', { method: 'POST', body: { name, branch: branch || null } }),
    onSuccess: () => {
      setName('')
      setBranch('')
      qc.invalidateQueries({ queryKey: ['departments'] })
    },
  })
  const remove = useMutation({
    mutationFn: (id: string) => api(`/api/departments/${id}`, { method: 'DELETE' }),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['departments'] }),
    onError: (e) => alert((e as Error).message),
  })
  return (
    <Card>
      <CardHeader title="Bo‘limlar va filiallar" subtitle={`${deps.data?.length ?? 0} ta bo‘lim`} />
      <ul className="divide-y divide-slate-100 dark:divide-white/5">
        {(deps.data ?? []).map((d) => (
          <li key={d.id} className="flex items-center justify-between gap-2 px-5 py-3 text-sm">
            <span className="flex min-w-0 items-center gap-2">
              <Building2 className="size-4 shrink-0 text-slate-400" /> <span className="truncate">{d.name}</span>
              {d.isDemo && <Badge tone="amber">DEMO</Badge>}
            </span>
            <span className="flex items-center gap-2">
              <span className="text-xs text-slate-500">
                {d.branch ?? '—'} · {d.employeeCount} xodim
              </span>
              {manage && (
                <button
                  className="shrink-0 rounded-lg p-1 text-slate-400 hover:text-rose-600"
                  title={`${d.name} bo‘limini o‘chirish`}
                  onClick={() => {
                    if (d.employeeCount > 0) {
                      alert(`“${d.name}” bo‘limida ${d.employeeCount} ta xodim bor. Avval ularni boshqa bo‘limga o‘tkazing, so‘ng o‘chiring.`)
                      return
                    }
                    if (confirm(`“${d.name}” bo‘limi o‘chirilsinmi?`)) remove.mutate(d.id)
                  }}
                >
                  <Trash2 className="size-4" />
                </button>
              )}
            </span>
          </li>
        ))}
        {deps.data?.length === 0 && <EmptyState title="Bo‘limlar yo‘q" />}
      </ul>
      {manage && (
        <form
          className="grid gap-2 border-t border-slate-100 p-4 dark:border-white/5"
          onSubmit={(e) => {
            e.preventDefault()
            if (name.trim()) add.mutate()
          }}
        >
          <Input placeholder="Bo‘lim nomi" value={name} onChange={(e) => setName(e.target.value)} maxLength={120} />
          <Input placeholder="Filial (ixtiyoriy)" value={branch} onChange={(e) => setBranch(e.target.value)} maxLength={120} />
          <Button type="submit" variant="secondary" loading={add.isPending}>
            <Plus className="size-4" /> Bo‘lim qo‘shish
          </Button>
          {add.isError && <p className="text-xs text-rose-600">{(add.error as Error).message}</p>}
        </form>
      )}
    </Card>
  )
}

export function EmployeesPage() {
  const { can } = useAuth()
  const qc = useQueryClient()
  const manage = can(Permissions.EmployeesManage)
  const [page, setPage] = useState(1)
  const [search, setSearch] = useState('')
  const [departmentId, setDepartmentId] = useState('')
  const [sort, setSort] = useState<SortState>({ by: 'fullName', dir: 'asc' })
  const [editing, setEditing] = useState<Employee | 'new' | null>(null)
  const deps = useDepartments()

  const q = useQuery({
    queryKey: ['employees', page, search, departmentId, sort],
    queryFn: ({ signal }) => api<PagedResult<Employee>>('/api/employees', { query: { page, pageSize: 25, search, departmentId, sortBy: sort.by, sortDir: sort.dir }, signal }),
    placeholderData: keepPreviousData,
  })
  const remove = useMutation({
    mutationFn: (id: string) => api(`/api/employees/${id}`, { method: 'DELETE' }),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['employees'] })
      qc.invalidateQueries({ queryKey: ['departments'] })
      qc.invalidateQueries({ queryKey: ['devices'] })
    },
    onError: (err) => alert(err instanceof Error ? err.message : 'Xodimni o‘chirib bo‘lmadi'),
  })

  return (
    <>
      <PageHeader
        title="Xodimlar"
        subtitle="Xodimlar, bo‘limlar, filiallar va lavozimlar. Har bir xodimga bir nechta kompyuter biriktirish mumkin."
        actions={manage && (
          <Button onClick={() => setEditing('new')}>
            <Plus className="size-4" /> Xodim qo‘shish
          </Button>
        )}
      />
      <div className="grid gap-6 xl:grid-cols-[1fr_320px]">
        <Card>
          <CardHeader
            title="Xodimlar ro‘yxati"
            subtitle={q.data ? `${q.data.total} ta` : undefined}
            actions={
              <div className="flex gap-2">
                <Input type="search" placeholder="Ism, raqam, lavozim…" defaultValue={search} onKeyDown={(e) => e.key === 'Enter' && (setSearch(e.currentTarget.value), setPage(1))} className="w-56" />
                <Select value={departmentId} onChange={(e) => (setDepartmentId(e.target.value), setPage(1))} className="w-44" aria-label="Bo‘lim">
                  <option value="">Barcha bo‘limlar</option>
                  {(deps.data ?? []).map((d) => (
                    <option key={d.id} value={d.id}>
                      {d.name}
                    </option>
                  ))}
                </Select>
              </div>
            }
          />
          {q.isPending ? (
            <LoadingState />
          ) : q.isError ? (
            <ErrorState error={q.error} onRetry={() => q.refetch()} />
          ) : q.data.items.length === 0 ? (
            <EmptyState title="Xodim topilmadi" hint="Yangi xodim qo‘shing yoki filtrlarni o‘zgartiring." />
          ) : (
            <div className="overflow-x-auto">
              <table className={tableClass}>
                <thead className={theadClass}>
                  <tr>
                    <SortHeader label="F.I.Sh." field="fullName" sort={sort} onSort={setSort} />
                    <SortHeader label="Tabel №" field="employeeNumber" sort={sort} onSort={setSort} />
                    <SortHeader label="Lavozim" field="position" sort={sort} onSort={setSort} />
                    <th className={th}>Bo‘lim</th>
                    <th className={th}>Qurilmalar</th>
                    <th className={th}>Holat</th>
                    {manage && <th className={`${th} text-right`}>Amallar</th>}
                  </tr>
                </thead>
                <tbody className={tbodyClass}>
                  {q.data.items.map((e) => (
                    <tr key={e.id}>
                      <td className={td}>
                        <p className="font-medium">{e.fullName}</p>
                        <p className="text-xs text-slate-500">{e.email ?? ''}</p>
                      </td>
                      <td className={`${td} font-mono text-xs`}>{e.employeeNumber}</td>
                      <td className={td}>{e.position ?? '—'}</td>
                      <td className={td}>{e.departmentName ?? '—'}</td>
                      <td className={td}>{e.deviceCount}</td>
                      <td className={td}>
                        <div className="flex gap-1">
                          <Badge tone={e.isActive ? 'green' : 'red'}>{e.isActive ? 'Faol' : 'Bloklangan'}</Badge>
                          {e.isDemo && <Badge tone="amber">DEMO</Badge>}
                        </div>
                      </td>
                      {manage && (
                        <td className={td}>
                          <div className="flex justify-end gap-1">
                            <Button variant="ghost" title="Tahrirlash" onClick={() => setEditing(e)}>
                              <Pencil className="size-4" />
                            </Button>
                            <Button
                              variant="ghost"
                              title="O‘chirish"
                              disabled={remove.isPending}
                              onClick={() => confirm(`${e.fullName} butunlay o‘chirilsinmi? Kompyuterlari o‘chirilmaydi, faqat xodimga biriktirilmagan holatga o‘tadi.`) && remove.mutate(e.id)}
                            >
                              <Trash2 className="size-4 text-rose-500" />
                            </Button>
                          </div>
                        </td>
                      )}
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
          {q.data && <Pagination page={page} pageSize={q.data.pageSize} total={q.data.total} onChange={setPage} />}
        </Card>
        <Departments manage={manage} />
      </div>
      <Modal open={editing !== null} title={editing === 'new' ? 'Yangi xodim' : 'Xodimni tahrirlash'} onClose={() => setEditing(null)} wide>
        {editing !== null && <EmployeeForm employee={editing === 'new' ? null : editing} onDone={() => setEditing(null)} />}
      </Modal>
    </>
  )
}
