import { useEffect, useRef, useState } from 'react'
import { BookOpen, Send, Sparkles, Trash2 } from 'lucide-react'
import { mutate, request } from '../api'
import type { Term } from '../planning'
import { action, field } from '../planning'
import { panel, secondaryButton } from '../ui'
import { ErrorMessage } from './Brand'

export interface Citation {
  policySectionId: string; catalogVersion: string; documentTitle: string; documentVersion: number; title: string; quote: string
}
interface Status { available: boolean; reason: string }
export interface AdvisorResult {
  status: 'ANSWERED' | 'ABSTAIN' | 'REFUSED' | 'UNAVAILABLE' | 'ERROR'
  catalogVersion: string; answer: string; citations: Citation[]; limitations: string[]; facts: string[]
}
interface Turn { question: string; result: AdvisorResult }

function Source({ citation }: { citation: Citation }) {
  return <details className="rounded-lg border border-blue-100 bg-white p-3">
    <summary className="min-h-11 cursor-pointer text-xs font-semibold leading-6 text-bms-blue">{citation.policySectionId} · {citation.title}</summary>
    <p className="mt-2 text-[11px] leading-5 text-bms-muted">{citation.documentTitle} · Version {citation.documentVersion} · {citation.catalogVersion}</p>
    <blockquote className="mt-3 whitespace-pre-wrap border-l-2 border-bms-orange pl-3 text-xs leading-6 text-bms-ink">{citation.quote}</blockquote>
  </details>
}

