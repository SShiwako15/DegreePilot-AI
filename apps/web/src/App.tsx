import { useEffect, useRef, useState } from 'react'
import type { FormEvent } from 'react'
import { ArrowDown, ArrowRight, BookOpen, Check, CheckCheck, ChevronRight, CircleHelp, ClipboardList, Compass, FileText, GraduationCap, LayoutDashboard, LockKeyhole, LogOut, Menu, ShieldCheck, Sparkles, X } from 'lucide-react'
import { ApiError, mutate, request } from './api'
import type { Account, AcademicData, Audit, Bucket, CoreCourse, Course, Transcript } from './api'
import './styles.css'

type View = 'dashboard' | 'audit' | 'transcript' | 'catalog'
const views = [
  { id: 'dashboard', label: 'Overview', icon: LayoutDashboard },
  { id: 'audit', label: 'Degree audit', icon: ClipboardList },
  { id: 'transcript', label: 'My transcript', icon: FileText },
  { id: 'catalog', label: 'Course catalog', icon: BookOpen },
] as const
function currentView(): View { return views.find(v => `#${v.id}` === location.hash)?.id ?? 'dashboard' }
function Brand() { return <div className="brand"><span className="brand-icon"><Compass size={26} strokeWidth={1.6} /></span><span>DegreePilot <small>AI</small></span></div> }
function ErrorMessage({ message }: { message: string }) { return <div className="error-message" role="alert">{message}</div> }

