import { ArrowDown, ArrowRight, BookOpen, Check, CircleHelp, ClipboardList, GraduationCap, Sparkles } from 'lucide-react'
import type { ReactNode } from 'react'
import type { Account, AcademicData, Audit, Bucket, CoreCourse, Transcript } from '../api'
import type { View } from '../navigation'
import { eyebrow, panel, pill, textButton } from '../ui'

export function SectionTitle({ label, title, children }: { label?: string; title: string; children?: ReactNode }) {
  return (
    <div className="flex flex-wrap items-center justify-between gap-3">
      <div>{label && <p className={`mb-2 ${eyebrow}`}>{label}</p>}<h2 className="text-lg font-semibold tracking-tight">{title}</h2></div>
      {children}
    </div>
  )
}

export function ProgressSummary({ audit }: { audit: Audit }) {
  const earned = audit.earnedCredits ?? 0
  const required = audit.requiredCredits ?? 0
  const percentage = required > 0 ? Math.min(100, Math.round(earned / required * 100)) : 0
  return (
    <section className="relative flex min-w-0 flex-col items-start justify-between gap-4 overflow-hidden rounded-2xl bg-bms-blue p-6 text-white shadow-sm sm:flex-row sm:items-center sm:p-8">
      <div aria-hidden="true" className="absolute inset-y-0 left-0 w-1.5 bg-bms-orange" />
      <div className="min-w-0 flex-1">
        <span className="mb-5 inline-flex items-center gap-2 rounded-full border border-white/25 bg-white/10 px-3 py-1.5 text-[10px] font-medium"><span className="size-1.5 shrink-0 rounded-full bg-bms-orange" />{audit.degreeComplete ? 'Degree requirements satisfied' : 'Your degree is in progress'}</span>
        <h2 className="text-xl font-semibold tracking-tight sm:text-2xl">B.S. Computer Science</h2>
        <p className="mt-2 text-xs leading-5 text-blue-100">Every completed course brings you closer.</p>
        <div className="mt-6 flex flex-wrap items-baseline gap-x-3 gap-y-1"><strong className="text-5xl font-semibold tracking-tight sm:text-6xl">{earned}</strong><span className="text-xs text-blue-100">/ {required} credits earned</span></div>
        <p className="mt-3 flex items-start gap-2 text-xs leading-5 text-blue-100"><ArrowRight size={15} className="mt-0.5 shrink-0 text-bms-orange" />{Math.max(0, required - earned)} credits to your degree minimum</p>
      </div>
      <div className="relative mx-auto size-28 shrink-0 sm:size-36 xl:size-40" role="img" aria-label={`${percentage}% of total degree credits earned`}>
        <svg viewBox="0 0 160 160" className="size-full -rotate-90" aria-hidden="true">
          <circle cx="80" cy="80" r="67" className="fill-none stroke-white/20 stroke-8" />
          <circle cx="80" cy="80" r="67" pathLength="100" strokeDasharray={`${percentage} 100`} strokeLinecap="round" className="fill-none stroke-bms-orange stroke-8" />
        </svg>
        <div className="absolute inset-0 flex flex-col items-center justify-center gap-1"><strong className="text-3xl font-semibold tracking-tight sm:text-4xl">{percentage}<small className="text-base">%</small></strong><span className="text-[10px] text-blue-100">of the journey</span></div>
      </div>
    </section>
  )
}

export function Buckets({ buckets }: { buckets: Bucket[] }) {
  return (
    <div className="mt-6 grid gap-6 sm:grid-cols-2 xl:grid-cols-4">
      {buckets.map(bucket => (
        <div key={bucket.id} className="min-w-0">
          <div className="mb-3 flex items-center justify-between gap-2 text-xs"><strong className="font-medium">{bucket.label}</strong><span className="shrink-0 text-base font-semibold">{bucket.earned} <small className="text-xs font-normal text-bms-muted">/ {bucket.required}</small></span></div>
          <progress
            aria-label={`${bucket.label} credits`}
            value={Math.min(bucket.earned, bucket.required)}
            max={bucket.required || 1}
            className="mb-2 block h-1.5 w-full overflow-hidden rounded-full border-0 bg-slate-100 accent-bms-blue [&::-moz-progress-bar]:rounded-full [&::-moz-progress-bar]:bg-bms-blue [&::-webkit-progress-bar]:rounded-full [&::-webkit-progress-bar]:bg-slate-100 [&::-webkit-progress-value]:rounded-full [&::-webkit-progress-value]:bg-bms-blue"
          />
          <small className="text-[11px] text-bms-muted">{bucket.complete ? 'Requirement satisfied' : `${bucket.remaining} credits remaining`}</small>
        </div>
      ))}
    </div>
  )
}

