import { useEffect, useState } from 'react'
import { mutate, request } from '../api'
import type { Offerings, PlanRequest, PlanResult, Term } from '../planning'
import { action, field, weekdays } from '../planning'
import { panel, secondaryButton } from '../ui'
import { ErrorMessage } from './Brand'

function PlanSummary({ result }: { result: PlanResult }) {
  return <section className={panel} aria-label="Plan result">
    <h2 className="text-lg font-semibold">{result.status === 'FEASIBLE' ? result.tentative ? 'Valid tentative course choices' : 'Validated schedule proposal' : result.status}</h2>
    {result.status !== 'FEASIBLE' && <p className="mt-2 text-sm text-bms-muted">{result.status === 'TIMEOUT' ? 'Time ran out before a solution was found. Infeasibility has not been proven.' : result.status === 'UNAVAILABLE' ? 'You can still select and validate courses manually.' : 'No valid proposal is being presented.'}</p>}
    <ul className="mt-3 space-y-2 text-xs leading-6 text-bms-muted">{result.issues.map((issue, index) => <li key={index}><strong>{issue.code}</strong>: {issue.message}</li>)}</ul>
    {result.status === 'FEASIBLE' && <>
      <p className="mt-3 text-sm font-semibold text-bms-blue">{result.totalCredits} credits · {result.termCode}{result.id ? ' · Saved' : ''}</p>
      <p className="mt-3 rounded-lg bg-orange-50 p-3 text-xs leading-6 text-orange-950">{result.tentative ? 'Tentative: based on catalog term patterns. No sections, seat availability, or conflict-free timetable is confirmed.' : 'Validated against current published section data. Seats are not reserved; this is a fictional planning service.'}</p>
      <ul className="mt-4 divide-y divide-slate-100">{result.selections.map(option => <li key={option.id} className="py-3 text-xs leading-6"><strong>{option.sectionCode ?? option.courseCode}</strong> · {option.title} · {option.credits} credits</li>)}</ul>
      {!result.tentative && <div className="mt-5">
        <h3 className="text-sm font-semibold">Weekly timetable · America/New_York</h3>
        <div className="mt-3 grid gap-3 sm:grid-cols-2 xl:grid-cols-5">{weekdays.map(day => <section key={day} className="rounded-lg border border-blue-100 bg-bms-sky p-3"><h4 className="text-xs font-bold text-bms-blue">{day}</h4><ul className="mt-3 space-y-3 text-xs leading-5">{result.selections.filter(option => option.days.includes(day)).sort((a, b) => (a.startLocal ?? '').localeCompare(b.startLocal ?? '')).map(option => <li key={option.id} className="rounded-md bg-white p-2"><strong className="block">{option.courseCode}</strong><span>{option.startLocal}–{option.endLocal}</span></li>)}</ul>{!result.selections.some(option => option.days.includes(day)) && <p className="mt-3 text-xs text-bms-muted">No classes</p>}</section>)}</div>
      </div>}
    </>}
  </section>
}