export default function App() {
  const [account, setAccount] = useState<Account | null>(null)
  const [data, setData] = useState<AcademicData | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const [refresh, setRefresh] = useState(0)
  const [view, setView] = useState<View>(currentView)
  const [mobileOpen, setMobileOpen] = useState(false)
  const [signingOut, setSigningOut] = useState(false)
  const heading = useRef<HTMLHeadingElement>(null)
  useEffect(() => {
    const onHash = () => { setView(currentView()); setMobileOpen(false) }
    window.addEventListener('hashchange', onHash)
    return () => window.removeEventListener('hashchange', onHash)
  }, [])
  useEffect(() => {
    const controller = new AbortController()
    const options = { signal: controller.signal }
    async function load() {
      setLoading(true); setData(null); setError('')
      try {
        const actor = await request<Account>('/api/me', options)
        if (controller.signal.aborted) return
        setAccount(actor)
        if (actor.role === 'student') {
          const [audit, transcript, courses] = await Promise.all([
            request<Audit>('/api/students/me/degree-audit', options),
            request<Transcript>('/api/students/me/transcript', options),
            request<Course[]>(`/api/catalog/${encodeURIComponent(actor.catalogVersion!)}/courses`, options),
          ])
          if (!controller.signal.aborted) setData({ audit, transcript, courses })
        }
      } catch (cause) {
        if (controller.signal.aborted) return
        if (cause instanceof ApiError && cause.status === 401) setAccount(null)
        else setError(cause instanceof Error ? cause.message : 'The service is unavailable.')
      } finally { if (!controller.signal.aborted) setLoading(false) }
    }
    void load()
    return () => controller.abort()
  }, [refresh])
  function navigate(next: View) {
    location.hash = next; setView(next); setMobileOpen(false)
    requestAnimationFrame(() => heading.current?.focus())
  }
  async function logout() {
    setSigningOut(true)
    try { await mutate('/api/auth/logout'); setAccount(null); setData(null); setError(''); setRefresh(n => n + 1) }
    catch (cause) {
      if (cause instanceof ApiError && cause.status === 401) { setAccount(null); setData(null) }
      else setError(cause instanceof Error ? cause.message : 'Sign out failed. Please retry.')
    } finally { setSigningOut(false) }
  }
  if (!account) return <><a className="skip-link" href="#main">Skip to content</a><Login loading={loading} error={error} onLogin={() => setRefresh(n => n + 1)} onRetry={() => setRefresh(n => n + 1)} /></>
  const title = views.find(v => v.id === view)!.label
  return <div className="app-shell">
    <a className="skip-link" href="#main">Skip to content</a>
    <div className="mobile-bar"><Brand /><button className="icon-button" aria-label={mobileOpen ? 'Close navigation' : 'Open navigation'} aria-expanded={mobileOpen} aria-controls="sidebar" onClick={() => setMobileOpen(!mobileOpen)}>{mobileOpen ? <X /> : <Menu />}</button></div>
    <aside className={`sidebar ${mobileOpen ? 'is-open' : ''}`} id="sidebar" onKeyDown={event => {
      if (event.key === 'Escape') {
        setMobileOpen(false)
        document.querySelector<HTMLButtonElement>('[aria-controls="sidebar"]')?.focus()
      }
    }}>
      <Brand /><div className="institution"><span className="institution-mark"><GraduationCap size={21} /></span><div>Northstar University<small>Fictional institution</small></div></div>
      <p className="nav-label">YOUR WORKSPACE</p>
      <nav aria-label="Main navigation">{views.map(item => <a key={item.id} href={`#${item.id}`} aria-current={view === item.id ? 'page' : undefined} onClick={event => { event.preventDefault(); navigate(item.id) }}><item.icon size={19} /><span>{item.label}</span>{view === item.id && <span className="nav-dot" />}</a>)}
        <div className="nav-unavailable" aria-label="Semester planner, unavailable"><GraduationCap size={19} /><span>Semester planner</span><LockKeyhole size={13} /></div>
        <div className="nav-unavailable" aria-label="AI advisor, unavailable"><Sparkles size={19} /><span>AI advisor</span><LockKeyhole size={13} /></div>
      </nav>
      <div className="sidebar-bottom"><div className="demo-note"><ShieldCheck size={20} /><strong>A space to explore.</strong><p>Made with fictional records.<br />Never official academic advice.</p></div>
        <div className="account"><span className="avatar">{account.displayName.split(' ').slice(0, 2).map(n => n[0]).join('')}</span><div><strong>{account.displayName.replace(' (fictional)', '')}</strong><small>{account.role === 'student' ? 'Demo student' : `Demo ${account.role}`}</small></div><button className="icon-button" aria-label="Sign out" title="Sign out" disabled={signingOut} onClick={() => void logout()}><LogOut size={18} /></button></div></div>
    </aside>
    <div className="workspace">
      <header className="topbar"><div><span>My workspace</span><ChevronRight size={14} /><strong>{title}</strong></div><span className="demo-pill"><span /> Demo environment</span></header>
      <main id="main" tabIndex={-1}>
        <div className="page-heading"><div><p className="eyebrow">YOUR DEGREE, IN VIEW</p><h1 ref={heading} tabIndex={-1}>{view === 'dashboard' ? `Your next chapter, ${account.displayName.split(' ')[0]}.` : title}</h1><p>{view === 'dashboard' ? 'See how far you’ve come. Know what comes next.' : view === 'audit' ? 'Every requirement, with the rules behind it.' : view === 'transcript' ? 'Your complete course history, including every attempt.' : 'Explore the courses in your assigned catalog.'}</p></div>{account.catalogVersion && <span className="catalog-tag"><BookOpen size={15} /> {account.catalogVersion}</span>}</div>
        {error && <><ErrorMessage message={error} /><button className="secondary-button" onClick={() => setRefresh(n => n + 1)}>Try again</button></>}
        {loading && <div className="loading-panel" role="status">Loading your academic records…</div>}
        {!loading && account.role !== 'student' && <section className="panel empty-state"><ShieldCheck /><h2>Student workspace</h2><p>You’re signed in as a demo {account.role}. Advisor and administrator workflows are not available in this phase. Student records remain restricted.</p></section>}
        {!loading && data && <>
          {data.audit.status === 'invalid' ? <section className="panel" role="alert"><h2>We couldn’t calculate this audit</h2><p>No credits or completion decision are shown because the source records need attention.</p><ul>{data.audit.issues.map((issue, i) => <li key={i}><strong>{issue.code}</strong>: {issue.message}</li>)}</ul></section> : <>
            {view === 'dashboard' && <Dashboard account={account} data={data} navigate={navigate} />}
            {view === 'audit' && <><ProgressSummary audit={data.audit} /><section className="panel section-panel"><SectionTitle eyebrow="DEGREE REQUIREMENTS" title="Four paths. One degree." /><Buckets buckets={data.audit.buckets} /><p className="fine-print">Every category and the total credit minimum must be met. Each course counts in exactly one category.</p></section><CoreList courses={data.audit.coreRequirements} /></>}
          </>}
          {view === 'transcript' && <TranscriptView transcript={data.transcript} audit={data.audit} />}
          {view === 'catalog' && <CatalogView courses={data.courses} />}
          <footer className="page-footer"><span><ShieldCheck size={14} /> Demo catalog — not official advising</span><span>{data.audit.catalogVersion} · Rules {data.audit.ruleEngineVersion}<br />Calculated <time dateTime={data.audit.generatedAt}>{new Date(data.audit.generatedAt).toLocaleString()}</time></span></footer>
        </>}
      </main>
    </div>
  </div>
}