export function Dashboard({ account, data, navigate }: { account: Account; data: AcademicData; navigate: (view: View) => void }) {
  const { audit } = data
  const completed = audit.coreRequirements.filter(course => course.complete).length
  const remaining = audit.coreRequirements.filter(course => !course.complete)
  const nextSteps: { view: View; title: string; description: string }[] = [
    { view: 'audit', title: 'Understand your requirements', description: 'See each category and what remains.' },
    { view: 'transcript', title: 'Look back at your coursework', description: 'Review completed courses and grades.' },
    { view: 'catalog', title: 'Get to know the catalog', description: 'Read the courses in your catalog year.' },
  ]
  return (
    <div className="space-y-6">
      <div className="grid gap-6 xl:grid-cols-[1.5fr_1fr]">
        <ProgressSummary audit={audit} />
        <section className={panel}>
          <SectionTitle label="AT A GLANCE" title="Your academic snapshot" />
          <dl className="my-4 divide-y divide-slate-100">
            <div className="flex items-center justify-between gap-3 py-3"><dt className="flex items-center gap-2 text-xs text-bms-muted"><GraduationCap size={17} className="shrink-0 text-bms-blue" />Core courses</dt><dd className="text-xl font-semibold">{completed}<span className="text-xs font-normal text-bms-muted"> / {audit.coreRequirements.length}</span></dd></div>
            <div className="flex items-center justify-between gap-3 py-3"><dt className="flex items-center gap-2 text-xs text-bms-muted"><BookOpen size={17} className="shrink-0 text-bms-blue" />Courses with credit</dt><dd className="text-xl font-semibold">{audit.earnedCourses.length}</dd></div>
            <div className="flex items-center justify-between gap-3 py-3"><dt className="flex items-center gap-2 text-xs text-bms-muted"><ClipboardList size={17} className="shrink-0 text-bms-blue" />Cumulative GPA</dt><dd className="shrink-0 text-xl font-semibold">{audit.gpa?.toFixed(2) ?? '—'}<span className="text-xs font-normal text-bms-muted"> / 4.00</span></dd></div>
          </dl>
          <p className="text-[11px] leading-5 text-bms-muted">GPA includes all letter-graded attempts, including repeats. Credits count once.</p>
        </section>
      </div>
      <section className={panel}>
        <SectionTitle label="BUILDING YOUR DEGREE" title="Every credit. A step forward."><button className={textButton} onClick={() => navigate('audit')}>View full audit <ArrowRight size={16} /></button></SectionTitle>
        <Buckets buckets={audit.buckets} />
      </section>
      <div className="grid gap-6 xl:grid-cols-2">
        <section className={panel}>
          <SectionTitle label="WHAT’S STILL AHEAD" title="Your remaining core"><span className={pill}>{remaining.length} courses</span></SectionTitle>
          <p className="mt-3 text-xs leading-6 text-bms-muted">Required courses you haven’t completed with a C or better.</p>
          <div className="mt-3 divide-y divide-slate-100">
            {remaining.slice(0, 4).map(course => (
              <div key={course.code} className="flex items-center gap-3 py-4">
                <span className="grid size-10 shrink-0 place-items-center rounded-lg border border-blue-100 bg-bms-sky text-bms-blue"><BookOpen size={18} /></span>
                <div className="min-w-0 flex-1"><strong className="text-xs">{course.code}</strong><p className="mt-1 text-xs leading-5 text-bms-muted">{course.title}</p></div>
                <span className="shrink-0 text-xs text-bms-muted">{course.credits} cr</span>
              </div>
            ))}
          </div>
          {remaining.length === 0 && <p className="py-6 text-sm">All required core courses are complete.</p>}
          <button className={`mt-3 ${textButton}`} onClick={() => navigate('audit')}>See all core requirements <ArrowRight size={16} /></button>
        </section>
        <div className="flex flex-col gap-5">
          <section className="rounded-2xl border-t-4 border-bms-orange bg-bms-navy p-6 text-white sm:p-7">
            <p className="text-[10px] font-bold uppercase tracking-[0.18em] text-blue-200">YOUR NEXT PLAY</p>
            <h2 className="mt-3 text-2xl font-semibold tracking-tight">Make your next step<br />an informed one.</h2>
            <div className="mt-5 divide-y divide-white/15">
              {nextSteps.map((step, index) => (
                <button key={step.view} onClick={() => navigate(step.view)} className="flex min-h-20 w-full cursor-pointer items-center gap-3 rounded-sm py-4 text-left hover:text-blue-100">
                  <span className="text-xs font-bold text-bms-orange">0{index + 1}</span>
                  <span className="min-w-0 flex-1"><strong className="text-xs font-semibold">{step.title}</strong><small className="mt-1 block text-[11px] leading-5 text-blue-200">{step.description}</small></span>
                  <ArrowRight size={17} className="shrink-0 text-bms-orange" />
                </button>
              ))}
            </div>
          </section>
          <section className="flex gap-3 rounded-xl border border-dashed border-blue-200 bg-bms-sky p-5">
            <Sparkles size={21} className="shrink-0 text-bms-blue" />
            <div><h3 className="text-sm font-semibold text-bms-blue">Your evidence-grounded advisor</h3><p className="mt-2 text-xs leading-6 text-bms-muted">{account.ai.reason}</p><button className={`mt-2 ${textButton}`} onClick={() => navigate('advisor')}>Open AI advisor <ArrowRight size={16} /></button></div>
          </section>
        </div>
      </div>
      <div className="flex items-start gap-2 text-bms-muted"><CircleHelp size={17} className="mt-1 shrink-0" /><p className="text-xs leading-6">Your audit follows the fixed rules in your assigned catalog, with no AI decisions. <button className="cursor-pointer rounded-sm text-bms-blue underline underline-offset-4" onClick={() => navigate('audit')}>Explore the breakdown</button></p></div>
    </div>
  )
}

