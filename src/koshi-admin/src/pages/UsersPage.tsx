import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Lock, Pencil, Plus, Save, ShieldCheck, Trash2, UserPlus } from 'lucide-react'
import { useEffect, useMemo, useState, type FormEvent } from 'react'
import { Checkbox, Field, Input, Modal, PageHeader, Pagination, Select, tableClass, tbodyClass, td, th, theadClass, useDepartments } from '@/components/forms'
import { Badge, Button, Card, CardHeader, EmptyState, ErrorState, LoadingState, formatDateTime } from '@/components/ui'
import { ApiError, api } from '@/lib/api'
import { Permissions, useAuth } from '@/lib/auth'
import { PERMISSION_GROUPS, PERMISSION_LABELS } from '@/lib/labels'
import type { AdminUser, PagedResult, PermissionInfo, Role } from '@/lib/types'

function UserForm({ user, roles, onDone }: { user: AdminUser | null; roles: Role[]; onDone: () => void }) {
  const qc = useQueryClient()
  const deps = useDepartments()
  const [selectedRoles, setSelectedRoles] = useState<string[]>(user?.roles ?? ['Viewer'])
  const save = useMutation({
    mutationFn: (f: FormData) => {
      const common = { displayName: String(f.get('displayName')).trim(), roles: selectedRoles, departmentScopeId: String(f.get('departmentScopeId')) || null }
      const password = String(f.get('password') ?? '')
      return user
        ? api(`/api/users/${user.id}`, { method: 'PUT', body: { ...common, isActive: f.get('isActive') === 'true', newPassword: password || null } })
        : api('/api/users', { method: 'POST', body: { ...common, email: String(f.get('email')).trim(), password } })
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['users'] })
      qc.invalidateQueries({ queryKey: ['roles'] })
      onDone()
    },
  })
  const submit = (e: FormEvent<HTMLFormElement>) => {
    e.preventDefault()
    save.mutate(new FormData(e.currentTarget))
  }
  const errors = save.error instanceof ApiError ? Object.values(save.error.errors).flat() : []

  return (
    <form onSubmit={submit} className="grid gap-4 sm:grid-cols-2">
      <Field label="E-mail">
        <Input name="email" type="email" required disabled={!!user} defaultValue={user?.email} />
      </Field>
      <Field label="Ism">
        <Input name="displayName" required maxLength={120} defaultValue={user?.displayName} />
      </Field>
      <Field label={user ? 'Yangi parol (ixtiyoriy)' : 'Parol'} hint="Kamida 12 belgi, katta/kichik harf, raqam yoki belgi">
        <Input name="password" type="password" autoComplete="new-password" minLength={12} required={!user} />
      </Field>
      <Field label="Ma’lumot doirasi" hint="HR/Viewer faqat shu bo‘lim ma’lumotini ko‘radi">
        <Select name="departmentScopeId" defaultValue={user?.departmentScopeId ?? ''}>
          <option value="">Cheklanmagan</option>
          {(deps.data ?? []).map((d) => (
            <option key={d.id} value={d.id}>
              {d.name}
            </option>
          ))}
        </Select>
      </Field>
      {user && (
        <Field label="Holat">
          <Select name="isActive" defaultValue={String(user.isActive)}>
            <option value="true">Faol</option>
            <option value="false">O‘chirilgan (barcha sessiyalar bekor qilinadi)</option>
          </Select>
        </Field>
      )}
      <div className="sm:col-span-2">
        <p className="mb-2 text-sm font-medium">Rollar</p>
        <div className="grid gap-2 sm:grid-cols-2">
          {roles.map((r) => (
            <Checkbox
              key={r.id}
              label={
                <span>
                  <span className="font-medium">{r.name}</span> <span className="text-xs text-slate-500">— {r.description || `${r.permissions.length} ta ruxsat`}</span>
                </span>
              }
              checked={selectedRoles.includes(r.name)}
              onChange={() => setSelectedRoles((s) => (s.includes(r.name) ? s.filter((x) => x !== r.name) : [...s, r.name]))}
            />
          ))}
        </div>
      </div>
      {save.isError && (
        <div className="rounded-xl bg-rose-50 p-3 text-sm text-rose-700 sm:col-span-2 dark:bg-rose-500/10 dark:text-rose-300">
          {(save.error as Error).message}
          {errors.length > 0 && (
            <ul className="mt-1 list-disc pl-5 text-xs">
              {errors.map((e) => (
                <li key={e}>{e}</li>
              ))}
            </ul>
          )}
        </div>
      )}
      <div className="flex justify-end gap-2 sm:col-span-2">
        <Button type="button" variant="secondary" onClick={onDone}>
          Bekor qilish
        </Button>
        <Button type="submit" loading={save.isPending} disabled={selectedRoles.length === 0}>
          Saqlash
        </Button>
      </div>
    </form>
  )
}

