import { useEffect, useRef, useState } from 'react'
import { ArrowUp, Sparkles, Trash2 } from 'lucide-react'
import { displayCatalogVersion, mutate, request } from '../api'
import type { Term } from '../planning'
import { secondaryButton } from '../ui'
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
    <p className="mt-2 text-[11px] leading-5 text-bms-muted">{citation.documentTitle} · Version {citation.documentVersion} · {displayCatalogVersion(citation.catalogVersion)}</p>
    <blockquote className="mt-3 whitespace-pre-wrap wrap-anywhere border-l-2 border-bms-orange pl-3 text-xs leading-6 text-bms-ink">{citation.quote}</blockquote>
  </details>
}

export function AdvisorChat({ catalogVersion }: { catalogVersion: string }) {
  const [status, setStatus] = useState<Status | null>(null)
  const [terms, setTerms] = useState<Term[]>([])
  const [termCode, setTermCode] = useState('')
  const [question, setQuestion] = useState('')
  const [turns, setTurns] = useState<Turn[]>([])
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const [retry, setRetry] = useState(0)
  const [pendingQuestion, setPendingQuestion] = useState('')
  const input = useRef<HTMLTextAreaElement>(null)
  const conversation = useRef<HTMLElement>(null)
  const pending = useRef<AbortController | null>(null)
  const generation = useRef(0)
  const focusAfterReply = useRef(false)

  useEffect(() => {
    if (!busy && focusAfterReply.current) {
      focusAfterReply.current = false
      input.current?.focus({ preventScroll: true })
    }
  }, [busy])

  useEffect(() => {
    const log = conversation.current
    if (log) log.scrollTop = log.scrollHeight
  }, [turns, pendingQuestion, error])

  useEffect(() => {
    const controller = new AbortController()
    setError('')
    Promise.all([
      request<Status>('/api/advisor/status', { signal: controller.signal }),
      request<Term[]>('/api/terms', { signal: controller.signal }),
    ]).then(([availability, items]) => {
      if (!controller.signal.aborted) { setStatus(availability); setTerms(items) }
    }).catch(cause => { if (!controller.signal.aborted) setError(cause.message) })
    return () => controller.abort()
  }, [retry])

  useEffect(() => () => { generation.current++; pending.current?.abort() }, [])

  function clear() {
    focusAfterReply.current = false
    generation.current++
    pending.current?.abort()
    setTurns([])
    setQuestion('')
    setPendingQuestion('')
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
    setPendingQuestion(text)
    setQuestion('')
    setBusy(true)
    setError('')
    try {
      const result = await mutate<AdvisorResult>('/api/advisor/chat', {
        question: text, catalogVersion, termCode: termCode || null,
        previousQuestions: turns.slice(-4).map(turn => turn.question),
      }, controller.signal)
      if (currentGeneration !== generation.current) return
      setTurns(value => [...value, { question: text, result }].slice(-20))
    } catch (cause) {
      if (!controller.signal.aborted && currentGeneration === generation.current) {
        setQuestion(text)
        setError(cause instanceof Error ? cause.message : 'The advisor is unavailable. Please retry.')
      }
    } finally {
      if (currentGeneration === generation.current) {
        focusAfterReply.current = true
        setPendingQuestion('')
        setBusy(false)
      }
    }
  }

  return <div className="relative flex min-h-0 flex-1 flex-col">
    <div className="relative z-20 flex shrink-0 flex-wrap items-center justify-between gap-2 border-b border-slate-200 pb-3">
      <p className="text-[11px] text-bms-muted">Approved sources · {displayCatalogVersion(catalogVersion)}</p>
      <div className="flex items-center gap-1">
        <button type="button" onClick={clear} className="inline-flex min-h-11 items-center gap-2 rounded-lg px-3 text-xs font-medium text-bms-muted hover:bg-white"><Trash2 size={15} />Clear conversation</button>
      </div>

    </div>

    <section ref={conversation} role="log" aria-label="Advisor conversation" aria-live="polite" aria-relevant="additions" tabIndex={0} className="min-h-0 flex-1 overflow-y-auto overscroll-contain px-1 py-6 sm:px-4">
      <div className="mx-auto w-full max-w-3xl space-y-8">
        {!turns.length && !pendingQuestion && <div className="mx-auto max-w-xl py-4 text-center sm:py-10">
          <span className="mx-auto grid size-12 place-items-center rounded-2xl bg-bms-blue text-white"><Sparkles size={23} /></span>
          <h2 className="mt-5 text-2xl font-semibold tracking-tight">What’s next on your academic path?</h2>
          <p className="mt-3 text-sm leading-7 text-bms-muted">Ask about prerequisites, your degree progress, or planning policies. Answers include approved sources and your checked academic facts.</p>
          <div className="mt-6 grid gap-3 text-left sm:grid-cols-2">{['What do I need before CS350 Computer Networks?', 'How many credits do I have left to graduate?'].map(example => <button key={example} type="button" disabled={!status?.available} onClick={() => { setQuestion(example); input.current?.focus({ preventScroll: true }) }} className="min-h-20 rounded-2xl border border-slate-200 bg-white px-4 py-3 text-left text-xs leading-6 text-bms-ink hover:border-bms-blue disabled:opacity-50">{example}</button>)}</div>
        </div>}
        {turns.map((turn, index) => <article key={index} className="min-w-0 space-y-5">
          <div className="ml-auto w-fit max-w-[90%] rounded-3xl rounded-br-lg bg-blue-100/70 px-5 py-3"><h3 className="sr-only">You</h3><p className="whitespace-pre-wrap wrap-anywhere text-sm leading-7">{turn.question}</p></div>
          <div className="flex min-w-0 gap-3"><span aria-hidden="true" className="mt-1 grid size-8 shrink-0 place-items-center rounded-full bg-bms-blue text-white"><Sparkles size={16} /></span><div className="min-w-0 flex-1 space-y-4">
            <h3 className="text-xs font-semibold text-bms-blue">DegreePilot advisor{turn.result.status !== 'ANSWERED' && ` · ${turn.result.status}`}</h3>
            <p className="whitespace-pre-wrap wrap-anywhere text-sm leading-7">{turn.result.answer}</p>
            {turn.result.facts.length > 0 && <details className="text-xs leading-6"><summary className="min-h-11 cursor-pointer font-semibold text-bms-blue">Deterministic facts used</summary><ul className="mt-2 list-inside list-disc space-y-2 text-bms-muted">{turn.result.facts.map((fact, i) => <li key={i}>{fact}</li>)}</ul></details>}
            {turn.result.citations.length > 0 && <div className="space-y-2"><h4 className="text-xs font-semibold">Approved sources</h4>{turn.result.citations.map(citation => <Source key={citation.policySectionId} citation={citation} />)}</div>}
            <ul className="space-y-1 text-[11px] leading-5 text-bms-muted">{turn.result.limitations.map((limitation, i) => <li key={i}>{limitation}</li>)}</ul>
          </div></div>
        </article>)}
        {pendingQuestion && <div className="space-y-5">
          <div className="ml-auto w-fit max-w-[90%] rounded-3xl rounded-br-lg bg-blue-100/70 px-5 py-3"><p className="sr-only">You</p><p className="whitespace-pre-wrap wrap-anywhere text-sm leading-7">{pendingQuestion}</p></div>
          <p role="status" className="flex items-center gap-3 text-sm text-bms-muted"><Sparkles size={18} className="text-bms-blue" />Checking approved evidence…</p>
        </div>}
        {error && <div><ErrorMessage message={error} /><button className={secondaryButton} onClick={() => setRetry(value => value + 1)}>Retry advisor connection</button></div>}
      </div>
    </section>

    <form aria-label="Message advisor" className="shrink-0 bg-slate-50 pb-[max(0.75rem,env(safe-area-inset-bottom))] pt-3" onSubmit={event => { event.preventDefault(); void send() }}>
      <div className="mx-auto w-full max-w-3xl">
        {!status && !error && <p role="status" className="mb-2 text-xs text-bms-muted">Checking advisor availability…</p>}
        {status && <p role="status" className={`mb-2 text-xs leading-5 ${status.available ? 'sr-only' : 'rounded-lg bg-slate-100 p-3 text-bms-muted'}`}>{status.reason}{!status.available && ' Your degree audit and planner remain available.'}</p>}
        <div className="rounded-3xl border border-slate-300 bg-white p-3 shadow-sm focus-within:border-bms-blue focus-within:ring-2 focus-within:ring-blue-100">
          <label htmlFor="advisor-question" className="sr-only">Your question</label>
          <textarea ref={input} id="advisor-question" value={question} onChange={event => setQuestion(event.target.value)} onKeyDown={event => { if (event.key === 'Enter' && !event.shiftKey && !event.nativeEvent.isComposing) { event.preventDefault(); void send() } }} required maxLength={1000} disabled={!status?.available || busy} aria-describedby="advisor-privacy" rows={2} className="max-h-40 min-h-14 w-full resize-none rounded-lg border-0 bg-transparent px-2 py-1 text-sm leading-7 focus-visible:outline-none disabled:opacity-60" placeholder="Ask DegreePilot advisor…" />
          <div className="flex items-center justify-between gap-2">
            <label className="min-w-0 text-[10px] text-bms-muted"><span className="sr-only">Eligibility term</span><select aria-label="Eligibility term" value={termCode} disabled={busy} onChange={event => setTermCode(event.target.value)} className="min-h-11 max-w-full rounded-xl bg-slate-50 px-2 text-[11px] text-bms-muted"><option value="">Next available future term</option>{terms.map(term => <option key={term.code} value={term.code}>{term.name}{!term.isPublished ? ' · Tentative' : ''}</option>)}</select></label>
            <div className="flex shrink-0 items-center gap-2"><span className="text-[10px] text-bms-muted">{question.length}/1000</span><button aria-label="Send question" title="Send question" className="grid size-11 shrink-0 place-items-center rounded-full bg-bms-blue text-white hover:bg-bms-navy disabled:cursor-not-allowed disabled:bg-slate-200 disabled:text-slate-400" disabled={!status?.available || busy || !question.trim()}><ArrowUp size={20} /></button></div>
          </div>
        </div>
        <p id="advisor-privacy" className="mt-2 text-center text-[10px] leading-5 text-bms-muted">Enter to send · Shift+Enter for a new line. Fictional policies; verify real requirements with an advisor. No real records or secrets. Conversation clears when you leave.</p>
      </div>
    </form>
  </div>
}
