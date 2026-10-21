import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, expect, test, vi } from 'vitest'
import { SemesterPlanner } from './SemesterPlanner'
import { CourseExplorer } from './CourseExplorer'
import type { PlanOption, PlanResult, Term } from '../planning'

const term: Term = { code: '2027-SPRING', name: 'Spring 2027', kind: 'Spring', startsOn: '2027-01-18', endsOn: '2027-05-14', timezone: 'America/New_York', isPublished: true }
const option: PlanOption = { id: 'section-1', courseCode: 'CS301', sectionCode: 'CS301-02', title: 'Algorithms', credits: 3, category: 'core', days: ['Tue'], startLocal: '13:00', endLocal: '14:15', timezone: term.timezone, availableSeats: 20, tentative: false }
const valid: PlanResult = { status: 'FEASIBLE', catalogVersion: 'NSU-BSCS-2026', termCode: term.code, tentative: false, totalCredits: 3, selections: [option], issues: [], id: null }

function mockPlanner(status: 'TIMEOUT' | 'UNAVAILABLE' | 'FEASIBLE' = 'FEASIBLE') {
  const fetchMock = vi.fn<(path: string, init?: RequestInit) => Promise<Response>>(async path => {
    const data = path === '/api/terms' ? [term] : path.endsWith('/sections') ? { catalogVersion: valid.catalogVersion, term, options: [option], issues: [] }
      : path === '/api/auth/csrf' ? { token: 'test-token' } : path === '/api/students/me/plans' ? []
        : path === '/api/plans/generate' ? { ...valid, status, selections: status === 'FEASIBLE' ? [option] : [], issues: [{ code: status, message: 'Try manual validation.' }] }
          : path === '/api/plans/save' ? { ...valid, id: 'saved-plan' } : valid
    return new Response(JSON.stringify(data), { status: 200, headers: { 'Content-Type': 'application/json' } })
  })
  vi.stubGlobal('fetch', fetchMock)
  return fetchMock
}
afterEach(() => vi.unstubAllGlobals())

test('manual plan needs backend validation and changing constraints disables save', async () => {
  const calls = mockPlanner()
  render(<SemesterPlanner />)
  await userEvent.click(await screen.findByLabelText('Select CS301-02'))
  expect(screen.getByRole('button', { name: 'Save plan' })).toBeDisabled()
  await userEvent.click(screen.getByRole('button', { name: 'Validate selections' }))
  expect(await screen.findByRole('heading', { name: 'Validated schedule proposal' })).toBeVisible()
  expect(screen.getByRole('button', { name: 'Save plan' })).toBeEnabled()
  expect(calls.mock.calls.find(([path]) => path === '/api/plans/validate')?.[1]?.body).toContain('section-1')
  await userEvent.clear(screen.getByLabelText('Maximum credits'))
  await userEvent.type(screen.getByLabelText('Maximum credits'), '20')
  expect(screen.getByRole('button', { name: 'Save plan' })).toBeDisabled()
})

test.each(['TIMEOUT', 'UNAVAILABLE'] as const)('%s offers manual selection without a success label', async status => {
  mockPlanner(status)
  render(<SemesterPlanner />)
  await screen.findByLabelText('Select CS301-02')
  await userEvent.click(screen.getByRole('button', { name: 'Generate proposal' }))
  expect(await screen.findByRole('heading', { name: status })).toBeVisible()
  expect(screen.queryByRole('heading', { name: 'Validated schedule proposal' })).not.toBeInTheDocument()
  expect(screen.getByRole('button', { name: 'Save plan' })).toBeDisabled()
  await userEvent.click(screen.getByLabelText('Select CS301-02'))
  expect(screen.getByRole('button', { name: 'Validate selections' })).toBeEnabled()
})

test('course explorer presents authoritative prerequisite reasons and searches the API', async () => {
  const blocked = {
    course: { code: 'CS401', title: 'Capstone', credits: 3, category: 'core', prerequisiteGroups: [['CS301'], ['CS310']] },
    eligibility: {
      courseCode: 'CS401', status: 'blocked', eligible: false, tentative: false,
      missingPrerequisiteGroups: [['CS301'], ['CS310']],
      reasons: [{ code: 'MISSING_PREREQUISITE', message: 'Complete CS301 and CS310 before this term.' }],
    },
  }
  const calls = vi.fn(async (path: string) => new Response(JSON.stringify(path === '/api/terms' ? [term] : [blocked]), { status: 200 }))
  vi.stubGlobal('fetch', calls)
  render(<CourseExplorer />)
  const course = await screen.findByRole('article')
  expect(course).toHaveTextContent('Blocked')
  expect(course).toHaveTextContent('Complete CS301 and CS310')
  await userEvent.type(screen.getByRole('searchbox'), 'CS401')
  await waitFor(() => expect(calls.mock.calls.some(([path]) => path.includes('search=CS401'))).toBe(true))
})
