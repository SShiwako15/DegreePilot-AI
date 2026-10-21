import { defineConfig } from '@playwright/test'

// Smoke the real, running local stack. No mocked API or model calls.
export default defineConfig({
  testDir: './e2e',
  fullyParallel: false,
  workers: 1,
  forbidOnly: !!process.env.CI,
  retries: 0,
  timeout: 60000,
  globalTimeout: 10 * 60 * 1000,
  reporter: 'list',
  use: {
    baseURL: process.env.BASE_URL ?? 'http://localhost:8080',
    browserName: 'chromium',
    channel: process.env.PLAYWRIGHT_CHANNEL || undefined,
    trace: 'off',
    screenshot: 'only-on-failure',
  },
  projects: [
    { name: 'desktop', use: { viewport: { width: 1440, height: 1050 } } },
    { name: 'mobile', use: { viewport: { width: 320, height: 800 } } },
  ],
})
