import { expect, test } from '@playwright/test'
import type { APIRequestContext, Page } from '@playwright/test'

const password = process.env.DEMO_PASSWORD ?? 'Local-Demo-Only!2026'
async function login(page: Page, username: string) {
  await page.getByLabel('Account').selectOption(username)
  await page.getByLabel('Password', { exact: true }).fill(password)
  await page.getByRole('button', { name: 'Login', exact: true }).click()
}
async function nav(page: Page) {
  const toggle = page.getByRole('button', { name: 'Open navigation' })
  if (await toggle.isVisible()) await toggle.click()
}
async function post(api: APIRequestContext, path: string, data: unknown) {
  const csrf = await (await api.get('/api/auth/csrf')).json() as { token: string }
  return api.post(path, { data, headers: { 'X-CSRF-TOKEN': csrf.token } })
}
async function a11ySmoke(page: Page) {
  await expect(page.getByRole('main')).toHaveCount(1)
  await expect(page.getByRole('heading', { level: 1 })).toHaveCount(1)
  expect(await page.evaluate(() => {
    const controls = [...document.querySelectorAll<HTMLInputElement>('input:not([type="hidden"]), select, textarea')]
    return controls.filter(c => !c.labels?.length && !c.getAttribute('aria-label') && !c.getAttribute('aria-labelledby')).length
  })).toBe(0)
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true)
}

test('assigned advisor sends a valid suggestion; student planner omits advisor suggestions', async ({ page }) => {
  await page.goto('/')
  await login(page, 'advisor-morgan')
  await expect(page.getByRole('heading', { level: 1, name: 'Assigned students' })).toBeVisible()
  await page.getByRole('combobox', { name: 'Student', exact: true }).selectOption('student-ava')
  await expect(page.getByRole('combobox', { name: 'Student', exact: true }).getByRole('option', { name: /Noah/ })).toHaveCount(0)
  await expect(page.getByRole('heading', { name: 'Suggest a schedule', exact: true })).toBeVisible()
  expect((await page.request.get('/api/students/student-noah/transcript')).status()).toBe(404)
  await page.getByLabel('Minimum suggestion credits').fill('3')
  await page.getByLabel('Maximum suggestion credits').fill('3')
  const section = page.getByRole('checkbox', { name: /CS301-02/ })
  await section.focus()
  await page.keyboard.press('Space')
  const send = page.getByRole('button', { name: 'Send validated suggestion' })
  await send.focus()
  await page.keyboard.press('Enter')
  await expect(page.getByRole('status').filter({ hasText: 'Validated suggestion sent' })).toBeVisible()
  await a11ySmoke(page)
  await nav(page)
  await page.getByRole('button', { name: 'Sign out' }).click()
  await login(page, 'student-ava')
  await expect(page.getByText('35', { exact: true })).toBeVisible()
  expect((await page.request.get('/api/admin/audit')).status()).toBe(403)
  await nav(page)
  await page.getByRole('link', { name: 'Semester planner' }).click()
  await expect(page.getByRole('region', { name: 'Advisor suggestions' })).toHaveCount(0)
  await expect(page.getByRole('heading', { name: 'Available eligible sections' })).toBeVisible()
  await a11ySmoke(page)
})

test('admin revokes assignment, publishes reviewed catalog and policy versions, and sees audit health', async ({ page, playwright, baseURL }, testInfo) => {
  const advisor = await playwright.request.newContext({ baseURL })
  expect((await post(advisor, '/api/auth/login', { username: 'advisor-morgan', password })).ok()).toBe(true)
  await page.goto('/')
  await login(page, 'admin-robin')
  await expect(page.getByRole('heading', { name: 'Administration', level: 1 })).toBeVisible()
  await expect(page.getByText('Assignment status:')).toContainText('Active')
  try {
    const revoke = page.getByRole('button', { name: 'Revoke assignment' })
    await revoke.focus(); await page.keyboard.press('Enter')
    await expect(page.getByText('Assignment status:')).toContainText('Inactive')
    expect((await advisor.get('/api/students/student-ava/transcript')).status()).toBe(404)
    await page.getByRole('button', { name: 'Grant assignment' }).click()
    await expect(page.getByText('Assignment status:')).toContainText('Active')
    expect((await advisor.get('/api/students/student-ava/transcript')).status()).toBe(200)
    await a11ySmoke(page)
    expect((await page.request.get('/api/students/student-ava/transcript')).status()).toBe(403)
    await page.getByRole('button', { name: 'Catalogs', exact: true }).click()
    const version = `BMS-E2E-${testInfo.project.name.toUpperCase()}-${Date.now()}`
    await page.getByLabel('New catalog version').fill(version)
    await page.getByRole('button', { name: 'Create catalog draft' }).click()
    await expect(page.getByRole('heading', { name: `${version} · draft` })).toBeVisible()
    await page.getByLabel('Total required credits').fill('124')
    await expect(page.getByRole('button', { name: 'Publish saved catalog' })).toBeDisabled()
    await page.getByText('Review courses and prerequisites', { exact: true }).click()
    await page.getByLabel('ELEC001 credits', { exact: true }).fill('8')
    await page.getByRole('button', { name: 'Save catalog draft' }).click()
    await expect(page.getByRole('status')).toHaveText('Catalog draft saved and validated.')
    await page.getByRole('button', { name: 'Publish saved catalog' }).click()
    await expect(page.getByRole('heading', { name: `${version} · published` })).toBeVisible()
    await expect(page.getByLabel('Total required credits')).toBeDisabled()
    await a11ySmoke(page)
    await page.getByRole('button', { name: 'Policies', exact: true }).click()
    await page.getByRole('button', { name: /· v\d+ · approved ·/ }).first().click()
    await page.getByRole('button', { name: 'Create policy revision' }).click()
    await expect(page.getByRole('heading', { name: /Review policy · v\d+ · draft/ })).toBeVisible()
    await page.getByRole('button', { name: 'Save policy draft' }).click()
    await expect(page.getByRole('status')).toHaveText('Policy draft saved. Review before publishing.')
    await page.getByRole('button', { name: 'Publish reviewed policy' }).click()
    await expect(page.getByRole('status')).toHaveText('Policy revision approved; previous version retired.')
    await a11ySmoke(page)
    await page.getByRole('button', { name: 'Audit and health', exact: true }).click()
    await expect(page.getByText('catalog.publish', { exact: true }).first()).toBeVisible()
    await expect(page.getByText('policy.publish', { exact: true }).first()).toBeVisible()
    await expect(page.getByText(/API process:/)).toBeVisible()
    await a11ySmoke(page)
  } finally {
    // Restore the active assignment even if a later browser assertion fails.
    const directory = await (await page.request.get('/api/admin/directory')).json() as { assignments: { advisorId: string; studentId: string; active: boolean; revision: number }[] }
    const current = directory.assignments.find(a => a.advisorId === 'advisor-morgan' && a.studentId === 'student-ava')
    if (current && !current.active) expect((await post(page.request, '/api/admin/assignments', { ...current, active: true })).ok()).toBe(true)
    await advisor.dispose()
  }
})
