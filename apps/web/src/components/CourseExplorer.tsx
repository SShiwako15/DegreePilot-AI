import { useEffect, useState } from 'react'
import { request } from '../api'
import type { CourseEligibility, Term } from '../planning'
import { field } from '../planning'
import { panel } from '../ui'
import { ErrorMessage } from './Brand'

const labels = { eligible: 'Eligible', blocked: 'Blocked', completed: 'Completed', not_offered: 'Not offered', invalid: 'Catalog invalid' }

export function CourseExplorer() {
  const [terms, setTerms] = useState<Term[]>([])
  const [term, setTerm] = useState('')
  const [search, setSearch] = useState('')
  const [category, setCategory] = useState('')
  const [status, setStatus] = useState('')
  const [rows, setRows] = useState<CourseEligibility[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const [retry, setRetry] = useState(0)

  useEffect(() => {
    const controller = new AbortController()
    request<Term[]>('/api/terms', { signal: controller.signal }).then(items => {
      setTerms(items)
      setTerm(value => value || items[0]?.code || '')
      if (!items.length) setLoading(false)
    }).catch(cause => { if (!controller.signal.aborted) { setError(cause.message); setLoading(false) } })
    return () => controller.abort()
  }, [retry])

  useEffect(() => {
    if (!term) return
    const controller = new AbortController()
    setLoading(true)
    setError('')
    const query = new URLSearchParams({ term, search, category, status })
    request<CourseEligibility[]>(`/api/courses?${query}`, { signal: controller.signal })
      .then(setRows)
      .catch(cause => { if (!controller.signal.aborted) setError(cause.message) })
      .finally(() => { if (!controller.signal.aborted) setLoading(false) })
    return () => controller.abort()
  }, [term, search, category, status, retry])

  return <div className="space-y-6">
    <section className={panel}>
      <h2 className="text-lg font-semibold">Find your next course</h2>
      <p className="mt-2 text-xs leading-6 text-bms-muted">Eligibility uses your pinned catalog and C-or-better grades completed before the selected term. Same-term courses do not satisfy prerequisites.</p>
      <div className="mt-5 grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
        <label className="space-y-2 text-xs font-semibold">Term<select aria-label="Course term" className={field} value={term} onChange={event => setTerm(event.target.value)}>{terms.map(item => <option key={item.code} value={item.code}>{item.name}</option>)}</select></label>
        <label className="space-y-2 text-xs font-semibold">Search courses<input className={field} type="search" value={search} onChange={event => setSearch(event.target.value)} placeholder="Code or title" /></label>
        <label className="space-y-2 text-xs font-semibold">Category<select className={field} value={category} onChange={event => setCategory(event.target.value)}><option value="">All categories</option><option value="core">Core</option><option value="cs_elective">CS electives</option><option value="gen_ed">General education</option><option value="free_elective">Free electives</option></select></label>
        <label className="space-y-2 text-xs font-semibold">Eligibility<select className={field} value={status} onChange={event => setStatus(event.target.value)}><option value="">All statuses</option>{Object.entries(labels).map(([value, label]) => <option key={value} value={value}>{label}</option>)}</select></label>
      </div>
      {terms.find(item => item.code === term)?.isPublished === false && <p className="mt-4 rounded-lg bg-orange-50 p-3 text-xs leading-6 text-orange-950">Tentative offerings from catalog term patterns. Published sections and meeting times are not available.</p>}
    </section>
    {error && <><ErrorMessage message={error} /><button className="min-h-11 cursor-pointer text-sm text-bms-blue underline" onClick={() => setRetry(value => value + 1)}>Retry course search</button></>}
    {loading ? <p role="status" className="py-8 text-sm text-bms-muted">Checking course eligibility…</p> : !error && <>
      <p className="text-xs text-bms-muted" role="status">{rows.length} courses found</p>
      {!rows.length && <p className={panel}>No courses match these filters.</p>}
      <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-3">{rows.map(({ course, eligibility }) => <article key={course.code} className="rounded-xl border border-slate-200 border-t-2 border-t-bms-blue bg-white p-5 shadow-xs">
        <div className="flex flex-wrap items-center justify-between gap-2"><strong className="text-sm text-bms-blue">{course.code}</strong><span className={`rounded-full px-2 py-1 text-[11px] font-semibold ${eligibility.eligible ? 'bg-bms-sky text-bms-blue' : 'bg-orange-50 text-orange-950'}`}>{labels[eligibility.status]}</span></div>
        <h2 className="mt-4 text-base font-semibold">{course.title}</h2>
        <p className="mt-3 text-xs text-bms-muted">{course.credits} credits · {course.category.replaceAll('_', ' ')}</p>
        <p className="mt-4 text-xs leading-6 text-bms-muted">Prerequisites: {(course.prerequisiteGroups ?? []).map(group => `(${group.join(' or ')})`).join(' AND ') || 'None'}</p>
        {eligibility.tentative && <p className="mt-2 text-xs font-semibold text-orange-950">Tentative term-pattern offering</p>}
        <ul className="mt-3 space-y-2 text-xs leading-5 text-bms-muted">{eligibility.reasons.map((reason, index) => <li key={index}><span className="font-semibold">{reason.code}</span>: {reason.message}</li>)}</ul>
      </article>)}</div>
    </>}
  </div>
}
