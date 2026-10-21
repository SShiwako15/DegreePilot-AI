import { Compass } from 'lucide-react'

export function Brand() {
  return (
    <div className="flex items-center gap-2.5 whitespace-nowrap text-xl font-bold tracking-tight">
      <span className="grid size-9 place-items-center rounded-lg bg-bms-orange text-bms-navy">
        <Compass size={25} strokeWidth={1.8} />
      </span>
      <span>DegreePilot <span className="align-middle text-[10px] tracking-widest opacity-75">AI</span></span>
    </div>
  )
}

export function UniversityLogo({ className = 'size-16' }: { className?: string }) {
  return (
    <img
      src="/branding/blue-mountain-state.png"
      alt="Blue Mountain State Mountain Goats logo"
      width={3840}
      height={2160}
      className={`shrink-0 object-cover ${className}`}
    />
  )
}

export function DemoBadge() {
  return <span className="inline-flex items-center gap-2 rounded-full border border-blue-200 bg-bms-sky px-3 py-1.5 text-[10px] font-semibold tracking-wide text-bms-blue"><span className="size-1.5 rounded-full bg-bms-orange" /> Demo environment</span>
}

export function SkipLink() {
  return <a className="fixed -top-24 left-4 z-50 rounded-md bg-white p-4 text-sm font-semibold text-bms-blue shadow-lg focus:top-4" href="#main">Skip to content</a>
}

export function ErrorMessage({ message }: { message: string }) {
  return <div className="my-3 rounded-lg border border-red-200 bg-red-50 p-4 text-sm leading-relaxed text-red-900" role="alert">{message}</div>
}
