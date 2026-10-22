import { useEffect, useState } from 'react'
import type { FormEvent } from 'react'
import { displayCatalogVersion, mutate, request } from '../api'
import type { Course } from '../api'
import { ErrorMessage } from './Brand'
import { panel, secondaryButton } from '../ui'
import { field } from '../planning'

type Tab = 'Assignments' | 'Catalogs' | 'Policies' | 'Sections' | 'Audit and health'
function useCommand() {
  const [error, setError] = useState('')
  const [notice, setNotice] = useState('')
  const [busy, setBusy] = useState(false)
  async function run(task: () => Promise<void>, message = '') {
    setError(''); setNotice(''); setBusy(true)
    try { await task(); setNotice(message) }
    catch (e) { setError(e instanceof Error ? e.message : 'Unable to complete the operation.') }
    finally { setBusy(false) }
  }
  return { busy, run, fail: setError, feedback: <>{error && <ErrorMessage message={error} />}{notice && <p role="status" className="my-3 text-sm text-bms-blue">{notice}</p>}</> }
}

export function AdminWorkspace() {
  const [tab, setTab] = useState<Tab>('Assignments')
  return <div className="space-y-6"><section className={panel}><h2 className="text-lg font-semibold">Administration</h2><p className="my-3 text-sm leading-6 text-bms-muted">Fictional data only. Publishing creates a reviewed immutable version. Students keep their pinned catalog; academic records and grades cannot be edited here.</p><nav aria-label="Administration sections" className="flex flex-wrap gap-2">{(['Assignments', 'Catalogs', 'Policies', 'Sections', 'Audit and health'] as Tab[]).map(t => <button key={t} aria-pressed={tab === t} className={`${secondaryButton} ${tab === t ? 'bg-bms-sky ring-2 ring-bms-blue' : ''}`} onClick={() => setTab(t)}>{t}</button>)}</nav></section>
    {tab === 'Assignments' && <Assignments />}{tab === 'Catalogs' && <Catalogs />}{tab === 'Policies' && <Policies />}{tab === 'Sections' && <Sections />}{tab === 'Audit and health' && <AuditHealth />}
  </div>
}

interface Directory { users: { id: string; displayName: string; role: string }[]; assignments: { advisorId: string; studentId: string; active: boolean; revision: number }[] }
function Assignments() {
  const [data, setData] = useState<Directory | null>(null)
  const [advisor, setAdvisor] = useState('advisor-morgan')
  const [student, setStudent] = useState('student-ava')
  const command = useCommand()
  const fail = command.fail
  useEffect(() => { const c = new AbortController(); request<Directory>('/api/admin/directory', { signal: c.signal }).then(setData).catch(e => { if (!c.signal.aborted) fail(e.message) }); return () => c.abort() }, [fail])
  const assignment = data?.assignments.find(a => a.advisorId === advisor && a.studentId === student)
  function change(active: boolean) {
    void command.run(async () => {
      await mutate('/api/admin/assignments', { advisorId: advisor, studentId: student, active, revision: assignment?.revision ?? 0 })
      setData(await request('/api/admin/directory'))
    }, active ? 'Assignment granted and audited.' : 'Assignment revoked and audited.')
  }
  return <section className={panel}><h2 className="text-lg font-semibold">Advisor assignments</h2>{command.feedback}{!data ? <p role="status">Loading directory… <button className={secondaryButton} onClick={() => void command.run(async () => setData(await request('/api/admin/directory')))}>Retry directory</button></p> : <><div className="my-4 grid gap-4 sm:grid-cols-2"><label className="text-sm">Advisor<select className={field} value={advisor} onChange={e => setAdvisor(e.target.value)}>{data.users.filter(u => u.role === 'advisor').map(u => <option key={u.id} value={u.id}>{u.displayName}</option>)}</select></label><label className="text-sm">Student assignment<select className={field} value={student} onChange={e => setStudent(e.target.value)}>{data.users.filter(u => u.role === 'student').map(u => <option key={u.id} value={u.id}>{u.displayName}</option>)}</select></label></div><p className="text-sm">Assignment status: <strong>{assignment?.active ? 'Active' : 'Inactive'}</strong> · revision {assignment?.revision ?? 0}</p><div className="flex flex-wrap gap-3"><button className={secondaryButton} disabled={command.busy || assignment?.active === true} onClick={() => change(true)}>Grant assignment</button><button className={secondaryButton} disabled={command.busy || !assignment?.active} onClick={() => change(false)}>Revoke assignment</button><button className={secondaryButton} disabled={command.busy} onClick={() => void command.run(async () => setData(await request('/api/admin/directory')))}>Refresh directory</button></div></>}</section>
}

