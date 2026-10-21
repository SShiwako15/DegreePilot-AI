import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, expect, test, vi } from 'vitest'
import App from './App'
import type { Account, Audit } from './api'

const actor: Account = { id: 'student-ava', displayName: 'Ava Chen (fictional)', role: 'student', catalogVersion: 'NSU-BSCS-2026', ai: { available: false, reason: 'AI advising is unavailable in this phase.' } }
const audit: Audit = {
  status: 'valid', catalogVersion: 'NSU-BSCS-2026', generatedAt: '2026-10-20T12:00:00Z', ruleEngineVersion: '1.0.0', earnedCredits: 35, requiredCredits: 120, gpa: 3.37, degreeComplete: false,
  buckets: [{ id: 'core', label: 'Required core', earned: 23, required: 44, remaining: 21, complete: false }],
  coreRequirements: [{ code: 'CS401', title: 'Senior Capstone', credits: 3, complete: false, grade: null, term: null }], earnedCourses: [], issues: [],
}
function mockSession(result = audit) {
  vi.stubGlobal('fetch', vi.fn(async (path: string) => new Response(JSON.stringify(
    path === '/api/me' ? actor : path.endsWith('degree-audit') ? result : path.endsWith('transcript') ? { catalogVersion: actor.catalogVersion, attempts: [] } : []), { status: 200, headers: { 'Content-Type': 'application/json' } })))
}
afterEach(() => { vi.unstubAllGlobals(); history.replaceState(null, '', '/'); })

test('shows server audit totals and unavailable AI; navigation works by keyboard', async () => {
  mockSession()
  render(<App />)
  expect(await screen.findByRole('heading', { name: 'Your next chapter, Ava.' })).toBeVisible()
  expect(await screen.findByText('35')).toBeVisible()
  expect(screen.getByRole('img', { name: '29% of total degree credits earned' })).toBeVisible()
  expect(screen.getByText(/AI advising is unavailable/)).toBeVisible()
  const link = screen.getByRole('link', { name: 'Degree audit' })
  link.focus()
  await userEvent.keyboard('{Enter}')
  expect(await screen.findByRole('heading', { name: 'Required core courses' })).toBeVisible()
  expect(screen.getByText('Still required')).toBeVisible()
})

test('invalid audit does not invent credits', async () => {
  mockSession({ ...audit, status: 'invalid', earnedCredits: null, degreeComplete: null, issues: [{ code: 'CATALOG_INVALID', message: 'The pinned catalog is missing.' }] })
  render(<App />)
  expect(await screen.findByText(/We couldn’t calculate/)).toBeVisible()
  expect(screen.getByRole('alert')).toHaveTextContent('CATALOG_INVALID')
  expect(screen.queryByText('35')).not.toBeInTheDocument()
})

test('anonymous and expired sessions show an accessible login', async () => {
  vi.stubGlobal('fetch', vi.fn(async () => new Response('{}', { status: 401 })))
  render(<App />)
  expect(await screen.findByLabelText('Demo account')).toBeVisible()
  expect(screen.getByLabelText('Demo password')).toHaveAttribute('type', 'password')
  await userEvent.click(screen.getByRole('button', { name: 'Show password' }))
  expect(screen.getByLabelText('Demo password')).toHaveAttribute('type', 'text')
  await userEvent.click(screen.getByRole('button', { name: 'Hide password' }))
  expect(screen.getByLabelText('Demo password')).toHaveAttribute('type', 'password')
  expect(screen.getByText(/All people, courses and policies/)).toBeVisible()
  expect(screen.queryByText('35')).not.toBeInTheDocument()
})

test('network failure shows retry without fabricated dashboard', async () => {
  vi.stubGlobal('fetch', vi.fn(async () => { throw new Error('Connection unavailable') }))
  render(<App />)
  expect(await screen.findByRole('alert')).toHaveTextContent('Connection unavailable')
  expect(screen.getByRole('button', { name: 'Retry connection' })).toBeVisible()
})

test('advisor account is not sent to student endpoints', async () => {
  const fetchMock = vi.fn(async (path: string) => new Response(JSON.stringify(path === '/api/me' ? { ...actor, role: 'advisor' } : []), { status: 200 }))
  vi.stubGlobal('fetch', fetchMock)
  render(<App />)
  expect(await screen.findByRole('heading', { name: 'Assigned students', level: 1 })).toBeVisible()
  expect(screen.queryByRole('link', { name: 'My transcript' })).not.toBeInTheDocument()
  expect(fetchMock.mock.calls.every(([path]) => path === '/api/me' || path.startsWith('/api/advisor-workspace/'))).toBe(true)
})

test('empty transcript is explicit', async () => {
  mockSession()
  render(<App />)
  await screen.findByText('35')
  await userEvent.click(screen.getByRole('link', { name: 'My transcript' }))
  expect(await screen.findByText('No course attempts recorded yet.')).toBeVisible()
})
