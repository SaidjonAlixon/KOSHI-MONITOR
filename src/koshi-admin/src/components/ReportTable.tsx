import { ChevronDown, ChevronRight } from 'lucide-react'
import { Fragment, useState } from 'react'
import { tableClass, tbodyClass, td, th, theadClass } from '@/components/forms'
import { Badge, formatDuration } from '@/components/ui'
import { CONFIDENCE_LABELS } from '@/lib/labels'
import type { Report } from '@/lib/types'

const confTone = { High: 'green', Medium: 'blue', Low: 'amber', None: 'gray' } as const

export function ReportTable({ report }: { report: Report }) {
  const [open, setOpen] = useState<string | null>(null)
  const groupLabel = report.groupBy === 'department' ? 'Bo‘lim' : report.groupBy === 'device' ? 'Qurilma' : 'Xodim'
  return (
    <div className="overflow-x-auto">
      <table className={tableClass}>
        <thead className={theadClass}>
          <tr>
            <th className={th} />
            <th className={th}>{groupLabel}</th>
            {report.groupBy === 'employee' && <th className={th}>Lavozim</th>}
            <th className={th}>Rejadagi</th>
            <th className={th}>Faol</th>
            <th className={th}>Idle</th>
            <th className={th}>Tanaffus</th>
            <th className={th}>Jadvaldan tashqari</th>
            <th className={th}>Nomaʼlum</th>
            <th className={th}>Coverage</th>
            <th className={th}>KPI</th>
          </tr>
        </thead>
        <tbody className={tbodyClass}>
          {report.rows.map((r) => (
            <Fragment key={r.groupKey}>
              <tr className="cursor-pointer hover:bg-slate-50/60 dark:hover:bg-white/[0.02]" onClick={() => setOpen(open === r.groupKey ? null : r.groupKey)}>
                <td className={td}>{open === r.groupKey ? <ChevronDown className="size-4" /> : <ChevronRight className="size-4" />}</td>
                <td className={td}>
                  <p className="font-medium">{r.groupName}</p>
                  <p className="text-xs text-slate-500">
                    {r.departmentName ?? ''} · {r.deviceCount} qurilma
                  </p>
                </td>
                {report.groupBy === 'employee' && <td className={`${td} text-xs`}>{r.position ?? '—'}</td>}
                <td className={`${td} text-xs`}>{formatDuration(r.scheduledSeconds)}</td>
                <td className={`${td} text-xs`}>{formatDuration(r.activeSeconds)}</td>
                <td className={`${td} text-xs`}>{formatDuration(r.idleSeconds)}</td>
                <td className={`${td} text-xs`}>{formatDuration(r.scheduledBreakSeconds)}</td>
                <td className={`${td} text-xs`}>{formatDuration(r.outsideScheduleSeconds)}</td>
                <td className={`${td} text-xs`}>{formatDuration(r.unknownSeconds)}</td>
                <td className={td}>
                  <span className="tabular-nums">{Math.round(r.coverage * 100)}%</span>{' '}
                  <Badge tone={confTone[r.confidence]}>{CONFIDENCE_LABELS[r.confidence]}</Badge>
                </td>
                <td className={td}>
                  {r.productivityScore === null ? (
                    <span className="text-xs text-slate-400">ma’lumot yetarli emas</span>
                  ) : (
                    <span className="flex items-center gap-2">
                      <span className={`font-semibold tabular-nums ${r.isReliable ? '' : 'text-slate-400'}`}>{r.productivityScore}</span>
                      {!r.isReliable && <Badge tone="amber">ishonchsiz</Badge>}
                    </span>
                  )}
                </td>
              </tr>
              {open === r.groupKey && (
                <tr className="bg-slate-50/60 dark:bg-white/[0.02]">
                  <td />
                  <td colSpan={report.groupBy === 'employee' ? 10 : 9} className="px-5 py-4">
                    <div className="grid gap-4 lg:grid-cols-2">
                      <div>
                        <p className="mb-2 text-xs font-semibold uppercase text-slate-500">Metrikalar</p>
                        <ul className="space-y-1.5 text-xs">
                          {r.metrics.map((m) => (
                            <li key={m.key} className="flex justify-between gap-4">
                              <span>
                                {m.label} <span className="text-slate-400">({m.formula}, vazn {m.weight})</span>
                              </span>
                              <span className="font-medium tabular-nums">{m.value === null ? <span className="text-slate-400">{m.missingReason}</span> : `${Math.round(m.value * 100)}%`}</span>
                            </li>
                          ))}
                        </ul>
                      </div>
                      <div>
                        <p className="mb-2 text-xs font-semibold uppercase text-slate-500">Top dasturlar</p>
                        <ul className="space-y-1.5 text-xs">
                          {r.topApplications.map((a) => (
                            <li key={a.processName} className="flex justify-between">
                              <span className="font-mono">{a.processName}</span>
                              <span>
                                {formatDuration(a.foregroundSeconds)} · {Math.round(a.share * 100)}%
                              </span>
                            </li>
                          ))}
                          {r.topApplications.length === 0 && <li className="text-slate-400">Ma’lumot yo‘q</li>}
                        </ul>
                      </div>
                    </div>
                  </td>
                </tr>
              )}
            </Fragment>
          ))}
        </tbody>
      </table>
    </div>
  )
}

export function FormulaCard({ report }: { report: Report }) {
  return (
    <div className="rounded-2xl border border-slate-200 bg-white p-5 text-xs leading-relaxed text-slate-600 dark:border-white/10 dark:bg-ink-900 dark:text-slate-300">
      <p className="text-sm font-semibold text-slate-900 dark:text-white">Formula va davr</p>
      <p className="mt-1">
        Davr: {new Date(report.from).toLocaleString('uz-UZ')} — {new Date(report.to).toLocaleString('uz-UZ')} ({report.timeZone})
      </p>
      <p className="mt-2">{report.formula.description}</p>
      <p className="mt-2">
        Vaznlar: tasdiqlangan dasturlar {report.formula.approvedUsageWeight}, jadvalga rioya {report.formula.scheduleAdherenceWeight}, faollik {report.formula.reportedActivityWeight}; minimal coverage {Math.round(report.formula.minimumCoverage * 100)}%.
      </p>
      <p className="mt-3 rounded-xl bg-amber-50 p-3 text-amber-800 dark:bg-amber-500/10 dark:text-amber-200">{report.disclaimer}</p>
    </div>
  )
}
