export interface Account {
  id: string
  displayName: string
  role: 'student' | 'advisor' | 'admin'
  catalogVersion: string | null
  ai: { available: boolean; reason: string }
}
export interface Course {
  code: string
  title: string
  credits: number
  category: string
  catalogVersion: string
  prerequisiteGroups: string[][]
  offeredTerms: string[]
}
export interface Bucket { id: string; label: string; earned: number; required: number; remaining: number; complete: boolean }
export interface CoreCourse { code: string; title: string; credits: number; complete: boolean; grade: string | null; term: string | null }
export interface Audit {
  status: 'valid' | 'invalid'
  catalogVersion: string
  generatedAt: string
  ruleEngineVersion: string
  earnedCredits: number | null
  requiredCredits: number | null
  gpa: number | null
  degreeComplete: boolean | null
  buckets: Bucket[]
  coreRequirements: CoreCourse[]
  earnedCourses: { code: string; credits: number; category: string; grade: string; term: string }[]
  issues: { code: string; message: string }[]
}
export interface Transcript {
  catalogVersion: string
  attempts: { courseCode: string; title: string; credits: number; term: string; grade: string }[]
}
export interface AcademicData { audit: Audit; transcript: Transcript; courses: Course[] }

export class ApiError extends Error {
  constructor(public status: number, message: string) { super(message) }
}

export async function request<T>(path: string, options: RequestInit = {}): Promise<T> {
  const response = await fetch(path, { credentials: 'same-origin', ...options })
  if (!response.ok) {
    const problem = await response.json().catch(() => ({})) as { title?: string; issues?: { code: string; message: string }[] }
    const details = problem.issues?.map(issue => `${issue.code}: ${issue.message}`).join(' ')
    throw new ApiError(response.status, details || problem.title || (response.status === 401 ? 'Your session has expired. Please sign in again.' : 'We could not load your records. Please try again.'))
  }
  if (response.status === 204) return undefined as T
  return response.json() as Promise<T>
}

export async function mutate<T = void>(path: string, body?: unknown, signal?: AbortSignal) {
  // Fetch after each identity change: ASP.NET binds antiforgery tokens to the actor.
  const { token } = await request<{ token: string }>('/api/auth/csrf', { signal })
  return request<T>(path, {
    method: 'POST', signal, headers: { 'Content-Type': 'application/json', 'X-CSRF-TOKEN': token },
    body: body === undefined ? undefined : JSON.stringify(body),
  })
}