interface Catalog { id: string; status: string; revision: number }
interface CatalogDetail extends Catalog { program: Record<string, unknown> & { minTotalCredits: number }; courses: Course[] }
function Catalogs() {
  const [catalogs, setCatalogs] = useState<Catalog[]>([])
  const [source, setSource] = useState('NSU-BSCS-2026')
  const [version, setVersion] = useState('')
  const [draft, setDraft] = useState<CatalogDetail | null>(null)
  const [savedDraft, setSavedDraft] = useState('')
  const command = useCommand()
  const fail = command.fail
  useEffect(() => { const c = new AbortController(); request<Catalog[]>('/api/admin/catalogs', { signal: c.signal }).then(setCatalogs).catch(e => { if (!c.signal.aborted) fail(e.message) }); return () => c.abort() }, [fail])
  async function choose(id: string) { const d = await request<CatalogDetail>(`/api/admin/catalogs/${encodeURIComponent(id)}`); setDraft(d); setSavedDraft(JSON.stringify(d)) }
  async function reload(id: string) { await choose(id); setCatalogs(await request('/api/admin/catalogs')) }
  function clone(e: FormEvent) {
    e.preventDefault()
    void command.run(async () => { const r = await mutate<{ id: string }>('/api/admin/catalogs', { sourceVersion: source, catalogVersion: version }); await reload(r.id) }, 'Draft created. Review before publishing.')
  }
  return <section className={panel}><h2 className="text-lg font-semibold">Versioned catalog management</h2>{command.feedback}<form onSubmit={clone} className="my-4 grid items-end gap-3 sm:grid-cols-3"><label className="text-sm">Published source<select className={field} value={source} onChange={e => setSource(e.target.value)}>{catalogs.filter(c => c.status === 'published').map(c => <option key={c.id} value={c.id}>{displayCatalogVersion(c.id)}</option>)}</select></label><label className="text-sm">New catalog version<input className={field} required pattern="[A-Z0-9][A-Z0-9-]{2,79}" maxLength={80} placeholder="BMSU-BSCS-2027" value={version} onChange={e => setVersion(e.target.value)} /></label><button className={secondaryButton} disabled={command.busy}>Create catalog draft</button></form>
    <ul className="my-4 space-y-2">{catalogs.map(c => <li key={c.id}><button className="min-h-11 break-all text-left text-sm font-semibold text-bms-blue underline" disabled={command.busy} onClick={() => void command.run(() => choose(c.id))}>{displayCatalogVersion(c.id)} · {c.status} · revision {c.revision}</button></li>)}</ul><button className={secondaryButton} onClick={() => void command.run(async () => setCatalogs(await request('/api/admin/catalogs')))}>Refresh catalogs</button>
    {draft && <div className="mt-5 space-y-4 border-t border-slate-200 pt-5"><h3 className="break-all font-semibold">{draft.id} · {draft.status}</h3><p className="text-sm text-bms-muted">{draft.courses.length} courses. Existing grades and student catalog pins are preserved. Published editions are immutable.</p><label className="block text-sm">Total required credits<input className={`${field} mt-2`} type="number" min={1} max={300} disabled={draft.status !== 'draft'} value={draft.program.minTotalCredits} onChange={e => setDraft({ ...draft, program: { ...draft.program, minTotalCredits: Number(e.target.value) } })} /></label><details><summary className="cursor-pointer py-3 text-sm font-semibold">Review courses and prerequisites</summary><ul className="space-y-2 text-sm">{draft.courses.map(c => <li key={c.code} className="rounded-lg border border-slate-200 p-3"><label className="block">{c.code} credits<input className={`${field} my-2`} type="number" min={1} max={30} disabled={draft.status !== 'draft'} value={c.credits} onChange={e => setDraft({ ...draft, courses: draft.courses.map(item => item.code === c.code ? { ...item, credits: Number(e.target.value) } : item) })} /></label>{c.category} · Prerequisites: {c.prerequisiteGroups.map(g => `(${g.join(' OR ')})`).join(' AND ') || 'none'}</li>)}</ul></details>{draft.status === 'draft' && <div className="flex flex-wrap gap-3"><button className={secondaryButton} disabled={command.busy} onClick={() => void command.run(async () => { await mutate(`/api/admin/catalogs/${encodeURIComponent(draft.id)}/draft`, { revision: draft.revision, program: draft.program, courses: draft.courses }); await reload(draft.id) }, 'Catalog draft saved and validated.')}>Save catalog draft</button><button className={secondaryButton} disabled={command.busy || JSON.stringify(draft) !== savedDraft} onClick={() => void command.run(async () => { await mutate(`/api/admin/catalogs/${encodeURIComponent(draft.id)}/publish`, { revision: draft.revision }); await reload(draft.id) }, 'Catalog published. Student pins are unchanged.')}>Publish saved catalog</button></div>}</div>}
  </section>
}

