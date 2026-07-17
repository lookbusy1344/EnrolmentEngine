import { defineConfig } from '@playwright/test'

const port = process.env.E2E_PORT ?? '5310'
const baseURL = `http://localhost:${port}`

// Viewport widths from the plan: phone (360, 390), tablet (768), laptop (1024), desktop (1366),
// and a wide desktop. Explicit viewport sizes, not device presets, so the widths match exactly.
export default defineConfig({
  testDir: './e2e',
  fullyParallel: true,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 1 : 0,
  reporter: 'list',
  use: {
    baseURL,
    trace: 'on-first-retry',
  },
  webServer: {
    command: 'node scripts/e2e-server.mjs',
    url: baseURL,
    reuseExistingServer: !process.env.CI,
    timeout: 120_000,
    env: { E2E_PORT: port },
  },
  projects: [
    { name: 'phone-360', use: { viewport: { width: 360, height: 740 } } },
    { name: 'phone-390', use: { viewport: { width: 390, height: 844 } } },
    { name: 'tablet-768', use: { viewport: { width: 768, height: 1024 } } },
    { name: 'laptop-1024', use: { viewport: { width: 1024, height: 768 } } },
    { name: 'desktop-1366', use: { viewport: { width: 1366, height: 900 } } },
    { name: 'wide-desktop-1920', use: { viewport: { width: 1920, height: 1080 } } },
  ],
})
