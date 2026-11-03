import {
  Activity, AppWindow, ArrowRight, BarChart3, Bell, CheckCircle2, Clock, Database, Download, FileSearch,
  FileSpreadsheet, Gauge, History, LayoutDashboard, Lock, Menu, MonitorSmartphone, Server, Settings, Shield,
  ShieldCheck, SlidersHorizontal, UserCog, Users, Workflow, X, Zap,
} from 'lucide-react'
import { useEffect, useState, type ComponentType } from 'react'
import { Link } from 'react-router'
import { KoshiIcon, KoshiLogo } from '@/components/Brand'
import { useAuth } from '@/lib/auth'
import { AGENT_INSTALLER } from '@/lib/labels'

type Icon = ComponentType<{ className?: string }>

const stats: [string, string][] = [
  ['13', 'Boshqaruv bo‘limi'],
  ['2–5s', 'Monitoring intervali'],
  ['TLS', 'Shifrlangan aloqa'],
  ['24/7', 'Offline navbat bilan'],
]

const features: { icon: Icon; title: string; text: string }[] = [
  { icon: Activity, title: 'Jonli monitoring', text: 'Faol dastur, maskalangan oyna nomi, faol/idle holat va agent aloqasi real vaqtda. Har bir qurilmaning aniq online/offline vaqti va davomiyligi ko‘rinadi.' },
  { icon: SlidersHorizontal, title: 'Dastur siyosatlari', text: 'Ruxsat etilgan va taqiqlangan ilovalar, kunlik vaqt limitlari. Audit-only rejimda sinab, keyin AppLocker orqali haqiqiy cheklashga o‘ting.' },
  { icon: BarChart3, title: 'Unumdorlik tahlili', text: 'Tushunarli, sozlanadigan formula. Data coverage va ishonchlilik darajasi bilan — ma’lumot yetarli bo‘lmasa, natija “ishonchsiz” deb belgilanadi.' },
  { icon: MonitorSmartphone, title: 'Qurilma boshqaruvi', text: 'Bir martalik aktivatsiya kodi, credential’ni bekor qilish, bloklash, o‘chirish, masofaviy boshqaruv va versiyalarni kuzatish.' },
  { icon: UserCog, title: 'Rollar va ruxsatlar', text: 'Super Admin, IT Admin, HR Manager, Viewer va o‘zingiz yaratgan rollar. Har bir ruxsat server tomonida tekshiriladi — frontend yashirish emas.' },
  { icon: FileSearch, title: 'Audit jurnali', text: 'Kim, qachon, qaysi qurilma yoki siyosatni o‘zgartirgani o‘chirib bo‘lmaydigan jurnalda saqlanadi. IP, vaqt va natija bilan.' },
  { icon: Zap, title: 'Avtomatik yangilanish', text: 'Yangi versiya chiqqanda agent uni o‘zi yuklab, SHA-256 bilan tekshirib, jim o‘rnatadi. Administrator istalgan vaqt o‘chirib qo‘yishi mumkin.' },
  { icon: Bell, title: 'Ogohlantirishlar', text: 'Aloqa uzilishi, siyosat xatolari, cheklov buzilishi va xavfsizlik hodisalari darhol bildiriladi va tasdiqlanadi.' },
  { icon: Shield, title: 'Masofaviy boshqaruv', text: 'Ekranni real vaqtda ko‘rish va boshqarish — xodim ekranida ko‘rinadigan ogohlantirish bilan. Har bir sessiya auditga yoziladi.' },
]