interface Policy { id: string; catalogVersion: string; title: string; status: string; version: number; revision: number }
interface PolicyDetail { document: Policy; sections: { id: string; title: string; text: string }[] }
function Policies() {
  const [policies, setPolicies] = useState<Policy[]>([])
  const [detail, setDetail] = useState<PolicyDetail | null>(null)
  const [savedDetail, setSavedDetail] = useState('')
  const command = useCommand()
  const fail = command.fail
  useEffect(() => { const c = new AbortController(); request<Policy[]>('/api/admin/policies', { signal: c.signal }).then(setPolicies).catch(e => { if (!c.signal.aborted) fail(e.message) }); return () => c.abort() }, [fail])
  async function choose(id: string) { const d = await request<PolicyDetail>(`/api/admin/policies/${encodeURIComponent(id)}`); setDetail(d); setSavedDetail(JSON.stringify(d)) }
  async function reload(id: string) { await choose(id); setPolicies(await request('/api/admin/policies')) }
  return <section className={panel}><h2 className="text-lg font-semibold">Approved policy revisions</h2><p className="my-3 text-sm leading-6 text-bms-muted">Review source text as untrusted data. Approval authorizes explanatory evidence only; it cannot override deterministic academic rules. Publishing retires the previous version atomically.</p>{command.feedback}<ul className="space-y-2">{policies.map(p => <li key={p.id}><button className="min-h-11 text-left text-sm font-semibold text-bms-blue underline" disabled={command.busy} onClick={() => void command.run(() => choose(p.id))}>{p.title} · v{p.version} · {p.status} · {displayCatalogVersion(p.catalogVersion)}</button></li>)}</ul><button className={secondaryButton} onClick={() => void command.run(async () => setPolicies(await request('/api/admin/policies')))}>Refresh policies</button>
    {detail && <div className="mt-4 space-y-4 border-t border-slate-200 pt-4"><h3 className="font-semibold">Review policy · v{detail.document.version} · {detail.document.status}</h3><label className="block text-sm">Document title<input className={`${field} mt-2`} maxLength={200} disabled={detail.document.status !== 'draft'} value={detail.document.title} onChange={e => setDetail({ ...detail, document: { ...detail.document, title: e.target.value } })} /></label>{detail.sections.map((s, index) => <label key={s.id} className="block text-sm">{s.id} · {s.title}<textarea className={`${field} mt-2 min-h-28 py-3`} maxLength={3000} disabled={detail.document.status !== 'draft'} value={s.text} onChange={e => setDetail({ ...detail, sections: detail.sections.map((item, i) => i === index ? { ...item, text: e.target.value } : item) })} /></label>)}
      {detail.document.status === 'approved' && <button className={secondaryButton} disabled={command.busy} onClick={() => void command.run(async () => { const r = await mutate<{ id: string }>('/api/admin/policies', { sourceDocumentId: detail.document.id }); await reload(r.id) }, 'Policy revision draft created.')}>Create policy revision</button>}
      {detail.document.status === 'draft' && <div className="flex flex-wrap gap-3"><button className={secondaryButton} disabled={command.busy} onClick={() => void command.run(async () => { await mutate(`/api/admin/policies/${detail.document.id}/draft`, { revision: detail.document.revision, title: detail.document.title, sections: detail.sections }); await reload(detail.document.id) }, 'Policy draft saved. Review before publishing.')}>Save policy draft</button><button className={secondaryButton} disabled={command.busy || JSON.stringify(detail) !== savedDetail} onClick={() => void command.run(async () => { await mutate(`/api/admin/policies/${detail.document.id}/publish`, { revision: detail.document.revision }); await reload(detail.document.id) }, 'Policy revision approved; previous version retired.')}>Publish reviewed policy</button></div>}
    </div>}
  </section>
}

