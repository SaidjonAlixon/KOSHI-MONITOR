import { Activity, ArrowRight, BarChart3, BellRing, Download, Eye, FileSearch, Lock, MonitorSmartphone, ShieldCheck, SlidersHorizontal, Users } from 'lucide-react'
import type { ComponentType } from 'react'
import { Link } from 'react-router'
import { KoshiIcon, KoshiLogo } from '@/components/Brand'
import { useAuth } from '@/lib/auth'
import { AGENT_INSTALLER } from '@/lib/labels'

const features: { icon: ComponentType<{ className?: string }>; title: string; text: string }[] = [
  { icon: Activity, title: 'Live Monitoring', text: 'Faol dastur, maskalangan oyna nomi, faol/idle holat va agent aloqasi real vaqtda.' },
  { icon: SlidersHorizontal, title: 'Dastur siyosatlari', text: 'Ruxsat etilgan va taqiqlangan ilovalar, kunlik limitlar, audit-only va AppLocker orqali cheklash.' },
  { icon: BarChart3, title: 'Unumdorlik tahlili', text: 'Tushunarli formula, coverage va ishonchlilik darajasi bilan kunlik, haftalik, oylik KPI.' },
  { icon: MonitorSmartphone, title: 'Qurilmalar', text: 'Bir martalik aktivatsiya kodi, credential’ni bekor qilish, bloklash va versiyalarni kuzatish.' },
  { icon: Users, title: 'Rollar va ruxsatlar', text: 'Super Admin, IT Admin, HR, Viewer va o‘zingiz yaratgan rollar — har bir ruxsat server tomonida tekshiriladi.' },
  { icon: FileSearch, title: 'Audit jurnali', text: 'Kim, qachon, qaysi qurilma yoki siyosatni o‘zgartirgani o‘chirib bo‘lmaydigan jurnalda saqlanadi.' },
]

const principles = [
  'Klaviatura bosishlari, parollar, clipboard, kamera va mikrofon yig‘ilmaydi',
  'Xodim monitoring holatini tray indikatori orqali doim ko‘radi',
  'Oyna sarlavhalari siyosat bo‘yicha maskalanadi yoki umuman saqlanmaydi',
  'Idle vaqt avtomatik ravishda “intizomsizlik” deb baholanmaydi',
]

