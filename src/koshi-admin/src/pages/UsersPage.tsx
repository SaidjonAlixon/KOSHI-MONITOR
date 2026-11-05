import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Copy, Eye, EyeOff, Lock, Pencil, Plus, Save, ShieldCheck, Trash2, UserPlus } from 'lucide-react'
import { useEffect, useMemo, useState, type FormEvent } from 'react'
import { Checkbox, Field, Input, Modal, PageHeader, Pagination, Select, tableClass, tbodyClass, td, th, theadClass, useDepartments } from '@/components/forms'
import { Badge, Button, Card, CardHeader, EmptyState, ErrorState, LoadingState, formatDateTime } from '@/components/ui'
import { ApiError, api } from '@/lib/api'
import { Permissions, useAuth } from '@/lib/auth'
import { PERMISSION_GROUPS, PERMISSION_LABELS } from '@/lib/labels'
import type { AdminUser, PagedResult, PermissionInfo, Role } from '@/lib/types'

function randomInt(maxExclusive: number): number {
  const r = crypto.getRandomValues(new Uint32Array(1))[0] ?? 0
  return Math.floor((r / 2 ** 32) * maxExclusive)
}

function generateAdminPassword(): string {
  const lower = 'abcdefghijkmnpqrstuvwxyz'
  const upper = 'ABCDEFGHJKLMNPQRSTUVWXYZ'
  const digits = '23456789'
  const symbols = '!@#$%&*?'
  const pick = (set: string) => set.charAt(randomInt(set.length))
  const chars = [pick(lower), pick(upper), pick(digits), pick(symbols)]
  const all = lower + upper + digits + symbols
  while (chars.length < 14) chars.push(pick(all))
  for (let i = chars.length - 1; i > 0; i--) {
    const j = randomInt(i + 1)
    const tmp = chars[i]!
    chars[i] = chars[j]!
    chars[j] = tmp
  }
  return chars.join('')
}

function CredentialCell({ value, empty }: { value: string | null | undefined; empty: string }) {
  const [shown, setShown] = useState(true)
  if (!value) return <span className="text-xs text-slate-400">{empty}</span>
  return (
    <div className="flex items-center gap-1">
      <code className="max-w-40 truncate font-mono text-xs">{shown ? value : '••••••••'}</code>
      <Button type="button" variant="ghost" className="!p-1" title={shown ? 'Yashirish' : 'Ko‘rsatish'} onClick={() => setShown((s) => !s)}>
        {shown ? <EyeOff className="size-3.5" /> : <Eye className="size-3.5" />}
      </Button>
      <Button type="button" variant="ghost" className="!p-1" title="Nusxa olish" onClick={() => navigator.clipboard.writeText(value)}>
        <Copy className="size-3.5" />
      </Button>
    </div>
  )
}

