import { mkdir } from 'node:fs/promises'
import { resolve } from 'node:path'
import { expect, test } from '@playwright/test'
import type { Page } from '@playwright/test'

const password = process.env.DEMO_PASSWORD ?? 'Local-Demo-Only!2026'

async function login(page: Page, username: string) {
  await page.getByLabel('Demo account').selectOption(username)
  await page.getByLabel('Demo password', { exact: true }).fill(password)
  await page.getByRole('button', { name: 'Open my workspace' }).click()
}

async function openNavigation(page: Page) {
  const toggle = page.getByRole('button', { name: 'Open navigation' })
  if (await toggle.isVisible()) await toggle.click()
}

async function noPageOverflow(page: Page) {
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true)
}

test('real login, audit, transcript, catalog, account isolation and logout', async ({ page }, testInfo) => {
  const errors: string[] = []
  page.on('pageerror', error => errors.push(error.message))
  await page.goto('/')
  await expect(page.getByLabel('Demo account')).toBeVisible()
  await noPageOverflow(page)
  await login(page, 'student-ava')
  await expect(page.getByRole('heading', { name: 'Your next chapter, Ava.' })).toBeVisible()
  await expect(page.getByText('35', { exact: true })).toBeVisible()
  await expect(page.getByRole('img', { name: '29% of total degree credits earned' })).toBeVisible()
  await expect(page.getByText(/AI advising is unavailable/)).toBeVisible()
  await noPageOverflow(page)
  if (process.env.CAPTURE_SCREENSHOTS === '1') {
    const folder = resolve('../../docs/screenshots')
    await mkdir(folder, { recursive: true })
    await page.screenshot({ path: resolve(folder, `dashboard-${testInfo.project.name}.png`), fullPage: true })
  }

  await openNavigation(page)
  const auditLink = page.getByRole('link', { name: 'Degree audit' })
  await auditLink.focus()
  await page.keyboard.press('Enter')
  await expect(page.getByRole('heading', { name: 'Degree audit', exact: true })).toBeFocused()
  await expect(page.getByText('Still required', { exact: true })).toHaveCount(7)
  await expect(page.getByText('6 of 13 complete')).toBeVisible()
  await noPageOverflow(page)

  await openNavigation(page)
  await page.getByRole('link', { name: 'My transcript' }).click()
  await expect(page.getByRole('row')).toHaveCount(11)
  await noPageOverflow(page)
  await openNavigation(page)
  await page.getByRole('link', { name: 'Course catalog' }).click()
  await expect(page.getByRole('article')).toHaveCount(36)
  await noPageOverflow(page)

  await openNavigation(page)
  await page.getByRole('button', { name: 'Sign out' }).click()
  await expect(page.getByLabel('Demo account')).toBeVisible()
  await login(page, 'student-noah')
  await openNavigation(page)
  await page.getByRole('link', { name: 'Overview' }).click()
  await expect(page.getByText('7', { exact: true })).toBeVisible()
  const denied = await page.request.get('/api/students/student-ava/transcript')
  expect(denied.status()).toBe(404)
  expect(await denied.text()).not.toContain('CS201')
  const own = await page.request.get('/api/students/me/degree-audit?studentId=student-ava')
  expect((await own.json()).earnedCredits).toBe(7)
  await openNavigation(page)
  await page.getByRole('button', { name: 'Sign out' }).click()
  await expect(page.getByLabel('Demo account')).toBeVisible()
  expect((await page.request.get('/api/me')).status()).toBe(401)
  expect(errors).toEqual([])
})