function RolesEditor({ roles, catalog }: { roles: Role[]; catalog: PermissionInfo[] }) {
  const { can } = useAuth()
  const qc = useQueryClient()
  const editable = can(Permissions.RolesManage)
  const [selectedId, setSelectedId] = useState(roles[0]?.id ?? '')
  const role = roles.find((r) => r.id === selectedId) ?? roles[0]
  const [perms, setPerms] = useState<string[]>(role?.permissions ?? [])
  const [description, setDescription] = useState(role?.description ?? '')
  const [newName, setNewName] = useState('')

  useEffect(() => {
    setPerms(role?.permissions ?? [])
    setDescription(role?.description ?? '')
  }, [role])

  const groups = useMemo(() => {
    const map = new Map<string, PermissionInfo[]>()
    catalog.forEach((p) => map.set(p.group, [...(map.get(p.group) ?? []), p]))
    return [...map.entries()]
  }, [catalog])

  const invalidate = () => {
    qc.invalidateQueries({ queryKey: ['roles'] })
  }
  const save = useMutation({ mutationFn: () => api<Role>(`/api/roles/${role!.id}`, { method: 'PUT', body: { name: role!.name, description, permissions: perms } }), onSuccess: invalidate })
  const create = useMutation({
    mutationFn: () => api<Role>('/api/roles', { method: 'POST', body: { name: newName, description: '', permissions: ['dashboard.read'] } }),
    onSuccess: (r) => {
      setNewName('')
      setSelectedId(r.id)
      invalidate()
    },
  })
  const remove = useMutation({ mutationFn: () => api(`/api/roles/${role!.id}`, { method: 'DELETE' }), onSuccess: () => (setSelectedId(roles[0]?.id ?? ''), invalidate()) })

  if (!role) return <EmptyState title="Rollar yo‘q" />
  const locked = role.permissionsLocked || !editable
  const dirty = JSON.stringify([...perms].sort()) !== JSON.stringify([...role.permissions].sort()) || description !== role.description

  return (
    <div className="grid gap-6 lg:grid-cols-[280px_1fr]">
      <Card>
        <CardHeader title="Rollar" />
        <ul className="divide-y divide-slate-100 dark:divide-white/5">
          {roles.map((r) => (
            <li key={r.id}>
              <button onClick={() => setSelectedId(r.id)} className={`flex w-full items-center justify-between px-5 py-3 text-left text-sm ${r.id === role.id ? 'bg-brand-50/70 dark:bg-brand-500/10' : 'hover:bg-slate-50 dark:hover:bg-white/[0.03]'}`}>
                <span>
                  <span className="font-medium">{r.name}</span>
                  <span className="block text-xs text-slate-500">
                    {r.userCount} foydalanuvchi · {r.permissions.length} ruxsat
                  </span>
                </span>
                {r.isSystem && <Badge tone="gray">tizim</Badge>}
              </button>
            </li>
          ))}
        </ul>
        {editable && (
          <form
            className="flex gap-2 border-t border-slate-100 p-4 dark:border-white/5"
            onSubmit={(e) => {
              e.preventDefault()
              if (newName.trim()) create.mutate()
            }}
          >
            <Input placeholder="Yangi rol nomi (lotin)" value={newName} onChange={(e) => setNewName(e.target.value)} pattern="^[A-Za-z][A-Za-z0-9_\-]{2,49}$" />
            <Button type="submit" variant="secondary" loading={create.isPending} aria-label="Rol qo‘shish">
              <Plus className="size-4" />
            </Button>
          </form>
        )}
        {create.isError && <p className="px-4 pb-3 text-xs text-rose-600">{(create.error as Error).message}</p>}
      </Card>

      <Card>
        <CardHeader
          title={`${role.name} — ruxsatlar`}
          subtitle={role.permissionsLocked ? 'Super Admin har doim barcha ruxsatlarga ega (bloklanib qolmaslik uchun)' : 'Belgilangan ruxsatlar darhol server tomonida kuchga kiradi'}
          actions={
            editable && (
              <div className="flex gap-2">
                {!role.isSystem && (
                  <Button variant="ghost" onClick={() => confirm(`${role.name} roli o‘chirilsinmi?`) && remove.mutate()} loading={remove.isPending}>
                    <Trash2 className="size-4 text-rose-500" />
                  </Button>
                )}
                <Button onClick={() => save.mutate()} loading={save.isPending} disabled={!dirty}>
                  <Save className="size-4" /> Saqlash
                </Button>
              </div>
            )
          }
        />
        <div className="space-y-5 p-5">
          <Field label="Tavsif">
            <Input value={description} maxLength={300} disabled={!editable} onChange={(e) => setDescription(e.target.value)} />
          </Field>
          <div className="grid gap-4 md:grid-cols-2">
            {groups.map(([group, items]) => (
              <div key={group} className="rounded-xl border border-slate-100 p-4 dark:border-white/5">
                <p className="mb-2 text-xs font-semibold uppercase tracking-wide text-slate-500">{PERMISSION_GROUPS[group] ?? group}</p>
                <div className="space-y-2">
                  {items.map((p) => (
                    <Checkbox
                      key={p.key}
                      disabled={locked || !p.assignable}
                      checked={perms.includes(p.key)}
                      onChange={() => setPerms((s) => (s.includes(p.key) ? s.filter((x) => x !== p.key) : [...s, p.key]))}
                      label={
                        <span className="flex items-center gap-1">
                          {PERMISSION_LABELS[p.key] ?? p.key}
                          {!p.assignable && <Lock className="size-3 text-slate-400" />}
                          <span className="font-mono text-[10px] text-slate-400">{p.key}</span>
                        </span>
                      }
                    />
                  ))}
                </div>
              </div>
            ))}
          </div>
          {(save.isError || remove.isError) && <ErrorState error={save.error ?? remove.error} />}
        </div>
      </Card>
    </div>
  )
}