export function LandingPage() {
  const { user } = useAuth()
  const cta = user ? { to: '/app', label: 'Admin panelga o‘tish' } : { to: '/login', label: 'Admin panelga kirish' }

  return (
    <div className="min-h-full bg-ink-950 text-slate-200">
      <header className="sticky top-0 z-30 border-b border-slate-200 bg-white shadow-sm">
        <div className="mx-auto flex h-16 max-w-6xl items-center justify-between gap-4 px-4 sm:px-6">
          <Link to="/" className="flex items-center" aria-label="KOSHI bosh sahifa">
            <KoshiLogo className="h-8" />
          </Link>
          <nav className="hidden items-center gap-8 text-sm font-medium text-slate-600 md:flex">
            <a href="#features" className="hover:text-brand-700">Imkoniyatlar</a>
            <a href="#how" className="hover:text-brand-700">Qanday ishlaydi</a>
            <a href="#download" className="hover:text-brand-700">Yuklab olish</a>
            <a href="#privacy" className="hover:text-brand-700">Maxfiylik</a>
          </nav>
          <div className="flex items-center gap-2">
            <a href={AGENT_INSTALLER} download className="hidden items-center gap-2 rounded-xl border border-slate-200 px-3.5 py-2 text-sm font-medium text-slate-700 hover:bg-slate-50 sm:inline-flex">
              <Download className="size-4" /> Agent (EXE)
            </a>
            <Link to={cta.to} className="brand-gradient rounded-xl px-4 py-2 text-sm font-medium text-white shadow-lg shadow-brand-600/30 hover:brightness-110">
              {user ? 'Panel' : 'Kirish'}
            </Link>
          </div>
        </div>
      </header>

      <section className="relative overflow-hidden">
        <div className="pointer-events-none absolute -left-40 top-0 size-[36rem] rounded-full bg-brand-500/20 blur-3xl" />
        <div className="pointer-events-none absolute -right-40 top-40 size-[32rem] rounded-full bg-accent-500/20 blur-3xl" />
        <div className="relative mx-auto grid max-w-6xl items-center gap-12 px-6 py-20 lg:grid-cols-2 lg:py-28">
          <div>
            <span className="inline-flex items-center gap-2 rounded-full border border-white/10 bg-white/5 px-3 py-1 text-xs text-slate-300">
              <ShieldCheck className="size-3.5 text-brand-400" /> Enterprise · Windows · Shaffof monitoring
            </span>
            <h1 className="mt-6 text-4xl font-bold leading-tight tracking-tight text-white sm:text-5xl">
              Korporativ kompyuterlarni <span className="brand-text">markazdan boshqaring</span>
            </h1>
            <p className="mt-5 max-w-xl text-lg text-slate-400">
              KOSHI — xodimlarga biriktirilgan Windows kompyuterlarni monitoring qilish, dastur siyosatlarini qo‘llash va mehnat unumdorligini ishonchli tahlil qilish platformasi.
            </p>
            <div className="mt-8 flex flex-wrap gap-3">
              <a href={AGENT_INSTALLER} download className="brand-gradient inline-flex items-center gap-2 rounded-xl px-5 py-3 font-medium text-white shadow-lg shadow-brand-600/30 hover:brightness-110">
                <Download className="size-4" /> Agentni yuklab olish (EXE)
              </a>
              <Link to={cta.to} className="inline-flex items-center gap-2 rounded-xl border border-white/10 bg-white/5 px-5 py-3 font-medium text-slate-100 hover:bg-white/10">
                {cta.label} <ArrowRight className="size-4" />
              </Link>
            </div>
          </div>
          <div className="relative">
            <div className="absolute inset-0 rounded-[2rem] bg-gradient-to-br from-brand-500/30 to-accent-500/30 blur-2xl" />
            <div className="relative rounded-[2rem] border border-white/10 bg-white/[0.03] p-8 backdrop-blur">
              <div className="flex justify-center">
                <KoshiIcon className="size-40 drop-shadow-[0_20px_40px_rgba(47,128,255,0.45)]" />
              </div>
              <div className="mt-8 grid grid-cols-3 gap-3 text-center">
                {[
                  ['Agent', 'Windows EXE'],
                  ['Server', 'ASP.NET Core'],
                  ['Baza', 'PostgreSQL'],
                ].map(([k, v]) => (
                  <div key={k} className="rounded-xl border border-white/10 bg-white/5 p-3">
                    <p className="text-xs text-slate-400">{k}</p>
                    <p className="mt-1 text-sm font-semibold text-white">{v}</p>
                  </div>
                ))}
              </div>
            </div>
          </div>
        </div>
      </section>

      <section id="features" className="mx-auto max-w-6xl px-6 py-20">
        <h2 className="text-center text-3xl font-bold text-white">Bitta platformada hammasi</h2>
        <p className="mx-auto mt-3 max-w-2xl text-center text-slate-400">Admin panelidagi har bir tugma haqiqiy API va Windows mexanizmlariga bog‘langan.</p>
        <div className="mt-12 grid gap-5 sm:grid-cols-2 lg:grid-cols-3">
          {features.map(({ icon: Icon, title, text }) => (
            <div key={title} className="group rounded-2xl border border-white/10 bg-white/[0.03] p-6 transition hover:border-brand-500/40 hover:bg-white/[0.05]">
              <span className="brand-gradient inline-flex rounded-xl p-2.5 text-white shadow-lg shadow-brand-600/20">
                <Icon className="size-5" />
              </span>
              <h3 className="mt-4 font-semibold text-white">{title}</h3>
              <p className="mt-2 text-sm leading-relaxed text-slate-400">{text}</p>
            </div>
          ))}
        </div>
      </section>

      <section id="how" className="border-y border-white/5 bg-white/[0.02]">
        <div className="mx-auto grid max-w-6xl gap-6 px-6 py-20 md:grid-cols-3">
          {[
            ['1', 'Xodim kompyuteri', 'KOSHI Agent o‘rnatiladi va bir martalik kod bilan aktivatsiya qilinadi. Tray indikatori monitoring holatini ko‘rsatadi.'],
            ['2', 'Markaziy server', 'Telemetriya TLS orqali qabul qilinadi, deduplikatsiya qilinadi, siyosatlar tekshiriladi va natijalar auditga yoziladi.'],
            ['3', 'Administrator paneli', 'Xodimlar, qurilmalar, cheklovlar, KPI va hisobotlar — rol va ruxsatlarga qarab.'],
          ].map(([n, t, d]) => (
            <div key={n} className="rounded-2xl border border-white/10 bg-ink-900 p-6">
              <span className="brand-text text-4xl font-bold">{n}</span>
              <h3 className="mt-3 font-semibold text-white">{t}</h3>
              <p className="mt-2 text-sm text-slate-400">{d}</p>
            </div>
          ))}
        </div>
      </section>

      <section id="download" className="mx-auto max-w-6xl px-6 py-20">
        <div className="relative overflow-hidden rounded-3xl border border-white/10 bg-gradient-to-br from-brand-600/20 via-ink-900 to-accent-600/20 p-10 text-center">
          <div className="pointer-events-none absolute -right-20 -top-20 size-64 rounded-full bg-brand-500/20 blur-3xl" />
          <span className="brand-gradient mx-auto inline-flex rounded-2xl p-3 text-white shadow-lg shadow-brand-600/30">
            <Download className="size-6" />
          </span>
          <h2 className="mt-4 text-2xl font-bold text-white">KOSHI Agentni yuklab oling</h2>
          <p className="mx-auto mt-3 max-w-2xl text-slate-400">
            Windows uchun o‘rnatuvchi (EXE). Administrator sifatida o‘rnatiladi, bir martalik aktivatsiya kodi bilan serverga ulanadi va kompyuter admin panelda paydo bo‘ladi.
          </p>
          <div className="mt-6 flex flex-wrap items-center justify-center gap-3">
            <a href={AGENT_INSTALLER} download className="brand-gradient inline-flex items-center gap-2 rounded-xl px-6 py-3 font-semibold text-white shadow-lg shadow-brand-600/30 hover:brightness-110">
              <Download className="size-5" /> Windows uchun yuklab olish
            </a>
            <span className="text-xs text-slate-500">Windows 10/11 · 64-bit · ~77 MB</span>
          </div>
          <p className="mx-auto mt-4 max-w-xl text-xs text-slate-500">
            Aktivatsiya kodini admin panelidagi “Qurilmalar → Qurilma qo‘shish” bo‘limidan oling. O‘rnatgandan so‘ng agent xodimga ko‘rinadigan tray indikatori bilan ishlaydi.
          </p>
        </div>
      </section>

      <section id="privacy" className="mx-auto max-w-6xl px-6 py-20">
        <div className="grid items-center gap-10 rounded-3xl border border-white/10 bg-gradient-to-br from-ink-900 to-ink-950 p-10 lg:grid-cols-2">
          <div>
            <span className="inline-flex rounded-xl bg-white/5 p-3">
              <Lock className="size-6 text-brand-400" />
            </span>
            <h2 className="mt-4 text-2xl font-bold text-white">Xavfsizlik va maxfiylik birinchi o‘rinda</h2>
            <p className="mt-3 text-slate-400">Faqat tashkilotga tegishli qurilmalarda, ichki siyosatga muvofiq ishlaydi. Yashirin kuzatuv funksiyalari yo‘q.</p>
          </div>
          <ul className="space-y-3">
            {principles.map((p) => (
              <li key={p} className="flex items-start gap-3 rounded-xl border border-white/5 bg-white/[0.03] p-3 text-sm">
                <Eye className="mt-0.5 size-4 shrink-0 text-brand-400" /> {p}
              </li>
            ))}
            <li className="flex items-start gap-3 rounded-xl border border-white/5 bg-white/[0.03] p-3 text-sm">
              <BellRing className="mt-0.5 size-4 shrink-0 text-brand-400" /> Har bir administrator amali audit jurnaliga yoziladi
            </li>
          </ul>
        </div>
      </section>

      <footer className="border-t border-white/5">
        <div className="mx-auto flex max-w-6xl flex-wrap items-center justify-between gap-4 px-6 py-8 text-sm text-slate-500">
          <div className="flex items-center gap-3">
            <KoshiIcon className="size-7" />
            <span>© {new Date().getFullYear()} KOSHI Monitoring Platform</span>
          </div>
          <Link to={cta.to} className="hover:text-white">
            {cta.label} →
          </Link>
        </div>
      </footer>
    </div>
  )
}