interface Section { id: string; catalogVersion: string; sectionCode: string; capacity: number; availableSeats: number; termCode: string }
function Sections() {
  const [sections, setSections] = useState<Section[]>([])
  const [selected, setSelected] = useState<Section | null>(null)
  const [capacity, setCapacity] = useState(30)
  const [seats, setSeats] = useState(0)
  const command = useCommand()
  const fail = command.fail
  useEffect(() => { const c = new AbortController(); request<Section[]>('/api/admin/sections', { signal: c.signal }).then(setSections).catch(e => { if (!c.signal.aborted) fail(e.message) }); return () => c.abort() }, [fail])
  return <section className={panel}><h2 className="text-lg font-semibold">Fictional section availability</h2><p className="my-3 text-sm text-bms-muted">Seat updates are audited. Saved plans reserve no seats and are revalidated against current availability.</p>{command.feedback}<label className="block text-sm">Section<select className={`${field} my-2`} value={selected ? `${selected.catalogVersion}/${selected.id}` : ''} onChange={e => { const s = sections.find(s => `${s.catalogVersion}/${s.id}` === e.target.value) ?? null; setSelected(s); if (s) { setCapacity(s.capacity); setSeats(s.availableSeats) } }}><option value="">Choose a section</option>{sections.map(s => <option key={`${s.catalogVersion}/${s.id}`} value={`${s.catalogVersion}/${s.id}`}>{s.termCode} · {s.sectionCode} · {s.availableSeats}/{s.capacity} seats</option>)}</select></label><button className={secondaryButton} onClick={() => void command.run(async () => { setSections(await request('/api/admin/sections')); setSelected(null) })}>Refresh sections</button>{selected && <form onSubmit={e => { e.preventDefault(); void command.run(async () => { await mutate('/api/admin/sections/seats', { catalogVersion: selected.catalogVersion, sectionId: selected.id, capacity, availableSeats: seats, expectedCapacity: selected.capacity, expectedAvailableSeats: selected.availableSeats }); setSections(await request('/api/admin/sections')); setSelected(null) }, 'Section availability saved and audited.') }} className="mt-4 grid items-end gap-4 sm:grid-cols-3"><label className="text-sm">Capacity<input className={field} type="number" min={1} max={1000} required value={capacity} onChange={e => setCapacity(Number(e.target.value))} /></label><label className="text-sm">Available seats<input className={field} type="number" min={0} max={capacity} required value={seats} onChange={e => setSeats(Number(e.target.value))} /></label><button className={secondaryButton} disabled={command.busy}>Save seat availability</button></form>}</section>
}

interface AuditRow { id: string; actorId: string; action: string; resourceId: string; revision: number; occurredAt: string }
interface Health { requests: number; serverErrors: number; rateLimited: number; uptimeSeconds: number }
function AuditHealth() {
  const [rows, setRows] = useState<AuditRow[]>([])
  const [health, setHealth] = useState<Health | null>(null)
  const command = useCommand()
  const fail = command.fail
  const [refresh, setRefresh] = useState(0)
  useEffect(() => { const c = new AbortController(); Promise.all([request<AuditRow[]>('/api/admin/audit', { signal: c.signal }), request<Health>('/api/admin/operations', { signal: c.signal })]).then(([r, h]) => { setRows(r); setHealth(h) }).catch(e => { if (!c.signal.aborted) fail(e.message) }); return () => c.abort() }, [refresh, fail])
  return <section className={panel}><h2 className="text-lg font-semibold">Mutation audit and service health</h2>{command.feedback}<button className={secondaryButton} onClick={() => setRefresh(r => r + 1)}>Refresh audit and health</button>{health && <p className="my-3 text-sm">API process: {health.requests} requests · {health.serverErrors} server errors · {health.rateLimited} rate limited · {Math.floor(health.uptimeSeconds)} seconds uptime</p>}<p className="mb-4 text-sm text-bms-muted">Last 100 accepted mutations. Audit history excludes transcript content, policy passages and chat messages. Counters reset when the API process restarts.</p><ol className="space-y-3">{rows.map(r => <li key={r.id} className="break-words rounded-lg border border-slate-200 p-3 text-xs leading-6"><strong>{r.action}</strong> · {r.actorId}<br />{r.resourceId} · revision {r.revision}<br /><time dateTime={r.occurredAt}>{new Date(r.occurredAt).toLocaleString()}</time></li>)}</ol>{rows.length === 0 && <p className="text-sm">No mutations recorded yet, or audit service unavailable. Use refresh to retry.</p>}</section>
}