export function UsersPage() {
  const [tab, setTab] = useState<'users' | 'roles'>('users')
  const [page, setPage] = useState(1)
  const [search, setSearch] = useState('')
  const [editing, setEditing] = useState<AdminUser | 'new' | null>(null)
  const users = useQuery({
    queryKey: ['users', page, search],
    queryFn: ({ signal }) => api<PagedResult<AdminUser>>('/api/users', { query: { page, pageSize: 25, search }, signal }),
    placeholderData: keepPreviousData,
  })
  const roles = useQuery({ queryKey: ['roles'], queryFn: () => api<Role[]>('/api/roles') })
  const catalog = useQuery({ queryKey: ['permissions'], queryFn: () => api<PermissionInfo[]>('/api/permissions'), staleTime: Infinity })

  return (
    <>
      <PageHeader
        title="Roles & Permissions"
        subtitle="Administratorlar, ularning rollari va har bir rolga beriladigan ruxsatlar"
        actions={tab === 'users' && (
          <Button onClick={() => setEditing('new')}>
            <UserPlus className="size-4" /> Administrator qo‘shish
          </Button>
        )}
      />
      <div className="inline-flex rounded-xl border border-slate-200 bg-white p-1 dark:border-white/10 dark:bg-ink-900">
        {[
          ['users', 'Foydalanuvchilar'],
          ['roles', 'Rollar va ruxsatlar'],
        ].map(([k, v]) => (
          <button key={k} onClick={() => setTab(k as 'users' | 'roles')} className={`rounded-lg px-4 py-1.5 text-sm font-medium ${tab === k ? 'brand-gradient text-white' : 'text-slate-600 hover:bg-slate-100 dark:text-slate-300 dark:hover:bg-white/5'}`}>
            {v}
          </button>
        ))}
      </div>

      {tab === 'users' ? (
        <Card>
          <CardHeader
            title="Administratorlar"
            subtitle={users.data ? `${users.data.total} ta` : undefined}
            actions={<Input type="search" placeholder="Ism yoki e-mail…" className="w-56" defaultValue={search} onKeyDown={(e) => e.key === 'Enter' && (setSearch(e.currentTarget.value), setPage(1))} />}
          />
          {users.isPending ? (
            <LoadingState />
          ) : users.isError ? (
            <ErrorState error={users.error} onRetry={() => users.refetch()} />
          ) : users.data.items.length === 0 ? (
            <EmptyState title="Foydalanuvchilar topilmadi" />
          ) : (
            <table className={tableClass}>
              <thead className={theadClass}>
                <tr>
                  <th className={th}>Foydalanuvchi</th>
                  <th className={th}>Rollar</th>
                  <th className={th}>Holat</th>
                  <th className={th}>MFA</th>
                  <th className={th}>Oxirgi kirish</th>
                  <th className={th} />
                </tr>
              </thead>
              <tbody className={tbodyClass}>
                {users.data.items.map((u) => (
                  <tr key={u.id}>
                    <td className={td}>
                      <p className="font-medium">{u.displayName}</p>
                      <p className="text-xs text-slate-500">{u.email}</p>
                    </td>
                    <td className={td}>
                      <div className="flex flex-wrap gap-1">
                        {u.roles.map((r) => (
                          <Badge key={r} tone={r === 'SuperAdmin' ? 'red' : 'blue'}>
                            {r}
                          </Badge>
                        ))}
                      </div>
                    </td>
                    <td className={td}>
                      <Badge tone={u.isActive ? 'green' : 'gray'}>{u.isActive ? 'Faol' : 'O‘chirilgan'}</Badge>
                    </td>
                    <td className={td}>{u.mfaEnabled ? <ShieldCheck className="size-4 text-emerald-500" /> : <span className="text-xs text-slate-400">—</span>}</td>
                    <td className={`${td} text-xs`}>{formatDateTime(u.lastLoginAt)}</td>
                    <td className={td}>
                      <Button variant="ghost" onClick={() => setEditing(u)} title="Tahrirlash">
                        <Pencil className="size-4" />
                      </Button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
          {users.data && <Pagination page={page} pageSize={users.data.pageSize} total={users.data.total} onChange={setPage} />}
        </Card>
      ) : roles.isPending || catalog.isPending ? (
        <LoadingState />
      ) : roles.isError || catalog.isError ? (
        <ErrorState error={roles.error ?? catalog.error} />
      ) : (
        <RolesEditor roles={roles.data} catalog={catalog.data} />
      )}

      <Modal open={editing !== null} title={editing === 'new' ? 'Yangi administrator' : 'Administratorni tahrirlash'} onClose={() => setEditing(null)} wide>
        {editing !== null && roles.data && <UserForm user={editing === 'new' ? null : editing} roles={roles.data} onDone={() => setEditing(null)} />}
      </Modal>
    </>
  )
}
