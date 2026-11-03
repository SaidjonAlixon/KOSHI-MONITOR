import { zodResolver } from '@hookform/resolvers/zod'
import { Lock, Mail, ShieldCheck } from 'lucide-react'
import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { Navigate, useLocation, useNavigate } from 'react-router'
import { z } from 'zod'
import { KoshiIcon, KoshiLogo } from '@/components/Brand'
import { Button } from '@/components/ui'
import { ApiError } from '@/lib/api'
import { useAuth } from '@/lib/auth'

const schema = z.object({
  email: z.email('To‘g‘ri e-mail kiriting'),
  password: z.string().min(1, 'Parolni kiriting').max(256),
})

type FormValues = z.infer<typeof schema>

export function LoginPage() {
  const { user, login } = useAuth()
  const navigate = useNavigate()
  const location = useLocation()
  const [serverError, setServerError] = useState<string | null>(null)
  const { register, handleSubmit, formState } = useForm<FormValues>({ resolver: zodResolver(schema) })

  if (user) return <Navigate to="/app" replace />

  const onSubmit = handleSubmit(async ({ email, password }) => {
    setServerError(null)
    try {
      await login(email, password)
      const from = (location.state as { from?: string } | null)?.from ?? '/app'
      navigate(from, { replace: true })
    } catch (e) {
      if (e instanceof ApiError && e.code === 'account_locked') setServerError('Hisob vaqtincha bloklangan. Birozdan so‘ng qayta urinib ko‘ring.')
      else if (e instanceof ApiError && e.status === 429) setServerError('Juda ko‘p urinish. Bir daqiqadan so‘ng qayta urinib ko‘ring.')
      else if (e instanceof ApiError && e.status === 401) setServerError('E-mail yoki parol noto‘g‘ri.')
      else setServerError('Server bilan bog‘lanib bo‘lmadi.')
    }
  })

  return (
    <div className="relative flex min-h-full items-center justify-center overflow-hidden bg-ink-950 px-4 py-12">
      <div className="pointer-events-none absolute -left-32 -top-32 size-[28rem] rounded-full bg-brand-500/25 blur-3xl" />
      <div className="pointer-events-none absolute -bottom-40 -right-24 size-[30rem] rounded-full bg-accent-500/25 blur-3xl" />

      <div className="relative w-full max-w-md">
        <div className="rounded-3xl border border-white/10 bg-white p-8 shadow-2xl shadow-black/40 dark:bg-white">
          <div className="flex flex-col items-center text-center">
            <KoshiIcon className="size-16" />
            <KoshiLogo className="mt-4 h-9" />
            <p className="mt-3 text-sm text-slate-500">Korporativ kompyuterlar monitoringi va unumdorlik boshqaruvi</p>
          </div>

          <form onSubmit={onSubmit} className="mt-8 space-y-4 text-slate-900" noValidate>
            <div>
              <label htmlFor="email" className="text-sm font-medium">
                E-mail
              </label>
              <div className="relative mt-1">
                <Mail className="pointer-events-none absolute left-3 top-1/2 size-4 -translate-y-1/2 text-slate-400" />
                <input
                  id="email"
                  type="email"
                  autoComplete="username"
                  {...register('email')}
                  aria-invalid={!!formState.errors.email}
                  className="w-full rounded-xl border border-slate-200 py-2.5 pl-9 pr-3 text-sm outline-none focus:border-brand-500 focus:ring-2 focus:ring-brand-500/30"
                />
              </div>
              {formState.errors.email && <p className="mt-1 text-xs text-rose-600">{formState.errors.email.message}</p>}
            </div>
            <div>
              <label htmlFor="password" className="text-sm font-medium">
                Parol
              </label>
              <div className="relative mt-1">
                <Lock className="pointer-events-none absolute left-3 top-1/2 size-4 -translate-y-1/2 text-slate-400" />
                <input
                  id="password"
                  type="password"
                  autoComplete="current-password"
                  {...register('password')}
                  aria-invalid={!!formState.errors.password}
                  className="w-full rounded-xl border border-slate-200 py-2.5 pl-9 pr-3 text-sm outline-none focus:border-brand-500 focus:ring-2 focus:ring-brand-500/30"
                />
              </div>
              {formState.errors.password && <p className="mt-1 text-xs text-rose-600">{formState.errors.password.message}</p>}
            </div>
            {serverError && (
              <p role="alert" className="rounded-xl bg-rose-50 px-3 py-2 text-sm text-rose-700">
                {serverError}
              </p>
            )}
            <Button type="submit" loading={formState.isSubmitting} className="w-full py-2.5">
              Kirish
            </Button>
          </form>
        </div>
        <p className="mt-6 flex items-center justify-center gap-2 text-xs text-slate-400">
          <ShieldCheck className="size-4" /> Barcha administrator amallari audit jurnaliga yoziladi
        </p>
      </div>
    </div>
  )
}
