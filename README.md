<p align="center"><img src="assets/brand/koshi-icon.png" width="96" alt="KOSHI"/></p>
<p align="center"><img src="assets/brand/koshi-logo.png" width="320" alt="KOSHI"/></p>

# KOSHI — Enterprise Windows Computer Monitoring Platform

KOSHI — tashkilotga tegishli Windows kompyuterlarni markazdan boshqarish, faol dastur va ish vaqtini kuzatish,
dastur siyosatlarini qo‘llash va unumdorlikni tahlil qilish platformasi. Monitoring shaffof: xodim tray
indikatori orqali holatni doim ko‘radi; keylogger, parol, clipboard, kamera/mikrofon yig‘ilmaydi.

| Qism | Texnologiya | Papka |
|---|---|---|
| Server API | ASP.NET Core 10, EF Core 10, PostgreSQL, SignalR, Serilog | `src/KOSHI.Api`, `src/KOSHI.Application`, `src/KOSHI.Domain`, `src/KOSHI.Infrastructure`, `src/KOSHI.Contracts` |
| Admin platforma | React 19, TypeScript (strict), Vite 8, Tailwind 4, TanStack Query, React Router, RHF + Zod, Recharts | `src/koshi-admin` |
| Windows agent | .NET 10, WPF tray + Windows Service | `src/KOSHI.Agent`, `src/KOSHI.Agent.Core`, `src/KOSHI.Agent.Service` |
| Installer | Inno Setup 6 | `deploy/installer` |

## Joriy holat

Ishlaydi (haqiqiy PostgreSQL bilan tekshirilgan):
- Admin login, PBKDF2 parol hash, 15 daqiqalik JWT, httpOnly refresh-token rotatsiyasi va qayta ishlatishni aniqlash, lockout.
- Rollar va ruxsatlar bazada: Super Admin har bir rolga ruxsatlarni panel orqali beradi/oladi, maxsus rollar yaratadi; o‘zgarish darhol server tomonida kuchga kiradi.
- Qurilmalar: bir martalik aktivatsiya kodi, qurilma credential’i, heartbeat, onlayn/offlayn, bloklash, revoke.
- Telemetriya: batch qabul qilish, idempotency-key deduplikatsiya, kunlik agregatsiya, timeline, retention.
- Policy engine: CRUD, versiyalash, audit-only/enforce (aniq tasdiq bilan), xavfli qoidalarni rad etuvchi validator, rollback, natija holatlari.
- Hisobotlar: kunlik/haftalik/oylik/unumdorlik, coverage va ishonchlilik, CSV eksport (CSV injection himoyasi).
- Live Monitoring SignalR orqali real vaqtda; audit jurnali; ogohlantirishlar.
- Admin platforma: ommaviy bosh sahifa + 13 bo‘lim (Dashboard, Live, Timeline, Xodimlar, Qurilmalar, App Management, Policies, Productivity, Reports, Alerts, Audit, Roles & Permissions, Settings).

Ishlanmoqda (keyingi bosqich): agent monitoring yadrosi (faol oyna, idle, offline navbat), masofaviy boshqaruv
(xodimga ko‘rinadigan banner bilan), Windows Service + AppLocker adapteri, installer, server deploy, test to‘plami.

## Lokal ishga tushirish (Windows)

Talablar: .NET SDK 10, Node.js 22+, PostgreSQL 14+.

```powershell
. .\scripts\dev-env.ps1          # secret'larni repo'dan tashqarida yaratadi va env'ni yuklaydi
dotnet tool restore
dotnet run --project src/KOSHI.Api --launch-profile http     # http://localhost:5044, Swagger: /swagger
cd src/koshi-admin; npm ci; npm run dev                       # http://localhost:5173
```

Birinchi Super Admin `KOSHI_Bootstrap__AdminEmail` / `KOSHI_Bootstrap__AdminPassword` orqali, faqat foydalanuvchilar jadvali bo‘sh bo‘lganda yaratiladi.

## Xavfsizlik

- Secret’lar faqat environment orqali (`.env.example` ga qarang); repo’da haqiqiy qiymatlar yo‘q.
- Har bir endpoint server tomonida ruxsat bilan himoyalangan; frontend’dagi yashirish faqat qulaylik uchun.
- Rate limiting (login, aktivatsiya, agent), CORS allowlist, xavfsizlik headerlari, ProblemDetails xatolari.
- Batafsil: `docs/SECURITY.md` (tayyorlanmoqda).