const modules: { icon: Icon; name: string; text: string }[] = [
  { icon: LayoutDashboard, name: 'Boshqaruv paneli', text: 'Qurilmalar, onlayn holat, bugungi monitoring vaqti va KPI bir ekranda.' },
  { icon: Activity, name: 'Jonli kuzatuv', text: 'Real vaqtda faol dastur, oyna va agent holati.' },
  { icon: History, name: 'Faoliyat tarixi', text: 'Kun davomida dasturlar, bo‘sh va to‘xtatilgan holatlar tarixi.' },
  { icon: Users, name: 'Xodimlar', text: 'Xodimlar, bo‘limlar, filiallar va lavozimlar boshqaruvi.' },
  { icon: MonitorSmartphone, name: 'Qurilmalar', text: 'Agent holati, aktivatsiya, bloklash, masofaviy boshqaruv.' },
  { icon: AppWindow, name: 'Dasturlar boshqaruvi', text: 'Ruxsat/taqiq qoidalari, kunlik limitlar, tasnif.' },
  { icon: ShieldCheck, name: 'Siyosatlar', text: 'Monitoring jadvali, cheklovlar, versiyalash va orqaga qaytarish.' },
  { icon: Gauge, name: 'Unumdorlik', text: 'KPI, faol vaqt, tanaffuslar va tendensiyalar.' },
  { icon: FileSpreadsheet, name: 'Hisobotlar', text: 'Kunlik, haftalik, oylik hisobotlar va CSV eksport.' },
  { icon: Bell, name: 'Ogohlantirishlar', text: 'Aloqa, siyosat va xavfsizlik hodisalari.' },
  { icon: FileSearch, name: 'Audit jurnali', text: 'Administrator amallarining to‘liq jurnali.' },
  { icon: Settings, name: 'Sozlamalar', text: 'Saqlash muddati, formula, ish jadvallari va bildirishnomalar.' },
]

const steps: { n: string; icon: Icon; title: string; text: string }[] = [
  { n: '01', icon: Download, title: 'Agentni o‘rnating', text: 'KOSHI Agent (EXE) administrator sifatida o‘rnatiladi. Bir martalik aktivatsiya kodini kiriting — qurilma serverda ro‘yxatdan o‘tadi.' },
  { n: '02', icon: Server, title: 'Server qabul qiladi', text: 'Telemetriya TLS orqali keladi, deduplikatsiya qilinadi, siyosatlar tekshiriladi va natijalar auditga yoziladi. Aloqa uzilsa, ma’lumot lokal navbatda saqlanadi.' },
  { n: '03', icon: LayoutDashboard, title: 'Paneldan boshqaring', text: 'Xodimlar, qurilmalar, cheklovlar, KPI va hisobotlar — rol va ruxsatlarga qarab, real vaqtda bir joyda.' },
]

const principles = [
  'Klaviatura bosishlari, parollar, clipboard, kamera va mikrofon yig‘ilmaydi',
  'Xodim monitoring holatini tray indikatori orqali doim ko‘radi',
  'Oyna sarlavhalari siyosat bo‘yicha maskalanadi yoki umuman saqlanmaydi',
  'Idle vaqt avtomatik ravishda “intizomsizlik” deb baholanmaydi',
  'Har bir administrator amali o‘chirib bo‘lmaydigan jurnalga yoziladi',
  'Faqat tashkilotga tegishli, boshqarishga vakolat berilgan qurilmalarda ishlaydi',
]

const faqs: [string, string][] = [
  ['Xodim monitoring borligini biladimi?', 'Ha. Agent har doim ko‘rinadigan tray indikatori va monitoring siyosati xabari bilan ishlaydi. Yashirin kuzatuv funksiyalari yo‘q.'],
  ['Qanday ma’lumot yig‘iladi?', 'Faol dastur nomi, faol/bo‘sh vaqt va (siyosat ruxsat bersa) maskalangan oyna sarlavhasi. Shaxsiy matn, parol, ovoz yoki video yig‘ilmaydi.'],
  ['Windows Home’da ishlaydimi?', 'Monitoring ishlaydi. Lekin dasturlarni haqiqiy bloklash (AppLocker) faqat Windows Enterprise/Education’da ishlaydi; boshqa nashrlarda natija “Unsupported” deb qayd etiladi.'],
  ['Aloqa uzilsa ma’lumot yo‘qoladimi?', 'Yo‘q. Agent ma’lumotni lokal navbatda saqlaydi va aloqa tiklanganda idempotency-key bilan takrorsiz yuboradi.'],
  ['Yangilanish qanday bo‘ladi?', 'Administrator ruxsat bersa, agent yangi versiyani o‘zi yuklab, SHA-256 bilan tekshirib, jim o‘rnatadi. Aktivatsiya saqlanadi.'],
]