export function AdvisorChat({ catalogVersion }: { catalogVersion: string }) {
  const [status, setStatus] = useState<Status | null>(null)
  const [sources, setSources] = useState<Citation[]>([])
  const [terms, setTerms] = useState<Term[]>([])
  const [termCode, setTermCode] = useState('')
  const [question, setQuestion] = useState('')
  const [turns, setTurns] = useState<Turn[]>([])
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const [retry, setRetry] = useState(0)
  const input = useRef<HTMLTextAreaElement>(null)
  const pending = useRef<AbortController | null>(null)
  const generation = useRef(0)

  useEffect(() => {
    const controller = new AbortController()
    setError('')
    Promise.all([
      request<Status>('/api/advisor/status', { signal: controller.signal }),
      request<Citation[]>('/api/policies', { signal: controller.signal }),
      request<Term[]>('/api/terms', { signal: controller.signal }),
    ]).then(([availability, policies, items]) => {
      if (!controller.signal.aborted) { setStatus(availability); setSources(policies); setTerms(items) }
    }).catch(cause => { if (!controller.signal.aborted) setError(cause.message) })
    return () => controller.abort()
  }, [retry])

  useEffect(() => () => { generation.current++; pending.current?.abort() }, [])

  function clear() {
    generation.current++
    pending.current?.abort()
    setTurns([])
    setQuestion('')
    setBusy(false)
    setError('')
    requestAnimationFrame(() => input.current?.focus())
  }

  async function send() {
    if (!question.trim() || !status?.available || busy) return
    const controller = new AbortController()
    pending.current = controller
    const currentGeneration = generation.current
    const text = question.trim()
    setBusy(true)
    setError('')
    try {
      const result = await mutate<AdvisorResult>('/api/advisor/chat', {
        question: text, catalogVersion, termCode: termCode || null,
        previousQuestions: turns.slice(-4).map(turn => turn.question),
      }, controller.signal)
      if (currentGeneration !== generation.current) return
      setTurns(value => [...value, { question: text, result }].slice(-20))
      setQuestion('')
      input.current?.focus()
    } catch (cause) {
      if (!controller.signal.aborted) setError(cause instanceof Error ? cause.message : 'The advisor is unavailable. Please retry.')
    } finally { if (currentGeneration === generation.current) setBusy(false) }
  }

  return <div className="grid gap-6 xl:grid-cols-[minmax(0,1.6fr)_minmax(0,1fr)]">
    <div className="min-w-0 space-y-5">
      <section className={panel}>
        <div className="flex items-start gap-3"><Sparkles className="mt-1 shrink-0 text-bms-blue" size={22} /><div><h2 className="text-lg font-semibold">Ask about your academic path</h2><p className="mt-2 text-xs leading-6 text-bms-muted">Explore capstone prerequisites, degree requirements, grades, or planning policies. Your pinned catalog is {catalogVersion}.</p></div></div>
        <p className="mt-4 rounded-lg bg-orange-50 p-3 text-xs leading-6 text-orange-950">Fictional demonstration policies. Replies are informational; verify real requirements with an academic advisor. Use fabricated records only.</p>
        {!status && !error && <p className="mt-4 text-sm text-bms-muted" role="status">Checking advisor availability…</p>}
        {status && <p role="status" className={`mt-4 rounded-lg p-3 text-xs leading-6 ${status.available ? 'bg-bms-sky text-bms-blue' : 'bg-slate-100 text-bms-muted'}`}>{status.reason}{!status.available && ' Your audit, planner, and approved policy library remain available.'}</p>}
      </section>
      {error && <><ErrorMessage message={error} /><button className={secondaryButton} onClick={() => setRetry(value => value + 1)}>Retry advisor connection</button></>}
      <section role="log" aria-label="Advisor conversation" aria-live="polite" aria-relevant="additions" className="space-y-4">
        {!turns.length && <div className={`${panel} text-sm leading-7 text-bms-muted`}>Start with “Why can’t I take CS401?” or “Do I have enough credits to graduate?” Open a cited source to read its approved text.</div>}
        {turns.map((turn, index) => <article key={index} className={`${panel} min-w-0 space-y-4`}>
          <div className="border-b border-slate-100 pb-4"><h3 className="text-xs font-semibold text-bms-muted">You</h3><p className="mt-2 whitespace-pre-wrap wrap-anywhere text-sm leading-7">{turn.question}</p></div>
          <div><h3 className="text-xs font-semibold text-bms-blue">DegreePilot advisor · {turn.result.status === 'ANSWERED' ? 'Grounded reply' : turn.result.status}</h3><p className="mt-2 whitespace-pre-wrap wrap-anywhere text-sm leading-7">{turn.result.answer}</p></div>
          {turn.result.facts.length > 0 && <details className="text-xs leading-6"><summary className="min-h-11 cursor-pointer font-semibold text-bms-blue">Deterministic facts used</summary><ul className="mt-2 list-inside list-disc space-y-2 text-bms-muted">{turn.result.facts.map((fact, i) => <li key={i}>{fact}</li>)}</ul></details>}
          {turn.result.citations.length > 0 && <div className="space-y-2"><h4 className="text-xs font-semibold">Approved sources</h4>{turn.result.citations.map(citation => <Source key={citation.policySectionId} citation={citation} />)}</div>}
          <ul className="space-y-1 text-[11px] leading-5 text-bms-muted">{turn.result.limitations.map((limitation, i) => <li key={i}>{limitation}</li>)}</ul>
        </article>)}
      </section>
      <form className={panel} onSubmit={event => { event.preventDefault(); void send() }}>
        <label className="block text-xs font-semibold">Eligibility term<select className={`${field} mt-2`} value={termCode} disabled={busy} onChange={event => setTermCode(event.target.value)}><option value="">Next available future term</option>{terms.map(term => <option key={term.code} value={term.code}>{term.name}{!term.isPublished ? ' · Tentative' : ''}</option>)}</select></label>
        <label htmlFor="advisor-question" className="mt-4 block text-xs font-semibold">Your question</label>
        <textarea ref={input} id="advisor-question" value={question} onChange={event => setQuestion(event.target.value)} required maxLength={1000} disabled={!status?.available || busy} aria-describedby="advisor-privacy" rows={4} className={`${field} mt-2 resize-y py-3 leading-6`} placeholder="Why can’t I take CS401 yet?" />
        <p id="advisor-privacy" className="mt-2 text-[11px] leading-5 text-bms-muted">{question.length}/1000 characters. Conversation stays in this page’s memory and clears when you leave. No names, emails, real records, or secrets. Follow-ups use at most four prior user questions.</p>
        <div className="mt-4 flex flex-wrap gap-3"><button className={`${action} flex items-center gap-2`} disabled={!status?.available || busy || !question.trim()}><Send size={15} />{busy ? 'Checking approved evidence…' : 'Send question'}</button><button type="button" className={`${secondaryButton} flex items-center gap-2`} onClick={clear}><Trash2 size={15} />Clear conversation</button></div>
        {busy && <p role="status" className="mt-3 text-xs text-bms-muted">Preparing a grounded reply…</p>}
      </form>
    </div>
    <aside className={`${panel} min-w-0 self-start`} aria-label="Approved policy library">
      <div className="flex items-center gap-2"><BookOpen size={19} className="text-bms-blue" /><h2 className="text-base font-semibold">Approved policy library</h2></div>
      <p className="mt-2 text-xs leading-6 text-bms-muted">These sources belong to your assigned catalog. Available even when AI is disabled.</p>
      <div className="mt-4 space-y-3">{sources.map(source => <Source key={source.policySectionId} citation={source} />)}</div>
      {status && sources.length === 0 && <p className="mt-4 text-xs text-bms-muted">No approved sources are available for this catalog.</p>}
    </aside>
  </div>
}
