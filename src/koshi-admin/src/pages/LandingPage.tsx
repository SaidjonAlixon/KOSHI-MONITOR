import {
  Activity, ArrowRight, BarChart3, BellRing, CheckCircle2, Clock, Download, Eye, FileSearch, Lock, Menu,
  MonitorSmartphone, ShieldCheck, SlidersHorizontal, Users, X,
} from 'lucide-react'
import { useEffect, useState, type ComponentType } from 'react'
import { Link } from 'react-router'
import { KoshiIcon, KoshiLogo } from '@/components/Brand'
import { useAuth } from '@/lib/auth'
import { AGENT_INSTALLER } from '@/lib/labels'

const features: { icon: ComponentType<{ className?: string }>; title: string; text: string }[] = [
  { icon: Activity, title: 'Live Monitoring', text: 'Faol dastur, maskalangan oyna nomi, faol/idle holat va agent aloqasi real vaqtda — aniq online/offline vaqti bilan.' },
  { icon: SlidersHorizontal, title: 'Dastur siyosatlari', text: 'Ruxsat etilgan va taqiqlangan ilovalar, kunlik limitlar, audit-only va AppLocker orqali haqiqiy cheklash.' },
  { icon: BarChart3, title: 'Unumdorlik tahlili', text: 'Tushunarli formula, data coverage va ishonchlilik darajasi bilan kunlik, haftalik va oylik KPI.' },
  { icon: MonitorSmartphone, title: 'Qurilmalar', text: 'Bir martalik aktivatsiya kodi, credential’ni bekor qilish, bloklash, masofaviy boshqaruv va versiyalar.' },
  { icon: Users, title: 'Rollar va ruxsatlar', text: 'Super Admin, IT Admin, HR, Viewer va o‘zingiz yaratgan rollar — har bir ruxsat server tomonida tekshiriladi.' },
  { icon: FileSearch, title: 'Audit jurnali', text: 'Kim, qachon, qaysi qurilma yoki siyosatni o‘zgartirgani o‘chirib bo‘lmaydigan jurnalda saqlanadi.' },
]

const stats = [
  ['13', 'Boshqaruv bo‘limi'],
  ['3', 'Qatlam: Agent · Server · Baza'],
  ['TLS', 'Shifrlangan aloqa'],
  ['24/7', 'Offline navbat bilan'],
]

const steps = [
  ['01', 'Xodim kompyuteri', 'KOSHI Agent (EXE) o‘rnatiladi va bir martalik kod bilan aktivatsiya qilinadi. Tray indikatori monitoring holatini doim ko‘rsatadi.'],
  ['02', 'Markaziy server', 'Telemetriya TLS orqali qabul qilinadi, deduplikatsiya qilinadi, siyosatlar tekshiriladi va natijalar auditga yoziladi.'],
  ['03', 'Administrator paneli', 'Xodimlar, qurilmalar, cheklovlar, KPI va hisobotlar — rol va ruxsatlarга qarab, real vaqtda.'],
]

const principles = [
  'Klaviatura bosishlari, parollar, clipboard, kamera va mikrofon yig‘ilmaydi',
  'Xodim monitoring holatini tray indikatori orqali doim ko‘radi',
  'Oyna sarlavhalari siyosat bo‘yicha maskalanadi yoki umuman saqlanmaydi',
  'Idle vaqt avtomatik ravishda “intizomsizlik” deb baholanmaydi',
  'Har bir administrator amali audit jurnaliga yoziladi',
]

function NavLinks({ onClick, className = '' }: { onClick?: () => void; className?: string }) {
  return (
    <>
      <a href="#features" onClick={onClick} className={className}>Imkoniyatlar</a>
      <a href="#how" onClick={onClick} className={className}>Qanday ishlaydi</a>
      <a href="#download" onClick={onClick} className={className}>Yuklab olish</a>
      <a href="#privacy" onClick={onClick} className={className}>Maxfiylik</a>
    </>
  )
}