function NavLinks({ onClick, className = '' }: { onClick?: () => void; className?: string }) {
  return (
    <>
      <a href="#features" onClick={onClick} className={className}>Imkoniyatlar</a>
      <a href="#modules" onClick={onClick} className={className}>Bo‘limlar</a>
      <a href="#how" onClick={onClick} className={className}>Qanday ishlaydi</a>
      <a href="#download" onClick={onClick} className={className}>Yuklab olish</a>
      <a href="#faq" onClick={onClick} className={className}>Savollar</a>
    </>
  )
}

interface AgentRelease {
  version: string
  buildDate: string
  fileName: string
  size: number
}

export function LandingPage() {
  const { user } = useAuth()
  const [menuOpen, setMenuOpen] = useState(false)
  const [release, setRelease] = useState<AgentRelease | null>(null)
  const cta = user ? { to: '/app', label: 'Admin panelga o‘tish' } : { to: '/login', label: 'Admin panelga kirish' }

  useEffect(() => {
    document.documentElement.classList.remove('dark')
    fetch('/downloads/latest.json', { cache: 'no-store' })
      .then((r) => (r.ok ? r.json() : null))
      .then((r: AgentRelease | null) => setRelease(r))
      .catch(() => setRelease(null))
  }, [])

  const releaseLabel = release ? `Versiya ${release.version} · ${release.buildDate} · ${(release.size / 1_048_576).toFixed(0)} MB` : 'Windows 10/11 · 64-bit'

  return (
    <div className="min-h-full scroll-smooth bg-white text-slate-700">
      {/* ---------- Navbar ---------- */}
      <header className="sticky top-0 z-40 border-b border-slate-200/80 bg-white/90 backdrop-blur-xl">
        <div className="mx-auto flex h-16 max-w-7xl items-center justify-between gap-4 px-4 sm:px-6 lg:px-8">
          <Link to="/" className="flex items-center" aria-label="KOSHI bosh sahifa">
            <KoshiLogo className="h-8" />
          </Link>
          <nav className="hidden items-center gap-1 rounded-full border border-slate-200 bg-slate-50/80 p-1 text-sm font-medium text-slate-600 lg:flex">
            <NavLinks className="rounded-full px-4 py-1.5 transition hover:bg-white hover:text-brand-600 hover:shadow-sm" />
          </nav>
          <div className="flex items-center gap-2.5">
            <a href={AGENT_INSTALLER} download className="hidden items-center gap-2 rounded-full border border-slate-200 px-4 py-2 text-sm font-medium text-slate-700 transition hover:border-brand-300 hover:bg-brand-50 hover:text-brand-700 sm:inline-flex">
              <Download className="size-4" /> Agent (EXE)
            </a>
            <Link to={cta.to} className="brand-gradient inline-flex items-center gap-1.5 rounded-full px-5 py-2 text-sm font-semibold text-white shadow-lg shadow-brand-600/25 transition hover:-translate-y-0.5 hover:shadow-xl hover:shadow-brand-600/30">
              {user ? 'Panel' : 'Kirish'} <ArrowRight className="size-4" />
            </Link>
            <button onClick={() => setMenuOpen((v) => !v)} className="rounded-xl border border-slate-200 p-2 text-slate-600 transition hover:bg-slate-50 lg:hidden" aria-label="Menyu">
              {menuOpen ? <X className="size-5" /> : <Menu className="size-5" />}
            </button>
          </div>
        </div>
        {menuOpen && (
          <nav className="flex flex-col gap-1 border-t border-slate-200 bg-white px-4 py-3 text-sm font-medium text-slate-700 lg:hidden">
            <NavLinks onClick={() => setMenuOpen(false)} className="rounded-lg px-3 py-2 hover:bg-slate-50" />
            <a href={AGENT_INSTALLER} download onClick={() => setMenuOpen(false)} className="mt-1 inline-flex items-center gap-2 rounded-lg px-3 py-2 text-brand-600 hover:bg-brand-50">
              <Download className="size-4" /> Agentni yuklab olish (EXE)
            </a>
          </nav>
        )}
      </header>

      {/* ---------- Hero ---------- */}
      <section className="mesh-light relative overflow-hidden">
        <div className="relative mx-auto grid max-w-7xl items-center gap-12 px-4 py-16 sm:px-6 lg:grid-cols-[1.1fr_0.9fr] lg:gap-10 lg:px-8 lg:py-28">
          <div>
            <span className="inline-flex items-center gap-2 rounded-full border border-brand-200 bg-brand-50 px-3 py-1 text-xs font-medium text-brand-700">
              <ShieldCheck className="size-3.5" /> Enterprise · Windows · Shaffof monitoring
            </span>
            <h1 className="mt-6 text-4xl font-bold leading-[1.08] tracking-tight text-slate-900 sm:text-5xl lg:text-6xl">
              Korporativ kompyuterlarni <span className="brand-text">markazdan boshqaring</span>
            </h1>
            <p className="mt-6 max-w-xl text-lg leading-relaxed text-slate-600">
              KOSHI — xodimlarga biriktirilgan Windows kompyuterlarni monitoring qilish, dastur siyosatlarini qo‘llash va mehnat unumdorligini ishonchli tahlil qilish uchun yagona, shaffof platforma. Agent, server va panel — hammasi haqiqiy kod bilan bog‘langan.
            </p>
            <div className="mt-8 flex flex-col gap-3 sm:flex-row sm:flex-wrap">
              <a href={AGENT_INSTALLER} download className="brand-gradient inline-flex items-center justify-center gap-2 rounded-xl px-6 py-3.5 font-semibold text-white shadow-xl shadow-brand-600/25 transition hover:brightness-110">
                <Download className="size-5" /> Agentni yuklab olish (EXE)
              </a>
              <Link to={cta.to} className="inline-flex items-center justify-center gap-2 rounded-xl border border-slate-300 bg-white px-6 py-3.5 font-medium text-slate-800 transition hover:border-brand-300 hover:bg-brand-50">
                {cta.label} <ArrowRight className="size-4" />
              </Link>
            </div>
            <dl className="mt-10 grid max-w-xl grid-cols-2 gap-3 sm:grid-cols-4">
              {stats.map(([v, k]) => (
                <div key={k} className="rounded-2xl border border-slate-200 bg-white p-3 text-center shadow-sm">
                  <dt className="brand-text text-xl font-bold">{v}</dt>
                  <dd className="mt-1 text-[11px] leading-tight text-slate-500">{k}</dd>
                </div>
              ))}
            </dl>
          </div>
          <div className="relative">
            <div className="absolute inset-0 rounded-[2.5rem] bg-gradient-to-br from-brand-500/20 to-accent-500/20 blur-2xl" />
            <div className="relative rounded-[2.5rem] border border-slate-200 bg-white p-8 shadow-2xl shadow-brand-900/10 sm:p-10">
              <div className="flex justify-center">
                <KoshiIcon className="size-36 drop-shadow-[0_20px_45px_rgba(99,102,241,0.35)] sm:size-44" />
              </div>
              <div className="mt-8 grid grid-cols-3 gap-3 text-center">
                {[
                  [MonitorSmartphone, 'Agent', 'Windows EXE'],
                  [Server, 'Server', 'ASP.NET Core'],
                  [Database, 'Baza', 'PostgreSQL'],
                ].map(([Ic, k, v]) => {
                  const I = Ic as Icon
                  return (
                    <div key={k as string} className="rounded-2xl border border-slate-200 bg-slate-50 p-3">
                      <I className="mx-auto size-5 text-brand-600" />
                      <p className="mt-2 text-[11px] text-slate-500">{k as string}</p>
                      <p className="text-sm font-semibold text-slate-900">{v as string}</p>
                    </div>
                  )
                })}
              </div>
            </div>
          </div>
        </div>
      </section>

      {/* ---------- Features ---------- */}
      <section id="features" className="mx-auto max-w-7xl scroll-mt-20 px-4 py-16 sm:px-6 lg:px-8 lg:py-24">
        <div className="mx-auto max-w-2xl text-center">
          <p className="text-sm font-semibold uppercase tracking-wider text-brand-600">Imkoniyatlar</p>
          <h2 className="mt-3 text-3xl font-bold text-slate-900 sm:text-4xl">Bitta platformada hammasi</h2>
          <p className="mt-4 text-slate-600">Admin panelidagi har bir tugma haqiqiy API va Windows mexanizmlariga bog‘langan — maketlar emas, ishlaydigan funksiyalar.</p>
        </div>
        <div className="mt-14 grid gap-5 sm:grid-cols-2 lg:grid-cols-3">
          {features.map(({ icon: Icon, title, text }) => (
            <div key={title} className="group rounded-3xl border border-slate-200 bg-white p-6 shadow-sm transition hover:-translate-y-1 hover:border-brand-300 hover:shadow-xl hover:shadow-brand-900/5">
              <span className="brand-gradient inline-flex rounded-2xl p-3 text-white shadow-lg shadow-brand-600/20">
                <Icon className="size-5" />
              </span>
              <h3 className="mt-5 text-lg font-semibold text-slate-900">{title}</h3>
              <p className="mt-2 text-sm leading-relaxed text-slate-600">{text}</p>
            </div>
          ))}
        </div>
      </section>

      {/* ---------- Modules ---------- */}
      <section id="modules" className="scroll-mt-20 border-y border-slate-200 bg-slate-50">
        <div className="mx-auto max-w-7xl px-4 py-16 sm:px-6 lg:px-8 lg:py-24">
          <div className="mx-auto max-w-2xl text-center">
            <p className="text-sm font-semibold uppercase tracking-wider text-brand-600">Admin panel</p>
            <h2 className="mt-3 text-3xl font-bold text-slate-900 sm:text-4xl">13 ta boshqaruv bo‘limi</h2>
            <p className="mt-4 text-slate-600">Har bir bo‘lim rol va ruxsatlarga qarab ko‘rinadi. Ma’lumot doirasi bo‘lim darajasida cheklanishi mumkin.</p>
          </div>
          <div className="mt-14 grid gap-4 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4">
            {modules.map(({ icon: Icon, name, text }) => (
              <div key={name} className="flex gap-3 rounded-2xl border border-slate-200 bg-white p-5 shadow-sm transition hover:border-brand-300 hover:shadow-md">
                <span className="flex size-10 shrink-0 items-center justify-center rounded-xl bg-brand-50 text-brand-600">
                  <Icon className="size-5" />
                </span>
                <div>
                  <h3 className="font-semibold text-slate-900">{name}</h3>
                  <p className="mt-1 text-xs leading-relaxed text-slate-600">{text}</p>
                </div>
              </div>
            ))}
          </div>
        </div>
      </section>

      {/* ---------- How it works ---------- */}
      <section id="how" className="scroll-mt-20 bg-white">
        <div className="mx-auto max-w-7xl px-4 py-16 sm:px-6 lg:px-8 lg:py-24">
          <div className="mx-auto max-w-2xl text-center">
            <p className="text-sm font-semibold uppercase tracking-wider text-brand-600">Jarayon</p>
            <h2 className="mt-3 text-3xl font-bold text-slate-900 sm:text-4xl">Qanday ishlaydi</h2>
            <p className="mt-4 text-slate-600">Uch qadam: o‘rnatish, serverga ulanish, paneldan boshqarish.</p>
          </div>
          <div className="mt-14 grid gap-6 md:grid-cols-3">
            {steps.map(({ n, icon: Icon, title, text }) => (
              <div key={n} className="relative overflow-hidden rounded-3xl border border-slate-200 bg-white p-7 shadow-sm transition hover:-translate-y-1 hover:border-brand-300 hover:shadow-xl hover:shadow-brand-900/5">
                <Workflow className="pointer-events-none absolute -right-4 -top-4 size-24 text-slate-100" />
                <div className="relative flex items-center gap-3">
                  <span className="brand-gradient inline-flex rounded-2xl p-3 text-white shadow-lg shadow-brand-600/25">
                    <Icon className="size-5" />
                  </span>
                  <span className="brand-text text-4xl font-bold">{n}</span>
                </div>
                <h3 className="relative mt-5 text-lg font-semibold text-slate-900">{title}</h3>
                <p className="relative mt-2 text-sm leading-relaxed text-slate-600">{text}</p>
              </div>
            ))}
          </div>
        </div>
      </section>

      {/* ---------- Security ---------- */}
      <section id="privacy" className="scroll-mt-20 bg-white">
        <div className="mx-auto grid max-w-7xl items-center gap-12 px-4 py-16 sm:px-6 lg:grid-cols-2 lg:px-8 lg:py-24">
          <div>
            <p className="text-sm font-semibold uppercase tracking-wider text-brand-600">Xavfsizlik va maxfiylik</p>
            <h2 className="mt-3 text-3xl font-bold text-slate-900 sm:text-4xl">Shaffof, qonuniy va himoyalangan</h2>
            <p className="mt-4 text-slate-600">
              KOSHI faqat tashkilotga tegishli qurilmalarda, ichki siyosatga muvofiq ishlaydi. Monitoring xodimga oshkor — yashirin kuzatuv, keylogger, mikrofon yoki kamera josusligi funksiyalari yo‘q.
            </p>
            <div className="mt-6 flex flex-wrap gap-3">
              <span className="inline-flex items-center gap-2 rounded-xl border border-slate-200 bg-slate-50 px-3 py-2 text-xs font-medium text-slate-700"><Lock className="size-4 text-brand-600" /> TLS shifrlash</span>
              <span className="inline-flex items-center gap-2 rounded-xl border border-slate-200 bg-slate-50 px-3 py-2 text-xs font-medium text-slate-700"><ShieldCheck className="size-4 text-brand-600" /> RBAC server tomonida</span>
              <span className="inline-flex items-center gap-2 rounded-xl border border-slate-200 bg-slate-50 px-3 py-2 text-xs font-medium text-slate-700"><FileSearch className="size-4 text-brand-600" /> To‘liq audit</span>
            </div>
          </div>
          <ul className="space-y-3">
            {principles.map((p) => (
              <li key={p} className="flex items-start gap-3 rounded-2xl border border-slate-200 bg-slate-50 p-4 text-sm text-slate-700">
                <CheckCircle2 className="mt-0.5 size-5 shrink-0 text-brand-600" /> {p}
              </li>
            ))}
          </ul>
        </div>
      </section>

      {/* ---------- Download (dark band) ---------- */}
      <section id="download" className="scroll-mt-20 bg-slate-50">
        <div className="mx-auto max-w-7xl px-4 py-16 sm:px-6 lg:px-8 lg:py-24">
          <div className="relative overflow-hidden rounded-[2rem] bg-ink-950 p-8 text-center shadow-2xl sm:p-14">
            <div className="pointer-events-none absolute -right-20 -top-24 size-72 rounded-full bg-brand-500/25 blur-3xl" />
            <div className="pointer-events-none absolute -bottom-24 -left-20 size-72 rounded-full bg-accent-500/25 blur-3xl" />
            <span className="brand-gradient relative mx-auto inline-flex rounded-2xl p-3.5 text-white shadow-lg shadow-brand-600/30">
              <Download className="size-7" />
            </span>
            <h2 className="relative mt-5 text-3xl font-bold text-white sm:text-4xl">KOSHI Agentni yuklab oling</h2>
            <p className="relative mx-auto mt-4 max-w-2xl text-slate-300">
              Windows uchun o‘rnatuvchi (EXE). Administrator sifatida o‘rnatiladi, bir martalik aktivatsiya kodi bilan serverga ulanadi va kompyuter admin panelda paydo bo‘ladi. Keyingi versiyalar avtomatik yangilanadi.
            </p>
            <div className="relative mt-8 flex flex-col items-center justify-center gap-3 sm:flex-row">
              <a href={AGENT_INSTALLER} download className="brand-gradient inline-flex w-full items-center justify-center gap-2 rounded-xl px-7 py-4 text-base font-semibold text-white shadow-lg shadow-brand-600/30 transition hover:brightness-110 sm:w-auto">
                <Download className="size-5" /> Windows uchun yuklab olish
              </a>
              <span className="text-xs text-slate-400">{releaseLabel}</span>
            </div>
            <div className="relative mx-auto mt-8 grid max-w-3xl gap-3 text-left sm:grid-cols-3">
              {([
                [MonitorSmartphone, 'Administrator sifatida o‘rnating'],
                [Clock, 'Aktivatsiya kodini kiriting'],
                [CheckCircle2, 'Panelda onlayn ko‘rinadi'],
              ] as [Icon, string][]).map(([Icon, label], i) => (
                <div key={i} className="flex items-center gap-3 rounded-xl border border-white/10 bg-white/[0.05] p-3 text-sm text-slate-200">
                  <Icon className="size-5 shrink-0 text-brand-400" /> {label}
                </div>
              ))}
            </div>
          </div>
        </div>
      </section>

      {/* ---------- FAQ ---------- */}
      <section id="faq" className="scroll-mt-20 bg-white">
        <div className="mx-auto max-w-4xl px-4 py-16 sm:px-6 lg:px-8 lg:py-24">
          <div className="mx-auto max-w-2xl text-center">
            <p className="text-sm font-semibold uppercase tracking-wider text-brand-600">Savol-javob</p>
            <h2 className="mt-3 text-3xl font-bold text-slate-900 sm:text-4xl">Ko‘p beriladigan savollar</h2>
          </div>
          <div className="mt-12 space-y-3">
            {faqs.map(([q, a]) => (
              <details key={q} className="group rounded-2xl border border-slate-200 bg-white p-5 shadow-sm [&_summary]:cursor-pointer">
                <summary className="flex items-center justify-between gap-4 text-base font-semibold text-slate-900 marker:content-['']">
                  {q}
                  <ArrowRight className="size-4 shrink-0 text-brand-600 transition group-open:rotate-90" />
                </summary>
                <p className="mt-3 text-sm leading-relaxed text-slate-600">{a}</p>
              </details>
            ))}
          </div>
        </div>
      </section>

      {/* ---------- CTA ---------- */}
      <section className="bg-white">
        <div className="mx-auto max-w-7xl px-4 pb-16 sm:px-6 lg:px-8">
          <div className="brand-gradient relative flex flex-col items-center gap-5 overflow-hidden rounded-[2rem] p-10 text-center text-white shadow-2xl shadow-brand-900/20 sm:p-14">
            <h2 className="max-w-2xl text-3xl font-bold sm:text-4xl">Tashkilotingiz kompyuterlarini bugun markazlashtiring</h2>
            <p className="max-w-xl text-white/85">Agentni o‘rnating, aktivatsiya qiling va bir necha daqiqada jonli monitoring, siyosatlar va hisobotlarni ko‘ring.</p>
            <div className="flex flex-col gap-3 sm:flex-row">
              <a href={AGENT_INSTALLER} download className="inline-flex items-center justify-center gap-2 rounded-xl bg-white px-6 py-3.5 font-semibold text-brand-700 shadow-lg transition hover:bg-slate-50">
                <Download className="size-5" /> Agentni yuklab olish
              </a>
              <Link to={cta.to} className="inline-flex items-center justify-center gap-2 rounded-xl border border-white/40 px-6 py-3.5 font-medium text-white transition hover:bg-white/10">
                {cta.label} <ArrowRight className="size-4" />
              </Link>
            </div>
          </div>
        </div>
      </section>

      {/* ---------- Footer ---------- */}
      <footer className="bg-ink-950 text-slate-300">
        <div className="mx-auto grid max-w-7xl gap-8 px-4 py-12 sm:px-6 lg:grid-cols-4 lg:px-8">
          <div className="lg:col-span-2">
            <KoshiIcon className="size-9" />
            <p className="mt-4 max-w-sm text-sm text-slate-400">
              KOSHI — korporativ Windows kompyuterlar uchun shaffof monitoring va unumdorlik platformasi. Agent · ASP.NET Core · PostgreSQL.
            </p>
          </div>
          <div>
            <p className="text-sm font-semibold text-white">Platforma</p>
            <ul className="mt-3 space-y-2 text-sm text-slate-400">
              <li><a href="#features" className="hover:text-white">Imkoniyatlar</a></li>
              <li><a href="#modules" className="hover:text-white">Bo‘limlar</a></li>
              <li><a href="#how" className="hover:text-white">Qanday ishlaydi</a></li>
            </ul>
          </div>
          <div>
            <p className="text-sm font-semibold text-white">Boshlash</p>
            <ul className="mt-3 space-y-2 text-sm text-slate-400">
              <li><a href={AGENT_INSTALLER} download className="hover:text-white">Agent (EXE)</a></li>
              <li><Link to={cta.to} className="hover:text-white">{user ? 'Panel' : 'Kirish'}</Link></li>
              <li><a href="#faq" className="hover:text-white">FAQ</a></li>
            </ul>
          </div>
        </div>
        <div className="border-t border-white/10">
          <div className="mx-auto max-w-7xl px-4 py-6 text-center text-xs text-slate-500 sm:px-6 lg:px-8">
            © {new Date().getFullYear()} KOSHI Monitoring Platform · Barcha administrator amallari audit jurnaliga yoziladi
          </div>
        </div>
      </footer>
    </div>
  )
}
