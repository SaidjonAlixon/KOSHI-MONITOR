/** Served by the API from the admin-managed release store (falls back to the bundled installer). */
export const AGENT_INSTALLER = '/api/agent/release/download'

export const PERMISSION_LABELS: Record<string, string> = {
  'dashboard.read': 'Dashboardni ko‘rish',
  'devices.read': 'Qurilmalarni ko‘rish',
  'devices.manage': 'Qurilmalarni boshqarish (qo‘shish, bloklash, bekor qilish)',
  'monitoring.read': 'Faoliyat tarixi va dastur statistikasi',
  'monitoring.live': 'Live Monitoring (faol dastur va oyna)',
  'policies.read': 'Siyosatlarni ko‘rish',
  'policies.manage': 'Siyosat va dastur cheklovlarini boshqarish',
  'reports.read': 'Hisobotlarni ko‘rish',
  'reports.individual': 'Xodim darajasidagi hisobotlar',
  'reports.export': 'Hisobotlarni eksport qilish',
  'employees.read': 'Xodimlarni ko‘rish',
  'employees.manage': 'Xodimlar va bo‘limlarni boshqarish',
  'audit.read': 'Audit jurnalini ko‘rish',
  'alerts.read': 'Ogohlantirishlarni ko‘rish',
  'alerts.manage': 'Ogohlantirishlarni tasdiqlash',
  'users.manage': 'Administratorlarni boshqarish',
  'settings.read': 'Sozlamalarni ko‘rish',
  'settings.manage': 'Sozlamalarni o‘zgartirish',
  'support.chat': 'Xodimlar bilan yordam chati',
  'remote.control': 'Masofaviy boshqaruv (ekran, buyruqlar)',
  'location.read': 'Kompyuterlar joylashuvi (xarita va tarix)',
  'data.unscoped': 'Barcha bo‘limlar ma’lumotini ko‘rish',
  'roles.manage': 'Rollar va ruxsatlarni boshqarish (faqat Super Admin)',
}

export const PERMISSION_GROUPS: Record<string, string> = {
  dashboard: 'Dashboard',
  devices: 'Qurilmalar',
  monitoring: 'Monitoring',
  policies: 'Siyosatlar',
  reports: 'Hisobotlar',
  employees: 'Xodimlar',
  audit: 'Audit',
  alerts: 'Ogohlantirishlar',
  users: 'Foydalanuvchilar',
  settings: 'Sozlamalar',
  support: 'Yordam chati',
  data: 'Ma’lumot doirasi',
  roles: 'Rollar',
  remote: 'Masofaviy boshqaruv',
  location: 'Joylashuv',
}

export const LOCATION_SOURCE_LABELS = {
  gps: 'GPS (aniq)',
  wifi: 'Wi-Fi (aniq)',
  cell: 'Mobil tarmoq',
  windows: 'Windows joylashuv',
  ip: 'IP bo‘yicha (taxminiy)',
} as const

export const CLASSIFICATION_LABELS = { Approved: 'Tasdiqlangan', NotApproved: 'Tasdiqlanmagan', Neutral: 'Neytral', Mixed: 'Aralash', Unclassified: 'Tasniflanmagan' } as const
export const MODE_LABELS = { AuditOnly: 'Audit-only (faqat qayd)', Enforce: 'Enforcement (cheklash)' } as const
export const CAPTURE_LABELS = { None: 'Saqlanmaydi', Masked: 'Maskalangan', Full: 'To‘liq' } as const
export const CONFIDENCE_LABELS = { High: 'Yuqori', Medium: 'O‘rta', Low: 'Past', None: 'Ma’lumot yo‘q' } as const
export const ALERT_LABELS = { DeviceOffline: 'Aloqa uzildi', PolicyFailed: 'Siyosat xatosi', PolicyViolation: 'Cheklov buzildi', TelemetryGap: 'Telemetriya bo‘shlig‘i', Security: 'Xavfsizlik' } as const
export const WEEKDAYS = [
  ['Monday', 'Du'],
  ['Tuesday', 'Se'],
  ['Wednesday', 'Ch'],
  ['Thursday', 'Pa'],
  ['Friday', 'Ju'],
  ['Saturday', 'Sh'],
  ['Sunday', 'Ya'],
] as const
