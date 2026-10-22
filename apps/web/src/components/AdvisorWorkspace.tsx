import { useEffect, useState } from 'react'
import { displayCatalogVersion, mutate, request } from '../api'
import type { Audit, Transcript } from '../api'
import type { PlanOption, PlanResult, Term } from '../planning'
import { CoreList, ProgressSummary, TranscriptView } from './AcademicViews'
import { ErrorMessage } from './Brand'
import { panel, secondaryButton } from '../ui'

interface AssignedStudent { id: string; displayName: string; catalogVersion: string }
const field = 'mt-1 min-h-11 w-full rounded-lg border border-slate-300 bg-white p-2 text-sm'

export function AdvisorWorkspace() {
  const [students, setStudents] = useState<AssignedStudent[]>([])
  const [terms, setTerms] = useState<Term[]>([])
  const [student, setStudent] = useState('')
  const [term, setTerm] = useState('2027-SPRING')
  const [records, setRecords] = useState<{ audit: Audit; transcript: Transcript; plans: PlanResult[]; options: PlanOption[] } | null>(null)
  const [selected, setSelected] = useState<string[]>([])
  const [minimum, setMinimum] = useState(12)
  const [maximum, setMaximum] = useState(18)
  const [error, setError] = useState('')
  const [notice, setNotice] = useState('')
  const [busy, setBusy] = useState(false)
  const [refresh, setRefresh] = useState(0)
  useEffect(() => {
    const controller = new AbortController()
    Promise.all([
      request<AssignedStudent[]>('/api/advisor-workspace/students', { signal: controller.signal }),
      request<Term[]>('/api/advisor-workspace/terms', { signal: controller.signal }),
    ]).then(([s, t]) => { setStudents(s); setTerms(t) }).catch(e => { if (!controller.signal.aborted) setError(e.message) })
    return () => controller.abort()
  }, [refresh])
  useEffect(() => {
    const controller = new AbortController()
    if (student) {
      const base = `/api/students/${encodeURIComponent(student)}`
      Promise.all([
        request<Audit>(`${base}/degree-audit`, { signal: controller.signal }),
        request<Transcript>(`${base}/transcript`, { signal: controller.signal }),
        request<PlanResult[]>(`${base}/plans`, { signal: controller.signal }),
        request<{ options: PlanOption[] }>(`/api/advisor-workspace/students/${encodeURIComponent(student)}/planning/${term}`, { signal: controller.signal }),
      ]).then(([audit, transcript, plans, planning]) => { if (!controller.signal.aborted) setRecords({ audit, transcript, plans, options: planning.options }) })
        .catch(e => { if (!controller.signal.aborted) { setRecords(null); setError(e.message) } })
    }
    return () => controller.abort()
  }, [student, term, refresh])
  async function suggest() {
    setBusy(true); setError(''); setNotice('')
    try {
      await mutate(`/api/advisor-workspace/students/${encodeURIComponent(student)}/suggestions`, {
        constraints: { termCode: term, minCredits: minimum, maxCredits: maximum }, selectionIds: selected,
      })
      setNotice('Validated suggestion sent. The student decides whether to save it.'); setSelected([])
    } catch (e) { setError(e instanceof Error ? e.message : 'Suggestion could not be sent.') }
    finally { setBusy(false) }
  }
  return <div className="space-y-6">
    <section className={panel}>
      <h2 className="text-lg font-semibold">Assigned students</h2>
      <p className="my-3 text-sm leading-6 text-bms-muted">Read only academic records. Suggest a validated schedule; students retain control of their plans. Assignments are checked on every request.</p>
      {error && <ErrorMessage message={error} />}
      <label className="block text-sm font-medium">Student<select className={field} value={student} onChange={e => { setStudent(e.target.value); setRecords(null); setSelected([]); setError(''); setNotice('') }}><option value="">Choose an assigned student</option>{students.map(s => <option key={s.id} value={s.id}>{s.displayName} · {displayCatalogVersion(s.catalogVersion)}</option>)}</select></label>
      {students.length === 0 && <p role="status" className="mt-3 text-sm">No active assignments.</p>}
      <button className={secondaryButton} onClick={() => { setRecords(null); setError(''); setRefresh(r => r + 1) }}>Refresh assignments and records</button>
    </section>
    {records?.audit.status === 'invalid' && <section className={panel} role="alert"><h2 className="text-lg font-semibold">This audit is unavailable</h2><p className="my-3 text-sm">No credits or completion decision can be calculated until the source records are corrected.</p><ul className="space-y-2 text-sm">{records.audit.issues.map((issue, i) => <li key={i}>{issue.code}: {issue.message}</li>)}</ul></section>}
    {records?.audit.status === 'valid' && <>
      <ProgressSummary audit={records.audit} />
      <CoreList courses={records.audit.coreRequirements} />
      <TranscriptView transcript={records.transcript} audit={records.audit} />
      <section className={panel}><h2 className="text-lg font-semibold">Student’s saved plans</h2>{records.plans.length === 0 ? <p className="mt-3 text-sm">No saved plans.</p> : records.plans.map(p => <p key={p.id} className="mt-3 text-sm">{p.termCode} · {p.totalCredits} credits · {p.status}{p.tentative ? ' · Tentative' : ''}</p>)}</section>
      <section className={panel} aria-label="Suggest a schedule">
        <h2 className="text-lg font-semibold">Suggest a schedule</h2>
        <div className="my-4 grid gap-4 sm:grid-cols-3">
          <label className="text-sm">Suggestion term<select className={field} value={term} onChange={e => { setTerm(e.target.value); setRecords(null); setSelected([]); setNotice('') }}>{terms.map(t => <option key={t.code} value={t.code}>{t.name}{!t.isPublished ? ' · Tentative' : ''}</option>)}</select></label>
          <label className="text-sm">Minimum suggestion credits<input className={field} type="number" min={1} max={30} value={minimum} onChange={e => setMinimum(Number(e.target.value))} /></label>
          <label className="text-sm">Maximum suggestion credits<input className={field} type="number" min={1} max={30} value={maximum} onChange={e => setMaximum(Number(e.target.value))} /></label>
        </div>
        <fieldset><legend className="mb-3 text-sm font-semibold">Eligible choices (unpublished offerings are tentative)</legend><div className="grid gap-2 sm:grid-cols-2">{records.options.map(o => <label key={o.id} className="flex min-h-11 items-center gap-3 rounded-lg border border-slate-200 p-3 text-sm"><input type="checkbox" checked={selected.includes(o.id)} onChange={e => setSelected(e.target.checked ? [...selected, o.id] : selected.filter(id => id !== o.id))} />{o.sectionCode ?? o.courseCode} · {o.credits} credits{o.tentative ? ' · Tentative' : ` · ${o.days.join('/')} ${o.startLocal}–${o.endLocal}`}</label>)}</div></fieldset>
        <button className={secondaryButton} disabled={busy || selected.length === 0} onClick={() => void suggest()}>{busy ? 'Validating…' : 'Send validated suggestion'}</button>
        {notice && <p role="status" className="text-sm text-bms-blue">{notice}</p>}
      </section>
    </>}
  </div>
}
