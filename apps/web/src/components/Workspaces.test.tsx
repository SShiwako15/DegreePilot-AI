import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, expect, test, vi } from 'vitest'
import { AdminWorkspace } from './AdminWorkspace'
import { AdvisorWorkspace } from './AdvisorWorkspace'

afterEach(() => vi.unstubAllGlobals())

test('invalid assigned audit displays its issues without fabricated zero-credit progress', async () => {
  vi.stubGlobal('fetch', vi.fn(async (path: string) => new Response(JSON.stringify(
    path === '/api/advisor-workspace/students' ? [{ id: 'student-ava', displayName: 'Ava', catalogVersion: 'NSU-BSCS-2026' }]
      : path.endsWith('/degree-audit') ? { status: 'invalid', earnedCredits: null, requiredCredits: null, issues: [{ code: 'CATALOG_INVALID', message: 'Pinned rules are invalid.' }] }
        : path.endsWith('/transcript') ? { attempts: [] } : path.includes('/planning/') ? { options: [], issues: [] } : []), { status: 200 })))
  render(<AdvisorWorkspace />)
  await screen.findByRole('option', { name: /Ava/ })
  await userEvent.selectOptions(screen.getByRole('combobox', { name: /^Student$/ }), 'student-ava')
  expect(await screen.findByRole('alert')).toHaveTextContent('CATALOG_INVALID')
  expect(screen.queryByText(/credits earned/)).not.toBeInTheDocument()
  expect(screen.queryByRole('button', { name: 'Send validated suggestion' })).not.toBeInTheDocument()
})

test('admin exposes a recoverable directory error without inventing assignment state', async () => {
  vi.stubGlobal('fetch', vi.fn(async () => new Response(JSON.stringify({ title: 'Directory temporarily unavailable' }), { status: 503 })))
  render(<AdminWorkspace />)
  expect(await screen.findByRole('alert')).toHaveTextContent('Directory temporarily unavailable')
  expect(screen.queryByText('Active', { exact: true })).not.toBeInTheDocument()
  expect(screen.getByRole('button', { name: 'Retry directory' })).toBeEnabled()
})

test('revoked advisor records fail closed and do not render a suggestion form', async () => {
  vi.stubGlobal('fetch', vi.fn(async (path: string) => {
    const body = path === '/api/advisor-workspace/students' ? [{ id: 'student-ava', displayName: 'Ava', catalogVersion: 'NSU-BSCS-2026' }]
      : path === '/api/advisor-workspace/terms' ? [] : { title: 'Record unavailable' }
    return new Response(JSON.stringify(body), { status: path.startsWith('/api/students/') ? 404 : 200 })
  }))
  render(<AdvisorWorkspace />)
  await screen.findByRole('option', { name: /Ava/ })
  await userEvent.selectOptions(screen.getByRole('combobox', { name: /^Student$/ }), 'student-ava')
  expect(await screen.findByRole('alert')).toHaveTextContent('Record unavailable')
  expect(screen.queryByRole('button', { name: 'Send validated suggestion' })).not.toBeInTheDocument()
})

test('assignment mutation waits for the server and reports a stale revision safely', async () => {
  vi.stubGlobal('fetch', vi.fn(async (path: string) => new Response(JSON.stringify(
    path === '/api/admin/directory' ? { users: [{ id: 'advisor-morgan', role: 'advisor', displayName: 'Morgan' }, { id: 'student-ava', role: 'student', displayName: 'Ava' }], assignments: [{ advisorId: 'advisor-morgan', studentId: 'student-ava', active: true, revision: 2 }] }
      : path === '/api/auth/csrf' ? { token: 'test-token' } : { title: 'Assignment changed. Refresh before retrying.' }), { status: path === '/api/admin/assignments' ? 409 : 200 })))
  render(<AdminWorkspace />)
  await screen.findByText('Active', { exact: true })
  const revoke = screen.getByRole('button', { name: 'Revoke assignment' })
  revoke.focus()
  await userEvent.keyboard('{Enter}')
  expect(await screen.findByRole('alert')).toHaveTextContent('Assignment changed.')
  expect(screen.getByText('Active', { exact: true })).toBeVisible()
})