function Login({ loading, error, onLogin, onRetry }: { loading: boolean; error: string; onLogin: () => void; onRetry: () => void }) {
  const [username, setUsername] = useState('student-ava')
  const [password, setPassword] = useState('')
  const [submitting, setSubmitting] = useState(false)
  const [loginError, setLoginError] = useState('')
  async function submit(event: FormEvent) {
    event.preventDefault(); setSubmitting(true); setLoginError('')
    try { await mutate('/api/auth/login', { username, password }); setPassword(''); onLogin() }
    catch (cause) { setLoginError(cause instanceof Error ? cause.message : 'Sign-in failed.') }
    finally { setSubmitting(false) }
  }
  return <div className="login-layout"><section className="login-story"><Brand /><div><p className="eyebrow">A LITTLE CLARITY. A LOT OF POSSIBILITY.</p><h1>Your degree.<br />A clearer direction.</h1><p>A thoughtful home for your academic journey.<br />Understand your progress, one requirement at a time.</p><div className="journey-illustration" aria-hidden="true"><span><CheckCheck /></span><i /><span><BookOpen /></span><i /><span><GraduationCap /></span></div></div><small>Northstar University · Fictional academic demonstration</small></section>
    <main className="login-main" id="main" tabIndex={-1}><div className="login-card"><span className="demo-pill"><span /> LOCAL DEMO</span><h2>Welcome to your workspace.</h2><p>Sign in with a fictional account to explore your degree progress.</p>
      {loading ? <p role="status">Checking your session…</p> : <>{error && <><ErrorMessage message={error} /><button className="text-button" onClick={onRetry}>Retry connection</button></>}
        <form onSubmit={event => void submit(event)}><label htmlFor="username">Demo account</label><select id="username" autoComplete="username" value={username} onChange={e => setUsername(e.target.value)}><option value="student-ava">Ava Chen · Student</option><option value="student-noah">Noah Rivera · Student</option><option value="advisor-morgan">Morgan Lee · Advisor (limited)</option><option value="admin-robin">Robin Patel · Admin (limited)</option></select><label htmlFor="password">Demo password</label><input id="password" type="password" autoComplete="current-password" required value={password} onChange={e => setPassword(e.target.value)} aria-describedby="password-help" /><small id="password-help">Use the local demo password from your setup instructions.</small>{loginError && <ErrorMessage message={loginError} />}<button className="primary-button" disabled={submitting}>{submitting ? 'Signing in…' : 'Open my workspace'}<ArrowRight size={18} /></button></form>
      </>}<div className="login-disclaimer"><ShieldCheck size={20} /><p>All people, courses and policies in this demo are fictional. Please don’t enter real student records.</p></div></div></main></div>
}