export function CoreList({ courses }: { courses: CoreCourse[] }) {
  return (
    <section className={panel}>
      <SectionTitle label="THE FOUNDATION" title="Required core courses"><span className={pill}>{courses.filter(course => course.complete).length} of {courses.length} complete</span></SectionTitle>
      <p className="mt-3 text-xs leading-6 text-bms-muted">Every listed course requires a letter grade of C or better.</p>
      <ul className="mt-4 divide-y divide-slate-100">
        {courses.map(course => (
          <li key={course.code} className="grid grid-cols-[auto_1fr_auto] items-center gap-x-3 gap-y-2 py-4 sm:grid-cols-[auto_1fr_auto_auto]">
            <span className={`grid size-7 place-items-center rounded-full border ${course.complete ? 'border-blue-200 bg-bms-sky text-bms-blue' : 'border-slate-300 text-bms-muted'}`}>{course.complete ? <Check size={16} /> : <span className="size-1.5 rounded-full bg-slate-400" />}</span>
            <div className="min-w-0"><strong className="text-xs">{course.code}</strong><p className="mt-1 text-xs leading-5 text-bms-muted">{course.title}</p></div>
            <span className="whitespace-nowrap text-xs text-bms-muted">{course.credits} cr</span>
            <span className={`col-start-2 w-fit rounded-md px-2 py-1.5 text-[10px] font-semibold sm:col-start-auto ${course.complete ? 'bg-bms-sky text-bms-blue' : 'bg-orange-50 text-orange-900'}`}>{course.complete ? `Completed · ${course.grade}` : 'Still required'}</span>
          </li>
        ))}
      </ul>
    </section>
  )
}

export function TranscriptView({ transcript, audit }: { transcript: Transcript; audit: Audit }) {
  return (
    <section className={panel}>
      <SectionTitle label="YOUR COURSE HISTORY" title="Every attempt, in one place"><span className={pill}>{transcript.attempts.length} attempts</span></SectionTitle>
      <p className="mt-3 text-xs leading-6 text-bms-muted">C or better earns credit. Repeated courses count once; D/F affect GPA but earn no credit. W/I earn no credit or GPA points. P requires explicit elective approval.</p>
      {transcript.attempts.length === 0 ? <p className="py-8 text-sm text-bms-muted">No course attempts recorded yet.</p> : (
        <div className="my-6 overflow-x-auto rounded-lg border border-slate-200" role="region" aria-label="Transcript table" tabIndex={0}>
          <table className="w-full border-collapse text-left text-xs">
            <caption className="sr-only">Academic transcript, most recent term first</caption>
            <thead className="bg-bms-sky text-bms-blue"><tr><th scope="col" className="p-4 font-semibold">Course</th><th scope="col" className="p-4 font-semibold">Term <ArrowDown size={12} className="inline" /></th><th scope="col" className="p-4 font-semibold">Course credits</th><th scope="col" className="p-4 font-semibold">Grade</th></tr></thead>
            <tbody className="divide-y divide-slate-100">
              {transcript.attempts.map(attempt => (
                <tr key={`${attempt.term}-${attempt.courseCode}`}>
                  <td className="min-w-52 p-4"><strong>{attempt.courseCode}</strong><span className="mt-1 block leading-5 text-bms-muted">{attempt.title}</span></td>
                  <td className="whitespace-nowrap p-4 text-bms-muted">{attempt.term}</td>
                  <td className="p-4">{attempt.credits}</td>
                  <td className="p-4"><span className={`inline-grid size-8 place-items-center rounded-md font-semibold ${['A', 'B', 'C'].includes(attempt.grade) ? 'bg-bms-sky text-bms-blue' : 'bg-orange-50 text-orange-900'}`}>{attempt.grade}</span></td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
      <p className="text-[11px] leading-6 text-bms-muted">GPA: {audit.gpa?.toFixed(2) ?? 'unavailable'}. All attempts are retained. Transfer equivalency is not supported.</p>
    </section>
  )
}