export function LandingPage() {
  const { user } = useAuth()
  const [menuOpen, setMenuOpen] = useState(false)
  const cta = user ? { to: '/app', label: 'Admin panelga o‘tish' } : { to: '/login', label: 'Admin panelga kirish' }

  useEffect(() => {
    document.documentElement.classList.remove('dark')
  }, [])

  return (
    <div className="min-h-full scroll-smooth bg-ink-950 text-slate-200">
      {/* ---------- Navbar ---------- */}
      <header className="sticky top-0 z-40 border-b border-slate-200 bg-white/95 shadow-sm backdrop-blur">
        <div className="mx-auto flex h-16 max-w-7xl items-center justify-between gap-4 px-4 sm:px-6 lg:px-8">
          <Link to="/" className="flex items-center" aria-label="KOSHI bosh sahifa">
            <KoshiLogo className="h-8" />
          </Link>
          <nav className="hidden items-center gap-8 text-sm font-medium text-slate-600 lg:flex">
            <NavLinks className="transition hover:text-brand-700" />
          </nav>
          <div className="flex items-center gap-2">
            <a href={AGENT_INSTALLER} download className="hidden items-center gap-2 rounded-xl border border-slate-200 px-3.5 py-2 text-sm font-medium text-slate-700 transition hover:border-brand-300 hover:bg-brand-50 sm:inline-flex">
              <Download className="size-4" /> Agent (EXE)
            </a>
            <Link to={cta.to} className="brand-gradient rounded-xl px-4 py-2 text-sm font-semibold text-white shadow-lg shadow-brand-600/30 transition hover:brightness-110">
              {user ? 'Panel' : 'Kirish'}
            </Link>
            <button onClick={() => setMenuOpen((v) => !v)} className="rounded-lg p-2 text-slate-600 lg:hidden" aria-label="Menyu">
              {menuOpen ? <X className="size-5" /> : <Menu className="size-5" />}
            </button>
          </div>
        </div>
        {menuOpen && (
          <nav className="flex flex-col gap-1 border-t border-slate-200 bg-white px-4 py-3 text-sm font-medium text-slate-700 lg:hidden">
            <NavLinks onClick={() => setMenuOpen(false)} className="rounded-lg px-3 py-2 hover:bg-slate-50" />
            <a href={AGENT_INSTALLER} download onClick={() => setMenuOpen(false)} className="mt-1 inline-flex items-center gap-2 rounded-lg px-3 py-2 text-brand-700 hover:bg-brand-50">
              <Download className="size-4" /> Agentni yuklab olish (EXE)
            </a>
          </nav>
        )}
      </header>

      {/* ---------- Hero ---------- */}
      <section className="relative overflow-hidden">
        <div className="pointer-events-none absolute -left-40 -top-24 size-[36rem] rounded-full bg-brand-500/20 blur-3xl" />
        <div className="pointer-events-none absolute -right-40 top-32 size-[32rem] rounded-full bg-accent-500/20 blur-3xl" />
        <div className="relative mx-auto grid max-w-7xl items-center gap-12 px-4 py-16 sm:px-6 lg:grid-cols-2 lg:gap-8 lg:px-8 lg:py-28">
          <div>
            <span className="inline-flex items-center gap-2 rounded-full border border-white/10 bg-white/5 px-3 py-1 text-xs text-slate-300">
              <ShieldCheck className="size-3.5 text-brand-400" /> Enterprise · Windows · Shaffof monitoring
            </span>
            <h1 className="mt-6 text-4xl font-bold leading-[1.1] tracking-tight text-white sm:text-5xl lg:text-6xl">
              Korporativ kompyuterlarni <span className="brand-text">markazdan boshqaring</span>
            </h1>
            <p className="mt-6 max-w-xl text-lg leading-relaxed text-slate-400">
              KOSHI — xodimlarga biriktirilgan Windows kompyuterlarni monitoring qilish, dastur siyosatlarini qo‘llash va mehnat unumdorligini ishonchli tahlil qilish platformasi.
            </p>
            <div className="mt-8 flex flex-col gap-3 sm:flex-row sm:flex-wrap">
              <a href={AGENT_INSTALLER} download className="brand-gradient inline-flex items-center justify-center gap-2 rounded-xl px-6 py-3.5 font-semibold text-white shadow-lg shadow-brand-600/30 transition hover:brightness-110">
                <Download className="size-5" /> Agentni yuklab olish (EXE)
              </a>
              <Link to={cta.to} className="inline-flex items-center justify-center gap-2 rounded-xl border border-white/10 bg-white/5 px-6 py-3.5 font-medium text-slate-100 transition hover:bg-white/10">
                {cta.label} <ArrowRight className="size-4" />
              </Link>
            </div>
            <dl className="mt-10 grid max-w-xl grid-cols-2 gap-4 sm:grid-cols-4">
              {stats.map(([v, k]) => (
                <div key={k} className="rounded-2xl border border-white/10 bg-white/[0.03] p-3 text-center">
                  <dt className="brand-text text-xl font-bold">{v}</dt>
                  <dd className="mt-1 text-[11px] leading-tight text-slate-400">{k}</dd>
                </div>
              ))}
            </dl>
          </div>
          <div className="relative">
            <div className="absolute inset-0 rounded-[2.5rem] bg-gradient-to-br from-brand-500/30 to-accent-500/30 blur-2xl" />
            <div className="relative rounded-[2.5rem] border border-white/10 bg-white/[0.03] p-8 backdrop-blur sm:p-10">
              <div className="flex justify-center">
                <KoshiIcon className="size-36 drop-shadow-[0_20px_45px_rgba(47,128,255,0.45)] sm:size-44" />
              </div>
              <div className="mt-8 grid grid-cols-3 gap-3 text-center">
                {[
                  ['Agent', 'Windows EXE'],
                  ['Server', 'ASP.NET Core'],
                  ['Baza', 'PostgreSQL'],
                ].map(([k, v]) => (
                  <div key={k} className="rounded-2xl border border-white/10 bg-white/5 p-3">
                    <p className="text-[11px] text-slate-400">{k}</p>
                    <p className="mt-1 text-sm font-semibold text-white">{v}</p>
                  </div>
                ))}
              </div>
            </div>
          </div>
        </div>
      </section>

      {/* ---------- Features ---------- */}
      <section id="features" className="mx-auto max-w-7xl scroll-mt-20 px-4 py-16 sm:px-6 lg:px-8 lg:py-24">
        <div className="mx-auto max-w-2xl text-center">
          <h2 className="text-3xl font-bold text-white sm:text-4xl">Bitta platformada hammasi</h2>
          <p className="mt-4 text-slate-400">Admin panelidagi har bir tugma haqiqiy API va Windows mexanizmlariga bog‘langan — maketlar emas.</p>
        </div>
        <div className="mt-14 grid gap-5 sm:grid-cols-2 lg:grid-cols-3">
          {features.map(({ icon: Icon, title, text }) => (
            <div key={title} className="group rounded-3xl border border-white/10 bg-white/[0.03] p-6 transition hover:-translate-y-1 hover:border-brand-500/40 hover:bg-white/[0.06]">
              <span className="brand-gradient inline-flex rounded-2xl p-3 text-white shadow-lg shadow-brand-600/20">
                <Icon className="size-5" />
              </span>
              <h3 className="mt-5 text-lg font-semibold text-white">{title}</h3>
              <p className="mt-2 text-sm leading-relaxed text-slate-400">{text}</p>
            </div>
          ))}
        </div>
      </section>

      {/* ---------- How it works ---------- */}
      <section id="how" className="scroll-mt-20 border-y border-white/5 bg-white/[0.02]">
        <div className="mx-auto max-w-7xl px-4 py-16 sm:px-6 lg:px-8 lg:py-24">
          <div className="mx-auto max-w-2xl text-center">
            <h2 className="text-3xl font-bold text-white sm:text-4xl">Qanday ishlaydi</h2>
            <p className="mt-4 text-slate-400">Uch qadam: o‘rnatish, serverga ulanish, panelдан boshqarish.</p>
          </div>
          <div className="mt-14 grid gap-6 md:grid-cols-3">
            {steps.map(([n, t, d]) => (
              <div key={n} className="relative rounded-3xl border border-white/10 bg-ink-900 p-7">
                <span className="brand-text text-5xl font-bold">{n}</span>
                <h3 className="mt-4 text-lg font-semibold text-white">{t}</h3>
                <p className="mt-2 text-sm leading-relaxed text-slate-400">{d}</p>
              </div>
            ))}
          </div>
        </div>
      </section>

      {/* ---------- Download ---------- */}
      <section id="download" className="mx-auto max-w-7xl scroll-mt-20 px-4 py-16 sm:px-6 lg:px-8 lg:py-24">
        <div className="relative overflow-hidden rounded-[2rem] border border-white/10 bg-gradient-to-br from-brand-600/20 via-ink-900 to-accent-600/20 p-8 text-center sm:p-14">
          <div className="pointer-events-none absolute -right-20 -top-24 size-72 rounded-full bg-brand-500/20 blur-3xl" />
          <div className="pointer-events-none absolute -bottom-24 -left-20 size-72 rounded-full bg-accent-500/20 blur-3xl" />
          <span className="brand-gradient relative mx-auto inline-flex rounded-2xl p-3.5 text-white shadow-lg shadow-brand-600/30">
            <Download className="size-7" />
          </span>
          <h2 className="relative mt-5 text-3xl font-bold text-white sm:text-4xl">KOSHI Agentni yuklab oling</h2>
          <p className="relative mx-auto mt-4 max-w-2xl text-slate-300">
            Windows uchun o‘rnatuvchi (EXE). Administrator sifatida o‘rnatiladi, bir martalik aktivatsiya kodi bilan serverga ulanadi va kompyuter admin panelda paydo bo‘ladi.
          </p>
          <div className="relative mt-8 flex flex-col items-center justify-center gap-3 sm:flex-row">
            <a href={AGENT_INSTALLER} download className="brand-gradient inline-flex w-full items-center justify-center gap-2 rounded-xl px-7 py-4 text-base font-semibold text-white shadow-lg shadow-brand-600/30 transition hover:brightness-110 sm:w-auto">
              <Download className="size-5" /> Windows uchun yuklab olish
            </a>
            <span className="text-xs text-slate-400">Windows 10/11 · 64-bit · ~77 MB</span>
          </div>
          <div className="relative mx-auto mt-8 grid max-w-3xl gap-3 text-left sm:grid-cols-3">
            {[
              [MonitorSmartphone, 'Administrator sifatida o‘rnating'],
              [Clock, 'Aktivatsiya kodini kiriting'],
              [CheckCircle2, 'Panelда onlayn ko‘rinadi'],
            ].map(([Icon, label], i) => {
              const Ic = Icon as ComponentType<{ className?: string }>
              return (
                <div key={i} className="flex items-center gap-3 rounded-xl border border-white/10 bg-white/[0.04] p-3 text-sm text-slate-200">
                  <Ic className="size-5 shrink-0 text-brand-400" /> {label as string}
                </div>
              )
            })}
          </div>
        </div>
      </section>

      {/* ---------- Privacy ---------- */}
      <section id="privacy" className="mx-auto max-w-7xl scroll-mt-20 px-4 py-16 sm:px-6 lg:px-8 lg:py-24">
        <div className="grid items-center gap-10 rounded-[2rem] border border-white/10 bg-gradient-to-br from-ink-900 to-ink-950 p-8 sm:p-12 lg:grid-cols-2">
          <div>
            <span className="inline-flex rounded-2xl bg-white/5 p-3">
              <Lock className="size-6 text-brand-400" />
            </span>
            <h2 className="mt-5 text-2xl font-bold text-white sm:text-3xl">Xavfsizlik va maxfiylik birinchi o‘rinda</h2>
            <p className="mt-4 text-slate-400">
              KOSHI faqat tashkilotga tegishli qurilmalarda, ichki siyosatga muvofiq ishlaydi. Yashirin kuzatuv funksiyalari yo‘q — monitoring xodimga oshkor.
            </p>
            <Link to={cta.to} className="mt-6 inline-flex items-center gap-2 rounded-xl border border-white/10 bg-white/5 px-5 py-3 font-medium text-slate-100 transition hover:bg-white/10">
              {cta.label} <ArrowRight className="size-4" />
            </Link>
          </div>
          <ul className="space-y-3">
            {principles.map((p) => (
              <li key={p} className="flex items-start gap-3 rounded-2xl border border-white/5 bg-white/[0.03] p-4 text-sm text-slate-200">
                <Eye className="mt-0.5 size-4 shrink-0 text-brand-400" /> {p}
              </li>
            ))}
            <li className="flex items-start gap-3 rounded-2xl border border-white/5 bg-white/[0.03] p-4 text-sm text-slate-200">
              <BellRing className="mt-0.5 size-4 shrink-0 text-brand-400" /> Aloqa uzilishi va siyosat xatolari bo‘yicha ogohlantirishlar
            </li>
          </ul>
        </div>
      </section>

      {/* ---------- Footer ---------- */}
      <footer className="border-t border-white/5">
        <div className="mx-auto flex max-w-7xl flex-col items-center justify-between gap-4 px-4 py-10 sm:flex-row sm:px-6 lg:px-8">
          <div className="flex items-center gap-3">
            <KoshiIcon className="size-8" />
            <span className="text-sm text-slate-500">© {new Date().getFullYear()} KOSHI Monitoring Platform</span>
          </div>
          <div className="flex items-center gap-5 text-sm text-slate-400">
            <a href={AGENT_INSTALLER} download className="transition hover:text-white">Agent (EXE)</a>
            <a href="#privacy" className="transition hover:text-white">Maxfiylik</a>
            <Link to={cta.to} className="transition hover:text-white">{user ? 'Panel' : 'Kirish'}</Link>
          </div>
        </div>
      </footer>
    </div>
  )
}
