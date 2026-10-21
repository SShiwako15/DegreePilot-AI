import { useState } from 'react'
import type { FormEvent } from 'react'
import { Eye, EyeOff, LockKeyhole, ShieldCheck, UserRound } from 'lucide-react'
import { mutate } from '../api'
import { ErrorMessage, UniversityLogo } from './Brand'

export function Login({ loading, error, onLogin, onRetry }: {
  loading: boolean
  error: string
  onLogin: () => void
  onRetry: () => void
}) {
  const [username, setUsername] = useState('student-ava')
  const [password, setPassword] = useState('')
  const [showPassword, setShowPassword] = useState(false)
  const [submitting, setSubmitting] = useState(false)
  const [loginError, setLoginError] = useState('')

  async function submit(event: FormEvent) {
    event.preventDefault()
    setSubmitting(true)
    setLoginError('')
    try {
      await mutate('/api/auth/login', { username, password })
      setPassword('')
      onLogin()
    } catch (cause) {
      setLoginError(cause instanceof Error ? cause.message : 'Sign-in failed.')
    } finally {
      setSubmitting(false)
    }
  }

  return (
    <div className="relative isolate flex min-h-dvh items-center justify-center overflow-hidden bg-bms-navy px-4 py-6 sm:px-8 sm:py-10 xl:px-16">
      <img src="/branding/berry-college.jpg" alt="" aria-hidden="true" className="absolute inset-0 -z-20 size-full object-cover" />
      <div aria-hidden="true" className="absolute inset-0 -z-10 bg-bms-navy/90" />
      <div className="relative isolate grid w-full max-w-7xl overflow-hidden rounded-3xl border border-white/15 shadow-2xl lg:min-h-[740px] lg:grid-cols-[44%_56%]">
        <img src="/branding/berry-college.jpg" alt="" aria-hidden="true" className="absolute inset-0 -z-20 size-full object-cover object-center" />
        <div aria-hidden="true" className="absolute inset-0 -z-10 bg-white/80 lg:left-[34%] lg:bg-white/75" />
        <div aria-hidden="true" className="absolute inset-y-0 left-0 -z-10 hidden w-[34%] overflow-hidden bg-bms-navy/95 lg:block">
          <div className="absolute -bottom-72 -left-96 size-[900px] rounded-full border border-blue-400/15" />
          <div className="absolute -bottom-60 -left-80 size-[760px] rounded-full border border-blue-400/15" />
          <div className="absolute -bottom-48 -left-64 size-[620px] rounded-full border border-blue-400/15" />
        </div>

        <section className="relative flex flex-col items-center justify-center px-6 pb-7 pt-9 text-center lg:order-last lg:px-12 lg:pb-20 lg:pt-16 xl:px-20">
          <p className="mb-4 text-[10px] font-bold uppercase tracking-[0.22em] text-bms-blue sm:text-xs">YOUR ACADEMIC HOME BASE</p>
          <h1 className="text-4xl font-bold leading-[1.06] tracking-tight text-bms-navy sm:text-6xl lg:text-[clamp(3.5rem,5.7vw,5.5rem)]">DegreePilot <span className="text-bms-blue lg:block">AI</span></h1>
          <div aria-hidden="true" className="my-5 h-1 w-12 rounded-full bg-bms-orange lg:my-7" />
          <p className="max-w-xs text-sm leading-6 text-slate-700">A clearer view of your degree.<br />A stronger plan for what comes next.</p>
          <blockquote className="mt-5 text-lg font-semibold italic text-bms-blue lg:mt-8">“Go, Goats!”</blockquote>
        </section>

        <main id="main" tabIndex={-1} aria-labelledby="login-heading" className="relative mx-4 mb-6 self-center rounded-2xl border border-blue-300/50 bg-linear-to-br from-blue-600/95 via-bms-blue/90 to-bms-navy/90 p-6 text-white shadow-[0_20px_60px_rgba(0,26,135,0.3)] backdrop-blur-xl sm:mx-auto sm:w-full sm:max-w-md sm:p-9 lg:order-first lg:my-14 lg:ml-12 lg:mr-0 lg:w-[calc(100%-1rem)] xl:ml-20">
          <div className="mb-7 flex items-center gap-4">
            <UniversityLogo className="size-16 rounded-lg bg-white p-0.5" />
            <div className="text-sm font-bold leading-6 tracking-wide">Blue Mountain State<span className="block text-xs font-medium tracking-[0.12em] text-blue-100">UNIVERSITY</span></div>
          </div>
          <h2 id="login-heading" className="text-3xl font-semibold tracking-tight">Log in</h2>
          <p className="mt-2 text-xs leading-6 text-blue-100">Welcome to your student workspace.</p>
          {loading ? <p className="mt-8 text-sm text-blue-100" role="status">Checking your session…</p> : <>
            {error && <><ErrorMessage message={error} /><button className="min-h-11 cursor-pointer rounded-sm text-sm font-semibold text-white underline underline-offset-4" onClick={onRetry}>Retry connection</button></>}
            <form onSubmit={event => void submit(event)} className="mt-6">
              <div className="overflow-hidden rounded-lg border border-white/50 bg-white text-bms-ink shadow-sm">
                <div className="relative border-b border-slate-200 px-4 pb-2 pt-3">
                  <label htmlFor="username" className="block text-[10px] font-medium text-bms-muted">Demo account</label>
                  <UserRound aria-hidden="true" size={16} className="pointer-events-none absolute right-4 top-4 text-slate-400" />
                  <select id="username" autoComplete="username" value={username} onChange={event => setUsername(event.target.value)} className="min-h-9 w-full cursor-pointer bg-white pr-3 text-sm">
                    <option value="student-ava">Ava Chen · Student</option>
                    <option value="student-noah">Noah Rivera · Student</option>
                    <option value="advisor-morgan">Morgan Lee · Advisor (limited)</option>
                    <option value="admin-robin">Robin Patel · Admin (limited)</option>
                  </select>
                </div>
                <div className="relative px-4 pb-2 pt-3">
                  <label htmlFor="password" className="block text-[10px] font-medium text-bms-muted">Demo password</label>
                  <LockKeyhole aria-hidden="true" size={15} className="pointer-events-none absolute right-4 top-3 text-slate-400" />
                  <input id="password" type={showPassword ? 'text' : 'password'} autoComplete="current-password" required value={password} onChange={event => setPassword(event.target.value)} aria-describedby="password-help" className="min-h-9 w-full rounded-sm bg-white pr-9 text-sm" />
                  <button type="button" aria-label={showPassword ? 'Hide password' : 'Show password'} aria-pressed={showPassword} onClick={() => setShowPassword(value => !value)} className="absolute bottom-1 right-1 flex size-11 cursor-pointer items-center justify-center rounded-md text-bms-muted hover:bg-slate-100">{showPassword ? <EyeOff size={18} /> : <Eye size={18} />}</button>
                </div>
              </div>
              <p id="password-help" className="mt-3 text-[11px] leading-5 text-blue-100">Use the local demo password from your setup instructions.</p>
              {loginError && <ErrorMessage message={loginError} />}
              <button disabled={submitting} className="mt-5 min-h-12 w-full cursor-pointer rounded-lg bg-bms-orange px-5 py-3 text-sm font-bold text-bms-navy shadow-sm hover:bg-orange-400 disabled:cursor-wait disabled:opacity-60">{submitting ? 'Signing in…' : 'Login'}</button>
            </form>
          </>}
          <div className="mt-6 flex gap-2.5 border-t border-white/20 pt-5 text-blue-100">
            <ShieldCheck size={16} className="mt-0.5 shrink-0" />
            <p className="text-[10px] leading-5">All people, courses and policies in this demo are fictional. Please don’t enter real student records.</p>
          </div>
        </main>
        <footer className="relative col-span-full flex flex-wrap items-center justify-center gap-x-3 gap-y-1 border-t border-slate-300/40 bg-white/70 px-5 py-3 text-center text-[10px] leading-5 text-slate-600 lg:absolute lg:bottom-0 lg:left-[34%] lg:right-0 lg:border-0 lg:bg-transparent">
          <span>DegreePilot AI · Unofficial fictional-university demo</span>
          <span>Photo: <a href="https://commons.wikimedia.org/wiki/File:Ford_Buildings_-_Berry_College.jpg" target="_blank" rel="noreferrer" className="rounded-sm underline underline-offset-2">Camsjo</a> · <a href="https://creativecommons.org/licenses/by-sa/4.0/" target="_blank" rel="noreferrer" className="rounded-sm underline underline-offset-2">CC BY-SA 4.0</a></span>
        </footer>
      </div>
    </div>
  )
}
