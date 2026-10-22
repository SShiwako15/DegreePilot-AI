import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, expect, test, vi } from 'vitest'
import { DegreeRoadmap } from './DegreeRoadmap'

afterEach(() => vi.unstubAllGlobals())

test('roadmap timeout is explicit and never shows a fabricated graduation plan', async () => {
  vi.stubGlobal('fetch', vi.fn(async (path: string) => new Response(JSON.stringify(path === '/api/auth/csrf' ? { token: 'mock' }
    : { status: 'TIMEOUT', terms: [], issues: [{ code: 'SOLVER_TIMEOUT', message: 'Infeasibility is not established.' }] }), { status: 200 })))
  render(<DegreeRoadmap />)
  await userEvent.click(screen.getByRole('button', { name: 'Generate degree roadmap' }))
  expect(await screen.findByRole('status')).toHaveTextContent('TIMEOUT')
  expect(screen.queryByText(/Validated conditional/)).not.toBeInTheDocument()
  expect(screen.getByText(/not a graduation prediction/)).toBeVisible()
})

test('conditional roadmap labels unpublished choices and clears results when constraints change', async () => {
  vi.stubGlobal('fetch', vi.fn(async (path: string) => new Response(JSON.stringify(path === '/api/auth/csrf' ? { token: 'mock' }
    : { status: 'FEASIBLE', optimal: true, completionTerms: 2, catalogVersion: 'NSU-BSCS-2026', inputFingerprint: 'example', issues: [],
      terms: [{ termCode: '2028-SPRING', totalCredits: 3, tentative: true, selections: [{ id: 'tentative:CS401', courseCode: 'CS401', title: 'Capstone', credits: 3 }] }] }), { status: 200 })))
  render(<DegreeRoadmap />)
  await userEvent.click(screen.getByRole('button', { name: 'Generate degree roadmap' }))
  expect(await screen.findByText(/Tentative offerings/)).toBeVisible()
  expect(screen.getByText(/conditional on successful future grades/)).toBeVisible()
  await userEvent.clear(screen.getByLabelText('Roadmap maximum credits'))
  expect(screen.queryByRole('region', { name: 'Degree roadmap result' })).not.toBeInTheDocument()
})