export function SemesterPlanner() {
  const [terms, setTerms] = useState<Term[]>([])
  const [termCode, setTermCode] = useState('')
  const [minCredits, setMinCredits] = useState(12)
  const [maxCredits, setMaxCredits] = useState(18)
  const [preferredDays, setPreferredDays] = useState<string[]>([])
  const [fewerCampusDays, setFewerCampusDays] = useState(true)
  const [offerings, setOfferings] = useState<Offerings | null>(null)
  const [selectedIds, setSelectedIds] = useState<string[]>([])
  const [result, setResult] = useState<PlanResult | null>(null)
  const [saved, setSaved] = useState<PlanResult[]>([])
  const [loading, setLoading] = useState(true)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const [retry, setRetry] = useState(0)

  useEffect(() => {
    const controller = new AbortController()
    Promise.all([request<Term[]>('/api/terms', { signal: controller.signal }), request<PlanResult[]>('/api/students/me/plans', { signal: controller.signal })])
      .then(([items, plans]) => { setTerms(items); setSaved(plans); setTermCode(value => value || items[0]?.code || ''); if (!items.length) setLoading(false) })
      .catch(cause => { if (!controller.signal.aborted) { setError(cause.message); setLoading(false) } })
    return () => controller.abort()
  }, [retry])

  useEffect(() => {
    if (!termCode) return
    const controller = new AbortController()
    setLoading(true)
    setError('')
    setResult(null)
    setSelectedIds([])
    setOfferings(null)
    request<Offerings>(`/api/terms/${encodeURIComponent(termCode)}/sections`, { signal: controller.signal })
      .then(setOfferings)
      .catch(cause => { if (!controller.signal.aborted) setError(cause.message) })
      .finally(() => { if (!controller.signal.aborted) setLoading(false) })
    return () => controller.abort()
  }, [termCode, retry])

  function constraints(): PlanRequest { return { termCode, minCredits, maxCredits, preferredDays, fewerCampusDays } }
  function toggle(id: string) { setSelectedIds(value => value.includes(id) ? value.filter(item => item !== id) : [...value, id]); setResult(null) }
  async function run(mode: 'generate' | 'validate' | 'save') {
    setBusy(true)
    setError('')
    try {
      const response = await mutate<PlanResult>(`/api/plans/${mode}`, mode === 'generate' ? constraints() : { constraints: constraints(), selectionIds: selectedIds })
      setResult(response)
      if (response.status === 'FEASIBLE') setSelectedIds(response.selections.map(option => option.id))
      if (mode === 'save') setSaved(await request<PlanResult[]>('/api/students/me/plans'))
    } catch (cause) {
      setResult(null)
      setError(cause instanceof Error ? cause.message : 'The request failed. Revalidate the selections and try again.')
    } finally { setBusy(false) }
  }

  return <div className="space-y-6">
    <section className={panel}>
      <h2 className="text-lg font-semibold">Build a semester that fits</h2>
      <p className="mt-2 text-xs leading-6 text-bms-muted">Use the bounded solver or choose courses manually. Every proposal and saved plan is checked again by the backend. Preferred weekdays are soft preferences.</p>
      <form className="mt-5" onSubmit={event => { event.preventDefault(); void run('generate') }}>
        <fieldset disabled={busy} className="space-y-4 disabled:opacity-70">
          <legend className="sr-only">Planning constraints</legend>
          <div className="grid gap-4 sm:grid-cols-3">
            <label className="space-y-2 text-xs font-semibold">Planning term<select className={field} value={termCode} onChange={event => setTermCode(event.target.value)}>{terms.map(term => <option key={term.code} value={term.code}>{term.name}</option>)}</select></label>
            <label className="space-y-2 text-xs font-semibold">Minimum credits<input className={field} type="number" min={1} max={30} required value={minCredits} onChange={event => { setMinCredits(Number(event.target.value)); setResult(null) }} /></label>
            <label className="space-y-2 text-xs font-semibold">Maximum credits<input className={field} type="number" min={1} max={30} required value={maxCredits} onChange={event => { setMaxCredits(Number(event.target.value)); setResult(null) }} /></label>
          </div>
          <fieldset><legend className="text-xs font-semibold">Preferred days (optional)</legend><div className="mt-2 flex flex-wrap gap-2">{weekdays.map(day => <label key={day} className="flex min-h-11 cursor-pointer items-center gap-2 rounded-lg border border-slate-200 px-3 text-xs"><input type="checkbox" checked={preferredDays.includes(day)} onChange={() => { setPreferredDays(value => value.includes(day) ? value.filter(item => item !== day) : [...value, day]); setResult(null) }} className="size-4 accent-bms-blue" />{day}</label>)}</div></fieldset>
          <label className="flex min-h-11 items-center gap-2 text-xs"><input type="checkbox" checked={fewerCampusDays} onChange={event => { setFewerCampusDays(event.target.checked); setResult(null) }} className="size-4 accent-bms-blue" />Prefer fewer campus days</label>
          <button className={action} disabled={loading || !offerings || offerings.issues.length > 0}>{busy ? 'Working…' : 'Generate proposal'}</button>
        </fieldset>
      </form>
      {offerings && !offerings.term.isPublished && <p className="mt-4 rounded-lg bg-orange-50 p-3 text-xs leading-6 text-orange-950">Tentative term: no published sections. Generated choices use advertised term patterns and are not registration-ready.</p>}
    </section>
    {error && <><ErrorMessage message={error} /><button className={secondaryButton} onClick={() => setRetry(value => value + 1)}>Retry planner data</button></>}
    {loading && <p className="py-5 text-sm text-bms-muted" role="status">Loading available offerings…</p>}
    {!loading && offerings && <section className={panel}>
      <h2 className="text-lg font-semibold">{offerings.term.isPublished ? 'Available eligible sections' : 'Tentative eligible courses'}</h2>
      <p className="mt-2 text-xs leading-6 text-bms-muted">{selectedIds.length} selections · {offerings.options.filter(option => selectedIds.includes(option.id)).reduce((total, option) => total + option.credits, 0)} credits selected. Validate before saving.</p>
      {offerings.issues.map((issue, index) => <ErrorMessage key={index} message={`${issue.code}: ${issue.message}`} />)}
      {!offerings.options.length && <p className="py-5 text-sm text-bms-muted">No eligible offerings with available seats. Explore the course catalog for prerequisite and offering reasons.</p>}
      <fieldset disabled={busy} className="mt-4 grid gap-3 sm:grid-cols-2"><legend className="sr-only">Choose plan courses</legend>{offerings.options.map(option => <label key={option.id} className="flex cursor-pointer items-start gap-3 rounded-lg border border-slate-200 p-4 has-checked:border-bms-blue has-checked:bg-bms-sky"><input aria-label={`Select ${option.sectionCode ?? option.courseCode}`} type="checkbox" checked={selectedIds.includes(option.id)} onChange={() => toggle(option.id)} className="mt-1 size-4 shrink-0 accent-bms-blue" /><span className="min-w-0 text-xs leading-6"><strong>{option.sectionCode ?? option.courseCode}</strong> · {option.credits} credits<span className="block text-bms-muted">{option.title}</span><span className="block text-bms-muted">{option.tentative ? 'Tentative · no meeting times' : `${option.days.join('/')} ${option.startLocal}–${option.endLocal} · ${option.availableSeats} seats`}</span></span></label>)}</fieldset>
      <div className="mt-5 flex flex-wrap gap-3"><button className={action} disabled={busy || !selectedIds.length} onClick={() => void run('validate')}>Validate selections</button><button className={action} disabled={busy || result?.status !== 'FEASIBLE' || !!result.id} onClick={() => void run('save')}>Save plan</button></div>
    </section>}
    <div aria-live="polite" aria-busy={busy}>{busy && <p role="status" className="py-3 text-sm text-bms-muted">Checking your plan…</p>}{result && <PlanSummary result={result} />}</div>
    <section className={panel}><h2 className="text-lg font-semibold">Your saved plans</h2><p className="mt-2 text-xs leading-6 text-bms-muted">Plans are revalidated against current section data when loaded.</p>{!saved.length ? <p className="mt-4 text-sm text-bms-muted">No saved plans yet.</p> : <div className="mt-4 space-y-3">{saved.map(plan => <details key={plan.id} className="rounded-lg border border-slate-200 p-3"><summary className="min-h-11 cursor-pointer text-sm font-semibold">{plan.termCode} · {plan.totalCredits} credits · {plan.tentative ? 'Tentative' : plan.status}</summary><PlanSummary result={plan} /></details>)}</div>}</section>
  </div>
}