function UserForm({ user, roles, onDone }: { user: AdminUser | null; roles: Role[]; onDone: () => void }) {
  const qc = useQueryClient()
  const { user: me } = useAuth()
  const isSuperAdmin = me?.roles.includes('SuperAdmin') ?? false
  const editingSuperAdmin = user?.roles.includes('SuperAdmin') ?? false
  const deps = useDepartments()
  const defaultRole = roles.find((r) => r.name === 'ITAdmin') ?? roles.find((r) => r.name === 'Viewer') ?? roles.find((r) => !r.permissionsLocked) ?? roles[0]
  const [selectedRoles, setSelectedRoles] = useState<string[]>(user?.roles ?? (defaultRole ? [defaultRole.name] : []))
  const [password, setPassword] = useState(user?.password ?? '')
  const [showPassword, setShowPassword] = useState(true)
  const save = useMutation({
    mutationFn: (f: FormData) => {
      const nextRoles = editingSuperAdmin
        ? selectedRoles.includes('SuperAdmin')
          ? selectedRoles
          : [...selectedRoles, 'SuperAdmin']
        : selectedRoles.filter((r) => r !== 'SuperAdmin')
      const common = { displayName: String(f.get('displayName')).trim(), roles: nextRoles, departmentScopeId: String(f.get('departmentScopeId')) || null }
      const passwordChanged = !!password && password !== (user?.password ?? '')
      return user
        ? api(`/api/users/${user.id}`, { method: 'PUT', body: { ...common, isActive: editingSuperAdmin ? true : f.get('isActive') === 'true', newPassword: passwordChanged ? password : null } })
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
    <form onSubmit={submit} autoComplete="off" className="grid gap-4 sm:grid-cols-2">
      <Field label="E-mail">
        <Input name="email" type="email" required disabled={!!user} defaultValue={user?.email} autoComplete="off" />
      </Field>
      <Field label="Ism">
        <Input name="displayName" required maxLength={120} defaultValue={user?.displayName} autoComplete="off" />
      </Field>
      <Field
        label={user ? (isSuperAdmin && user.password ? 'Parol (o‘zgartirish ixtiyoriy)' : 'Yangi parol (ixtiyoriy)') : 'Parol'}
        hint={
          isSuperAdmin && user?.password
            ? 'Super Admin joriy login va parolni ko‘radi. O‘zgartirish uchun yangi qiymat yozing.'
            : 'Kamida 12 belgi: katta va kichik harf, raqam yoki belgi. Ko‘zcha bilan ochiq ko‘rinadi.'
        }
        className="sm:col-span-2"
      >
        <div className="flex flex-wrap items-center gap-2">
          <div className="relative min-w-64 flex-1">
            <Input
              type={showPassword ? 'text' : 'password'}
              autoComplete="off"
              autoCorrect="off"
              spellCheck={false}
              minLength={user ? undefined : 12}
              required={!user}
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              placeholder={user ? 'o‘zgartirish uchun yangi parol' : 'parol ochiq ko‘rinadi'}
              className="w-full pr-11 font-mono tracking-wide"
            />
            <button
              type="button"
              onClick={() => setShowPassword((v) => !v)}
              className="absolute right-1.5 top-1/2 -translate-y-1/2 rounded-lg border border-slate-200 bg-white p-1.5 text-slate-600 hover:bg-slate-100 hover:text-slate-800 dark:border-white/10 dark:bg-white/10 dark:text-slate-200 dark:hover:bg-white/20"
              aria-label={showPassword ? 'Parolni yashirish' : 'Parolni ko‘rsatish'}
              title={showPassword ? 'Parolni yashirish' : 'Parolni ko‘rsatish'}
            >
              {showPassword ? <EyeOff className="size-4" /> : <Eye className="size-4" />}
            </button>
          </div>
          <Button
            type="button"
            variant="secondary"
            onClick={() => {
              setPassword(generateAdminPassword())
              setShowPassword(true)
            }}
          >
            Avtomatik yaratish
          </Button>
          {password && (
            <Button type="button" variant="ghost" onClick={() => navigator.clipboard.writeText(password)}>
              <Copy className="size-4" /> Nusxa olish
            </Button>
          )}
        </div>
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
          <Select name="isActive" defaultValue={String(editingSuperAdmin ? true : user.isActive)} disabled={editingSuperAdmin}>
            <option value="true">Faol</option>
            <option value="false">O‘chirilgan (barcha sessiyalar bekor qilinadi)</option>
          </Select>
          {editingSuperAdmin && <p className="mt-1 text-xs text-slate-500">Super Adminni o‘chirib yoki faolsizlantirib bo‘lmaydi.</p>}
        </Field>
      )}
      <div className="sm:col-span-2">
        <p className="mb-2 text-sm font-medium">Rollar</p>
        <p className="mb-2 text-xs text-slate-500">
          {editingSuperAdmin
            ? 'Super Admin roli olib tashlanmaydi.'
            : 'Admin deb belgilansa, odam kuzatuv va boshqaruvning hammasini qila oladi. Super Admin emas — parollarni faqat Super Admin ko‘radi.'}
        </p>
        <div className="grid gap-2 sm:grid-cols-2">
          {roles
            .filter((r) => r.name !== 'SuperAdmin' || editingSuperAdmin)
            .map((r) => {
              const lockedSuper = r.name === 'SuperAdmin'
              return (
                <Checkbox
                  key={r.id}
                  label={
                    <span>
                      <span className="font-medium">{r.name === 'ITAdmin' ? 'Admin' : r.name}</span>{' '}
                      <span className="text-xs text-slate-500">— {r.description || `${r.permissions.length} ta ruxsat`}</span>
                    </span>
                  }
                  checked={selectedRoles.includes(r.name)}
                  disabled={lockedSuper}
                  onChange={() => {
                    if (lockedSuper) return
                    setSelectedRoles((s) => (s.includes(r.name) ? s.filter((x) => x !== r.name) : [...s, r.name]))
                  }}
                />
              )
            })}
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
  const remove = useMutation({
    mutationFn: (id: string) => api(`/api/roles/${id}`, { method: 'DELETE' }),
    onSuccess: () => {
      setSelectedId(roles[0]?.id ?? '')
      invalidate()
    },
  })

  if (!role) return <EmptyState title="Rollar yo‘q" />
  const locked = role.permissionsLocked || !editable
  const dirty = JSON.stringify([...perms].sort()) !== JSON.stringify([...role.permissions].sort()) || description !== role.description

  return (
    <div className="grid gap-6 lg:grid-cols-[280px_1fr]">
      <Card>
        <CardHeader title="Rollar" />
        <ul className="divide-y divide-slate-100 dark:divide-white/5">
          {roles.map((r) => (
            <li key={r.id} className={`flex items-center ${r.id === role.id ? 'bg-brand-50/70 dark:bg-brand-500/10' : 'hover:bg-slate-50 dark:hover:bg-white/[0.03]'}`}>
              <button onClick={() => setSelectedId(r.id)} className="flex flex-1 items-center justify-between px-5 py-3 text-left text-sm">
                <span>
                  <span className="font-medium">{r.name}</span>
                  <span className="block text-xs text-slate-500">
                    {r.userCount} foydalanuvchi · {r.permissions.length} ruxsat
                  </span>
                </span>
                {r.isSystem && <Badge tone="gray">tizim</Badge>}
              </button>
              {editable && !r.permissionsLocked && (
                <button
                  className="px-3 text-slate-400 hover:text-rose-600"
                  title={`${r.name} rolini o‘chirish`}
                  onClick={() => {
                    if (r.userCount > 0) {
                      alert(`“${r.name}” roli ${r.userCount} ta foydalanuvchida bor. Avval ularga boshqa rol bering, so‘ng o‘chiring.`)
                      return
                    }
                    if (confirm(`“${r.name}” roli butunlay o‘chirilsinmi?`)) remove.mutate(r.id)
                  }}
                >
                  <Trash2 className="size-4" />
                </button>
              )}
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
                {!role.permissionsLocked && (
                  <Button
                    variant="ghost"
                    loading={remove.isPending}
                    onClick={() => {
                      if (role.userCount > 0) {
                        alert(`“${role.name}” roli ${role.userCount} ta foydalanuvchida bor. Avval ularga boshqa rol bering, so‘ng o‘chiring.`)
                        return
                      }
                      if (confirm(`“${role.name}” roli butunlay o‘chirilsinmi?`)) remove.mutate(role.id)
                    }}
                  >
                    <Trash2 className="size-4 text-rose-500" /> O‘chirish
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
  const qc = useQueryClient()
  const { user: currentUser } = useAuth()
  const isSuperAdmin = currentUser?.roles.includes('SuperAdmin') ?? false
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
  const removeUser = useMutation({
    mutationFn: (id: string) => api(`/api/users/${id}`, { method: 'DELETE' }),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['users'] }),
    onError: (e) => alert((e as Error).message),
  })

  return (
    <>
      <PageHeader
        title="Rollar va ruxsatlar"
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
                  {isSuperAdmin && <th className={th}>Login</th>}
                  {isSuperAdmin && <th className={th}>Parol</th>}
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
                      {!isSuperAdmin && <p className="text-xs text-slate-500">{u.email}</p>}
                    </td>
                    {isSuperAdmin && (
                      <td className={td}>
                        <CredentialCell value={u.email} empty="—" />
                      </td>
                    )}
                    {isSuperAdmin && (
                      <td className={td}>
                        <CredentialCell value={u.password} empty="Saqlanmagan — parolni yangilang" />
                      </td>
                    )}
                    <td className={td}>
                      <div className="flex flex-wrap gap-1">
                        {u.roles.map((r) => (
                          <Badge key={r} tone={r === 'SuperAdmin' ? 'red' : 'blue'}>
                            {r === 'ITAdmin' ? 'Admin' : r}
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
                      <div className="flex justify-end gap-1">
                        <Button variant="ghost" onClick={() => setEditing(u)} title="Tahrirlash">
                          <Pencil className="size-4" />
                        </Button>
                        {u.id !== currentUser?.id && !u.roles.includes('SuperAdmin') && (
                          <Button
                            variant="ghost"
                            title="O‘chirish"
                            loading={removeUser.isPending && removeUser.variables === u.id}
                            onClick={() => confirm(`“${u.displayName}” (${u.email}) administratori o‘chirilsinmi? Uning barcha sessiyalari bekor qilinadi.`) && removeUser.mutate(u.id)}
                          >
                            <Trash2 className="size-4 text-rose-500" />
                          </Button>
                        )}
                        {u.roles.includes('SuperAdmin') && (
                          <span title="Super Adminni o‘chirib bo‘lmaydi" className="inline-flex items-center px-1 text-slate-400">
                            <Lock className="size-4" />
                          </span>
                        )}
                      </div>
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
