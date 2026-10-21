import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, expect, test, vi } from 'vitest'
import { AdvisorChat } from './AdvisorChat'
import type { AdvisorResult, Citation } from './AdvisorChat'

const source: Citation = { policySectionId: 'CAT-PREREQ-CS401', catalogVersion: 'NSU-BSCS-2026', documentTitle: 'Demo catalog', documentVersion: 1, title: 'Capstone eligibility', quote: 'CS401 requires CS301 and CS310 before the target term.' }
const result: AdvisorResult = { status: 'ANSWERED', catalogVersion: source.catalogVersion, answer: 'CS401 is blocked by CS301 and CS310.', citations: [source], limitations: ['Fictional guidance only.'], facts: ['CS401 is blocked.'] }

function mockAdvisor(available = true, response = result) {
  const fetchMock = vi.fn(async (path: string) => new Response(JSON.stringify(
    path === '/api/advisor/status' ? { available, reason: available ? 'AI advising is available.' : 'AI advising is unavailable: optional provider is not configured.' }
      : path === '/api/policies' ? [source] : path === '/api/auth/csrf' ? { token: 'fake-token' }
        : path === '/api/advisor/chat' ? response : [],
  ), { status: 200, headers: { 'Content-Type': 'application/json' } }))
  vi.stubGlobal('fetch', fetchMock)
  return fetchMock
}

afterEach(() => vi.unstubAllGlobals())

test('missing key disables sending while approved policies remain readable', async () => {
  const calls = mockAdvisor(false)
  render(<AdvisorChat catalogVersion={source.catalogVersion} />)
  expect(await screen.findByText(/AI advising is unavailable/)).toBeVisible()
  expect(screen.getByRole('button', { name: 'Send question' })).toBeDisabled()
  expect(screen.getByRole('textbox', { name: 'Your question' })).toBeDisabled()
  await userEvent.click(screen.getByText('CAT-PREREQ-CS401 · Capstone eligibility'))
  expect(screen.getByText(source.quote)).toBeVisible()
  expect(calls.mock.calls.some(([path]) => path === '/api/advisor/chat')).toBe(false)
})

test('sends bounded user context, opens citations, and clears the local conversation', async () => {
  const calls = mockAdvisor()
  render(<AdvisorChat catalogVersion={source.catalogVersion} />)
  await screen.findByText('AI advising is available.')
  await userEvent.type(screen.getByRole('textbox', { name: 'Your question' }), "Why can't I take CS401?")
  await userEvent.click(screen.getByRole('button', { name: 'Send question' }))
  const conversation = screen.getByRole('log', { name: 'Advisor conversation' })
  expect(await within(conversation).findByText(result.answer)).toBeVisible()
  await userEvent.click(within(conversation).getByText('CAT-PREREQ-CS401 · Capstone eligibility'))
  expect(within(conversation).getByText(source.quote)).toBeVisible()
  await userEvent.type(screen.getByRole('textbox', { name: 'Your question' }), 'Tell me more')
  await userEvent.click(screen.getByRole('button', { name: 'Send question' }))
  await waitFor(() => expect(within(conversation).getAllByRole('article')).toHaveLength(2))
  const requests = (calls.mock.calls as unknown as [string, RequestInit][]).filter(([path]) => path === '/api/advisor/chat')
  expect(JSON.parse(requests[1][1].body as string).previousQuestions).toEqual(["Why can't I take CS401?"])
  await userEvent.click(screen.getByRole('button', { name: 'Clear conversation' }))
  expect(within(conversation).queryByText(result.answer)).not.toBeInTheDocument()
  await waitFor(() => expect(screen.getByRole('textbox', { name: 'Your question' })).toHaveFocus())
})

test('renders abstention and markup as plain text without invented citation links', async () => {
  mockAdvisor(true, { ...result, status: 'ABSTAIN', answer: '<script>alert(1)</script> No approved evidence.', citations: [], facts: [] })
  render(<AdvisorChat catalogVersion={source.catalogVersion} />)
  await screen.findByText('AI advising is available.')
  await userEvent.type(screen.getByRole('textbox', { name: 'Your question' }), 'Transfer policy?')
  await userEvent.click(screen.getByRole('button', { name: 'Send question' }))
  const conversation = screen.getByRole('log', { name: 'Advisor conversation' })
  expect(await within(conversation).findByText(/<script>/)).toBeVisible()
  expect(conversation.querySelector('script')).toBeNull()
  expect(within(conversation).queryByText('Approved sources')).not.toBeInTheDocument()
})

test('clearing an in-flight conversation prevents a late reply from restoring it', async () => {
  const normal = mockAdvisor()
  let resolveReply!: (response: Response) => void
  vi.stubGlobal('fetch', vi.fn((path: string) => path === '/api/advisor/chat'
    ? new Promise<Response>(resolve => { resolveReply = resolve }) : normal(path)))
  render(<AdvisorChat catalogVersion={source.catalogVersion} />)
  await screen.findByText('AI advising is available.')
  await userEvent.type(screen.getByRole('textbox', { name: 'Your question' }), 'Why CS401?')
  await userEvent.click(screen.getByRole('button', { name: 'Send question' }))
  await waitFor(() => expect(resolveReply).toBeTypeOf('function'))
  await userEvent.click(screen.getByRole('button', { name: 'Clear conversation' }))
  resolveReply(new Response(JSON.stringify(result), { status: 200 }))
  await waitFor(() => expect(screen.getByRole('button', { name: 'Send question' })).toBeDisabled())
  expect(screen.getByRole('textbox', { name: 'Your question' })).toHaveValue('')
  expect(within(screen.getByRole('log')).queryByText(result.answer)).not.toBeInTheDocument()
})