function SectionTitle({ eyebrow, title, children }: { eyebrow?: string; title: string; children?: React.ReactNode }) {
  return <div className="section-heading"><div>{eyebrow && <p className="eyebrow">{eyebrow}</p>}<h2>{title}</h2></div>{children}</div>
}
function ProgressSummary({ audit }: { audit: Audit }) {
  const earned = audit.earnedCredits ?? 0
  const required = audit.requiredCredits ?? 0
  const percentage = required > 0 ? Math.min(100, Math.round(earned / required * 100)) : 0
  return <section className="progress-summary panel"><div><span className="status-pill"><span /> {audit.degreeComplete ? 'Demo requirements satisfied' : 'Your degree is in progress'}</span><h2>B.S. Computer Science</h2><p>Every completed course brings you closer.</p><div className="credit-total"><strong>{earned}</strong><span>/ {required} credits earned</span></div><p className="credit-remaining"><ArrowRight size={15} /> {Math.max(0, required - earned)} credits to your degree minimum</p></div><div className="progress-ring" role="img" aria-label={`${percentage}% of total degree credits earned`}><svg viewBox="0 0 160 160" aria-hidden="true"><circle className="ring-track" cx="80" cy="80" r="67" /><circle className="ring-fill" cx="80" cy="80" r="67" pathLength="100" strokeDasharray={`${percentage} 100`} /></svg><div><strong>{percentage}<small>%</small></strong><span>of the journey</span></div></div></section>
}
function Buckets({ buckets }: { buckets: Bucket[] }) {
  return <div className="buckets">{buckets.map(bucket => <div className={`bucket bucket-${bucket.id}`} key={bucket.id}><div><strong>{bucket.label}</strong><span>{bucket.earned} <small>/ {bucket.required}</small></span></div><progress aria-label={`${bucket.label} credits`} value={Math.min(bucket.earned, bucket.required)} max={bucket.required || 1} /><small>{bucket.complete ? 'Requirement satisfied' : `${bucket.remaining} credits remaining`}</small></div>)}</div>
}
function Dashboard({ account, data, navigate }: { account: Account; data: AcademicData; navigate: (view: View) => void }) {
  const { audit } = data
  const completed = audit.coreRequirements.filter(c => c.complete).length
  const remaining = audit.coreRequirements.filter(c => !c.complete)
  return <>
    <div className="overview-grid"><ProgressSummary audit={audit} /><section className="panel snapshot"><SectionTitle eyebrow="AT A GLANCE" title="Your academic snapshot" /><dl><div><dt><GraduationCap size={18} /> Core courses</dt><dd>{completed}<span> / {audit.coreRequirements.length}</span></dd></div><div><dt><BookOpen size={18} /> Courses with credit</dt><dd>{audit.earnedCourses.length}</dd></div><div><dt><ClipboardList size={18} /> Cumulative demo GPA</dt><dd>{audit.gpa?.toFixed(2) ?? '—'}<span> / 4.00</span></dd></div></dl><p className="fine-print">GPA includes all letter-graded attempts, including repeats. Credits count once.</p></section></div>
    <section className="panel section-panel"><SectionTitle eyebrow="BUILDING YOUR DEGREE" title="A little progress in every direction"><button className="text-button" onClick={() => navigate('audit')}>View full audit <ArrowRight size={16} /></button></SectionTitle><Buckets buckets={audit.buckets} /></section>
    <div className="detail-grid"><section className="panel section-panel"><SectionTitle eyebrow="WHAT’S STILL AHEAD" title="Your remaining core"><span className="count-pill">{remaining.length} courses</span></SectionTitle><p className="muted intro-text">Required courses you haven’t completed with a C or better.</p><div className="course-preview">{remaining.slice(0, 4).map(c => <div key={c.code}><span className="course-symbol"><BookOpen size={18} /></span><div><strong>{c.code}</strong><p>{c.title}</p></div><span>{c.credits} cr</span></div>)}</div>{remaining.length === 0 && <p>All required core courses are complete.</p>}<button className="text-button bottom-link" onClick={() => navigate('audit')}>See all core requirements <ArrowRight size={16} /></button></section>
      <div className="right-stack"><section className="next-steps"><p className="eyebrow">A GOOD PLACE TO START</p><h2>Make your next step<br />an informed one.</h2><button onClick={() => navigate('audit')}><span>01</span><div><strong>Understand your requirements</strong><small>See each category and what remains.</small></div><ArrowRight size={17} /></button><button onClick={() => navigate('transcript')}><span>02</span><div><strong>Look back at your coursework</strong><small>Review completed courses and grades.</small></div><ArrowRight size={17} /></button><button onClick={() => navigate('catalog')}><span>03</span><div><strong>Get to know the catalog</strong><small>Read the courses in your catalog year.</small></div><ArrowRight size={17} /></button></section><section className="ai-notice"><Sparkles size={21} /><div><h3>A thoughtful advisor, coming later.</h3><p>{account.ai.reason} Semester planning is also unavailable in this phase.</p></div></section></div></div>
    <div className="audit-note"><CircleHelp size={17} /><p>How is this calculated? Your audit follows the fixed rules in your assigned catalog, with no AI decisions. <button className="inline-link" onClick={() => navigate('audit')}>Explore the breakdown</button></p></div>
  </>
}
function CoreList({ courses }: { courses: CoreCourse[] }) {
  return <section className="panel section-panel"><SectionTitle eyebrow="THE FOUNDATION" title="Required core courses"><span className="count-pill">{courses.filter(c => c.complete).length} of {courses.length} complete</span></SectionTitle><p className="muted">Every listed course requires a letter grade of C or better.</p><ul className="core-list">{courses.map(c => <li key={c.code}><span className={`completion-icon ${c.complete ? 'complete' : ''}`}>{c.complete ? <Check size={16} /> : <span />}</span><div><strong>{c.code}</strong><p>{c.title}</p></div><span className="core-credit">{c.credits} cr</span><span className={`course-status ${c.complete ? 'complete' : ''}`}>{c.complete ? `Completed · ${c.grade}` : 'Still required'}</span></li>)}</ul></section>
}
function TranscriptView({ transcript, audit }: { transcript: Transcript; audit: Audit }) {
  return <section className="panel section-panel"><SectionTitle eyebrow="YOUR COURSE HISTORY" title="Every attempt, in one place"><span className="count-pill">{transcript.attempts.length} attempts</span></SectionTitle><p className="muted">C or better earns credit. Repeated courses count once; D/F affect GPA but earn no credit. W/I earn no credit or GPA points. P requires explicit elective approval.</p>{transcript.attempts.length === 0 ? <p>No course attempts recorded yet.</p> : <div className="table-wrap" role="region" aria-label="Transcript table" tabIndex={0}><table><caption className="sr-only">Academic transcript, most recent term first</caption><thead><tr><th scope="col">Course</th><th scope="col">Term <ArrowDown size={12} /></th><th scope="col">Course credits</th><th scope="col">Grade</th></tr></thead><tbody>{transcript.attempts.map(a => <tr key={`${a.term}-${a.courseCode}`}><td><strong>{a.courseCode}</strong><span>{a.title}</span></td><td>{a.term}</td><td>{a.credits}</td><td><span className={`grade ${['A', 'B', 'C'].includes(a.grade) ? 'passing' : ''}`}>{a.grade}</span></td></tr>)}</tbody></table></div>}<p className="fine-print">Demo GPA: {audit.gpa?.toFixed(2) ?? 'unavailable'}. All attempts are retained. Transfer equivalency is not supported.</p></section>
}
function CatalogView({ courses }: { courses: Course[] }) {
  return <><div className="catalog-intro"><BookOpen size={20} /><p>These are fictional catalog descriptions. Eligibility checks, published sections and planning will be added in a later phase.</p></div>{courses.length === 0 ? <section className="panel empty-state">No courses are available in this catalog.</section> : <div className="catalog-grid">{courses.map(course => <article className="panel catalog-course" key={course.code}><div><span className="course-code">{course.code}</span><span>{course.credits} credits</span></div><h2>{course.title}</h2><span className="category-label">{course.category.replaceAll('_', ' ')}</span><p>Catalog offerings: {course.offeredTerms.join(' · ') || 'Not specified'}</p></article>)}</div>}</>
}
